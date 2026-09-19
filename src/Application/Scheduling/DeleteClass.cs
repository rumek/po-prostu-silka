using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Deletes an occurrence that nobody is booked into.
/// </summary>
public static class DeleteClass
{
    /// <summary>
    /// Deletes a class outright. For MISTAKES only — see the class doc comment.
    ///
    /// <para>
    /// GUARDED SINCE S-08. Once somebody has signed up, taking the class off the schedule is a
    /// CANCELLATION — a state transition that owes everyone booked an email and a push (S-09) — and
    /// deleting the row would destroy the very list of people owed that message, along with the
    /// history FR-009 requires be kept.
    /// </para>
    ///
    /// <para>
    /// ANY booking guards it, not only active ones. Partly because the database says so — both FKs
    /// on Bookings are RESTRICT, so an active-only guard would wave the delete through and then fail
    /// on a foreign-key violation — and partly because it is the honest rule: this endpoint erases a
    /// class created by MISTAKE, and a class somebody signed up for and then cancelled is a class
    /// that happened. Deleting it would take that member's history with it.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid id,
        IClassStore store,
        IBookingStore bookings,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (await bookings.HasAnyAsync(id, cancellationToken))
        {
            return Results.Json(new ClassFailure("has_bookings"), statusCode: 409);
        }

        store.Remove(existing);

        // TrySaveChangesAsync for the reason UpdateAsync records: Classes now carries a concurrency
        // token, so a booking committed between the check above and this line makes the DELETE match
        // no row. That is precisely the race the check exists to lose - somebody booked the class
        // being deleted - so has_bookings is the honest answer, not a 500.
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new ClassFailure("has_bookings"), statusCode: 409);
        }

        return Results.NoContent();
    }
}
