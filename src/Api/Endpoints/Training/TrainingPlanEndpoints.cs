using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;
using po_prostu_silka.Application.Training;

namespace po_prostu_silka.Api.Endpoints.Training;

/// <summary>
/// Training-plan authoring (prd.md FR-015, FR-016) - the write half of S-11.
///
/// <para>
/// TRAINER OR ADMIN, with the policy applied at the GROUP as everywhere else in this codebase. This
/// is the first surface the Trainer role can reach, which retires prd-v2's "No trainer screen"
/// Non-Goal and answers its Open Question 3. prd.md FR-015 named only the admin; the rule was widened
/// rather than moved, so an admin who does not teach keeps every capability the PRD gave them.
/// </para>
///
/// <para>
/// THERE IS NO OWNERSHIP RULE. Any trainer may assign to any active member and edit any plan. This
/// product has no trainer-to-member relationship to enforce one against, and inventing one here would
/// be a data model nothing else uses. <see cref="TrainingPlan.AssignedByMemberId"/> is recorded for
/// display, not for authorization - see its doc comment.
/// </para>
///
/// <para>
/// NO DELETE. Assignment archives the plan it replaces (FR-016), and nothing removes a plan, matching
/// every other aggregate here.
/// </para>
///
/// <para>
/// NO LIST AND NO MEMBER PICKER since S-22. <c>GET /</c> (every active plan) and <c>GET /members</c>
/// (the picker) were retired with the Plany screen: a plan is reached through its member now, and the
/// reads moved to <c>/api/trainer/members</c> (TrainerMemberEndpoints). Writes stay here, addressed by
/// plan id, so a stale tab editing a plan that was replaced underneath it gets a 404 rather than
/// overwriting the new one.
/// </para>
/// </summary>
public static class TrainingPlanEndpoints
{













    public static IEndpointRouteBuilder MapTrainingPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var plans = app.MapGroup("/api/trainer/plans")
            .WithTags("Training")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        plans.MapGet("/{id:guid}", GetTrainingPlan.HandleAsync);
        plans.MapPost("/", CreateTrainingPlan.HandleAsync);
        plans.MapPut("/{id:guid}", UpdateTrainingPlan.HandleAsync);

        return app;
    }
}
