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
/// admin endpoint. <see cref="MemberId"/> travels for the same reason it does on
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
/// route and the member is named by the caller's own request — so there is nothing here that could be
/// a 400. What can fail is always a disagreement with state the caller could not see: the class was
/// cancelled, it has started, the person already holds a spot, the spots ran out, their karnet does
/// not cover the day or has no entries left.
/// </para>
///
/// <para>
/// Reasons: <c>class_cancelled</c>, <c>class_started</c>, <c>already_booked</c>, <c>class_full</c>,
/// <c>member_blocked</c>, <c>no_valid_pass</c>, <c>no_entries_left</c>, <c>conflict</c>. Adding one
/// means adding it to the SPA's BookingFailure union too. A missing class is a 404 and not a reason —
/// an unknown id is not a state disagreement, and neither is a member id nobody issued.
/// </para>
///
/// <para>
/// <c>not_booked</c> IS GONE as of S-16 and must not come back. It belonged to the member's own
/// cancel route, which produced it when there was no spot to release; the staff release addresses a
/// booking by id and answers 404 for one that does not exist, belongs to another class, or is already
/// cancelled — see <c>ReleaseAsync</c> for why those three collapse.
/// </para>
///
/// <para>
/// THE TWO KARNET REFUSALS ARE DIFFERENT QUESTIONS AND MUST STAY DISTINGUISHABLE (S-16).
/// <c>no_valid_pass</c> means the member holds no pass covering the class's club-local date — either
/// they have none at all, or the one they have does not reach that day; those two are indistinguishable
/// from the lookup's side and are deliberately one reason, because the club's answer to both is "sell
/// them a karnet". <c>no_entries_left</c> means a pass DOES cover the date and its entries are all
/// spent, which is a different conversation at the desk.
/// </para>
///
/// <para>
/// <c>member_blocked</c> exists because the staff route names its member in the BODY, where no policy
/// can vouch for them (S-14). It used to be documented as impossible on the member's own route; that
/// route is gone, so it is now simply one of the ordinary refusals.
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
/// Who the staff member is booking (S-14 AM-007, widened by S-16 MP-02).
///
/// <para>
/// A MEMBER ID IN A BODY. That used to be the exception among booking routes — every member-facing
/// one took its member from the cookie on principle — and since MP-01 removed those routes it is
/// simply how booking works: the person being booked is by definition not the person asking, and may
/// have no account to be a caller with at all. What makes it safe is the <c>TrainerOrAdmin</c> policy
/// on the group plus each handler's instructor check, never the shape of the request.
/// </para>
/// </summary>
public record AdminBookingRequest(Guid MemberId);

/// <summary>
/// Booking and releasing a spot (prd.md FR-010, FR-014; S-16 MP-01 and MP-02).
///
/// <para>
/// SELF-SERVICE BOOKING IS GONE. prd.md US-01, FR-008 and FR-009 described a member who books and
/// cancels their own spot; S-16 retires all three as member-facing capabilities. The behaviour
/// survives on the staff routes — an admin anywhere, a trainer on the classes they instruct — and
/// what a member has left here is a read of their own upcoming bookings.
/// </para>
///
/// <para>
/// THE NO-OVERBOOKING GUARANTEE IS IMPLEMENTED HERE, and it rests entirely on one line in
/// <see cref="TryBookAsync"/>: the rotation of <see cref="Class.ConcurrencyStamp"/>. A booking inserts a
/// row into Bookings and touches nothing on Classes by itself, so without that assignment EF issues
/// no UPDATE against Classes, no WHERE clause carries the token, and two members racing for the last
/// spot both commit. Rotating the stamp is not bookkeeping — it is the mechanism. Every write that
/// changes how many spots are taken must do it, a release included: a release and a booking racing
/// for the same last spot must not both believe they won.
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
/// ONE member-facing group since S-16, and it is READ-ONLY. <c>GET /api/bookings/mine</c> is all a
/// member has: MP-01 removed self-service booking and cancellation entirely, because the karnet
/// decides who trains and the desk is what knows whether somebody holds one. The route still resolves
/// the member from the COOKIE and never from the request, which is what makes "your bookings" mean
/// yours by construction.
///
/// <para>
/// THE STAFF GROUP IS THE EXCEPTION, and it is the only place in this application where the group
/// policy is not the whole answer (S-16, MP-02). It admits trainers as well as admins, and a trainer
/// may act only on classes they personally instruct — a narrowing that depends on the class in the
/// route and therefore cannot live in a policy. See the comment on that group before adding anything
/// to it.
/// </para>
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
        // THE MEMBER'S WRITE ROUTES ARE GONE (S-16, MP-01). POST /api/classes/{id}/bookings and
        // DELETE /api/classes/{id}/bookings/mine were removed with their handlers: booking is a staff
        // action now, because the karnet is what entitles somebody to a spot and the desk is what
        // knows whether they hold one. The member keeps the READ below - they still see what they are
        // committed to, they just do not change it themselves.
        var myBookings = app.MapGroup("/api/bookings")
            .WithTags("Bookings")
            .RequireAuthorization(AuthorizationPolicyNames.ActiveMember);

        myBookings.MapGet("/mine", GetMineAsync);

        // THE STAFF HALF. Under TrainerOrAdmin since S-16, and addressed under /api/admin/classes so
        // it sits beside the management endpoints it belongs with. The path still says "admin"; the
        // policy no longer does, and that is not an oversight - renaming the route would break the
        // SPA's whole booking surface for a cosmetic gain.
        //
        // ------------------------------------------------------------------
        // THE GROUP POLICY ALONE IS NO LONGER SUFFICIENT HERE. READ THIS BEFORE ADDING AN ENDPOINT.
        //
        // TrainerOrAdmin admits every trainer in the club, and a trainer may act only on the classes
        // they personally instruct (MP-02). That narrowing cannot live in the policy: it depends on
        // the CLASS in the route, which no policy can see. So each handler below carries an inline
        // ownership check against Class.InstructorMemberId, and ANY endpoint added to this group must
        // carry the same one - a new route inherits the group's admission and none of its narrowing,
        // which would silently let a trainer act on somebody else's class.
        //
        // Inline rather than an IAuthorizationHandler because that is what this codebase does
        // everywhere: authorization here is either a group policy or a hand-rolled field check, and
        // nothing in this repository registers a resource handler. This is the known cost of that
        // choice, written down where the next person will read it.
        // ------------------------------------------------------------------
        var staffBookings = app.MapGroup("/api/admin/classes")
            .WithTags("Bookings")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        staffBookings.MapGet("/{classId:guid}/bookings", GetForClassAsync);
        staffBookings.MapPost("/{classId:guid}/bookings", BookForMemberAsync);
        staffBookings.MapDelete("/{classId:guid}/bookings/{bookingId:guid}", ReleaseAsync);

        return app;
    }

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
    private static async Task<IResult> TryBookAsync(
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
            var entriesUsed = await bookings.CountActiveForPassAsync(pass.Id, cancellationToken);
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
                return Results.Ok(ClassEndpoints.ToDto(
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
        ClaimsPrincipal principal,
        IMemberStore members,
        IClassStore classes,
        IBookingStore bookings,
        IMembershipPassStore passes,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // OWNERSHIP FIRST, before anything about the member is revealed. Checking it after the member
        // lookup would answer 404 for a member id that does not exist even to a trainer with no
        // business on this class, which turns the route into a probe for which member ids are real.
        var entity = await classes.FindAsync(classId, cancellationToken);
        if (entity is null)
        {
            return Results.NotFound();
        }

        if (!MayActOn(principal, entity))
        {
            return NotYourClass();
        }

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
            classId, member.Id, classes, bookings, passes, unitOfWork, timeProvider,
            cancellationToken);
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
        ClaimsPrincipal principal,
        IClassStore classes,
        IBookingQuery query,
        CancellationToken cancellationToken)
    {
        // The class is loaded for the ownership check and for nothing else. That is one extra read on
        // a read-only route, which is the price of the narrowing - and it is a keyed lookup.
        var entity = await classes.FindAsync(classId, cancellationToken);
        if (entity is null)
        {
            return Results.NotFound();
        }

        if (!MayActOn(principal, entity))
        {
            return NotYourClass();
        }

        return Results.Ok(await query.GetForClassAsync(classId, cancellationToken));
    }

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
        ClaimsPrincipal principal,
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

            // Inside the loop rather than above it, because the class is re-read on every attempt and
            // the check must be against the row this attempt is actually acting on. Reassigning an
            // instructor mid-retry is vanishingly unlikely; deciding on a stale copy of the field
            // that authorises the write is not a thing to leave to likelihood.
            if (!MayActOn(principal, entity))
            {
                return NotYourClass();
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
            await ReturnEntryAsync(booking, passes, cancellationToken);

            if (await unitOfWork.TrySaveAsync(cancellationToken) == SaveOutcome.Saved)
            {
                return Results.NoContent();
            }

            unitOfWork.DiscardChanges();
        }

        return Refuse("conflict");
    }

    /// <summary>
    /// Whether this caller may act on this class (S-16, MP-02).
    ///
    /// <para>
    /// THE FIRST RESOURCE-OWNERSHIP CHECK IN THIS CODEBASE. Until now every authorization decision
    /// here was either a group policy or a field check about the CALLER; this one compares the caller
    /// against a property of the resource, which is why it could not stay in the policy — a policy
    /// cannot see the class in the route.
    /// </para>
    ///
    /// <para>
    /// An ADMIN PASSES UNCONDITIONALLY and is checked first, so an owner who also teaches is never
    /// narrowed to their own classes by holding the Trainer role as well. Roles are additive in this
    /// product (see <see cref="ApplicationRoles"/>) and the realistic staff account holds both.
    /// </para>
    /// </summary>
    private static bool MayActOn(ClaimsPrincipal principal, Class entity) =>
        principal.IsInRole(ApplicationRoles.Admin)
        || principal.GetMemberId() == entity.InstructorMemberId;

    /// <summary>
    /// 403, NOT 404, when a trainer reaches for a class they do not instruct.
    ///
    /// <para>
    /// The class's existence is not a secret — every member can see it on the schedule, instructor
    /// included. What is refused is the ACTION, and saying so is the honest answer. Hiding it behind a
    /// 404 would also make the SPA's error handling wrong: "this class is gone, refresh" and "this is
    /// not your class" call for different screens.
    /// </para>
    /// </summary>
    private static IResult NotYourClass() => Results.Forbid();

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
    private static async Task ReturnEntryAsync(
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
    /// How many entries of this karnet are currently spent (S-16).
    ///
    /// <para>
    /// THERE IS NO STORED COUNTER here either; entries used IS the number of active bookings carrying
    /// this pass's id. Reading it is only half of an entry check — the other half is rotating
    /// <see cref="Domain.Members.MembershipPass.ConcurrencyStamp"/> before saving, without which this
    /// number is stale by the time it is acted on. Exactly the shape
    /// <see cref="CountActiveAsync"/> has, one pool up.
    /// </para>
    ///
    /// <para>
    /// Seeks IX_Bookings_MembershipPassId_Status. It runs on EVERY attempt of the booking retry loop,
    /// so it must be a seek.
    /// </para>
    /// </summary>
    Task<int> CountActiveForPassAsync(Guid passId, CancellationToken cancellationToken);

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
