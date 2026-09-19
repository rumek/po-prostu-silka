using System.Security.Claims;
using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// The caller's active plan, or 204 when they have none.
///
/// <para>
/// 204 RATHER THAN 404, deliberately. Having no plan yet is an ordinary state for a member who
/// has just been approved - most members are in it - and a 404 would make the SPA guess whether
/// the request failed or the answer is "nothing". The screen renders an empty card for 204 and an
/// error with a retry button for anything else, and it can only tell those apart if the API does.
/// </para>
///
/// <para>
/// DROPPED AN INJECTED UserManager IN S-18. It was bound on every request and never read: the
/// caller is resolved from the principal's member claim, not from the account row.
/// </para>
/// </summary>
public static class GetMyPlan
{
    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        ITrainingPlanQuery query,
        CancellationToken cancellationToken)
    {
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        var plan = await query.FindActiveForMemberAsync(memberId.Value, cancellationToken);

        return plan is null ? Results.NoContent() : Results.Ok(plan);
    }
}
