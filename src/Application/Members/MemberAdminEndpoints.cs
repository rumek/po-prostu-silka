using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// A member waiting for approval, as the admin's queue sees them. This is a CONTRACT the SPA's
/// member-admin service mirrors — renaming a field breaks the approvals screen silently.
///
/// <para>
/// Addressed by <see cref="MemberId"/> since S-14, like everything else on this surface, even though
/// approval is an ACCOUNT action: the queue's rows are people, and a screen that addressed some of
/// them by member and others by account would be one mix-up away from approving the wrong person.
/// <see cref="UserId"/> travels too, because the queue is by definition accounts-only and the screen
/// has legitimate uses for it.
/// </para>
/// </summary>
public record PendingMember(
    Guid MemberId,
    string UserId,
    string Email,
    string DisplayName,
    DateTimeOffset CreatedAt);

/// <summary>
/// A member as the admin's full list sees them (FR-005, and S-14's accountless records).
///
/// This is a CONTRACT the SPA's member-admin service mirrors — renaming a field breaks the members
/// screen silently.
///
/// <para>
/// TWO STATUSES, AND THEY ARE NOT THE SAME QUESTION. <see cref="MembershipStatus"/> is always present
/// and says whether the person may use the club; <see cref="AccountStatus"/> is null exactly when
/// <see cref="UserId"/> is null and says whether their login works. Collapsing them into one field was
/// the obvious simplification and it is wrong: an accountless member has no account status to report,
/// and reporting "Active" for them would claim a login exists.
/// </para>
///
/// <para>
/// Both cross the wire as enum NAMES, never their ints — the numeric values exist for persistence
/// stability and a badge keyed on 2 would break the day someone renumbers.
/// <see cref="Roles"/> follows the same rule and is empty for a member with no account, because roles
/// live in Identity and a record without a login holds none.
/// </para>
/// </summary>
public record MemberSummary(
    Guid Id,
    string? UserId,
    string DisplayName,
    string? Email,
    string MembershipStatus,
    string? AccountStatus,
    IReadOnlyList<string> Roles,
    bool HasAccessCode,
    DateTimeOffset CreatedAt);

/// <summary>
/// One member with everything the admin's edit form needs. Separate from <see cref="MemberSummary"/>
/// rather than widening it: the list renders dozens of rows and has no business shipping everybody's
/// home address to build a table.
/// </summary>
public record MemberDetail(
    Guid Id,
    string? UserId,
    string DisplayName,
    string? Email,
    string MembershipStatus,
    string? AccountStatus,
    IReadOnlyList<string> Roles,
    string? PhoneNumber,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    DateTimeOffset CreatedAt);

/// <summary>
/// The positions the admin's filter offers. Bound as a nullable enum, so an unparseable value is a 400
/// from the framework's binding rather than a silent fall-through to "no filter" — the same reasoning
/// the account-status filter used before S-14 widened it.
///
/// <para>
/// These are the states an ADMIN thinks in, not a projection of either enum. That is why they overlap
/// the two underlying statuses rather than mirroring one: "pending" is a fact about a login, "without
/// account" is the absence of one, and "active" has to mean the same thing for a person with a login
/// and a person without.
/// </para>
/// </summary>
public enum MemberListFilter
{
    /// <summary>Has an account that is awaiting approval.</summary>
    Pending = 0,

    /// <summary>May use the club: active membership, and an active account if there is one at all.</summary>
    Active = 1,

    /// <summary>Barred. Since S-14 a block sets both statuses together, so membership alone answers this.</summary>
    Blocked = 2,

    /// <summary>Recorded by the club, never registered. The case S-14 exists for.</summary>
    WithoutAccount = 3,
}

/// <summary>
/// What the admin submits to create or edit a member record.
///
/// <para>
/// The five contact fields are ALL-OR-NOTHING rather than individually optional — see
/// <see cref="MemberAdminEndpoints"/>'s remarks on why they are not simply required here the way they
/// are at registration.
/// </para>
/// </summary>
public record MemberRequest(
    string DisplayName,
    string? Email,
    string? PhoneNumber,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City);

public record ApproveFailure(string Reason);

/// <summary>
/// Why a block was refused. <c>is_admin</c> — the target holds the Admin role and is not a member;
/// <c>conflict</c> — someone changed the row between our read and our write, so the caller's view is
/// stale and should be refetched.
/// </summary>
public record BlockFailure(string Reason);

/// <summary>
/// Why an unblock was refused. <c>conflict</c> — someone changed the row between our read and our
/// write, so the caller's view is stale and should be refetched.
///
/// <c>not_blocked</c> is GONE as of S-14 and must not come back: membership has two states, so
/// "not blocked" is "already active", and that is a no-op the handler reports as success rather than
/// an error.
/// </summary>
public record UnblockFailure(string Reason);

/// <summary>
/// Why a Trainer-role change was refused. <c>not_active</c> — the target is Pending or Blocked, and
/// FR-001 grants the role to an approved account only; letting it through would put an unvetted
/// account into the instructor selection S-06 builds on top of this.
///
/// <c>no_account</c> — the member has no login, and roles live in Identity. S-14 makes an accountless
/// instructor REPRESENTABLE (the class's instructor is a member now) but deliberately still refuses
/// one; see roadmap Open Question 3.
///
/// <c>failed</c> — Identity refused the write. This IS a real concurrency failure, despite the role
/// change looking like a simple insert: AddToRoleAsync goes through UpdateUserAsync, which is a
/// read-then-write against the ConcurrencyStamp token, so a BlockAsync landing at the same moment
/// (it rotates that stamp) makes the role write lose. The account is then Blocked and holds no
/// Trainer role — NOT the outcome the caller asked for. What makes that safe is the SPA's generic
/// 409 branch, which refetches rather than patching the row from a guess.
/// </summary>
public record TrainerRoleFailure(string Reason);

/// <summary>
/// Why a create or edit was refused. <c>invalid_display_name</c> — blank or too long;
/// <c>invalid_email</c> — present but malformed; <c>email_taken</c> — another member or account
/// already holds it; <c>conflict</c> — a lost optimistic race. The five contact codes
/// (<c>invalid_phone</c> and friends) come straight from <see cref="ContactDetails"/> and are the same
/// strings <c>/register</c> and <c>PUT /api/profile</c> answer with.
/// </summary>
public record MemberFailure(string Reason);

/// <summary>
/// A member code as the admin sees it: formatted for reading aloud, with the moment it stops working.
/// </summary>
public record AccessCodeView(string Code, DateTimeOffset ExpiresAt);

/// <summary>
/// Why issuing a code was refused. <c>has_account</c> — the member already has a login, so there is
/// nothing to claim; <c>conflict</c> — a lost optimistic race, or the generator lost every retry.
/// </summary>
public record AccessCodeFailure(string Reason);

/// <summary>
/// The admin's member surface: the approval queue (S-01), the full member list S-02 added on top of
/// it, and since S-14 the records of people who have never registered at all.
///
/// <para>
/// EVERY ROUTE HERE IS ADDRESSED BY MEMBER ID, including the ones that act on an account. That is a
/// deliberate uniformity: this screen's rows are people, half of them may have no account id to be
/// addressed by, and a surface where the identifier in the URL depended on which action you were
/// taking would be a mix-up waiting to happen. The account-shaped actions — approve, and the Trainer
/// role — resolve the member first and answer 409 <c>no_account</c> when there is nothing to act on.
/// </para>
///
/// <para>
/// WHY CONTACT DETAILS ARE ALL-OR-NOTHING HERE, AND REQUIRED AT REGISTRATION. FR-025 makes them
/// mandatory when someone registers, and that rule is unchanged. An admin recording a person at the
/// desk is a different situation: demanding a full postal address before the club may write down that
/// someone trains here would make this slice unusable for the case it exists for. So the block is
/// optional — but if ANY of the five is supplied, all five must be, validated by the same
/// <see cref="ContactDetails.TryCreate"/> the other two callers use. Half an address is the one
/// outcome nobody wants, and reusing the validator verbatim is what keeps the three surfaces from
/// drifting.
/// </para>
///
/// <para>
/// There is still no reject — FR-003 dropped it from the MVP. The PRD's open question about a blocked
/// member's existing bookings is ANSWERED as of S-08: blocking silently cancels the member's FUTURE
/// bookings and leaves past ones alone. Unblocking restores nothing.
/// </para>
///
/// The policy name comes from Domain (AuthorizationPolicyNames), not from Infrastructure's
/// AuthorizationPolicies, so this file holds no upward reference: Application -> Domain only.
/// </summary>
public static class MemberAdminEndpoints
{
    public static IEndpointRouteBuilder MapMemberAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // The policy is applied at the GROUP, not per endpoint: an endpoint added here later cannot
        // accidentally ship unauthenticated. Admin implies Active, so a pending admin is refused too.
        var group = app.MapGroup("/api/admin/members")
            .WithTags("Members")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        group.MapGet("/pending", GetPendingAsync);
        group.MapGet("/", GetMembersAsync);
        group.MapGet("/{memberId:guid}", GetMemberAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{memberId:guid}", UpdateAsync);
        group.MapPost("/{memberId:guid}/approve", ApproveAsync);
        group.MapPost("/{memberId:guid}/block", BlockAsync);
        group.MapPost("/{memberId:guid}/unblock", UnblockAsync);
        group.MapPost("/{memberId:guid}/roles/trainer", GrantTrainerAsync);
        group.MapDelete("/{memberId:guid}/roles/trainer", RevokeTrainerAsync);

        // A SEPARATE ROUTE FOR READING THE CODE, not a field on the member list. The list is loaded
        // on every visit to the members screen; shipping every live code into it would put the whole
        // set in the browser's memory and its network log for no reason. The list carries a boolean.
        group.MapGet("/{memberId:guid}/access-code", GetAccessCodeAsync);
        group.MapPost("/{memberId:guid}/access-code", IssueAccessCodeAsync);
        group.MapDelete("/{memberId:guid}/access-code", RevokeAccessCodeAsync);

        return app;
    }

    /// <summary>
    /// Oldest waiting first — the admin works a queue, not a list.
    ///
    /// No pagination: a single gym's pending queue is small, and D5 rules out the search/filter UI
    /// that would make paging meaningful.
    /// </summary>
    private static async Task<IResult> GetPendingAsync(
        IPendingMemberQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetPendingAsync(cancellationToken));

    /// <summary>
    /// Every member, or one filter position of them (FR-005). Admins ARE included since S-04 — prd-v2
    /// FR-003 needs an owner who teaches to be grantable the Trainer role, and this list is the
    /// surface that grant lives on. Nothing here is a security boundary: the only thing stopping the
    /// club from blocking its own admin is <see cref="BlockAsync"/>'s is_admin check.
    ///
    /// No pagination, for the reason GetPendingAsync gives. Search is the SPA's job — it filters the
    /// loaded rows, which is instant and costs no round-trip.
    /// </summary>
    private static async Task<IResult> GetMembersAsync(
        MemberListFilter? filter,
        IMemberQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetMembersAsync(filter, cancellationToken));

    private static async Task<IResult> GetMemberAsync(
        Guid memberId,
        IMemberQuery query,
        CancellationToken cancellationToken)
    {
        var member = await query.FindDetailAsync(memberId, cancellationToken);

        return member is null ? Results.NotFound() : Results.Ok(member);
    }

    /// <summary>
    /// Records a person who has no account (S-14, AM-001).
    ///
    /// Creates a member and NOTHING ELSE — no Identity row, no password, no invitation. The person
    /// gets a login only if they later register with an access code, and until then they exist purely
    /// as the club's own record.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        [FromBody] MemberRequest request,
        IMemberStore members,
        IMemberQuery query,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryReadRequest(request, out var displayName, out var email, out var contact, out var failure))
        {
            return failure;
        }

        // Checked before the insert so the ordinary case answers in this endpoint's own vocabulary
        // rather than as a unique-index violation. The index is still what makes it true - two admins
        // creating the same address at the same moment both pass this, and the loser gets the 500 that
        // an unhandled DbUpdateException produces. Rare enough to leave, and safe: nothing is written.
        if (email is not null && await query.EmailExistsAsync(email, null, cancellationToken))
        {
            return Results.Json(new MemberFailure("email_taken"), statusCode: 409);
        }

        var member = new Member
        {
            Id = Guid.NewGuid(),
            UserId = null,
            DisplayName = displayName,
            Email = email,
            PhoneNumber = contact?.PhoneNumber,
            Street = contact?.Street,
            HouseNumber = contact?.HouseNumber,
            PostalCode = contact?.PostalCode,
            City = contact?.City,

            // Active on creation: an admin typing someone in has vetted them by the act of typing them
            // in. See MembershipStatus for why there is no membership Pending to land in instead.
            Status = MembershipStatus.Active,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        members.Add(member);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/admin/members/{member.Id}", new { id = member.Id });
    }

    /// <summary>
    /// Edits a member's own details (S-14, AM-002).
    ///
    /// <para>
    /// APPLIES TO MEMBERS WITH ACCOUNTS TOO, and that is a real widening: before this the admin could
    /// change nobody's details, only their status and roles. It follows from the record being the
    /// club's rather than the account holder's — the same reasoning that makes the display name
    /// un-editable by the member themselves (S-13, FR-006).
    /// </para>
    ///
    /// <para>
    /// The linked account's copies are updated alongside, while both rows still carry them. Letting
    /// them diverge would mean the profile screen and the admin screen disagreeing about a phone
    /// number, with no way to tell which was right.
    /// </para>
    /// </summary>
    private static async Task<IResult> UpdateAsync(
        Guid memberId,
        [FromBody] MemberRequest request,
        IMemberStore members,
        IMemberQuery query,
        UserManager<ApplicationUser> userManager,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (!TryReadRequest(request, out var displayName, out var email, out var contact, out var failure))
        {
            return failure;
        }

        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        if (email is not null && await query.EmailExistsAsync(email, memberId, cancellationToken))
        {
            return Results.Json(new MemberFailure("email_taken"), statusCode: 409);
        }

        member.DisplayName = displayName;
        member.Email = email;
        member.PhoneNumber = contact?.PhoneNumber;
        member.Street = contact?.Street;
        member.HouseNumber = contact?.HouseNumber;
        member.PostalCode = contact?.PostalCode;
        member.City = contact?.City;
        member.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (member.UserId is not null)
        {
            var user = await userManager.FindByIdAsync(member.UserId);
            if (user is not null)
            {
                // The EMAIL IS NOT TOUCHED on the account. It is the login identifier, Identity keeps a
                // normalised copy and a uniqueness index over it, and changing it here would rename
                // somebody's username as a side effect of an admin fixing a typo in a phone number.
                // Changing a login address is its own operation and nobody has asked for it.
                user.DisplayName = displayName;
                // Only the phone, since S-14 Phase 8: the address is the member's and the account's
                // four columns go in the next release. PhoneNumber is Identity's own and survives,
                // so it is kept in step — the same rule ProfileEndpoints follows.
                user.PhoneNumber = contact?.PhoneNumber;
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
            }
        }

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new MemberFailure("conflict"), statusCode: 409);
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Approves the member's ACCOUNT (FR-003). Addressed by member, acts on the login.
    /// </summary>
    private static async Task<IResult> ApproveAsync(
        Guid memberId,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        IAccountApprovedNotification notification,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        // Nothing to approve: approval is about a login, and this person has none. Not a 404 - the
        // member exists, the action does not apply to them.
        if (member.UserId is null)
        {
            return Results.Json(new ApproveFailure("no_account"), statusCode: 409);
        }

        var user = await userManager.FindByIdAsync(member.UserId);
        if (user is null)
        {
            return Results.NotFound();
        }

        // Idempotent: two admins clicking Approve on the same row must not send two emails. The
        // second call reports success and enqueues nothing. This check alone is NOT enough when the
        // two calls overlap - see the concurrency-stamp rotation below, which closes that window.
        if (user.Status == AccountStatus.Active)
        {
            return Results.Ok();
        }

        // With Active handled above, the only status left to refuse is Blocked. Letting a blocked
        // member in through the approvals queue would be a second, quieter way to unblock — one that
        // skips the members screen entirely. POST /{id}/unblock is the action for that.
        if (user.Status != AccountStatus.Pending)
        {
            return Results.Json(new ApproveFailure("not_pending"), statusCode: 409);
        }

        user.Status = AccountStatus.Active;

        // Rotate the concurrency stamp, so the status check above is atomic rather than merely
        // logical.
        //
        // ConcurrencyStamp is a concurrency token, so EF's UPDATE carries
        // WHERE ConcurrencyStamp = <the value we read>. Nothing rotates it here on its own: this
        // handler deliberately bypasses UserManager.UpdateAsync (which normally does) to keep the
        // flip and the outbox rows inside ONE SaveChangesAsync. Without this line two admins
        // approving the same row at the same moment both read Pending, both pass the check above,
        // and both UPDATEs match - so the member is emailed twice, which is exactly what the
        // idempotency rule exists to prevent.
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        // Enqueue does NOT save (IOutboxEnqueuer), and the user entity above is tracked by the same
        // scoped DbContext that Identity uses — so the single save below writes the status flip and
        // the outbox rows in one transaction. Either the member is approved and the email is queued,
        // or neither happened.
        await notification.NotifyAsync(user, cancellationToken);

        // NO explicit transaction here, deliberately. A single SaveChangesAsync is already atomic,
        // and EnableRetryOnFailure (Program.cs) means an explicit transaction must go through
        // Database.CreateExecutionStrategy().ExecuteAsync(...) or it throws at RUNTIME.
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            // We lost the race: someone approved this member between our read and our write. They
            // enqueued the email; nothing of ours was committed, so reporting success is accurate
            // and still sends exactly one email in total.
            return Results.Ok();
        }

        // The member's cookie still carries account_status=Pending until they call
        // POST /api/auth/refresh or the security-stamp validation interval fires.
        return Results.Ok();
    }

    /// <summary>
    /// Blocks a member (FR-004, and S-14's AM-002): bar them from the club, refuse them at login if
    /// they have one, and cut the session they may already hold.
    ///
    /// <para>
    /// SINCE S-14 THE BLOCK IS ON THE MEMBERSHIP, not the account, which is what lets an accountless
    /// record be blocked at all. When there IS an account the two move together and always have to:
    /// a person barred from the club whose login still worked would reach every screen the
    /// ActiveMember policy guards.
    /// </para>
    ///
    /// <para>
    /// It rotates the account's SECURITY stamp as well, which is what actually ends a live session.
    /// Without it, a blocked member keeps a valid cookie carrying account_status=Active and sails
    /// past the policy until it happens to be re-minted. Note this is assigned directly rather than
    /// via UserManager.UpdateSecurityStampAsync, which would issue its OWN save and split the block
    /// into two writes.
    /// </para>
    ///
    /// <para>
    /// No notification. The PRD asks for no block email, and telling someone they have been blocked is
    /// a product decision nobody has made.
    /// </para>
    /// </summary>
    private static async Task<IResult> BlockAsync(
        Guid memberId,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        IBookingStore bookings,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        ApplicationUser? user = null;
        if (member.UserId is not null)
        {
            user = await userManager.FindByIdAsync(member.UserId);

            // THE ONLY THING stopping the club from locking itself out of its own app. Do not remove
            // or weaken it. The screen must not offer block on an admin row — but the screen is not
            // the boundary; this is. Checked by ROLE, so it holds if a second admin is ever seeded.
            if (user is not null && await userManager.IsInRoleAsync(user, ApplicationRoles.Admin))
            {
                return Results.Json(new BlockFailure("is_admin"), statusCode: 409);
            }
        }

        // Idempotent, like approve: a double-click must not be an error.
        if (member.Status == MembershipStatus.Blocked)
        {
            return Results.Ok();
        }

        member.Status = MembershipStatus.Blocked;
        member.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (user is not null)
        {
            // Blockable from Active AND Pending: a junk registration should be stoppable without first
            // approving it, which would be an absurd thing to make the admin do.
            user.Status = AccountStatus.Blocked;
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
            user.SecurityStamp = Guid.NewGuid().ToString();
        }

        // THE ONE STORED CASCADE IN THIS APPLICATION, and a deliberate exception to the convention
        // this file otherwise follows: access consequences are enforced at READ time by policy
        // claims, never by rewriting stored state. The exception is product-driven - a blocked member
        // cannot attend, and leaving their seats held would have the schedule promise spots to
        // nobody while other members are turned away as full.
        //
        // Queued into the SAME unit of work as the status flips, so a member is never blocked with
        // their bookings still held, nor released while still Active.
        //
        // KEYED ON THE MEMBER, so it now covers someone the admin blocked who has no account at all —
        // which is the case the whole slice exists for, and the one this call could not reach while
        // bookings were keyed on the login.
        //
        // FUTURE ONLY. Past bookings are attendance history and rewriting them would falsify it.
        //
        // NO CLASS STAMP IS ROTATED, and none is owed: cancelling only ever FREES spots, so a booker
        // racing this cascade reads a count that is conservative rather than permissive.
        await bookings.CancelActiveFutureForMemberAsync(
            member.Id, timeProvider.GetUtcNow(), cancellationToken);

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            // Unlike approve, a lost race here is NOT safe to report as success. The winner may have
            // approved this member rather than blocked them, which would leave us telling the admin
            // "blocked" about an account that is now Active. Say the view is stale and let the
            // screen refetch.
            return Results.Json(new BlockFailure("conflict"), statusCode: 409);
        }

        return Results.Ok();
    }

    /// <summary>
    /// Unblocks a member (FR-004) — return them to the club, and to their login if they have one.
    ///
    /// Deliberately does NOT rotate the security stamp. Rotation exists to destroy a session carrying
    /// a stale PERMISSIVE claim; a blocked member has no such session, because block already rotated
    /// their stamp and their claim is refused either way. There is nothing to revoke, so revoking
    /// would only sign out a member we just let back in.
    ///
    /// No approval email either: IAccountApprovedNotification fires on approve, and an account being
    /// unblocked was approved once already - a second welcome would be a lie about what happened.
    /// </summary>
    private static async Task<IResult> UnblockAsync(
        Guid memberId,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        // Idempotent, mirroring BlockAsync: a double-click must not be an error. Before S-14 this
        // branch could also mean "the account is Pending, so the action wanted is approve" and
        // answered 409 not_blocked. Membership has only two states, so that case no longer exists —
        // a member whose LOGIN is pending has an active membership and nothing to unblock, and
        // saying so with an error would be a lie about a no-op.
        if (member.Status == MembershipStatus.Active)
        {
            return Results.Ok();
        }

        member.Status = MembershipStatus.Active;
        member.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (member.UserId is not null)
        {
            var user = await userManager.FindByIdAsync(member.UserId);
            if (user is not null && user.Status == AccountStatus.Blocked)
            {
                // Always to Active, never back to Pending. A member blocked while still Pending is
                // approved by this action - accepted deliberately (S-02 planning) so that no
                // prior-status column has to exist. The members screen says so on the button.
                user.Status = AccountStatus.Active;
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
            }
        }

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new UnblockFailure("conflict"), statusCode: 409);
        }

        return Results.Ok();
    }

    /// <summary>
    /// Grant the Trainer role (FR-001). Additive: it takes nothing away, and on its own it confers
    /// nothing — S-06 consumes it to populate the instructor selection.
    ///
    /// DELIBERATELY NOT the transition shape ApproveAsync and BlockAsync use, and this is the one
    /// place on this surface that departs from it. Those two bypass UserManager and rotate the
    /// concurrency stamp by hand so a status flip and its outbox rows land in ONE SaveChangesAsync. A
    /// role change enqueues nothing, so there is no second write to bind to it — and hand-writing the
    /// UserRoles join would mean re-implementing Identity's own name normalisation.
    ///
    /// No security-stamp rotation either. The role reaches the holder's own cookie when the security
    /// stamp next validates or they call POST /api/auth/refresh; that latency is harmless while the
    /// role grants nothing.
    /// </summary>
    private static Task<IResult> GrantTrainerAsync(
        Guid memberId,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken) =>
        ChangeTrainerRoleAsync(memberId, members, userManager, grant: true, cancellationToken);

    /// <summary>
    /// Revoke the Trainer role (FR-001).
    ///
    /// Mirrors <see cref="GrantTrainerAsync"/>, including the status guard: revoking is refused on a
    /// non-active account for the same reason granting is, so the two directions cannot disagree
    /// about which accounts this surface may touch. An account that is blocked WHILE holding the
    /// role keeps it — S-06 filters the selection by status, so a blocked trainer is already
    /// unselectable there.
    /// </summary>
    private static Task<IResult> RevokeTrainerAsync(
        Guid memberId,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken) =>
        ChangeTrainerRoleAsync(memberId, members, userManager, grant: false, cancellationToken);

    /// <summary>
    /// The shared body of the two role routes. One method because the guards are identical in both
    /// directions and were already duplicated before S-14 re-keyed them; two copies of a status check
    /// is exactly how the two directions come to disagree.
    /// </summary>
    private static async Task<IResult> ChangeTrainerRoleAsync(
        Guid memberId,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        bool grant,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        // Roles live in Identity; a member with no login holds none and cannot be given one. See the
        // record's remarks - this is the case S-14 made representable and still refuses.
        if (member.UserId is null)
        {
            return Results.Json(new TrainerRoleFailure("no_account"), statusCode: 409);
        }

        var user = await userManager.FindByIdAsync(member.UserId);
        if (user is null)
        {
            return Results.NotFound();
        }

        // Idempotent, like approve and block: a double-click must not be an error, and must not
        // write. Checked before the status guard so that re-granting to an account that was
        // approved-then-blocked reports the truth — it already holds the role — rather than
        // refusing a change that would be a no-op anyway.
        var holdsRole = await userManager.IsInRoleAsync(user, ApplicationRoles.Trainer);
        if (holdsRole == grant)
        {
            return Results.Ok();
        }

        // FR-001 grants to an approved account only. Pending and Blocked are both refused: an
        // unvetted or barred account must not reach the instructor selection. Membership is checked
        // too, because since S-14 that is the status that can bar a person on its own.
        if (user.Status != AccountStatus.Active || member.Status != MembershipStatus.Active)
        {
            return Results.Json(new TrainerRoleFailure("not_active"), statusCode: 409);
        }

        var result = grant
            ? await userManager.AddToRoleAsync(user, ApplicationRoles.Trainer)
            : await userManager.RemoveFromRoleAsync(user, ApplicationRoles.Trainer);

        // A failed result here is a genuine Identity concurrency failure - typically a BlockAsync
        // that rotated the concurrency stamp underneath us - so the caller's view is stale and the
        // SPA's 409 branch refetches. Do NOT report success: the account may now be Blocked without
        // the role.
        //
        // Note this does not cover a MISSING role row: UserStore throws InvalidOperationException
        // there rather than returning a failed result, so that surfaces as a 500. AdminSeeder
        // creates the role on every start, but Program.cs deliberately swallows seeder failures, so
        // a started-but-unseeded app is the one state where that happens.
        return result.Succeeded
            ? Results.Ok()
            : Results.Json(new TrainerRoleFailure("failed"), statusCode: 409);
    }

    /// <summary>
    /// How many times generation retries after colliding with a live code.
    ///
    /// Three is plenty and is not a concurrency bound like the booking loop's: the collision it guards
    /// against is 1-in-8.5×10¹¹ per attempt, so exhausting this means the random source is broken
    /// rather than that the club is busy.
    /// </summary>
    private const int CodeAttempts = 3;

    /// <summary>
    /// Issues a member code (AM-004), replacing any outstanding one.
    ///
    /// <para>
    /// REPLACING RATHER THAN REFUSING is deliberate: the admin pressing this again is someone who
    /// could not find the code they wrote down, and making them revoke first would be ceremony. The
    /// previous code stops working the moment this one is stored, which is the honest behaviour — one
    /// live code per member, always.
    /// </para>
    /// </summary>
    private static async Task<IResult> IssueAccessCodeAsync(
        Guid memberId,
        IMemberStore members,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        // Nothing to claim. The code's entire power is "attach the account being created to this
        // member", and this member already has one.
        if (member.UserId is not null)
        {
            return Results.Json(new AccessCodeFailure("has_account"), statusCode: 409);
        }

        var expiresAt = timeProvider.GetUtcNow() + MemberAccessCode.Validity;

        for (var attempt = 1; attempt <= CodeAttempts; attempt++)
        {
            var code = MemberAccessCode.Generate();

            member.AccessCode = code;
            member.AccessCodeExpiresAt = expiresAt;
            member.ConcurrencyStamp = Guid.NewGuid().ToString();

            // TrySaveAsync, not TrySaveChangesAsync: this write is guarded by a unique index
            // (IX_Members_AccessCode) as well as a concurrency token, and the two mean different
            // things here. A unique violation is a collision worth retrying with a new code; a
            // concurrency conflict means somebody else changed this member and our view is stale.
            var outcome = await unitOfWork.TrySaveAsync(cancellationToken);

            if (outcome == SaveOutcome.Saved)
            {
                return Results.Ok(new AccessCodeView(MemberAccessCode.Format(code), expiresAt));
            }

            if (outcome == SaveOutcome.ConcurrencyConflict)
            {
                return Results.Json(new AccessCodeFailure("conflict"), statusCode: 409);
            }

            // A unique violation. Discard so the retry re-reads rather than re-sending the rejected
            // value against a now-stale token — the same reason the booking loop discards.
            unitOfWork.DiscardChanges();

            member = await members.FindAsync(memberId, cancellationToken);
            if (member is null)
            {
                return Results.NotFound();
            }
        }

        return Results.Json(new AccessCodeFailure("conflict"), statusCode: 409);
    }

    /// <summary>
    /// The member's outstanding code, or 204 when there is none.
    ///
    /// <para>
    /// AN EXPIRED CODE IS REPORTED AS NONE. It is dead either way, and showing the admin a code that
    /// will be refused is worse than showing them nothing — they would read it out and the member
    /// would fail, which is the one outcome this screen exists to prevent.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetAccessCodeAsync(
        Guid memberId,
        IMemberStore members,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        if (member.AccessCode is null
            || member.AccessCodeExpiresAt is null
            || member.AccessCodeExpiresAt <= timeProvider.GetUtcNow())
        {
            return Results.NoContent();
        }

        return Results.Ok(new AccessCodeView(
            MemberAccessCode.Format(member.AccessCode),
            member.AccessCodeExpiresAt.Value));
    }

    /// <summary>Revokes the outstanding code. Idempotent — revoking nothing is a no-op, not an error.</summary>
    private static async Task<IResult> RevokeAccessCodeAsync(
        Guid memberId,
        IMemberStore members,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        if (member.AccessCode is null)
        {
            return Results.NoContent();
        }

        // BOTH FIELDS. The expiry is meaningless without the code and leaving it behind would make a
        // revoked member look, to any future reader, like one whose code merely lapsed.
        member.AccessCode = null;
        member.AccessCodeExpiresAt = null;
        member.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new AccessCodeFailure("conflict"), statusCode: 409);
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Validates the fields shared by create and edit, in the order they appear on the form.
    ///
    /// <paramref name="contact"/> comes back null when the caller supplied none of the five fields,
    /// which is the "recorded at the desk with nothing but a name" case. Supplying SOME of them is a
    /// failure, not a partial save.
    /// </summary>
    private static bool TryReadRequest(
        MemberRequest request,
        out string displayName,
        out string? email,
        out ContactDetails? contact,
        out IResult failure)
    {
        displayName = string.Empty;
        email = null;
        contact = null;
        failure = Results.Empty;

        var trimmedName = request.DisplayName?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0 || trimmedName.Length > 100)
        {
            failure = Results.Json(new MemberFailure("invalid_display_name"), statusCode: 400);
            return false;
        }

        displayName = trimmedName;

        var trimmedEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (trimmedEmail is not null
            && (trimmedEmail.Length > 256 || !trimmedEmail.Contains('@') || trimmedEmail.Contains(' ')))
        {
            // Deliberately not a full RFC parse. The address is a way to reach someone, this endpoint
            // never sends to it, and Identity does the real validation on the path where it becomes a
            // login. What this rejects is the typo that would otherwise sit in the club's records.
            failure = Results.Json(new MemberFailure("invalid_email"), statusCode: 400);
            return false;
        }

        email = trimmedEmail;

        var supplied = new[]
        {
            request.PhoneNumber, request.Street, request.HouseNumber, request.PostalCode, request.City,
        };

        if (supplied.All(string.IsNullOrWhiteSpace))
        {
            return true;
        }

        if (!ContactDetails.TryCreate(
                request.PhoneNumber,
                request.Street,
                request.HouseNumber,
                request.PostalCode,
                request.City,
                out var details,
                out var contactFailure))
        {
            failure = Results.Json(new MemberFailure(contactFailure), statusCode: 400);
            return false;
        }

        contact = details;
        return true;
    }
}

/// <summary>
/// Narrow read seam over the member table, so Application does not reference EF Core
/// (AGENTS.md layering). Implemented in Infrastructure.
/// </summary>
public interface IPendingMemberQuery
{
    Task<IReadOnlyList<PendingMember>> GetPendingAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The same seam for the full member list (FR-005). Separate from
/// <see cref="IPendingMemberQuery"/> rather than replacing it: the approvals queue orders oldest
/// first and needs no status, this one browses alphabetically and needs everything.
/// </summary>
public interface IMemberQuery
{
    /// <param name="filter">Narrow to one filter position, or null for everyone.</param>
    Task<IReadOnlyList<MemberSummary>> GetMembersAsync(
        MemberListFilter? filter,
        CancellationToken cancellationToken);

    /// <summary>One member with the fields the edit form needs, or null.</summary>
    Task<MemberDetail?> FindDetailAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this address is already in use, by a member or by an account.
    /// </summary>
    /// <param name="exceptMemberId">
    /// The member being edited, so that saving a form without changing the address is not a
    /// collision with itself.
    /// </param>
    Task<bool> EmailExistsAsync(
        string email,
        Guid? exceptMemberId,
        CancellationToken cancellationToken);
}
