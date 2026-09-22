using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Books a member into an occurrence on their behalf (MP-01, MP-02).
/// </summary>
public static class BookForMember
{
    /// <summary>
    /// Books somebody else in (S-14, AM-007).
    ///
    /// <para>
    /// THE REASON THE SLICE NEEDED THIS: a member with no account cannot tap Book, so without an
    /// admin route the club could record them, plan for them, and never get them into a class. It
    /// works for members WITH accounts too — a phone call to the desk is a perfectly ordinary way to
    /// sign up.
    /// </para>
    ///
    /// <para>
    /// Two pre-checks the member route cannot need, both BEFORE the loop because neither depends on
    /// state the loop re-reads: an unknown member is a 404, exactly like an unknown class, and a
    /// blocked one is refused. The rest is <see cref="BookingProtocol.TryBookAsync"/> — the same loop, the same
    /// refusals, the same guarantee — and it deliberately grants no exemptions: an admin cannot
    /// overfill a class or book into one that has started, because the club would then have to honour
    /// a seat that does not exist.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid classId,
        AdminBookingRequest request,
        ClaimsPrincipal principal,
        IMemberStore members,
        IClassStore classes,
        IBookingStore bookings,
        IMembershipPassStore passes,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // OWNERSHIP FIRST, before anything about the member is revealed. Checking it after the member
        // lookup would answer 404 for a member id that does not exist even to a trainer with no
        // business on this class, which turns the route into a probe for which member ids are real.
        var entity = await classes.FindAsync(classId, cancellationToken);
        if (entity is null)
        {
            return Results.NotFound();
        }

        if (!BookingAuthorization.MayActOn(principal, entity))
        {
            return BookingAuthorization.NotYourClass();
        }

        var member = await members.FindAsync(request.MemberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        // A blocked member may not attend, and the block cascade would cancel this booking the next
        // time anyone blocked them anyway. Refusing is the honest answer rather than writing a spot
        // the club has already decided not to honour.
        if (member.Status != MembershipStatus.Active)
        {
            return BookingProtocol.Refuse("member_blocked");
        }

        // Staff are never participants (S-25). Checked HERE and not in the protocol, which stays a
        // pure capacity-and-karnet transaction that knows nothing about roles.
        if (await members.IsStaffAsync(member.Id, cancellationToken))
        {
            return BookingProtocol.Refuse("member_is_staff");
        }

        return await BookingProtocol.TryBookAsync(
            classId, member.Id, classes, bookings, passes, unitOfWork, timeProvider,
            cancellationToken);
    }
}
