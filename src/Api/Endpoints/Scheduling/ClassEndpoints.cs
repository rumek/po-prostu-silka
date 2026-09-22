using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Application.Scheduling;

namespace po_prostu_silka.Api.Endpoints.Scheduling;

/// <summary>
/// The class schedule (prd.md FR-007) and the admin's management of it (prd-v2 US-01, FR-008 –
/// FR-013).
///
/// Three groups with different policies, applied at each GROUP rather than per endpoint, so an
/// endpoint added here later cannot accidentally ship unauthenticated:
/// <list type="bullet">
/// <item>the schedule, under TrainerOrAdmin since S-25 — a member no longer sees the whole gym, and a
/// trainer without Admin is narrowed to the classes they instruct inside the handler;</item>
/// <item>the instructed-classes feed at <c>/api/trainer/classes</c> (S-25), the staff dashboard's
/// "Twoje zajęcia", also under TrainerOrAdmin;</item>
/// <item>the admin's management, under Admin.</item>
/// </list>
///
/// <para>
/// THE ONE RULE THIS FILE EXISTS TO PROTECT (prd-v2 FR-007): the class type is loaded to be
/// VALIDATED, never to be read from. <see cref="CreateClass.HandleAsync"/> copies duration and capacity out of
/// the REQUEST — the client prefilled them from the type and the admin may have overridden them —
/// and <see cref="UpdateClass.HandleAsync"/> and <see cref="DuplicateClass.HandleAsync"/> never touch the type's defaults at
/// all. Reading <c>DefaultCapacity</c> here would let a later type edit change the capacity of a
/// class that already has bookings. ClassEndpointTests pins this; nothing in the compiler does.
/// </para>
///
/// CANCEL AND DELETE ARE DIFFERENT ACTIONS, and S-09 is where the difference became real. DELETE is
/// for a MISTAKE — a class created seconds ago, which S-08's guard refuses once anybody has ever
/// booked it. <see cref="CancelClass.HandleAsync"/> is FR-013's state transition: the class stays, its bookings
/// and history stay, and everyone holding a spot is emailed and pushed in the SAME unit of work that
/// performs the flip. An admin who meant "this is not happening" wants the second one.
/// </summary>
public static class ClassEndpoints
{
    public static IEndpointRouteBuilder MapClassEndpoints(this IEndpointRouteBuilder app)
    {
        var schedule = app.MapGroup("/api/classes")
            .WithTags("Schedule")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        schedule.MapGet("/", GetSchedule.HandleAsync);

        var instructed = app.MapGroup("/api/trainer/classes")
            .WithTags("Schedule")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        instructed.MapGet("/", GetInstructedClasses.HandleAsync);

        var admin = app.MapGroup("/api/admin/classes")
            .WithTags("Schedule")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        admin.MapGet("/", GetAdminClasses.HandleAsync);
        admin.MapGet("/{id:guid}", GetClass.HandleAsync);
        admin.MapPost("/", CreateClass.HandleAsync);
        admin.MapPut("/{id:guid}", UpdateClass.HandleAsync);
        admin.MapDelete("/{id:guid}", DeleteClass.HandleAsync);
        admin.MapPost("/{id:guid}/cancel", CancelClass.HandleAsync);
        admin.MapPost("/{id:guid}/duplicate", DuplicateClass.HandleAsync);

        return app;
    }
}
