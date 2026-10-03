using System.Security.Claims;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The resource check for roster routes (S-37), beside <see cref="BookingAuthorization"/>.
///
/// <para>
/// THE GROUP POLICY ALONE IS NOT SUFFICIENT, for the reason it is not on the staff booking routes:
/// TrainerOrAdmin admits every trainer, and a trainer manages only a group they instruct. "Instruct" is
/// derived - a group has no fixed trainer - as "instructs at least one upcoming, non-cancelled class of
/// it", so a substitute gains access exactly while they substitute. The trainer's group list
/// (<see cref="IGroupRosterQuery.GetTrainerGroupsAsync"/>) uses the same rule, so list and access
/// cannot drift.
/// </para>
///
/// <para>
/// MANAGING THE ROSTER IS NOT BOOKING EVERY CLASS. Even a trainer who may manage the roster books and
/// releases only on the classes they instruct (<see cref="ActingInstructorId"/>), as S-16 requires.
/// </para>
/// </summary>
public static class RosterAuthorization
{
    /// <summary>Admin always; anyone else when they instruct an upcoming class of the group.</summary>
    public static async Task<bool> MayManageAsync(
        ClaimsPrincipal principal,
        Guid groupId,
        IGroupRosterStore roster,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (principal.IsInRole(ApplicationRoles.Admin))
        {
            return true;
        }

        var memberId = principal.GetMemberId();
        return memberId is not null
               && await roster.IsInstructorOfUpcomingAsync(
                   groupId, memberId.Value, timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <summary>
    /// 404 for an unknown group, 403 for a known group the caller may not manage - revealing that a
    /// group id exists costs nothing (group names are on the schedule), the reasoning of
    /// <see cref="BookingAuthorization.NotYourClass"/>. Null failure means go ahead.
    /// </summary>
    public static async Task<(IResult? Failure, ClassGroup? Group)> AuthorizeAsync(
        ClaimsPrincipal principal,
        Guid groupId,
        IClassGroupStore groups,
        IGroupRosterStore roster,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var group = await groups.FindAsync(groupId, cancellationToken);
        if (group is null)
        {
            return (Results.NotFound(), null);
        }

        if (!await MayManageAsync(principal, groupId, roster, timeProvider, cancellationToken))
        {
            return (Results.Forbid(), null);
        }

        return (null, group);
    }

    /// <summary>
    /// Null for an admin (who books anyone into anything); the caller's member id otherwise, narrowing
    /// every booking and release the roster makes to the classes they instruct.
    /// </summary>
    public static Guid? ActingInstructorId(ClaimsPrincipal principal) =>
        principal.IsInRole(ApplicationRoles.Admin) ? null : principal.GetMemberId();
}
