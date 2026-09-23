using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The body of the attendance PUT: <c>"present"</c> or <c>"absent"</c>. A string rather than the enum,
/// because the API serialises no enum as a string anywhere else and this one value is not worth a
/// global converter.
/// </summary>
public record AttendanceRequest(string? Attendance);

/// <summary>
/// Staff mark one booked member present or absent, and correct the mark later (S-27, AT-01–AT-03).
/// </summary>
public static class RecordAttendance
{
    /// <summary>
    /// Records attendance on one booking of a class that has started.
    ///
    /// <para>
    /// WHO: <see cref="BookingAuthorization.MayActOn"/> — an admin on any class, a trainer on the
    /// classes they instruct (AT-01). WHAT: only an ACTIVE booking of this class (AT-02); a walk-in
    /// without a booking has nothing to mark. WHEN: from the start, with no end — a correction a week
    /// later is as legitimate as one in the room.
    /// </para>
    ///
    /// <para>
    /// ABSENT → PRESENT SPENDS AN ENTRY AGAIN, so it passes the same gate a booking does and can be
    /// refused <c>no_entries_left</c>: the entry an absence freed may have been booked elsewhere since.
    /// Every change rotates the pass stamp, inside a retry loop shaped like
    /// <see cref="BookingProtocol.TryBookAsync"/> — present → absent returns an entry, which is exactly
    /// the kind of change a concurrent booker is racing for. The CLASS stamp is not rotated: attendance
    /// changes no capacity.
    /// </para>
    ///
    /// <para>
    /// Idempotent: the same mark twice is a 200 with no write, so a double tap on a phone does not
    /// rotate anything or lose a race against itself.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid classId,
        Guid bookingId,
        AttendanceRequest request,
        ClaimsPrincipal principal,
        IClassStore classes,
        IBookingStore bookings,
        IBookingQuery query,
        IMembershipPassStore passes,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        BookingAttendance attendance;
        switch (request.Attendance)
        {
            case "present":
                attendance = BookingAttendance.Present;
                break;
            case "absent":
                attendance = BookingAttendance.Absent;
                break;
            default:
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["attendance"] = ["Expected \"present\" or \"absent\"."],
                });
        }

        for (var attempt = 1; attempt <= BookingProtocol.MaxAttempts; attempt++)
        {
            var entity = await classes.FindAsync(classId, cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            // Inside the loop, against the row this attempt acts on — ReleaseBooking's reasoning.
            if (!BookingAuthorization.MayActOn(principal, entity))
            {
                return BookingAuthorization.NotYourClass();
            }

            // The release path's three 404s, for its reasons: a wrong class is a wrong address, and a
            // released booking holds nothing to mark.
            var booking = await bookings.FindByIdAsync(bookingId, cancellationToken);
            if (booking is null
                || booking.ClassId != classId
                || booking.Status != BookingStatus.Active)
            {
                return Results.NotFound();
            }

            if (entity.Status == ClassStatus.Cancelled)
            {
                return BookingProtocol.Refuse("class_cancelled");
            }

            var now = timeProvider.GetUtcNow();
            if (entity.StartsAt > now)
            {
                return BookingProtocol.Refuse("class_not_started");
            }

            if (booking.Attendance == attendance)
            {
                return await RowAsync(query, classId, bookingId, cancellationToken);
            }

            if (booking.MembershipPassId is { } passId)
            {
                var pass = await passes.FindAsync(passId, cancellationToken);
                if (pass is not null)
                {
                    // Only absent → present moves a booking back into the pool; unrecorded already
                    // counts as spent, so unrecorded → present spends nothing new.
                    var respends = attendance == BookingAttendance.Present
                                   && booking.Attendance == BookingAttendance.Absent;

                    // Greater-or-equal, as in TryBookAsync. The count excludes this booking, since
                    // it is absent right now.
                    if (respends
                        && await bookings.CountConsumingForPassAsync(pass.Id, cancellationToken) >= pass.EntryCount)
                    {
                        return BookingProtocol.Refuse("no_entries_left");
                    }

                    pass.ConcurrencyStamp = Guid.NewGuid().ToString();
                }
            }

            booking.Attendance = attendance;
            booking.AttendanceRecordedAt = now;
            booking.AttendanceRecordedBy = principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (await unitOfWork.TrySaveAsync(cancellationToken) == SaveOutcome.Saved)
            {
                return await RowAsync(query, classId, bookingId, cancellationToken);
            }

            unitOfWork.DiscardChanges();
        }

        return BookingProtocol.Refuse("conflict");
    }

    /// <summary>The roster row as it now stands, so the overlay replaces it without a refetch.</summary>
    private static async Task<IResult> RowAsync(
        IBookingQuery query, Guid classId, Guid bookingId, CancellationToken cancellationToken)
    {
        var rows = await query.GetForClassAsync(classId, cancellationToken);
        var row = rows.FirstOrDefault(r => r.BookingId == bookingId);

        return row is null ? Results.NotFound() : Results.Ok(row);
    }
}
