using System.Security.Claims;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// A group's fixed roster with each member's gaps (S-37).
/// </summary>
public static class GetGroupRoster
{
    public static async Task<IResult> HandleAsync(
        Guid groupId,
        ClaimsPrincipal principal,
        IClassGroupStore groups,
        IGroupRosterStore roster,
        IGroupRosterQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var (failure, _) = await RosterAuthorization.AuthorizeAsync(
            principal, groupId, groups, roster, timeProvider, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var view = await query.GetViewAsync(
            groupId, timeProvider.GetUtcNow(), RosterAuthorization.ActingInstructorId(principal), cancellationToken);

        return view is null ? Results.NotFound() : Results.Ok(view);
    }
}
