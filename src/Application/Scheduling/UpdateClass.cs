using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Edits an occurrence and notifies everyone booked into it (FR-013, FR-021).
/// </summary>
public static class UpdateClass
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ClassRequest request,
        IClassStore store,
        IBookingStore bookings,
        IBookingQuery bookingQuery,
        IClassChangeNotification notification,
        UserManager<ApplicationUser> userManager,
        IMemberStore members,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        // CAPTURED BEFORE ANYTHING IS MUTATED, and that is the whole reason these three locals exist
        // rather than being read where they are used. FindAsync returns a TRACKED entity, so after
        // the assignment block below `existing.StartsAt` IS the request's value — a message built
        // from it would render "18:00 -> 18:00" and tell the member nothing. The instructor's NAME
        // cannot be captured the same way (the navigation is the previous account's, which is
        // exactly what this needs) so it is read here too, before ClassRequestValidator.ValidateInstructorAsync resolves
        // the new one.
        var previous = new ClassDescription(
            existing.ClassType.Name,
            existing.StartsAt,
            existing.DurationMinutes,
            existing.Instructor!.DisplayName);

        var previousInstructorMemberId = existing.InstructorMemberId;

        var invalid = ClassRequestValidator.Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        // THE TYPE IS IMMUTABLE. Refused rather than silently ignored: a client sending a different
        // type has a bug, and a server that quietly discards the field would leave the admin
        // believing they had changed something. Repointing an occurrence is delete-and-recreate.
        //
        // Because of this, no active-type check runs here - see CreateAsync. An occurrence whose type
        // was deactivated after it was created stays fully editable, which is what FR-006 promises.
        if (request.ClassTypeId != existing.ClassTypeId)
        {
            return Results.Json(new ClassFailure("class_type_immutable"), statusCode: 400);
        }

        // The instructor, unlike the type, IS mutable - reassigning a class to another trainer is
        // ordinary admin work - so it is re-validated on every edit.
        var (instructorFailure, instructor) =
            await ClassRequestValidator.ValidateInstructorAsync(
                request.InstructorMemberId, userManager, members, cancellationToken);
        if (instructorFailure is not null)
        {
            return instructorFailure;
        }

        // No starts_in_past check here — see CreateAsync. Correcting a class that already ran is a
        // legitimate thing for an admin to do; refusing it would leave a wrong record permanently
        // wrong.

        // Excluding its own id, or every edit that keeps the time would conflict with itself.
        if (await store.HasTimeConflictAsync(
                request.StartsAt, request.DurationMinutes, id, cancellationToken))
        {
            return Results.Json(new ClassFailure("time_conflict"), statusCode: 409);
        }

        // THE NO-OVERBOOKING GUARANTEE, FROM THE OTHER SIDE. Bookings cannot exceed capacity; an
        // edit must not be able to move capacity below the bookings instead. Refused rather than
        // truncated - the club has to decide WHO loses their spot, and this endpoint cannot.
        //
        // Equal is allowed: shrinking a class to exactly the number of people already in it is a
        // legitimate "no more sign-ups" move.
        var bookedCount = await bookings.CountActiveAsync(id, cancellationToken);
        if (request.Capacity < bookedCount)
        {
            return Results.Json(new ClassFailure("capacity_below_bookings"), statusCode: 409);
        }

        existing.StartsAt = request.StartsAt;
        existing.DurationMinutes = request.DurationMinutes;
        existing.Capacity = request.Capacity;
        existing.InstructorMemberId = request.InstructorMemberId;

        // AND THIS EDIT ROTATES THE STAMP TOO. IsConcurrencyToken only puts the column in the WHERE
        // clause; it does not generate a new value the way a SQL rowversion would. So without this
        // line a capacity shrink leaves the stamp in the database exactly as it was, and a member
        // who read the class BEFORE the shrink still holds a token that matches:
        //
        //   member reads Capacity=10, stamp=S1; counts 5 -> room
        //   admin shrinks to 5        -> UPDATE ... WHERE stamp=S1, stamp still S1
        //   member inserts, rotates   -> UPDATE ... WHERE stamp=S1 MATCHES -> 6 in a class of 5
        //
        // The guard above validates against a count; this line is what stops the capacity it
        // validated against from moving underneath a booking already in flight. Every writer that
        // changes how many spots are TAKEN rotates - so must the one that changes how many EXIST.
        existing.ConcurrencyStamp = Guid.NewGuid().ToString();

        // S-09: THE SECOND TRIGGER. A member is told when the class they signed up for MOVES, on the
        // same terms as when it is cancelled — enqueued here, before the save, so the edit and its
        // messages are one unit of work exactly as CancelAsync's are.
        //
        // THREE FIELDS, NOT FOUR. Start time, duration and instructor are precisely what a member
        // sees in "Moje zajęcia", so a change to any of them changes the appointment they are
        // holding. Capacity is administrative: it moves for reasons that have nothing to do with the
        // people already in, and mailing them about it would be noise that erodes trust in the
        // messages that do matter. A PUT that changes nothing is likewise silent.
        //
        // The instructor is compared on the ID, not the display name — the id is what the admin
        // changed, and two trainers may share a name. The NAME for the message comes from the
        // instructor ClassRequestValidator.ValidateInstructorAsync already resolved, never from existing.Instructor, which
        // still points at the previous account.
        var current = new ClassDescription(
            existing.ClassType.Name,
            request.StartsAt,
            request.DurationMinutes,
            instructor!.DisplayName);

        var moved = previous.StartsAt != current.StartsAt
                    || previous.DurationMinutes != current.DurationMinutes
                    || previousInstructorMemberId != request.InstructorMemberId;

        // A CANCELLED class notifies nothing. Its members were already told it is not happening;
        // correcting its record afterwards is bookkeeping, and there is no live appointment to
        // update. bookedCount is the guard's own count from above — zero means no recipient list to
        // fetch, so the query is skipped entirely rather than asked for an empty answer.
        if (moved && bookedCount > 0 && existing.Status == ClassStatus.Scheduled)
        {
            var recipients = await bookingQuery.GetForClassAsync(id, cancellationToken);

            await notification.NotifyChangedAsync(previous, current, recipients, cancellationToken);
        }

        // TrySaveChangesAsync since S-08, and the reason is new: Class now CARRIES a concurrency
        // token, so this UPDATE's WHERE clause includes it and a booking that committed between the
        // count above and this line makes the save match no row.
        //
        // The old comment here said there was no second writer to race - true while only one admin
        // account exists, and no longer true at all: every member who books is a writer against this
        // row. A bare SaveChangesAsync would now surface that as an unhandled
        // DbUpdateConcurrencyException, i.e. a 500 for a race the server understands perfectly well.
        //
        // Answering conflict is not a formality: the count this edit was validated against has
        // moved, so the admin must see the new one before deciding again.
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new ClassFailure("conflict"), statusCode: 409);
        }

        // Projected from what this handler already holds, NOT from a re-read - see CreateAsync for
        // why the re-read was wrong. The type is immutable on an edit, so existing.ClassType (loaded
        // by FindAsync) is still correct; the instructor may have just changed, which is exactly why
        // the validated account is used rather than the tracked entity's navigation - that one still
        // points at the PREVIOUS account and would render a stale display name.
        return Results.Ok(ClassDtoMapping.ToDto(existing, existing.ClassType, instructor!.DisplayName, bookedCount));
    }
}
