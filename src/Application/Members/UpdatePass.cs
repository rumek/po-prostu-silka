using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Edits an issued karnet (MP-01).
/// </summary>
public static class UpdatePass
{
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
    public static async Task<IResult> HandleAsync(
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

        if (!MembershipPassProjection.TryRead(request, out var typeName, out var failure))
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

        var used = await MembershipPassProjection.EntriesUsedAsync(query, memberId, passId, cancellationToken);

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

        return Results.Ok(await MembershipPassProjection.ViewOfAsync(pass, used, timeProvider));
    }
}
