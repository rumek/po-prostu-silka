using System.Security.Claims;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Removes a member from a group's fixed roster and releases their future bookings in it (S-37).
/// </summary>
public static class RemoveFromRoster
{
    /// <summary>
    /// The removal and every release land in ONE save, retried as a whole on a conflict.
    ///
    /// <para>
    /// WHAT IS RELEASED: the member's ACTIVE bookings on the group's classes still to come, except
    /// makeups (<see cref="Booking.MakeupForBookingId"/>) - a makeup was earned by an absence, not by the
    /// roster, and leaving the group does not forfeit it. Past and started classes keep their bookings and
    /// attendance. Nothing marks roster-made bookings apart from manual ones; a future booking in the
    /// group is released either way.
    /// </para>
    ///
    /// <para>
    /// A TRAINER RELEASES ONLY ON THE CLASSES THEY INSTRUCT (S-16). The member's bookings on another
    /// trainer's class of the group stay; the roster row goes regardless.
    /// </para>
    ///
    /// <para>
    /// Each release is <see cref="ReleaseBooking"/>'s in miniature: the class stamp rotated, so a release
    /// and a booking racing for the spot cannot both win, and the entry returned by rotating the pass.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid groupId,
        Guid memberId,
        ClaimsPrincipal principal,
        IClassGroupStore groups,
        IGroupRosterStore roster,
        IGroupRosterQuery query,
        IClassStore classes,
        IMembershipPassStore passes,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var (failure, _) = await RosterAuthorization.AuthorizeAsync(
            principal, groupId, groups, roster, timeProvider, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var acting = RosterAuthorization.ActingInstructorId(principal);
        var saved = false;

        for (var attempt = 1; attempt <= BookingProtocol.MaxAttempts && !saved; attempt++)
        {
            var entry = await roster.FindAsync(groupId, memberId, cancellationToken);
            if (entry is null)
            {
                return Results.NotFound();
            }

            var now = timeProvider.GetUtcNow();
            var releasable = await roster.ReleasableBookingsAsync(groupId, memberId, now, acting, cancellationToken);

            foreach (var booking in releasable)
            {
                booking.Status = BookingStatus.Cancelled;
                booking.CancelledAt = now;

                // Through the store that owns loading classes rather than the booking's navigation: the
                // stamp rotation is the point of loading it.
                var entity = await classes.FindAsync(booking.ClassId, cancellationToken);
                if (entity is not null)
                {
                    entity.ConcurrencyStamp = Guid.NewGuid().ToString();
                }

                await BookingProtocol.ReturnEntryAsync(booking, passes, cancellationToken);
            }

            roster.Remove(entry);

            if (await unitOfWork.TrySaveAsync(cancellationToken) == SaveOutcome.Saved)
            {
                saved = true;
            }
            else
            {
                unitOfWork.DiscardChanges();
            }
        }

        if (!saved)
        {
            return RosterFailure.Refuse("conflict");
        }

        var view = await query.GetViewAsync(groupId, timeProvider.GetUtcNow(), acting, cancellationToken);
        return Results.Ok(view);
    }
}
