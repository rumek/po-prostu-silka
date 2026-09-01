using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// A member waiting for approval, as the admin's queue sees them. This is a CONTRACT the SPA's
/// member-admin service mirrors — renaming a field breaks the approvals screen silently.
/// </summary>
public record PendingMember(string Id, string Email, string DisplayName, DateTimeOffset CreatedAt);

public record ApproveFailure(string Reason);

/// <summary>
/// The admin's approval surface: see who is waiting, let one of them in.
///
/// Deliberately only those two operations (S-01 decision D5). There is no reject — FR-003 dropped it
/// from the MVP — and no block/unblock or full member list, which S-02 owns and which is blocked on
/// the PRD's open question about a blocked member's existing bookings.
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
        group.MapPost("/{id}/approve", ApproveAsync);

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

        // Approving a blocked member is S-02's unblock, not this endpoint — it would have to answer
        // what happens to their old bookings, which is still an open PRD question.
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
        // POST /api/auth/refresh or the 30-minute security-stamp validation fires. That is why that
        // endpoint exists; see AuthEndpoints.RefreshAsync.
        return Results.Ok();
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
