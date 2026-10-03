using System.Security.Claims;
using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The trainer's "Grupy" (S-37): the groups whose upcoming classes they instruct.
/// </summary>
public static class GetTrainerGroups
{
    /// <summary>
    /// Narrowed for every caller, an admin included - an admin manages rosters from the group list, and
    /// this is the instructor's question. Fails closed, as GetInstructedClasses does.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        IGroupRosterQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Ok(Array.Empty<TrainerGroup>());
        }

        return Results.Ok(await query.GetTrainerGroupsAsync(memberId.Value, timeProvider.GetUtcNow(), cancellationToken));
    }
}
