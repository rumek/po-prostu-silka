using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;
using po_prostu_silka.Application.Training;

namespace po_prostu_silka.Api.Endpoints.Training;

/// <summary>
/// The admin's exercise library (prd.md FR-018, FR-019) — the first surface in the training context
/// and the vocabulary S-11's training plans will be assembled from.
///
/// <para>
/// TWO GROUPS OVER ONE PATH SINCE S-11, each with its own policy applied at the GROUP rather than
/// per endpoint, so an endpoint added later cannot accidentally ship unauthenticated. Reading is
/// TrainerOrAdmin - a trainer assembles plans from this library and has to see what is in it.
/// Writing stays Admin: FR-018 makes curating the library the admin's job, and S-11 widened who may
/// read it without widening who may change it.
/// </para>
///
/// <para>
/// MEMBERS STILL NEVER READ THIS SURFACE. FR-020 reaches an exercise from inside an assigned plan,
/// which MyPlanEndpoints serves under MemberOnly by joining through the member's own plan; prd.md
/// cuts standalone library browsing for members, and that is enforced by there being no route here
/// they can reach.
/// </para>
///
/// <para>
/// NO DELETE, by design — deactivation instead, for the reason FR-006 gives class types and which
/// applies harder here: S-11's plans will reference exercises, so a deleted row would either orphan
/// a plan or be blocked by a foreign key.
/// </para>
/// </summary>
public static class ExerciseEndpoints
{









    public static IEndpointRouteBuilder MapExerciseEndpoints(this IEndpointRouteBuilder app)
    {
        // TWO GROUPS ON ONE PATH, split by policy rather than by resource. S-11 gave trainers plan
        // authoring, and a plan is assembled from this library - so a trainer must be able to READ it
        // while the library itself stays an admin's to curate (prd.md FR-018).
        //
        // The split is what keeps the repo's rule intact: ONE POLICY PER GROUP, applied at the
        // MapGroup and never per route. Hanging a second RequireAuthorization off individual routes
        // inside one group would have been fewer lines and would have made this the only surface where
        // a reader has to check each route to know who may call it - and the EveryRoute test theory
        // would no longer be able to assert one answer per group.
        var read = app.MapGroup("/api/admin/exercises")
            .WithTags("Training")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        read.MapGet("/", GetExercises.HandleAsync);
        read.MapGet("/{id:guid}", GetExercise.HandleAsync);

        var admin = app.MapGroup("/api/admin/exercises")
            .WithTags("Training")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        admin.MapPost("/", CreateExercise.HandleAsync);
        admin.MapPut("/{id:guid}", UpdateExercise.HandleAsync);

        // Two verbs rather than a boolean on the edit payload, matching the class-type surface: the
        // action the admin took is legible in the request, and an edit cannot perform it by accident.
        admin.MapPost("/{id:guid}/deactivate", DeactivateExercise.HandleAsync);
        admin.MapPost("/{id:guid}/activate", ActivateExercise.HandleAsync);

        return app;
    }
}
