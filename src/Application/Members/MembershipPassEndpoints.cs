using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// One karnet as the admin's screen sees it. This is a CONTRACT the SPA's member-admin service
/// mirrors — renaming a field breaks the pass screen silently.
///
/// <para>
/// <see cref="EntriesUsed"/> AND <see cref="EntriesLeft"/> ARE DERIVED, NOT STORED. Used is the count
/// of active bookings carrying this pass's id; left is <see cref="EntryCount"/> minus that. See
/// <see cref="MembershipPass"/> for why there is no counter column behind them — and note the
/// consequence for this record: these two fields are a reading taken at query time, not a fact about
/// the row, so a screen holding them is holding a snapshot and must refetch after any booking action.
/// </para>
///
/// <para>
/// The validity range crosses the wire as <see cref="DateOnly"/>, which System.Text.Json renders as
/// <c>"2026-09-30"</c> — a DAY, with no hour and no offset to be misread in another timezone. Every
/// other timestamp in this app is a UTC instant; this one deliberately is not, because a karnet valid
/// "to the 30th" means the whole 30th wherever the reader is standing.
/// </para>
/// </summary>
public record MembershipPassView(
    Guid Id,
    string TypeName,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int EntryCount,
    int EntriesUsed,
    int EntriesLeft,
    DateTimeOffset IssuedAt,
    bool CoversToday);

/// <summary>
/// What the admin submits to issue or edit a karnet.
///
/// <para>
/// <see cref="EntryCount"/> is NOT nullable and has no "unlimited" spelling. MP-04 settles that a pass
/// always carries a count, and representing unlimited as null here would put a second gate shape into
/// the booking path for a product the club does not sell.
/// </para>
/// </summary>
public record IssuePassRequest(
    string TypeName,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int EntryCount);

/// <summary>
/// Why a karnet action was refused. The full vocabulary, which the SPA maps to Polish messages as a
/// complete <c>Record</c>:
///
/// <list type="bullet">
/// <item><c>member_not_found</c> — no such member. Answered as 404, not in this record.</item>
/// <item><c>member_blocked</c> — the member's membership is not Active, so a pass would entitle them
/// to nothing. Refused for the same reason issuing an access code to a blocked member is.</item>
/// <item><c>invalid_type_name</c> — blank, or longer than
/// <see cref="MembershipPassRules.TypeNameMaxLength"/>.</item>
/// <item><c>invalid_range</c> — <c>ValidTo</c> before <c>ValidFrom</c>, or a span wider than
/// <see cref="MembershipPassRules.MaxValidityDays"/>.</item>
/// <item><c>invalid_entry_count</c> — outside
/// <see cref="MembershipPassRules.MinEntryCount"/>..<see cref="MembershipPassRules.MaxEntryCount"/>.</item>
/// <item><c>overlapping_pass</c> — another pass of this member already covers part of the range.
/// Passes are a HISTORY, not a stack.</item>
/// <item><c>has_active_bookings</c> — the pass paid for bookings that still hold spots, so it cannot
/// vanish underneath them.</item>
/// <item><c>conflict</c> — a lost optimistic race; the caller's view is stale and should be
/// refetched.</item>
/// </list>
/// </summary>
public record MembershipPassFailure(string Reason);

/// <summary>
/// Read seam for karnety. Untracked and projected in the database, like every other <c>I*Query</c>
/// here — the write side is <see cref="IMembershipPassStore"/>.
/// </summary>
public interface IMembershipPassQuery
{
    /// <summary>
    /// This member's whole pass history, newest first, with entries used counted per pass.
    /// </summary>
    Task<IReadOnlyList<MembershipPassView>> GetForMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The pass whose inclusive range contains <paramref name="clubLocalDate"/>, or null.
    ///
    /// <para>
    /// AT MOST ONE ROW CAN MATCH, because passes may not overlap — but that is an invariant enforced
    /// on the write path, not by this query, so the implementation still orders and takes one rather
    /// than assuming.
    /// </para>
    /// </summary>
    Task<MembershipPassView?> FindCoveringAsync(
        Guid memberId,
        DateOnly clubLocalDate,
        CancellationToken cancellationToken);
}

/// <summary>
/// The admin's karnet surface (S-16, MP-04..MP-06): issue a pass, read a member's history, correct a
/// mistake, revoke one that should not have been issued.
///
/// <para>
/// NESTED UNDER THE MEMBER, not a top-level /api/admin/passes. A karnet has no existence apart from
/// the person holding it, and every screen that reaches these routes arrives from a member row — so
/// the member id belongs in the path rather than in a query string, and every handler here verifies
/// that the pass it was addressed with actually belongs to the member in the URL. Without that check
/// the nesting would be decoration and <c>/members/{someone-else}/passes/{id}</c> would work.
/// </para>
///
/// <para>
/// WHY NON-OVERLAP IS CHECKED HERE AND NOT BY AN INDEX. "No two of this member's ranges intersect" is
/// a rule about PAIRS OF ROWS, and SQL Server has no index shape that expresses it — unlike
/// IX_Members_AccessCode, which is a single-column uniqueness rule and can be. So the probe runs in
/// the handler, and what makes it an invariant rather than a guess is that the insert rotates
/// <see cref="Member.ConcurrencyStamp"/> in the same save: two admins issuing overlapping passes at
/// the same instant both pass the probe, and exactly one of them commits. That is the same protocol
/// claiming an access code and blocking already use, applied to a new shape.
/// </para>
///
/// <para>
/// The policy name comes from Domain (<see cref="AuthorizationPolicyNames"/>), not from
/// Infrastructure's AuthorizationPolicies, so this file holds no upward reference: Application ->
/// Domain only.
/// </para>
/// </summary>
public static class MembershipPassEndpoints
{
    public static IEndpointRouteBuilder MapMembershipPassEndpoints(this IEndpointRouteBuilder app)
    {
        // The policy is applied at the GROUP, not per endpoint: an endpoint added here later cannot
        // accidentally ship unauthenticated. Admin implies Active.
        var group = app.MapGroup("/api/admin/members/{memberId:guid}/passes")
            .WithTags("MembershipPasses")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        group.MapGet("/", GetPassesAsync);
        group.MapPost("/", IssuePassAsync);
        group.MapPut("/{passId:guid}", UpdatePassAsync);
        group.MapDelete("/{passId:guid}", RevokePassAsync);

        return app;
    }

    /// <summary>
    /// The member's pass history, newest first — expired passes included.
    ///
    /// <para>
    /// HISTORY, NOT "THE CURRENT PASS". The admin's question at the desk is usually "what has this
    /// person bought and what did they use", which a single current row cannot answer. The row that
    /// covers today is marked with <see cref="MembershipPassView.CoversToday"/> rather than being
    /// returned separately, so the screen can highlight it without a second request.
    /// </para>
    ///
    /// No pagination: a member accumulates a handful of passes a year.
    /// </summary>
    private static async Task<IResult> GetPassesAsync(
        Guid memberId,
        IMemberStore members,
        IMembershipPassQuery passes,
        CancellationToken cancellationToken)
    {
        // Resolved first so an unknown member is a 404 rather than an empty list — "this person has no
        // passes" and "this person does not exist" are different answers and the screen acts on them
        // differently.
        if (await members.FindAsync(memberId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(await passes.GetForMemberAsync(memberId, cancellationToken));
    }

    /// <summary>
    /// Issues a karnet (MP-04).
    ///
    /// <para>
    /// The insert and the rotation of <see cref="Member.ConcurrencyStamp"/> land in ONE
    /// <c>SaveChangesAsync</c>. That pairing is the whole non-overlap guarantee — see the type's
    /// remarks. Removing the rotation would leave a probe that is right most of the time.
    /// </para>
    /// </summary>
    private static async Task<IResult> IssuePassAsync(
        Guid memberId,
        [FromBody] IssuePassRequest request,
        IMemberStore members,
        IMembershipPassStore passes,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        if (!TryReadRequest(request, out var typeName, out var failure))
        {
            return failure;
        }

        // THE SCREEN IS NOT THE BOUNDARY, the same rule IssueAccessCodeAsync states. A pass issued to
        // a blocked member entitles them to nothing — every booking would be refused by the
        // membership check before the pass is even consulted — so handing the admin a receipt for it
        // would be a lie about what the club just sold.
        if (member.Status != MembershipStatus.Active)
        {
            return Results.Json(new MembershipPassFailure("member_blocked"), statusCode: 409);
        }

        var overlapping = await passes.FindOverlappingAsync(
            memberId,
            request.ValidFrom,
            request.ValidTo,
            excludingPassId: null,
            cancellationToken);

        if (overlapping is not null)
        {
            return Results.Json(new MembershipPassFailure("overlapping_pass"), statusCode: 409);
        }

        var pass = new MembershipPass
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            TypeName = typeName,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            EntryCount = request.EntryCount,
            IssuedAt = timeProvider.GetUtcNow(),
        };

        passes.Add(pass);

        // The rotation that makes the probe above an invariant. It has nothing to do with the member
        // row's own contents — it is a lock taken on the member for the duration of this write.
        member.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new MembershipPassFailure("conflict"), statusCode: 409);
        }

        // Freshly issued, so nothing can have consumed an entry yet — but read it back through the
        // query rather than constructing the view here, so the screen's first render comes from the
        // same projection every later refresh uses and CoversToday cannot disagree between them.
        var view = await ViewOfAsync(pass, 0, timeProvider);

        return Results.Created($"/api/admin/members/{memberId}/passes/{pass.Id}", view);
    }

    /// <summary>
    /// Corrects a karnet: its name, its range, its entry count.
    ///
    /// <para>
    /// EDITING NEVER REATTRIBUTES HISTORY. Bookings point at a pass by id
    /// (<c>Booking.MembershipPassId</c>), so narrowing a range does not hand the entries back or move
    /// them to a neighbouring pass — the bookings that this pass paid for keep consuming it. That is
    /// why the entry count may not be lowered below what is already spent: the alternative is a pass
    /// reporting negative entries left, which no screen can render honestly.
    /// </para>
    /// </summary>
    private static async Task<IResult> UpdatePassAsync(
        Guid memberId,
        Guid passId,
        [FromBody] IssuePassRequest request,
        IMemberStore members,
        IMembershipPassStore passes,
        IMembershipPassQuery query,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        var pass = await passes.FindAsync(passId, cancellationToken);

        // The nesting is enforced, not decorative: a pass belonging to somebody else is a 404 here
        // rather than a 403, because from this URL's point of view it does not exist.
        if (pass is null || pass.MemberId != memberId)
        {
            return Results.NotFound();
        }

        if (!TryReadRequest(request, out var typeName, out var failure))
        {
            return failure;
        }

        var overlapping = await passes.FindOverlappingAsync(
            memberId,
            request.ValidFrom,
            request.ValidTo,
            excludingPassId: passId,
            cancellationToken);

        if (overlapping is not null)
        {
            return Results.Json(new MembershipPassFailure("overlapping_pass"), statusCode: 409);
        }

        var used = await EntriesUsedAsync(query, memberId, passId, cancellationToken);

        if (request.EntryCount < used)
        {
            return Results.Json(new MembershipPassFailure("invalid_entry_count"), statusCode: 409);
        }

        pass.TypeName = typeName;
        pass.ValidFrom = request.ValidFrom;
        pass.ValidTo = request.ValidTo;
        pass.EntryCount = request.EntryCount;

        // Both stamps: the member's, because the overlap probe above is only atomic against it, and
        // the pass's own, because lowering EntryCount changes the entry pool a concurrent booking may
        // be counting against right now.
        member.ConcurrencyStamp = Guid.NewGuid().ToString();
        pass.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new MembershipPassFailure("conflict"), statusCode: 409);
        }

        return Results.Ok(await ViewOfAsync(pass, used, timeProvider));
    }

    /// <summary>
    /// Removes a karnet that should never have been issued.
    ///
    /// <para>
    /// REFUSED WHILE ANY BOOKING STILL POINTS AT IT. A pass that paid for a spot cannot vanish
    /// underneath it — the restrict foreign key would refuse the delete anyway, and answering that as
    /// a 500 rather than a 409 with a reason the admin can act on would be the difference between a
    /// screen that explains itself and one that appears broken. To retire a live pass the admin
    /// releases the bookings first, which is a decision about people's spots and should be a
    /// deliberate act.
    /// </para>
    /// </summary>
    private static async Task<IResult> RevokePassAsync(
        Guid memberId,
        Guid passId,
        IMemberStore members,
        IMembershipPassStore passes,
        IMembershipPassQuery query,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (await members.FindAsync(memberId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var pass = await passes.FindAsync(passId, cancellationToken);
        if (pass is null || pass.MemberId != memberId)
        {
            return Results.NotFound();
        }

        var used = await EntriesUsedAsync(query, memberId, passId, cancellationToken);

        if (used > 0)
        {
            return Results.Json(new MembershipPassFailure("has_active_bookings"), statusCode: 409);
        }

        passes.Remove(pass);

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new MembershipPassFailure("conflict"), statusCode: 409);
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Validates the shape of a request — the parts that are true regardless of what is in the
    /// database. Whether the range collides with another pass is a question about rows and is answered
    /// by the caller.
    /// </summary>
    /// <returns>
    /// <c>true</c> with the trimmed name; <c>false</c> with a 400 the caller returns unchanged.
    /// 400 rather than 409 throughout: these are malformed inputs, not conflicts with existing state.
    /// </returns>
    private static bool TryReadRequest(
        IssuePassRequest request,
        out string typeName,
        out IResult failure)
    {
        typeName = request.TypeName?.Trim() ?? string.Empty;

        if (typeName.Length == 0 || typeName.Length > MembershipPassRules.TypeNameMaxLength)
        {
            failure = Results.Json(new MembershipPassFailure("invalid_type_name"), statusCode: 400);
            return false;
        }

        // INCLUSIVE, so a one-day pass has ValidFrom == ValidTo and is legal. Only a range that runs
        // backwards is refused.
        if (request.ValidTo < request.ValidFrom)
        {
            failure = Results.Json(new MembershipPassFailure("invalid_range"), statusCode: 400);
            return false;
        }

        // DayNumber arithmetic, +1 because both ends count: the 1st to the 1st is one day, not zero.
        if (request.ValidTo.DayNumber - request.ValidFrom.DayNumber + 1 > MembershipPassRules.MaxValidityDays)
        {
            failure = Results.Json(new MembershipPassFailure("invalid_range"), statusCode: 400);
            return false;
        }

        if (request.EntryCount < MembershipPassRules.MinEntryCount
            || request.EntryCount > MembershipPassRules.MaxEntryCount)
        {
            failure = Results.Json(new MembershipPassFailure("invalid_entry_count"), statusCode: 400);
            return false;
        }

        failure = Results.Empty;
        return true;
    }

    /// <summary>
    /// How many entries this pass has already spent, read through the same projection the screen
    /// reads — so the number the handler refuses on and the number the admin is looking at cannot
    /// disagree.
    /// </summary>
    private static async Task<int> EntriesUsedAsync(
        IMembershipPassQuery query,
        Guid memberId,
        Guid passId,
        CancellationToken cancellationToken)
    {
        var history = await query.GetForMemberAsync(memberId, cancellationToken);

        return history.FirstOrDefault(x => x.Id == passId)?.EntriesUsed ?? 0;
    }

    /// <summary>
    /// The wire view of a pass the handler already holds, with a used count it already knows.
    /// </summary>
    private static Task<MembershipPassView> ViewOfAsync(
        MembershipPass pass,
        int entriesUsed,
        TimeProvider timeProvider)
    {
        // Club-local, not UTC and not the server's clock: "does this pass cover today" is a question
        // about the day at the gym. Same reasoning the booking gate uses for the class's date.
        var today = DateOnly.FromDateTime(
            Domain.Scheduling.ClubTime.ToClubLocal(timeProvider.GetUtcNow()).DateTime);

        return Task.FromResult(new MembershipPassView(
            pass.Id,
            pass.TypeName,
            pass.ValidFrom,
            pass.ValidTo,
            pass.EntryCount,
            entriesUsed,
            pass.EntryCount - entriesUsed,
            pass.IssuedAt,
            pass.ValidFrom <= today && today <= pass.ValidTo));
    }
}
