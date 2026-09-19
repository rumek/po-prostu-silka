using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

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
