using po_prostu_silka.Domain;
using po_prostu_silka.Application.Training;

namespace po_prostu_silka.Api.Endpoints.Training;

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

        mine.MapGet("/mine", GetMyPlan.HandleAsync);
        mine.MapGet("/mine/exercises/{exerciseId:guid}", GetMyPlanExercise.HandleAsync);

        return app;
    }
}
