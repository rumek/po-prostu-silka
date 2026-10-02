using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Api.Endpoints.Scheduling;

/// <summary>
/// Makeups (S-36): the staff "Odrabianie" list and its actions, and the member's open count.
///
/// <para>
/// THE STAFF GROUP CALLS NO <see cref="BookingAuthorization.MayActOn"/>, unlike /api/admin/classes.
/// That is the point of it, not an omission: any trainer may arrange any member's makeup in any class.
/// The persona gate (TrainerOrAdmin) is the whole of the authorization, and the only write the group
/// can make to a class is a makeup booking. Do not add an ordinary booking route here.
/// </para>
/// </summary>
public static class MakeupEndpoints
{
    public static IEndpointRouteBuilder MapMakeupEndpoints(this IEndpointRouteBuilder app)
    {
        // Registered before the staff group's "{absenceBookingId:guid}" routes; the guid constraint
        // already keeps "mine" out of them, and the order keeps that obvious.
        app.MapGet("/api/makeups/mine", GetMakeups.MineAsync)
            .WithTags("Makeups")
            .RequireAuthorization(AuthorizationPolicyNames.MemberOnly);

        var staff = app.MapGroup("/api/makeups")
            .WithTags("Makeups")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        staff.MapGet("/", GetMakeups.ListAsync);
        staff.MapGet("/{absenceBookingId:guid}/classes", GetMakeups.ClassesAsync);
        staff.MapPost("/{absenceBookingId:guid}/booking", BookMakeup.HandleAsync);
        staff.MapDelete("/{absenceBookingId:guid}/booking", ReleaseMakeup.HandleAsync);
        staff.MapPut("/{absenceBookingId:guid}/closed", CloseMakeup.CloseAsync);
        staff.MapDelete("/{absenceBookingId:guid}/closed", CloseMakeup.ReopenAsync);

        return app;
    }
}
