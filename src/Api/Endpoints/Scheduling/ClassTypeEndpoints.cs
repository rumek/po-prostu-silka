using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Application.Scheduling;

namespace po_prostu_silka.Api.Endpoints.Scheduling;

/// <summary>
/// The admin's class-type definitions (prd-v2 FR-004, FR-005, FR-006, FR-007) — the layer that gives
/// a class an identity outliving any single week.
///
/// <para>
/// Admin-only, with the policy applied at the GROUP rather than per endpoint, so an endpoint added
/// here later cannot accidentally ship unauthenticated. Members never read this surface: a class
/// type reaches them through the occurrence, which is S-06's work.
/// </para>
///
/// <para>
/// NO DELETE, by design. FR-006 replaces deletion with deactivation: a retired type disappears from
/// every selection while the occurrences referencing it stay intact. An orphaned occurrence is a
/// worse failure than a hidden row.
/// </para>
///
/// <para>
/// S-05 defines and manages types. It does NOT wire them into occurrence creation — no selector, no
/// prefill, no name resolution. That is S-06.
/// </para>
/// </summary>
public static class ClassTypeEndpoints
{






    public static IEndpointRouteBuilder MapClassTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/class-types")
            .WithTags("Schedule")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        admin.MapGet("/", GetClassTypes.HandleAsync);
        admin.MapGet("/{id:guid}", GetClassType.HandleAsync);
        admin.MapPost("/", CreateClassType.HandleAsync);
        admin.MapPut("/{id:guid}", UpdateClassType.HandleAsync);

        // Two verbs rather than a boolean on the edit payload, for the same reason the member
        // surface exposes block/unblock instead of a status patch: the action the admin took is
        // legible in the request, and an edit cannot perform it by accident.
        admin.MapPost("/{id:guid}/deactivate", DeactivateClassType.HandleAsync);
        admin.MapPost("/{id:guid}/activate", ActivateClassType.HandleAsync);

        return app;
    }
}
