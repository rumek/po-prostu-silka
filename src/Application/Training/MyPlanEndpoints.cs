using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// The member's own training plan (prd.md FR-017, FR-020) - the read half of S-11.
///
/// <para>
/// EVERY ROUTE IS SCOPED TO THE CALLER, and the scoping is a filter on the authenticated principal's
/// id rather than a check on an id the request supplied. No route here takes a member id at all,
/// which is the strongest form of "a member cannot read another member's plan": there is no
/// parameter to tamper with.
/// </para>
///
/// <para>
/// SEPARATE FROM TrainingPlanEndpoints on purpose, rather than a few extra routes on that group. The
/// two surfaces answer to different policies - ActiveMember here, TrainerOrAdmin there - and this
/// codebase applies one policy per group. Splitting the file follows the split in the group.
/// </para>
///
/// <para>
/// The exercise route is the mechanism behind prd.md:163's Non-Goal "no standalone exercise library
/// browsing". A member reaches an exercise only through their own plan, so the library is not a thing
/// they can enumerate.
/// </para>
/// </summary>
public static class MyPlanEndpoints
{
    public static IEndpointRouteBuilder MapMyPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/plans")
            .WithTags("Training")
            .RequireAuthorization(AuthorizationPolicyNames.ActiveMember);

        mine.MapGet("/mine", GetMineAsync);
        mine.MapGet("/mine/exercises/{exerciseId:guid}", GetMyExerciseAsync);

        return app;
    }

    /// <summary>
    /// The caller's active plan, or 204 when they have none.
    ///
    /// <para>
    /// 204 RATHER THAN 404, deliberately. Having no plan yet is an ordinary state for a member who
    /// has just been approved - most members are in it - and a 404 would make the SPA guess whether
    /// the request failed or the answer is "nothing". The screen renders an empty card for 204 and an
    /// error with a retry button for anything else, and it can only tell those apart if the API does.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetMineAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ITrainingPlanQuery query,
        CancellationToken cancellationToken)
    {
        var memberUserId = userManager.GetUserId(principal);
        if (memberUserId is null)
        {
            return Results.Unauthorized();
        }

        var plan = await query.FindActiveForMemberAsync(memberUserId, cancellationToken);

        return plan is null ? Results.NoContent() : Results.Ok(plan);
    }

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
    /// </summary>
    private static async Task<IResult> GetMyExerciseAsync(
        Guid exerciseId,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ITrainingPlanQuery query,
        CancellationToken cancellationToken)
    {
        var memberUserId = userManager.GetUserId(principal);
        if (memberUserId is null)
        {
            return Results.Unauthorized();
        }

        var exercise = await query.FindPlanExerciseAsync(memberUserId, exerciseId, cancellationToken);

        return exercise is null ? Results.NotFound() : Results.Ok(exercise);
    }
}
