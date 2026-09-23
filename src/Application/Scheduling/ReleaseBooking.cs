using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Releases a booking and returns its karnet entry.
/// </summary>
public static class ReleaseBooking
{
    /// <summary>
    /// Releases somebody else's spot.
    ///
    /// <para>
    /// BEYOND FR-014, WHICH ASKS ONLY FOR A VIEW, and deliberately so: it is what makes the
    /// capacity_below_bookings refusal workable — an admin told they cannot shrink a class needs a
    /// way to free a seat — and the server-side cancel path had to exist for the block cascade
    /// anyway. Chosen by the product owner during planning.
    /// </para>
    ///
    /// <para>
    /// BEFORE THE START ONLY (S-27). A no-show is recorded as absence on the roster, which returns the
    /// entry and keeps the booking in the member's history.
    /// </para>
    ///
    /// <para>
    /// Rotates the class stamp and retries exactly like the member's cancel, so an admin releasing a
    /// spot and a member claiming it cannot both win. 204 rather than the class, because the admin
    /// screen is a list of people and reloads that list rather than a tile.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid classId,
        Guid bookingId,
        ClaimsPrincipal principal,
        IClassStore classes,
        IBookingStore bookings,
        IMembershipPassStore passes,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= BookingProtocol.MaxAttempts; attempt++)
        {
            var entity = await classes.FindAsync(classId, cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            // Inside the loop rather than above it, because the class is re-read on every attempt and
            // the check must be against the row this attempt is actually acting on. Reassigning an
            // instructor mid-retry is vanishingly unlikely; deciding on a stale copy of the field
            // that authorises the write is not a thing to leave to likelihood.
            if (!BookingAuthorization.MayActOn(principal, entity))
            {
                return BookingAuthorization.NotYourClass();
            }

            var booking = await bookings.FindByIdAsync(bookingId, cancellationToken);

            // WRONG CLASS IS A 404, NOT A REFUSAL. A booking id addressed under a class it does not
            // belong to is a wrong address, exactly like an id nobody ever issued - and collapsing
            // the two also stops this route being used to probe which booking ids exist.
            //
            // An ALREADY CANCELLED booking is a 404 too, for a plainer reason: there is no spot here
            // to release.
            if (booking is null
                || booking.ClassId != classId
                || booking.Status != BookingStatus.Active)
            {
                return Results.NotFound();
            }

            // AT OR AFTER the start, the booking path's rule and reason (S-27). Once a class has
            // begun, "Nieobecny" is the honest record of a no-show; a release would erase the booking
            // from the member's history instead.
            var now = timeProvider.GetUtcNow();
            if (entity.StartsAt <= now)
            {
                return BookingProtocol.Refuse("class_started");
            }

            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = now;

            entity.ConcurrencyStamp = Guid.NewGuid().ToString();
            await BookingProtocol.ReturnEntryAsync(booking, passes, cancellationToken);

            if (await unitOfWork.TrySaveAsync(cancellationToken) == SaveOutcome.Saved)
            {
                return Results.NoContent();
            }

            unitOfWork.DiscardChanges();
        }

        return BookingProtocol.Refuse("conflict");
    }
}
