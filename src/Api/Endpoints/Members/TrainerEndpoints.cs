using po_prostu_silka.Domain;
using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Api.Endpoints.Members;

/// <summary>
/// The people an occurrence may name as its instructor (prd-v2 FR-009).
///
/// <para>
/// A SEPARATE GROUP FROM MemberAdminEndpoints, on the same policy. The two are about the same table
/// but answer different questions: that one manages accounts, this one populates a selection in the
/// scheduling context. Keeping them apart is what lets S-07 consume this without inheriting the
/// member list's shape.
/// </para>
///
/// <para>
/// Admin-only, with the policy applied at the GROUP rather than per endpoint, so an endpoint added
/// here later cannot accidentally ship unauthenticated. Members never read this: a trainer reaches
/// them as a resolved display name on the schedule, never as a list to choose from.
/// </para>
///
/// <para>
/// There is no write surface here. Granting and revoking the Trainer role stays on
/// <see cref="MemberAdminEndpoints"/>, where the account lives; this slice consumes the role and
/// does not change how it is given.
/// </para>
/// </summary>
public static class TrainerEndpoints
{
    public static IEndpointRouteBuilder MapTrainerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/trainers")
            .WithTags("Members")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        group.MapGet("/", GetTrainers.HandleAsync);

        return app;
    }
}
