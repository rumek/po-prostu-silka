using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The no-overbooking protocol: re-read, check the capacity and the karnet, insert, rotate
/// two stamps, save, discard and retry.
///
/// <para>
/// THE SEQUENCE MUST STAY LEGIBLE ENOUGH TO VERIFY BY READING, which is why it has its own
/// file rather than sitting inside the handler that calls it. It settles the PRD guardrail
/// that a class never accepts more bookings than it has spots.
/// </para>
/// </summary>
public static class BookingProtocol
{
    /// <summary>
    /// How many times a booking write re-reads and tries again after losing an optimistic race.
    ///
    /// <para>
    /// THE BOUND HAS TO EXCEED THE NUMBER OF SIMULTANEOUS WRITERS ON ONE CLASS, and that is why it is
    /// ten rather than the two or three a retry usually wants. Each racer that COMMITS rotates the
    /// stamp and costs every other racer one attempt, so N members tapping Book on the same class in
    /// the same instant make the last of them lose up to N-1 times before its read is current. A
    /// bound of three turned a class with three spots and four takers into <c>conflict</c> for the
    /// fourth, when the honest answer was <c>class_full</c>.
    /// </para>
    ///
    /// <para>
    /// It does not loop forever, and it does not need to: a losing attempt only repeats while spots
    /// still LOOK available, so a full class refuses on the next read rather than retrying. Ten is
    /// comfortably above what a club of dozens can produce in one instant, and exhausting it means
    /// something other than contention is wrong — <c>conflict</c> then tells the member to try again
    /// instead of showing them a 500.
    /// </para>
    /// </summary>
    public const int MaxAttempts = 10;

    /// <summary>
    /// Books a spot for a member the caller has already established the right to book for (S-14).
    ///
    /// <para>
    /// ONE CALLER SINCE S-16, and it stays extracted anyway. It was written for two — the member's
    /// own route and the staff one — and MP-01 removed the first. Folding it back into
    /// <see cref="BookForMemberAsync"/> would mix the no-overbooking protocol (re-read, check the
    /// capacity and the karnet, insert, rotate two stamps, save, discard and retry) with that
    /// handler's authorization and member resolution, in the one place where the sequence must stay
    /// legible enough to verify by reading.
    /// </para>
    ///
    /// <para>
    /// It takes no <see cref="ClaimsPrincipal"/> on purpose. Who may book for whom is settled by the
    /// CALLER — the group policy plus the instructor check — and passing the principal in here would
    /// invite a future authorization check in the one place that must stay a pure mechanism.
    /// </para>
    /// </summary>
    public static async Task<IResult> TryBookAsync(
        Guid classId,
        Guid memberId,
        IClassStore classes,
        IBookingStore bookings,
        IMembershipPassStore passes,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var entity = await classes.FindAsync(classId, cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            if (entity.Status == ClassStatus.Cancelled)
            {
                return Refuse("class_cancelled");
            }

            // AT OR AFTER the start, not merely after: a class beginning this instant is one nobody
            // can still join. There is no window before that - booking right up to the start is what
            // prd.md leaves in place, and the PRD's free-cancel-anytime rule means the mirror image
            // does NOT hold on the cancel path.
            var now = timeProvider.GetUtcNow();
            if (entity.StartsAt <= now)
            {
                return Refuse("class_started");
            }

            if (await bookings.FindActiveAsync(classId, memberId, cancellationToken) is not null)
            {
                return Refuse("already_booked");
            }

            // Greater-or-equal, not equal: if the count has somehow passed capacity the answer is
            // still "full". Equality here would turn a broken invariant into an open door.
            var bookedCount = await bookings.CountActiveAsync(classId, cancellationToken);
            if (bookedCount >= entity.Capacity)
            {
                return Refuse("class_full");
            }

            // ------------------------------------------------------------------
            // THE KARNET GATE (S-16, MP-06). A SECOND POOL, GUARDED THE SAME WAY.
            //
            // Deliberately INSIDE the loop and re-read on every attempt. Hoisting it above the loop
            // would be the exact stale guess the loop exists to prevent: a racer that lost on the
            // class stamp may have lost to a booking that spent this member's last entry, and an
            // entry count read before that write is a number about a world that no longer exists.
            //
            // The date is the CLASS'S club-local date, not today's and not UTC's. A 21:00 class on
            // the last day a karnet covers is covered; the 06:00 class the next morning is not. This
            // is ClubTime's second read-path consumer - see that type's doc comment, which anticipates
            // exactly this kind of narrow exception and explains why a JSON path still returns UTC.
            // ------------------------------------------------------------------
            var classDate = DateOnly.FromDateTime(ClubTime.ToClubLocal(entity.StartsAt).DateTime);

            var pass = await passes.FindCoveringAsync(memberId, classDate, cancellationToken);
            if (pass is null)
            {
                return Refuse("no_valid_pass");
            }

            // Greater-or-equal for the reason the capacity check is: if the count has somehow passed
            // the issued number the answer is still "none left". Equality would turn a broken
            // invariant into an open door.
            var entriesUsed = await bookings.CountConsumingForPassAsync(pass.Id, cancellationToken);
            if (entriesUsed >= pass.EntryCount)
            {
                return Refuse("no_entries_left");
            }

            bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                ClassId = entity.Id,
                MemberId = memberId,
                Status = BookingStatus.Active,

                // ATTRIBUTION, recorded now. It is what keeps entries-left stable when the pass's
                // validity range is later edited - see Booking.MembershipPassId.
                MembershipPassId = pass.Id,
                CreatedAt = now,
            });

            // THE GUARANTEE. Read the class doc comment before touching this line: without it the
            // count above is a guess that happens to be right most of the time.
            entity.ConcurrencyStamp = Guid.NewGuid().ToString();

            // THE SECOND GUARANTEE, and it is a SEPARATE pool rather than a duplicate of the first.
            // One member's last entry spent on two DIFFERENT classes races on nothing else: the two
            // bookings touch two different Class rows, so the class stamp above serializes neither of
            // them against the other. Both stamps rotate in the same SaveChangesAsync below, so one
            // atomic write guards both invariants.
            pass.ConcurrencyStamp = Guid.NewGuid().ToString();

            var outcome = await unitOfWork.TrySaveAsync(cancellationToken);
            if (outcome == SaveOutcome.Saved)
            {
                // Projected from the tracked entity, whose navigations FindAsync included. Both
                // failure modes below mean NOTHING was written, so there is no half-state to undo.
                //
                // bookedCount + 1 rather than a re-count, and that is EXACT rather than optimistic:
                // the save succeeded, so no other booking write committed between the count above and
                // this commit - any that had tried would have rotated the stamp and taken this save
                // down with it. The count is therefore this class as of the instant it committed,
                // which is the most any answer can claim.
                //
                // Exact WITH RESPECT TO STAMPED WRITES, which is every writer but one: the block
                // cascade in MemberAdminEndpoints cancels future bookings without rotating anything
                // (see Class.ConcurrencyStamp for why that is safe). A cascade committing in this
                // window makes the number one too low - never too high - so it can only understate
                // the spots available, which is the direction that cannot overbook.
                return Results.Ok(ClassDtoMapping.ToDto(
                    entity, entity.ClassType, entity.Instructor!.DisplayName, bookedCount + 1));
            }

            // ConcurrencyConflict: someone else's booking or cancellation rotated the stamp first.
            // UniqueViolation: the filtered index caught a double booking the check above missed,
            // which needs two requests from the SAME member at the same instant. Both mean "re-read
            // and decide again", and both need the tracked graph thrown away first - it still holds
            // the rejected insert and a class whose stamp is stale.
            unitOfWork.DiscardChanges();
        }

        return Refuse("conflict");
    }

    /// <summary>
    /// Returns the entry a cancelling booking was holding, by rotating its karnet's stamp (S-16).
    ///
    /// <para>
    /// WHY THIS IS NOT THE SAME CASE AS FREEING A CLASS SPOT. The block cascade skips the class stamp
    /// because cancelling can only ever free spots — a concurrent booker reading a pre-cancel count
    /// is being conservative. An entry is the OPPOSITE: returning one makes a booking possible that
    /// was refused a moment ago, so a booker whose entry check straddles this cancel would be deciding
    /// on a pool that is mid-change, and could spend the same entry twice. Every path that flips a
    /// booking to Cancelled owes this rotation.
    /// </para>
    ///
    /// <para>
    /// A booking with no pass (a pre-S-16 row) consumes no entry and returns none — hence the null
    /// check, which is the whole of the handling that case needs. Stages only; the caller commits.
    /// </para>
    /// </summary>
    public static async Task ReturnEntryAsync(
        Booking booking,
        IMembershipPassStore passes,
        CancellationToken cancellationToken)
    {
        if (booking.MembershipPassId is null)
        {
            return;
        }

        var pass = await passes.FindAsync(booking.MembershipPassId.Value, cancellationToken);
        if (pass is not null)
        {
            pass.ConcurrencyStamp = Guid.NewGuid().ToString();
        }
    }

    /// <summary>
    /// Every booking refusal, as a 409 — see <see cref="BookingFailure"/> for why there is no 400 in
    /// this file.
    /// </summary>
    public static IResult Refuse(string reason) =>
        Results.Json(new BookingFailure(reason), statusCode: 409);
}
