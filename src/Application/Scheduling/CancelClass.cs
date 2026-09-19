using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Cancels an occurrence as a state transition and fans out the notification (FR-013).
/// </summary>
public static class CancelClass
{
    /// <summary>
    /// Cancels a class (prd.md FR-013, US-02) — the state transition that replaces DELETE once
    /// anybody has signed up.
    ///
    /// <para>
    /// THE CLASS SURVIVES. Status moves to <see cref="ClassStatus.Cancelled"/> and nothing else is
    /// touched: every booking row stays <c>Active</c>, because cancellation is a state of the CLASS
    /// and cascading it onto the bookings would record that the MEMBER cancelled, which is false and
    /// would be the club's own attendance history rewritten. Visibility is driven by the class's
    /// status instead — the member schedule already filters on it, and S-09 phase 2 adds the same
    /// filter to "Moje zajęcia".
    /// </para>
    ///
    /// <para>
    /// ONE-WAY, deliberately. There is no un-cancel: the emails and pushes below cannot be recalled,
    /// so an admin who mis-clicked creates a new class rather than reversing this one. That is what
    /// <c>already_cancelled</c> enforces, and why the confirmation on the admin screen carries the
    /// weight.
    /// </para>
    ///
    /// <para>
    /// THE HANDLER ORDER IS THE LOAD-BEARING PART. Recipients are resolved BEFORE the flip and the
    /// enqueue happens BEFORE the save, so the status change and every outbox row land in ONE
    /// SaveChangesAsync. An enqueue after the save would be a second unit of work and would reopen
    /// exactly the "cancelled, nobody told" window the outbox exists to close. No explicit
    /// transaction, for the reason IUnitOfWork records: EnableRetryOnFailure is on, and a
    /// user-initiated transaction must go through Database.CreateExecutionStrategy().ExecuteAsync or
    /// it throws at runtime.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid id,
        IClassStore store,
        IBookingQuery bookings,
        IClassChangeNotification notification,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        // Checked before the past-class guard, so a second cancel of a class that has since started
        // answers the state it is actually in rather than a rule about time.
        if (existing.Status == ClassStatus.Cancelled)
        {
            return Results.Json(new ClassFailure("already_cancelled"), statusCode: 409);
        }

        // AT OR AFTER the start, matching BookAsync's rule and reusing its reason name. A class that
        // has begun cannot be un-happened, and emailing the people who attended it that it is
        // cancelled is disinformation this endpoint has no way to take back.
        if (existing.StartsAt <= timeProvider.GetUtcNow())
        {
            return Results.Json(new ClassFailure("class_started"), statusCode: 409);
        }

        // BEFORE the flip and before the save: this is the list of people owed a message, and it is
        // read through the same projection the admin's "Zapisani" panel uses. An empty list is
        // ordinary — cancelling a class nobody booked simply enqueues nothing.
        var recipients = await bookings.GetForClassAsync(id, cancellationToken);

        existing.Status = ClassStatus.Cancelled;

        // NOT OPTIONAL — read Class.ConcurrencyStamp before touching this line. A cancel changes
        // whether spots exist at all, so it moves one side of the capacity inequality exactly as a
        // booking moves the other. Without the rotation EF's UPDATE carries a WHERE clause the
        // in-flight booker's stale token still matches, and a cancel racing the last booking lets
        // both believe they won: a member holding a confirmed spot on a cancelled class, and a
        // message that went out before their booking existed.
        existing.ConcurrencyStamp = Guid.NewGuid().ToString();

        await notification.NotifyCancelledAsync(
            new ClassDescription(
                existing.ClassType.Name,
                existing.StartsAt,
                existing.DurationMinutes,

                // From the tracked entity's navigation, which is correct HERE and would not be on the
                // edit path: this handler changes no instructor, so FindAsync's Instructor is still
                // the class's own.
                existing.Instructor!.DisplayName),
            recipients,
            cancellationToken);

        // The single save. Everything above is in the change tracker; either the flip and all of its
        // messages commit, or none of them do.
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            // A booking committed between the recipient read and this write, so the list we rendered
            // messages for is already wrong. Nothing was written — including the outbox rows — so the
            // admin retrying gets a fresh list rather than one member silently missing their email.
            return Results.Json(new ClassFailure("conflict"), statusCode: 409);
        }

        // The class as it now stands, matching what UpdateAsync returns so the calendar can replace a
        // tile from the response. The recipient count IS the active booking count as of the commit:
        // the save succeeded, so no booking write landed in between — any that had tried would have
        // rotated the stamp and taken this save down with it.
        return Results.Ok(
            ClassDtoMapping.ToDto(existing, existing.ClassType, existing.Instructor!.DisplayName, recipients.Count));
    }
}
