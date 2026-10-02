using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>The body of the makeup booking POST: the class the member makes up in.</summary>
public record MakeupBookingRequest(Guid ClassId);

/// <summary>
/// What turns <see cref="BookingProtocol.TryBookAsync"/> into a makeup booking (S-36): the absence it
/// makes up, and the last club-local date its class may take place on.
/// </summary>
public sealed record MakeupGrant(Guid AbsenceBookingId, DateOnly Deadline);

/// <summary>
/// The one concurrency token every writer of a makeup item rotates (S-36).
///
/// <para>
/// Booking the makeup, re-marking the absence and closing or reopening the item each write a
/// DIFFERENT row - the makeup's class, the absence, the absence again without a stamp - so without a
/// shared token two of them can both read "open" and both commit: a free makeup for an absence that is
/// no longer "odrobi", or a live makeup on an item closed by hand. The token is the absence's karnet
/// stamp, which <see cref="RecordAttendance"/> already rotates on every mark; an absence no karnet paid
/// for (pre-S-16) falls back to its class's stamp, which RecordAttendance then rotates instead.
/// </para>
/// </summary>
public static class MakeupClaim
{
    public static async Task RotateAsync(
        Booking absence, IMembershipPassStore passes, IClassStore classes, CancellationToken cancellationToken)
    {
        if (absence.MembershipPassId is { } passId)
        {
            var pass = await passes.FindAsync(passId, cancellationToken);
            if (pass is not null)
            {
                pass.ConcurrencyStamp = Guid.NewGuid().ToString();
            }

            return;
        }

        var absenceClass = await classes.FindAsync(absence.ClassId, cancellationToken);
        if (absenceClass is not null)
        {
            absenceClass.ConcurrencyStamp = Guid.NewGuid().ToString();
        }
    }
}

/// <summary>
/// Staff book the one free makeup an "odrobi" absence earned (S-36).
/// </summary>
public static class BookMakeup
{
    /// <summary>
    /// Books <paramref name="absenceBookingId"/>'s member into <c>request.ClassId</c> as its makeup.
    ///
    /// <para>
    /// WHO: anyone on the TrainerOrAdmin group, into ANY class - deliberately without
    /// <see cref="BookingAuthorization.MayActOn"/>. A makeup is usually at another trainer's class, and
    /// the club lets any trainer arrange one. This is the one place a trainer writes to a class they do
    /// not instruct, and it can only ever add a makeup, never an ordinary booking.
    /// </para>
    ///
    /// <para>
    /// THE SAME LOOP AS EVERY BOOKING. Capacity, the time rule and the karnet's coverage of the class's
    /// date run in <see cref="BookingProtocol.TryBookAsync"/> under the class stamp; only the entry
    /// check is skipped, because the absence already spent the entry.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid absenceBookingId,
        MakeupBookingRequest request,
        IMakeupQuery query,
        IMemberStore members,
        IClassStore classes,
        IBookingStore bookings,
        IMembershipPassStore passes,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var item = await query.GetItemAsync(absenceBookingId, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        var member = await members.FindAsync(item.MemberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        if (member.Status != MembershipStatus.Active)
        {
            return MakeupFailure.Refuse("member_blocked");
        }

        if (await members.IsStaffAsync(member.Id, cancellationToken))
        {
            return MakeupFailure.Refuse("member_is_staff");
        }

        if (item.Status != "open")
        {
            return MakeupFailure.Refuse("makeup_not_open");
        }

        // A makeup on a class that was cancelled since is no longer live, but its row still holds the
        // filtered unique index. Saved away ON ITS OWN, before the loop: in one SaveChanges with the
        // insert, nothing guarantees the update runs first, and the index would refuse the insert.
        // Cancelling it is the truth anyway - the member is not attending a class that is not happening.
        if (await bookings.CancelSupersededMakeupsAsync(
                absenceBookingId, timeProvider.GetUtcNow(), cancellationToken))
        {
            // Lost to a racing write: the row still holds the index, so every insert below would fail
            // too. Say so now rather than after ten doomed attempts.
            if (await unitOfWork.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
            {
                unitOfWork.DiscardChanges();
                return MakeupFailure.Refuse("conflict");
            }
        }

        var result = await BookingProtocol.TryBookAsync(
            request.ClassId, member.Id, classes, bookings, passes, unitOfWork, timeProvider,
            cancellationToken, new MakeupGrant(absenceBookingId, item.Deadline));

        if (result is not IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK })
        {
            return result;
        }

        return Results.Ok(await query.GetItemAsync(absenceBookingId, cancellationToken));
    }
}
