using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One of the caller's own upcoming bookings, as "Moje zajęcia" shows it (prd.md FR-010).
///
/// <para>
/// A CONTRACT the SPA's booking service mirrors, like <see cref="ScheduledClass"/>. It is
/// deliberately NOT a ScheduledClass: that shape carries capacity, free spots and the instructor's
/// Identity id, none of which the list needs, and it lacks the two fields that make a row a booking
/// rather than a class — <see cref="BookingId"/> and <see cref="BookedAt"/>.
/// </para>
///
/// <para>
/// <see cref="Name"/>, <see cref="Description"/> and <see cref="Instructor"/> are RESOLVED through
/// the class's type and instructor, exactly as they are on the schedule — a booking stores none of
/// the three, so correcting a typo on a class type corrects it here too.
/// </para>
/// </summary>
public record MyBooking(
    Guid BookingId,
    Guid ClassId,
    string Name,
    string? Description,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string Instructor,
    DateTimeOffset BookedAt);

/// <summary>
/// One signed-up member, as the admin's "Zapisani" panel shows them (prd.md FR-014).
///
/// <para>
/// The email is here because the club's actual use for this list is reaching people — a class is
/// moved, a trainer is ill — and it is admin-only surface, gated by the same policy as every other
/// admin endpoint. <see cref="MemberUserId"/> travels for the same reason it does on
/// <see cref="ScheduledClass"/>: the client needs a stable key, and it grants nothing on its own.
/// </para>
/// </summary>
/// <param name="MemberId">Who holds the spot.</param>
/// <param name="UserId">
/// Their account, or null when they have none (S-14).
///
/// <para>
/// IT TRAVELS BESIDE THE MEMBER ID BECAUSE PUSH IS ACCOUNT-KEYED and stays that way — a device
/// belongs to a login, not to a person, so the notification fan-out needs this to find the
/// subscriptions. Do not "simplify" it away by keying push on the member.
/// </para>
/// </param>
/// <param name="Email">
/// Where to reach them, or empty when the club has no address for them. Empty is a REAL case since
/// S-14 rather than the theoretical one it used to be: a person recorded at the desk may never have
/// given one, and they simply receive nothing.
/// </param>
public record ClassBooking(
    Guid BookingId,
    Guid MemberId,
    string? UserId,
    string DisplayName,
    string Email,
    DateTimeOffset BookedAt);

/// <summary>
/// Why a booking write was refused.
///
/// <para>
/// EVERY ONE OF THESE IS A 409, which is what makes this union different from
/// <see cref="ClassFailure"/>. A booking request carries no fields to get wrong — the class is in the
/// route and the member is the caller — so there is nothing here that could be a 400. What can fail
/// is always a disagreement with state the caller could not see: the class was cancelled, it has
/// started, they already hold a spot, the spots ran out, they had no booking to cancel.
/// </para>
///
/// <para>
/// Reasons: <c>class_cancelled</c>, <c>class_started</c>, <c>already_booked</c>, <c>class_full</c>,
/// <c>not_booked</c>, <c>conflict</c>, and — on the admin route only — <c>member_blocked</c>. Adding
/// one means adding it to the SPA's BookingFailure union too. A missing class is a 404 and not a
/// reason — an unknown id is not a state disagreement, and neither is a member id nobody issued.
/// </para>
///
/// <para>
/// <c>member_blocked</c> cannot occur on the member's own route: the <c>ActiveMember</c> policy
/// already requires an active membership, so a blocked member never reaches the handler. It exists
/// because the admin route names its member in the body, where no policy can vouch for them (S-14).
/// </para>
///
/// <para>
/// <c>conflict</c> is the only one that is not a product rule: it means the retry loop lost its race
/// on every one of its attempts, which for a club of dozens should never happen. It exists so the
/// caller is told to try again rather than shown a 500.
/// </para>
/// </summary>
public record BookingFailure(string Reason);

/// <summary>
/// Who the admin is booking (S-14, AM-007).
///
/// <para>
/// A MEMBER ID IN A BODY, which every other booking route refuses on principle — see
/// <c>BookAsync</c>. The exception is deliberate and is the whole point of the route: the admin is
/// acting for somebody else, typically somebody with no account who could not book for themselves.
/// What makes it safe is the <c>Admin</c> policy on the group, not the shape of the request.
/// </para>
/// </summary>
public record AdminBookingRequest(Guid MemberId);

/// <summary>
/// Booking and cancelling a spot (prd.md US-01, FR-008, FR-009, FR-010).
///
/// <para>
/// THE NO-OVERBOOKING GUARANTEE IS IMPLEMENTED HERE, and it rests entirely on one line in
/// <see cref="BookAsync"/>: the rotation of <see cref="Class.ConcurrencyStamp"/>. A booking inserts a
/// row into Bookings and touches nothing on Classes by itself, so without that assignment EF issues
/// no UPDATE against Classes, no WHERE clause carries the token, and two members racing for the last
/// spot both commit. Rotating the stamp is not bookkeeping — it is the mechanism. Every write that
/// changes how many spots are taken must do it, cancellation included: a cancel and a book racing for
/// the same last spot must not both believe they won.
/// </para>
///
/// <para>
/// There is no explicit transaction and there must not be one. A single SaveChangesAsync is already
/// atomic, and the stamp is what pulls the capacity CHECK inside that atom; opening a transaction
/// would additionally require Database.CreateExecutionStrategy().ExecuteAsync, because
/// EnableRetryOnFailure is on (Program.cs) and BeginTransaction throws at runtime without it.
/// </para>
///
/// <para>
/// Two groups, both under ActiveMember and both applying the policy at the GROUP, per
/// <see cref="ClassEndpoints"/>: a Pending or Blocked account cannot book, cancel, or list. That is
/// the whole of the authorization story on the member side — a booking belongs to its caller by
/// construction, since every route resolves the member from the cookie and never from the request.
/// </para>
/// </summary>
public static class BookingEndpoints
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
    private const int MaxAttempts = 10;

    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        // Shares the /api/classes prefix with ClassEndpoints on purpose: a booking is addressed as a
        // sub-resource of the class it claims. Two MapGroups over one prefix is fine - routes are
        // matched by pattern, not by group.
        var classBookings = app.MapGroup("/api/classes")
            .WithTags("Bookings")
            .RequireAuthorization(AuthorizationPolicyNames.ActiveMember);

        classBookings.MapPost("/{classId:guid}/bookings", BookAsync);
        classBookings.MapDelete("/{classId:guid}/bookings/mine", CancelMineAsync);

        var myBookings = app.MapGroup("/api/bookings")
            .WithTags("Bookings")
            .RequireAuthorization(AuthorizationPolicyNames.ActiveMember);

        myBookings.MapGet("/mine", GetMineAsync);

        // The admin's half. Under Admin rather than ActiveMember, and addressed under
        // /api/admin/classes so it sits beside the management endpoints it belongs with.
        var adminBookings = app.MapGroup("/api/admin/classes")
            .WithTags("Bookings")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        adminBookings.MapGet("/{classId:guid}/bookings", GetForClassAsync);
        adminBookings.MapPost("/{classId:guid}/bookings", BookForMemberAsync);
        adminBookings.MapDelete("/{classId:guid}/bookings/{bookingId:guid}", ReleaseAsync);

        return app;
    }

    /// <summary>
    /// Claims a spot.
    ///
    /// <para>
    /// Returns the class AS IT NOW STANDS rather than the booking, so the schedule tile can be
    /// replaced in place without a refetch. That is why the response shape is
    /// <see cref="ScheduledClass"/> and not something booking-shaped: the client already knows it
    /// booked — what it does not know is the new free-spot count.
    /// </para>
    ///
    /// <para>
    /// THE LOOP IS NOT DEFENSIVE PROGRAMMING. Everything between the re-read and the save is a check
    /// against state another request may change a microsecond later; the stamp turns "check then
    /// write" into one atomic operation, and the loop is what turns a lost race into a fresh read
    /// rather than a refusal the member did not deserve. Each attempt must re-read, because the
    /// capacity count it refused or accepted on is exactly what the lost race invalidated.
    /// </para>
    /// </summary>
    private static async Task<IResult> BookAsync(
        Guid classId,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IClassStore classes,
        IBookingStore bookings,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // FROM THE COOKIE, NEVER FROM THE BODY. A booking belongs to its caller by construction, and
        // that is the whole of the authorization story on the member side.
        //
        // The account id is still written beside the member id for one more release — see the entity —
        // so both are resolved here.
        var memberUserId = userManager.GetUserId(principal);
        var memberId = principal.GetMemberId();
        if (memberUserId is null || memberId is null)
        {
            return Results.Unauthorized();
        }

        return await TryBookAsync(
            classId, memberId.Value, memberUserId, classes, bookings, unitOfWork, timeProvider,
            cancellationToken);
    }

    /// <summary>
    /// Books a spot for a member the caller has already established the right to book for (S-14).
    ///
    /// <para>
    /// ONE LOOP, TWO CALLERS, and that is the point of the extraction rather than a tidiness
    /// exercise: the no-overbooking protocol is a specific sequence — re-read, check, insert, rotate
    /// the stamp, save, discard and retry — and a second copy of it written for the admin route would
    /// be a second chance to get that sequence subtly wrong. <c>AdminBookingEndpointTests</c> runs
    /// the concurrency race through the ADMIN route for exactly this reason.
    /// </para>
    ///
    /// <para>
    /// It takes no <see cref="ClaimsPrincipal"/> on purpose. Who may book for whom is settled by the
    /// caller — the cookie on the member route, the <c>Admin</c> policy on the other — and passing
    /// the principal in here would invite a future authorization check in the one place that must
    /// stay a pure mechanism.
    /// </para>
    /// </summary>
    /// <param name="memberUserId">
    /// The member's account, written beside <paramref name="memberId"/> for one more release. NULL
    /// for a member with no login — the case this slice exists for.
    /// </param>
    private static async Task<IResult> TryBookAsync(
        Guid classId,
        Guid memberId,
        string? memberUserId,
        IClassStore classes,
        IBookingStore bookings,
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

            bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                ClassId = entity.Id,
                MemberUserId = memberUserId,
                MemberId = memberId,
                Status = BookingStatus.Active,
                CreatedAt = now,
            });

            // THE GUARANTEE. Read the class doc comment before touching this line: without it the
            // count above is a guess that happens to be right most of the time.
            entity.ConcurrencyStamp = Guid.NewGuid().ToString();

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
                return Results.Ok(ClassEndpoints.ToDto(
                    entity, entity.ClassType, entity.InstructorAccount, bookedCount + 1));
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
    /// Books somebody else in (S-14, AM-007).
    ///
    /// <para>
    /// THE REASON THE SLICE NEEDED THIS: a member with no account cannot tap Book, so without an
    /// admin route the club could record them, plan for them, and never get them into a class. It
    /// works for members WITH accounts too — a phone call to the desk is a perfectly ordinary way to
    /// sign up.
    /// </para>
    ///
    /// <para>
    /// Two pre-checks the member route cannot need, both BEFORE the loop because neither depends on
    /// state the loop re-reads: an unknown member is a 404, exactly like an unknown class, and a
    /// blocked one is refused. The rest is <see cref="TryBookAsync"/> — the same loop, the same
    /// refusals, the same guarantee — and it deliberately grants no exemptions: an admin cannot
    /// overfill a class or book into one that has started, because the club would then have to honour
    /// a seat that does not exist.
    /// </para>
    /// </summary>
    private static async Task<IResult> BookForMemberAsync(
        Guid classId,
        AdminBookingRequest request,
        IMemberStore members,
        IClassStore classes,
        IBookingStore bookings,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(request.MemberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        // A blocked member may not attend, and the block cascade would cancel this booking the next
        // time anyone blocked them anyway. Refusing is the honest answer rather than writing a spot
        // the club has already decided not to honour.
        if (member.Status != MembershipStatus.Active)
        {
            return Refuse("member_blocked");
        }

        return await TryBookAsync(
            classId, member.Id, member.UserId, classes, bookings, unitOfWork, timeProvider,
            cancellationToken);
    }

    /// <summary>
    /// Releases the caller's own spot, keeping the booking in history (prd.md FR-009).
    ///
    /// <para>
    /// NO TIME RULE AT ALL, deliberately. prd.md §Non-Goals locks free-cancel-anytime, so a member
    /// may cancel after the class has started or ended. The cancelled row stays, which is what makes
    /// re-booking the same class legal — the uniqueness index is filtered to active rows precisely so
    /// that history does not hold the pair hostage.
    /// </para>
    ///
    /// <para>
    /// Rotates the stamp for the same reason <see cref="BookAsync"/> does, even though freeing a spot
    /// can never overbook on its own: a cancel and a book racing for the last spot must serialize, or
    /// the booker's capacity check reads a count the cancel is in the middle of changing.
    /// </para>
    /// </summary>
    private static async Task<IResult> CancelMineAsync(
        Guid classId,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IClassStore classes,
        IBookingStore bookings,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var entity = await classes.FindAsync(classId, cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            var booking = await bookings.FindActiveAsync(classId, memberId.Value, cancellationToken);
            if (booking is null)
            {
                return Refuse("not_booked");
            }

            var bookedCount = await bookings.CountActiveAsync(classId, cancellationToken);

            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = timeProvider.GetUtcNow();

            entity.ConcurrencyStamp = Guid.NewGuid().ToString();

            var outcome = await unitOfWork.TrySaveAsync(cancellationToken);
            if (outcome == SaveOutcome.Saved)
            {
                // Minus one, exact for the same reason BookAsync's plus one is: the stamp serialized
                // this write against every other STAMPED booking write on the class - and, with the
                // same single exception, the block cascade, whose effect can only be to free further
                // spots this number does not yet know about.
                return Results.Ok(ClassEndpoints.ToDto(
                    entity, entity.ClassType, entity.InstructorAccount, bookedCount - 1));
            }

            unitOfWork.DiscardChanges();
        }

        return Refuse("conflict");
    }

    /// <summary>
    /// The caller's upcoming bookings, chronological (prd.md FR-010).
    ///
    /// <para>
    /// UPCOMING ONLY, and the cut is by the class's start rather than by the booking's age. A member
    /// looking at "Moje zajęcia" is looking at what they still have to attend; the past belongs to
    /// history, which this slice keeps but does not display.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetMineAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IBookingQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await query.GetUpcomingForMemberAsync(
            memberId.Value, timeProvider.GetUtcNow(), cancellationToken));
    }

    /// <summary>
    /// Who signed up for a class (prd.md FR-014).
    ///
    /// <para>
    /// Active only. The admin is looking at who to expect, not at who changed their mind — the
    /// cancelled rows are history the application keeps but does not put in front of anyone.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetForClassAsync(
        Guid classId,
        IBookingQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetForClassAsync(classId, cancellationToken));

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
    /// Rotates the class stamp and retries exactly like the member's cancel, so an admin releasing a
    /// spot and a member claiming it cannot both win. 204 rather than the class, because the admin
    /// screen is a list of people and reloads that list rather than a tile.
    /// </para>
    /// </summary>
    private static async Task<IResult> ReleaseAsync(
        Guid classId,
        Guid bookingId,
        IClassStore classes,
        IBookingStore bookings,
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

            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = timeProvider.GetUtcNow();

            entity.ConcurrencyStamp = Guid.NewGuid().ToString();

            if (await unitOfWork.TrySaveAsync(cancellationToken) == SaveOutcome.Saved)
            {
                return Results.NoContent();
            }

            unitOfWork.DiscardChanges();
        }

        return Refuse("conflict");
    }

    /// <summary>
    /// Every booking refusal, as a 409 — see <see cref="BookingFailure"/> for why there is no 400 in
    /// this file.
    /// </summary>
    private static IResult Refuse(string reason) =>
        Results.Json(new BookingFailure(reason), statusCode: 409);
}

/// <summary>
/// The write seam over the booking table, so Application does not reference EF Core (AGENTS.md
/// layering). Implemented in Infrastructure.
///
/// <para>
/// Nothing here saves — the endpoint commits through <see cref="IUnitOfWork"/>. That is what lets the
/// booking insert and the class's stamp rotation land in ONE SaveChangesAsync, which is the entire
/// no-overbooking design.
/// </para>
///
/// <para>
/// Intention-revealing methods rather than a generic repository; this codebase has no repository
/// pattern and this slice does not introduce one.
/// </para>
/// </summary>
public interface IBookingStore
{
    void Add(Booking entity);

    /// <summary>
    /// The member's active booking on this class, or null. TRACKED — the cancel path mutates what
    /// this returns and expects the change tracker to notice.
    /// </summary>
    Task<Booking?> FindActiveAsync(
        Guid classId, Guid memberId, CancellationToken cancellationToken);

    /// <summary>
    /// One booking by id, TRACKED, for the admin's release. Returns it whatever its status and
    /// whichever class it belongs to — the caller checks both, because "wrong class" and "already
    /// cancelled" are answers it has to distinguish.
    /// </summary>
    Task<Booking?> FindByIdAsync(Guid bookingId, CancellationToken cancellationToken);

    /// <summary>
    /// How many spots this class currently has taken.
    ///
    /// <para>
    /// THERE IS NO STORED COUNTER; the count IS the number of active rows. Reading it is only half of
    /// a capacity check — the other half is rotating <see cref="Class.ConcurrencyStamp"/> before
    /// saving, without which this number is stale by the time it is acted on.
    /// </para>
    /// </summary>
    Task<int> CountActiveAsync(Guid classId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this class has EVER been booked, cancelled bookings included.
    ///
    /// <para>
    /// The delete guard, and deliberately wider than <see cref="CountActiveAsync"/>. Both FKs on
    /// Bookings are RESTRICT, so a class with any booking row at all cannot be deleted by the
    /// database either — a guard that counted only active rows would answer "go ahead" and then let
    /// the save fail with a foreign-key violation. Widening it also states the product rule
    /// honestly: DELETE erases a class created by mistake, and a class somebody once signed up for
    /// is not one.
    /// </para>
    /// </summary>
    Task<bool> HasAnyAsync(Guid classId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks every active booking this member holds on a class starting after
    /// <paramref name="asOf"/> as cancelled, without saving.
    ///
    /// <para>
    /// For the block cascade: a blocked member cannot attend, so the club must not keep promising
    /// their seats to nobody. PAST bookings are deliberately untouched — they are attendance history,
    /// and rewriting them would be falsifying it.
    /// </para>
    ///
    /// <para>
    /// Does not save, and does not rotate any class stamp. Safe with respect to capacity because
    /// cancelling only ever FREES spots: a concurrent booker reading a pre-cascade count is being
    /// conservative, never permissive.
    /// </para>
    /// </summary>
    Task CancelActiveFutureForMemberAsync(
        Guid memberId, DateTimeOffset asOf, CancellationToken cancellationToken);
}

/// <summary>
/// The read counterpart. AsNoTracking projections for display, mirroring
/// <see cref="IClassScheduleQuery"/>; nothing here is ever fed back into a write.
/// </summary>
public interface IBookingQuery
{
    /// <summary>
    /// The member's active bookings on SCHEDULED classes starting at or after
    /// <paramref name="from"/>, ordered by the class's start.
    ///
    /// <para>
    /// BOTH STATUSES MATTER SINCE S-09. A cancelled class keeps its bookings Active on purpose, so
    /// the class's own status is the only thing that takes it off this list — see the implementation
    /// for why that predicate is load-bearing rather than defensive.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<MyBooking>> GetUpcomingForMemberAsync(
        Guid memberId, DateTimeOffset from, CancellationToken cancellationToken);

    /// <summary>
    /// Everyone actively signed up for a class, in the order they booked — so the club can see who
    /// was first.
    ///
    /// <para>
    /// TWO CALLERS SINCE S-09, and the second one is not a screen. The admin's "Zapisani" panel
    /// displays this; <see cref="ClassEndpoints"/>' cancel and edit paths use the SAME projection as
    /// the fan-out list for the email and push owed to everyone holding a spot. That is deliberate
    /// rather than convenient: a member the club can see on the list and a member the club must tell
    /// are by definition the same set, and a second query would let the two drift.
    /// </para>
    ///
    /// <para>
    /// ACTIVE ONLY, which is what makes it right for the fan-out: somebody who released their spot
    /// is owed nothing.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ClassBooking>> GetForClassAsync(
        Guid classId, CancellationToken cancellationToken);
}
