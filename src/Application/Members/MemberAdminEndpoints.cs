using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Authorization;

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
/// LAYERING NOTE: this file names AuthorizationPolicies, which lives in Infrastructure — the one
/// upward reference in Application. The policy NAMES are an application-level contract (the file
/// says so itself); only the builder that turns them into ASP.NET policies is infrastructure. The
/// alternative is a bare "Admin" string literal, which is exactly the typo-that-never-matches the
/// constants exist to prevent. If Application ever grows a second such reference, the fix is to move
/// the name constants down into Domain and leave AddApplicationPolicies behind. AGENTS.md's hard
/// rule — EF Core only in Infrastructure — is not affected: the two seams below keep it.
/// </summary>
public static class MemberAdminEndpoints
{
    public static IEndpointRouteBuilder MapMemberAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // The policy is applied at the GROUP, not per endpoint: an endpoint added here later cannot
        // accidentally ship unauthenticated. Admin implies Active, so a pending admin is refused too.
        var group = app.MapGroup("/api/admin/members")
            .WithTags("Members")
            .RequireAuthorization(AuthorizationPolicies.Admin);

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
        // second call reports success and enqueues nothing.
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

        // Enqueue does NOT save (IOutboxEnqueuer), and the user entity above is tracked by the same
        // scoped DbContext that Identity uses — so the single SaveChangesAsync below writes the
        // status flip and the outbox rows in one transaction. Either the member is approved and the
        // email is queued, or neither happened.
        await notification.NotifyAsync(user, cancellationToken);

        // NO explicit transaction here, deliberately. A single SaveChangesAsync is already atomic,
        // and EnableRetryOnFailure (Program.cs) means an explicit transaction must go through
        // Database.CreateExecutionStrategy().ExecuteAsync(...) or it throws at RUNTIME. If a later
        // edit genuinely needs multiple saves in one transaction, that is the rule to follow.
        await unitOfWork.SaveChangesAsync(cancellationToken);

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
