using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Granting and revoking the Trainer role (FR-001).
///
/// <para>
/// ONE FILE BEHIND TWO ROUTES, deliberately. The two directions share a body because the
/// guards are identical in both, and two copies of a status check is exactly how they come
/// to disagree. Splitting this into a file per route would have duplicated that body - a
/// behaviour change wearing the clothes of a reorganisation.
/// </para>
/// </summary>
public static class ChangeTrainerRole
{
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
    public static Task<IResult> GrantAsync(
        Guid memberId,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken) =>
        ChangeAsync(memberId, members, userManager, grant: true, cancellationToken);

    /// <summary>
    /// Revoke the Trainer role (FR-001).
    ///
    /// Mirrors <see cref="GrantTrainerAsync"/>, including the status guard: revoking is refused on a
    /// non-active account for the same reason granting is, so the two directions cannot disagree
    /// about which accounts this surface may touch. An account that is blocked WHILE holding the
    /// role keeps it — S-06 filters the selection by status, so a blocked trainer is already
    /// unselectable there.
    /// </summary>
    public static Task<IResult> RevokeAsync(
        Guid memberId,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken) =>
        ChangeAsync(memberId, members, userManager, grant: false, cancellationToken);

    /// <summary>
    /// The shared body of the two role routes. One method because the guards are identical in both
    /// directions and were already duplicated before S-14 re-keyed them; two copies of a status check
    /// is exactly how the two directions come to disagree.
    /// </summary>
    public static async Task<IResult> ChangeAsync(
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
}
