using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// A member waiting for approval, as the admin's queue sees them. This is a CONTRACT the SPA's
/// member-admin service mirrors — renaming a field breaks the approvals screen silently.
/// </summary>
public record PendingMember(string Id, string Email, string DisplayName, DateTimeOffset CreatedAt);

/// <summary>
/// A member as the admin's full list sees them (FR-005). Wider than <see cref="PendingMember"/>,
/// which stays as it is — the approvals queue does not need a status it already knows.
///
/// This is a CONTRACT the SPA's member-admin service mirrors — renaming a field breaks the members
/// screen silently.
///
/// <para>
/// Status crosses the wire as the enum NAME ("Pending" / "Active" / "Blocked"), not its int. The
/// numeric values exist for persistence stability (see <see cref="AccountStatus"/>) and are nobody
/// else's business; a badge keyed on 2 would break the day someone renumbers, which is exactly the
/// scenario that enum's comment warns about.
/// </para>
///
/// <para>
/// <see cref="Roles"/> carries the account's role NAMES as stored ("User", "Admin", "Trainer") — the
/// same reasoning as Status: a name survives a renumbering, and the screen needs to render a badge
/// per role and decide which actions a row offers. It is a list rather than a boolean because
/// admins now appear in this list, so a single is-trainer flag would immediately need a second
/// is-admin flag beside it.
/// </para>
/// </summary>
public record MemberSummary(
    string Id,
    string Email,
    string DisplayName,
    string Status,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);

public record ApproveFailure(string Reason);

/// <summary>
/// Why a block was refused. <c>is_admin</c> — the target holds the Admin role and is not a member;
/// <c>conflict</c> — someone changed the row between our read and our write, so the caller's view is
/// stale and should be refetched.
/// </summary>
public record BlockFailure(string Reason);

/// <summary>
/// Why an unblock was refused. <c>not_blocked</c> — the target is Pending, so the action wanted is
/// approve, not unblock; <c>conflict</c> — as above.
/// </summary>
public record UnblockFailure(string Reason);

/// <summary>
/// Why a Trainer-role change was refused. <c>not_active</c> — the target is Pending or Blocked, and
/// FR-001 grants the role to an approved account only; letting it through would put an unvetted
/// account into the instructor selection S-06 builds on top of this.
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
/// The admin's member surface: the approval queue (S-01), and the full member list S-02 adds on top
/// of it.
///
/// There is still no reject — FR-003 dropped it from the MVP. The PRD's open question about a
/// blocked member's existing bookings, recorded here by S-01 and reassigned during S-02's framing
/// because it asked about an aggregate that did not exist, is ANSWERED as of S-08: blocking silently
/// cancels the member's FUTURE bookings and leaves past ones alone. Unblocking restores nothing.
///
/// These are the FIRST production consumers of the Admin policy; before this it existed only for the
/// environment-guarded probes in Program.cs.
///
/// The policy name comes from Domain (AuthorizationPolicyNames), not from Infrastructure's
/// AuthorizationPolicies, so this file holds no upward reference: Application -> Domain only. The
/// builder that turns the name into an ASP.NET policy stays in Infrastructure, which is the half
/// that genuinely is infrastructure.
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
        group.MapPost("/{id}/approve", ApproveAsync);
        group.MapPost("/{id}/block", BlockAsync);
        group.MapPost("/{id}/unblock", UnblockAsync);
        group.MapPost("/{id}/roles/trainer", GrantTrainerAsync);
        group.MapDelete("/{id}/roles/trainer", RevokeTrainerAsync);

        return app;
    }

    /// <summary>
    /// Oldest waiting first — the admin works a queue, not a list.
    ///
    /// No pagination: a single gym's pending queue is small, and D5 rules out the search/filter UI
    /// that would make paging meaningful. The query is covered by the Status index in
    /// ApplicationUserConfiguration.
    /// </summary>
    private static async Task<IResult> GetPendingAsync(
        IPendingMemberQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetPendingAsync(cancellationToken));

    /// <summary>
    /// Every account, or one status of them (FR-005). Admins ARE included since S-04 — prd-v2
    /// FR-003 needs an owner who teaches to be grantable the Trainer role, and this list is the
    /// surface that grant lives on. Nothing here is a security boundary: the only thing stopping the
    /// club from blocking its own admin is <see cref="BlockAsync"/>'s is_admin check. See the note
    /// on MemberQuery before assuming otherwise.
    ///
    /// <paramref name="status"/> binds as a nullable enum, so an unparseable value is a 400 from the
    /// framework's binding rather than a silent fall-through to "no filter". That distinction
    /// matters: a typo in the SPA must surface as a broken request, not as the admin quietly being
    /// shown everyone when they asked for the blocked.
    ///
    /// No pagination, for the reason GetPendingAsync gives: a single gym's list is small. Search is
    /// the SPA's job — it filters the loaded rows, which is instant and costs no round-trip.
    /// </summary>
    private static async Task<IResult> GetMembersAsync(
        AccountStatus? status,
        IMemberQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetMembersAsync(status, cancellationToken));

    private static async Task<IResult> ApproveAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAccountApprovedNotification notification,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id);
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
        // Database.CreateExecutionStrategy().ExecuteAsync(...) or it throws at RUNTIME. If a later
        // edit genuinely needs multiple saves in one transaction, that is the rule to follow.
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            // We lost the race: someone approved this member between our read and our write. They
            // enqueued the email; nothing of ours was committed, so reporting success is accurate
            // and still sends exactly one email in total.
            return Results.Ok();
        }

        // The member's cookie still carries account_status=Pending until they call
        // POST /api/auth/refresh or the security-stamp validation interval fires. That is why that
        // endpoint exists; see AuthEndpoints.RefreshAsync.
        return Results.Ok();
    }

    /// <summary>
    /// Block a member (FR-004): refuse them at login, and cut the session they may already hold.
    ///
    /// Follows ApproveAsync's transition shape — idempotency check, manual concurrency-stamp
    /// rotation, one save — for the same reasons documented there. Two differences:
    ///
    /// 1. It rotates the SECURITY stamp as well, which is what actually ends a live session. F-02
    ///    deferred this obligation here by name (auth-identity-foundation plan, D-notes): without
    ///    it, a blocked member keeps a valid cookie carrying account_status=Active and sails past
    ///    the ActiveMember policy until it happens to be re-minted. Note this is assigned directly
    ///    rather than via UserManager.UpdateSecurityStampAsync, which would issue its OWN save and
    ///    split the block into two writes - the exact thing ApproveAsync bypasses UpdateAsync to
    ///    avoid.
    /// 2. No notification. The PRD asks for no block email, and telling someone they have been
    ///    blocked is a product decision nobody has made.
    ///
    /// SINCE S-08 IT ALSO RELEASES THEIR FUTURE SPOTS — see the cascade comment in the body. That
    /// answers the PRD open question this file recorded as reassigned, and it is the only place in
    /// the application where a status change rewrites another aggregate.
    /// </summary>
    private static async Task<IResult> BlockAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IBookingStore bookings,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        // THE ONLY THING stopping the club from locking itself out of its own app. Do not remove or
        // weaken it. This used to be the second of two layers - MemberQuery also excluded admins
        // structurally - but S-04 lifted that exclusion so the Trainer role could be granted to an
        // owner who teaches (prd-v2 FR-003). The screen hides block on an admin row, but a screen is
        // not a boundary and a hand-made request must still be refused here. Checked by ROLE, so it
        // holds if a second admin is ever seeded.
        if (await userManager.IsInRoleAsync(user, ApplicationRoles.Admin))
        {
            return Results.Json(new BlockFailure("is_admin"), statusCode: 409);
        }

        // Idempotent, like approve: a double-click must not be an error.
        if (user.Status == AccountStatus.Blocked)
        {
            return Results.Ok();
        }

        // Blockable from Active AND Pending: a junk registration should be stoppable without first
        // approving it, which would be an absurd thing to make the admin do.
        user.Status = AccountStatus.Blocked;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        user.SecurityStamp = Guid.NewGuid().ToString();

        // THE ONE STORED CASCADE IN THIS APPLICATION, and a deliberate exception to the convention
        // this file otherwise follows: access consequences are enforced at READ time by policy
        // claims, never by rewriting stored state. The exception is product-driven - a blocked member
        // cannot attend, and leaving their seats held would have the schedule promise spots to
        // nobody while other members are turned away as full.
        //
        // Queued into the SAME unit of work as the status flip, so a member is never blocked with
        // their bookings still held, nor released while still Active.
        //
        // FUTURE ONLY. Past bookings are attendance history and rewriting them would falsify it.
        //
        // NO CLASS STAMP IS ROTATED, and none is owed: cancelling only ever FREES spots, so a booker
        // racing this cascade reads a count that is conservative rather than permissive - the worst
        // it can do is refuse a spot that had just come free. That makes this the ONE writer against
        // booking counts that stands outside the stamp protocol, which is why BookAsync's and
        // CancelMineAsync's "exact" free-spot answers qualify themselves against it: a cascade in
        // their window leaves them understating the spots available, and understating is the
        // direction that cannot overbook. Both consequences resolve on the member's next read.
        //
        // NO NOTIFICATION, consistent with the block itself sending none.
        await bookings.CancelActiveFutureForMemberAsync(
            user.Id, timeProvider.GetUtcNow(), cancellationToken);

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
    /// Unblock a member (FR-004) — return them to Active.
    ///
    /// Deliberately does NOT rotate the security stamp. Rotation exists to destroy a session
    /// carrying a stale PERMISSIVE claim; a blocked member has no such session, because block
    /// already rotated their stamp and their claim is refused either way. There is nothing to
    /// revoke, so revoking would only sign out a member we just let back in.
    ///
    /// No approval email either: IAccountApprovedNotification fires on approve, and an account being
    /// unblocked was approved once already - a second welcome would be a lie about what happened.
    /// </summary>
    private static async Task<IResult> UnblockAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Status == AccountStatus.Active)
        {
            return Results.Ok();
        }

        // Pending is not unblockable - it was never blocked. Approve is the action, and routing it
        // here would let unblock silently double as approval for an account nobody has vetted.
        if (user.Status != AccountStatus.Blocked)
        {
            return Results.Json(new UnblockFailure("not_blocked"), statusCode: 409);
        }

        // Always to Active, never back to Pending. A member blocked while still Pending is approved
        // by this action - accepted deliberately (S-02 planning) so that no prior-status column has
        // to exist. The members screen says so on the button.
        user.Status = AccountStatus.Active;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

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
    /// concurrency stamp by hand so a status flip and its outbox rows land in ONE
    /// SaveChangesAsync. A role change enqueues nothing, so there is no second write to bind to it
    /// — and hand-writing the UserRoles join would mean re-implementing Identity's own name
    /// normalisation, which MemberQuery already has to be careful to agree with.
    ///
    /// No security-stamp rotation either. The role reaches the holder's own cookie when the
    /// security stamp next validates or they call POST /api/auth/refresh; that latency is harmless
    /// while the role grants nothing. A later slice that gives Trainer real permissions must
    /// revisit revocation timing — see the plan's Open Risks.
    /// </summary>
    private static async Task<IResult> GrantTrainerAsync(
        string id,
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        // Idempotent, like approve and block: a double-click must not be an error, and must not
        // write. Checked before the status guard so that re-granting to an account that was
        // approved-then-blocked reports the truth — it already holds the role — rather than
        // refusing a change that would be a no-op anyway.
        if (await userManager.IsInRoleAsync(user, ApplicationRoles.Trainer))
        {
            return Results.Ok();
        }

        // FR-001 grants to an approved account only. Pending and Blocked are both refused: an
        // unvetted or barred account must not reach the instructor selection.
        if (user.Status != AccountStatus.Active)
        {
            return Results.Json(new TrainerRoleFailure("not_active"), statusCode: 409);
        }

        var result = await userManager.AddToRoleAsync(user, ApplicationRoles.Trainer);

        // A failed result here is a genuine Identity concurrency failure - typically a BlockAsync
        // that rotated the concurrency stamp underneath us - so the caller's view is stale and the
        // SPA's 409 branch refetches. Do NOT report success: the account may now be Blocked without
        // the role.
        //
        // Note this does not cover a MISSING role row: UserStore throws InvalidOperationException
        // there rather than returning a failed result, so that surfaces as a 500. AdminSeeder
        // creates the role on every start, but Program.cs deliberately swallows seeder failures, so
        // a started-but-unseeded app is the one state where that happens.
        return result.Succeeded ? Results.Ok() : Results.Json(new TrainerRoleFailure("failed"), statusCode: 409);
    }

    /// <summary>
    /// Revoke the Trainer role (FR-001).
    ///
    /// Mirrors <see cref="GrantTrainerAsync"/>, including the status guard: revoking is refused on a
    /// non-active account for the same reason granting is, so the two directions cannot disagree
    /// about which accounts this surface may touch. An account that is blocked WHILE holding the
    /// role keeps it — S-06 filters the selection by status, so a blocked trainer is already
    /// unselectable there.
    ///
    /// Does not rotate the security stamp. Block rotates because it must destroy a session carrying
    /// a stale permissive claim; this role carries no permission to destroy.
    /// </summary>
    private static async Task<IResult> RevokeTrainerAsync(
        string id,
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (!await userManager.IsInRoleAsync(user, ApplicationRoles.Trainer))
        {
            return Results.Ok();
        }

        if (user.Status != AccountStatus.Active)
        {
            return Results.Json(new TrainerRoleFailure("not_active"), statusCode: 409);
        }

        var result = await userManager.RemoveFromRoleAsync(user, ApplicationRoles.Trainer);

        return result.Succeeded ? Results.Ok() : Results.Json(new TrainerRoleFailure("failed"), statusCode: 409);
    }
}

/// <summary>
/// Narrow read seam over the user table, so Application does not reference EF Core
/// (AGENTS.md layering). Implemented in Infrastructure.
/// </summary>
public interface IPendingMemberQuery
{
    Task<IReadOnlyList<PendingMember>> GetPendingAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The same seam for the full member list (FR-005). Separate from
/// <see cref="IPendingMemberQuery"/> rather than replacing it: the approvals queue orders oldest
/// first and needs no status, this one browses alphabetically and needs nothing else.
/// </summary>
public interface IMemberQuery
{
    /// <param name="status">
    /// Narrow to one status, or null for every member. Applied in SQL against the Status index.
    /// </param>
    Task<IReadOnlyList<MemberSummary>> GetMembersAsync(
        AccountStatus? status,
        CancellationToken cancellationToken);
}
