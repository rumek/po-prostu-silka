using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Staff release a planned makeup before its class starts (S-36), which opens the item again.
/// </summary>
public static class ReleaseMakeup
{
    /// <summary>
    /// Cancels the absence's live makeup booking. The CLASS stamp rotates, as on every release, because
    /// a spot frees up; no pass stamp does, because a makeup holds no entry.
    ///
    /// <para>
    /// WHO: the TrainerOrAdmin group, on any class - the mirror of <see cref="BookMakeup"/>. A release
    /// after the start is refused <c>class_started</c>: by then the makeup is marked, not released.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid absenceBookingId,
        IMakeupQuery query,
        IClassStore classes,
        IBookingStore bookings,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= BookingProtocol.MaxAttempts; attempt++)
        {
            var makeup = await bookings.FindLiveMakeupAsync(absenceBookingId, cancellationToken);
            if (makeup is null)
            {
                return Results.NotFound();
            }

            var entity = await classes.FindAsync(makeup.ClassId, cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            var now = timeProvider.GetUtcNow();
            if (entity.StartsAt <= now)
            {
                return MakeupFailure.Refuse("class_started");
            }

            makeup.Status = BookingStatus.Cancelled;
            makeup.CancelledAt = now;
            entity.ConcurrencyStamp = Guid.NewGuid().ToString();

            if (await unitOfWork.TrySaveAsync(cancellationToken) == SaveOutcome.Saved)
            {
                return Results.Ok(await query.GetItemAsync(absenceBookingId, cancellationToken));
            }

            unitOfWork.DiscardChanges();
        }

        return MakeupFailure.Refuse("conflict");
    }
}
