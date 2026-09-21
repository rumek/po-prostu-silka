using po_prostu_silka.Application.Training;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Api.Endpoints.Training;

/// <summary>
/// The trainer's way into a member's plan (S-22, UX-07/UX-08): a member list of their own, and one
/// member's plan addressed by the member.
///
/// <para>
/// TRAINER OR ADMIN, with the policy on the GROUP. Admins pass it too, which is what lets one API
/// serve both SPA mounts of the plan screen — <c>/admin/members/:id/plan</c> and
/// <c>/trainer/members/:id/plan</c>.
/// </para>
///
/// <para>
/// WHY THIS IS NOT <c>/api/admin/members</c> LOOSENED. That group carries the Admin policy as a whole,
/// and its rows carry e-mail, account id, roles and statuses. Opening it to trainers would hand every
/// trainer the club's contact list along with every write route on the group. The precedent is a
/// separate group with a minimized projection — <c>/api/trainer/plans/members</c> before this — so
/// that is what this is.
/// </para>
///
/// <para>
/// WHY THE SEARCH IS NAME-ONLY. The admin's search matches the e-mail too. Here it would be an e-mail
/// oracle: a trainer could ask "does anyone's address contain x" and read the answer from the result
/// count, without a single address ever being returned. <c>TrainerMemberEndpointTests</c> pins that a
/// phrase found only in an e-mail matches nobody.
/// </para>
/// </summary>
public static class TrainerMemberEndpoints
{
    public static IEndpointRouteBuilder MapTrainerMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup("/api/trainer/members")
            .WithTags("Training")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        members.MapGet("/", GetTrainerMembers.HandleAsync);
        members.MapGet("/{memberId:guid}/plan", GetMemberPlan.HandleAsync);

        return app;
    }
}
