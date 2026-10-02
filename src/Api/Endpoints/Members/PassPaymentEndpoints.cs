using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Api.Endpoints.Members;

/// <summary>
/// Marking a karnet paid or unpaid (pass-paid-flag).
///
/// <para>
/// NOT IN THE ADMIN PASS GROUP, because its policy differs: a trainer may record a payment but must not
/// reach the admin's issue / edit / revoke routes. Addressed by the PASS alone rather than nested under
/// the member — the trainer's screen and the admin's both hold the pass id, and the handler resolves
/// the member from the pass, so there is no second id to disagree with. <c>{passId:guid}</c> keeps it
/// clear of <c>/api/passes/mine</c>, which lives in its own MemberOnly group.
/// </para>
/// </summary>
public static class PassPaymentEndpoints
{
    public static IEndpointRouteBuilder MapPassPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/passes")
            .WithTags("MembershipPasses")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        group.MapPut("/{passId:guid}/paid", SetPassPaid.HandleAsync);

        return app;
    }
}
