using System.Security.Claims;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// "Uzupełnij zapisy" (S-37): retries every gap of the roster.
/// </summary>
public static class SyncRoster
{
    /// <summary>
    /// Idempotent: a member already booked is the protocol's <c>already_booked</c>, which the batch
    /// drops, so a second run books nothing and reports only what still refuses.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid groupId,
        ClaimsPrincipal principal,
        IClassGroupStore groups,
        IGroupRosterStore roster,
        IGroupRosterQuery query,
        RosterBooking rosterBooking,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var (failure, _) = await RosterAuthorization.AuthorizeAsync(
            principal, groupId, groups, roster, timeProvider, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var acting = RosterAuthorization.ActingInstructorId(principal);
        var memberIds = await roster.MemberIdsAsync(groupId, cancellationToken);
        var classes = await roster.UpcomingClassesAsync(groupId, timeProvider.GetUtcNow(), cancellationToken);

        var report = await rosterBooking.BookAsync(memberIds, classes, acting, cancellationToken);

        var view = await query.GetViewAsync(groupId, timeProvider.GetUtcNow(), acting, cancellationToken);
        return Results.Ok(new RosterChange(view!, report));
    }
}
