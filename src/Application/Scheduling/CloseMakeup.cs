using System.Security.Claims;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Staff close a makeup item as "nie odrobił" by hand, and reopen it within its deadline (S-36).
/// </summary>
public static class CloseMakeup
{
    /// <summary>
    /// Closes an OPEN item - nothing booked, deadline not passed. Closing a planned item would orphan
    /// its booking, so that is refused <c>makeup_not_open</c>: release the makeup first. Idempotent on an
    /// item already closed by hand.
    /// </summary>
    public static async Task<IResult> CloseAsync(
        Guid absenceBookingId,
        ClaimsPrincipal principal,
        IMakeupQuery query,
        IBookingStore bookings,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var item = await query.GetItemAsync(absenceBookingId, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        if (item.ClosedByHand)
        {
            return Results.Ok(item);
        }

        if (item.Status != "open")
        {
            return MakeupFailure.Refuse("makeup_not_open");
        }

        var absence = await bookings.FindByIdAsync(absenceBookingId, cancellationToken);
        absence!.MakeupClosedAt = timeProvider.GetUtcNow();
        absence.MakeupClosedBy = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return await SaveAsync(absenceBookingId, query, unitOfWork, cancellationToken);
    }

    /// <summary>
    /// Reopens an item closed by hand, while club-local today is still within its deadline - after
    /// that it would read "nie odrobił" anyway (<c>makeup_not_reopenable</c>). Idempotent on an item
    /// that is open and was never closed.
    /// </summary>
    public static async Task<IResult> ReopenAsync(
        Guid absenceBookingId,
        IMakeupQuery query,
        IBookingStore bookings,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var item = await query.GetItemAsync(absenceBookingId, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        if (!item.ClosedByHand)
        {
            return item.Status == "open" ? Results.Ok(item) : MakeupFailure.Refuse("makeup_not_reopenable");
        }

        var today = DateOnly.FromDateTime(ClubTime.ToClubLocal(timeProvider.GetUtcNow()).DateTime);
        if (today > item.Deadline)
        {
            return MakeupFailure.Refuse("makeup_not_reopenable");
        }

        var absence = await bookings.FindByIdAsync(absenceBookingId, cancellationToken);
        absence!.MakeupClosedAt = null;
        absence.MakeupClosedBy = null;

        return await SaveAsync(absenceBookingId, query, unitOfWork, cancellationToken);
    }

    /// <summary>
    /// One save, no retry loop: the close fields guard no pool, and the only concurrent writer of the
    /// row - a re-mark - clears them, which is the outcome a lost race would leave anyway.
    /// </summary>
    private static async Task<IResult> SaveAsync(
        Guid absenceBookingId, IMakeupQuery query, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        if (await unitOfWork.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            unitOfWork.DiscardChanges();
            return MakeupFailure.Refuse("conflict");
        }

        return Results.Ok(await query.GetItemAsync(absenceBookingId, cancellationToken));
    }
}
