using System.Security.Claims;
using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// One exercise's full details, but only if the caller's active plan prescribes it (FR-020).
///
/// <para>
/// An exercise that exists but is not in the caller's plan is a 404, the same answer as one that
/// does not exist at all. That is intentional: distinguishing them would turn this route into an
/// oracle for enumerating the library one guid at a time, which is the browsing the PRD cut.
/// </para>
///
/// <para>
/// A deactivated exercise still resolves here, because the plan still shows it - see
/// ITrainingPlanQuery.FindPlanExerciseAsync and TrainingPlanItem.Exercise for why the read path
/// does not filter on IsActive.
/// </para>
///
/// <para>
/// DROPPED AN INJECTED UserManager IN S-18, for the same reason as <see cref="GetMyPlan"/>.
/// </para>
/// </summary>
public static class GetMyPlanExercise
{
    public static async Task<IResult> HandleAsync(
        Guid exerciseId,
        ClaimsPrincipal principal,
        ITrainingPlanQuery query,
        CancellationToken cancellationToken)
    {
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        var exercise = await query.FindPlanExerciseAsync(memberId.Value, exerciseId, cancellationToken);

        return exercise is null ? Results.NotFound() : Results.Ok(exercise);
    }
}
