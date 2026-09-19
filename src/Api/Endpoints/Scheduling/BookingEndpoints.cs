using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Application.Scheduling;

namespace po_prostu_silka.Api.Endpoints.Scheduling;

/// <summary>
/// Booking and releasing a spot (prd.md FR-010, FR-014; S-16 MP-01 and MP-02).
///
/// <para>
/// SELF-SERVICE BOOKING IS GONE. prd.md US-01, FR-008 and FR-009 described a member who books and
/// cancels their own spot; S-16 retires all three as member-facing capabilities. The behaviour
/// survives on the staff routes — an admin anywhere, a trainer on the classes they instruct — and
/// what a member has left here is a read of their own upcoming bookings.
/// </para>
///
/// <para>
/// THE NO-OVERBOOKING GUARANTEE IS IMPLEMENTED HERE, and it rests entirely on one line in
/// <see cref="BookingProtocol.TryBookAsync"/>: the rotation of <see cref="Class.ConcurrencyStamp"/>. A booking inserts a
/// row into Bookings and touches nothing on Classes by itself, so without that assignment EF issues
/// no UPDATE against Classes, no WHERE clause carries the token, and two members racing for the last
/// spot both commit. Rotating the stamp is not bookkeeping — it is the mechanism. Every write that
/// changes how many spots are taken must do it, a release included: a release and a booking racing
/// for the same last spot must not both believe they won.
/// </para>
///
/// <para>
/// There is no explicit transaction and there must not be one. A single SaveChangesAsync is already
/// atomic, and the stamp is what pulls the capacity CHECK inside that atom; opening a transaction
/// would additionally require Database.CreateExecutionStrategy().ExecuteAsync, because
/// EnableRetryOnFailure is on (Program.cs) and BeginTransaction throws at runtime without it.
/// </para>
///
/// <para>
/// ONE member-facing group since S-16, and it is READ-ONLY. <c>GET /api/bookings/mine</c> is all a
/// member has: MP-01 removed self-service booking and cancellation entirely, because the karnet
/// decides who trains and the desk is what knows whether somebody holds one. The route still resolves
/// the member from the COOKIE and never from the request, which is what makes "your bookings" mean
/// yours by construction.
///
/// <para>
/// THE STAFF GROUP IS THE EXCEPTION, and it is the only place in this application where the group
/// policy is not the whole answer (S-16, MP-02). It admits trainers as well as admins, and a trainer
/// may act only on classes they personally instruct — a narrowing that depends on the class in the
/// route and therefore cannot live in a policy. See the comment on that group before adding anything
/// to it.
/// </para>
/// </para>
/// </summary>
public static class BookingEndpoints
{

    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        // THE MEMBER'S WRITE ROUTES ARE GONE (S-16, MP-01). POST /api/classes/{id}/bookings and
        // DELETE /api/classes/{id}/bookings/mine were removed with their handlers: booking is a staff
        // action now, because the karnet is what entitles somebody to a spot and the desk is what
        // knows whether they hold one. The member keeps the READ below - they still see what they are
        // committed to, they just do not change it themselves.
        var myBookings = app.MapGroup("/api/bookings")
            .WithTags("Bookings")
            .RequireAuthorization(AuthorizationPolicyNames.ActiveMember);

        myBookings.MapGet("/mine", GetMyBookings.HandleAsync);

        // THE STAFF HALF. Under TrainerOrAdmin since S-16, and addressed under /api/admin/classes so
        // it sits beside the management endpoints it belongs with. The path still says "admin"; the
        // policy no longer does, and that is not an oversight - renaming the route would break the
        // SPA's whole booking surface for a cosmetic gain.
        //
        // ------------------------------------------------------------------
        // THE GROUP POLICY ALONE IS NO LONGER SUFFICIENT HERE. READ THIS BEFORE ADDING AN ENDPOINT.
        //
        // TrainerOrAdmin admits every trainer in the club, and a trainer may act only on the classes
        // they personally instruct (MP-02). That narrowing cannot live in the policy: it depends on
        // the CLASS in the route, which no policy can see. So each handler below carries an inline
        // ownership check against Class.InstructorMemberId, and ANY endpoint added to this group must
        // carry the same one - a new route inherits the group's admission and none of its narrowing,
        // which would silently let a trainer act on somebody else's class.
        //
        // Inline rather than an IAuthorizationHandler because that is what this codebase does
        // everywhere: authorization here is either a group policy or a hand-rolled field check, and
        // nothing in this repository registers a resource handler. This is the known cost of that
        // choice, written down where the next person will read it.
        // ------------------------------------------------------------------
        var staffBookings = app.MapGroup("/api/admin/classes")
            .WithTags("Bookings")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        staffBookings.MapGet("/{classId:guid}/bookings", GetClassBookings.HandleAsync);
        staffBookings.MapPost("/{classId:guid}/bookings", BookForMember.HandleAsync);
        staffBookings.MapDelete("/{classId:guid}/bookings/{bookingId:guid}", ReleaseBooking.HandleAsync);

        return app;
    }
}
