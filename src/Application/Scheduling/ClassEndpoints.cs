using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One class occurrence as the schedule and the admin list see it. This is a CONTRACT the SPA's class
/// service mirrors — renaming a field breaks both screens silently.
///
/// <para>
/// TWO OF THESE FIELDS ARE RESOLVED, NOT STORED (prd-v2 FR-007, FR-010). <see cref="Name"/> and
/// <see cref="Description"/> come from the occurrence's ClassType and <see cref="Instructor"/> from
/// the assigned account's display name — the occurrence itself holds none of the three. That is what
/// makes correcting a typo on the type correct it on every week at once, past occurrences included.
/// </para>
///
/// <para>
/// <see cref="Capacity"/> and <see cref="DurationMinutes"/> are the opposite: COPIES taken at
/// creation, owned by this occurrence, and never re-read from the type. The asymmetry is deliberate
/// and load-bearing — capacity resolved through the type would let a type edit move the value the
/// no-overbooking guarantee is checked against.
/// </para>
///
/// <para>
/// Status crosses the wire as the enum NAME ("Scheduled" / "Cancelled"), not its int, for the same
/// reason AccountStatus does: the numeric values exist for persistence stability, and a badge keyed
/// on 1 would break the day someone renumbers.
/// </para>
///
/// <para>
/// FreeSpots is <see cref="Capacity"/> until S-08 — see IClassScheduleQuery. There is no Room: the
/// club has one, so the field never carried information (prd-v2 FR-011).
/// </para>
///
/// <para>
/// <see cref="InstructorUserId"/> REACHES MEMBERS, and that is a considered decision rather than an
/// oversight. ClassScheduleQuery projects one shape for both the admin list and the member schedule,
/// so every active member receives the trainer's Identity id. The member SPA never reads it — the
/// field exists for the admin form's trainer select — and it grants nothing on its own, since every
/// admin surface is policy-gated. Splitting the projection in two was weighed and declined: it would
/// hand S-07 and S-08 a branch to maintain for a field with no exploit path. Revisit if the id ever
/// becomes guessable-to-useful, e.g. if a member-facing endpoint ever accepts a user id.
/// </para>
/// </summary>
public record ScheduledClass(
    Guid Id,
    Guid ClassTypeId,
    string Name,
    string? Description,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string InstructorUserId,
    string Instructor,
    int Capacity,
    int FreeSpots,
    string Status);

/// <summary>
/// Create/edit payload. Same shape for both — an edit replaces every field it is allowed to change.
///
/// <para>
/// A FORM OF SELECTIONS, NOT OF TEXT (prd-v2 US-01). There is no name and no room to type;
/// <see cref="ClassTypeId"/> and <see cref="InstructorUserId"/> are references the client picked from
/// two lists. What remains typed are the two numbers — and they arrive here PREFILLED from the type's
/// defaults, which the admin may have overridden for this session.
/// </para>
///
/// <para>
/// <see cref="ClassTypeId"/> is required on an edit too, but only so the server can refuse a change
/// to it: the type is immutable once an occurrence exists (<c>class_type_immutable</c>).
/// </para>
/// </summary>
public record ClassRequest(
    Guid ClassTypeId,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string InstructorUserId,
    int Capacity);

/// <summary>How many following weeks to copy a class into.</summary>
public record DuplicateRequest(int Weeks);

/// <summary>
/// What a duplicate actually did. NOT a bare success: a batch where some weeks collided is a partial
/// success, and reporting it as "done" would leave the admin believing in classes that were never
/// created.
/// </summary>
/// <param name="Created">How many copies were written.</param>
/// <param name="SkippedWeeks">
/// 1-based week offsets refused because another class already occupies that time. The REASON changed
/// in S-06 — it used to be a room collision — but the shape and the partial-success behaviour did
/// not (prd-v2 FR-013).
/// </param>
public record DuplicateResult(int Created, IReadOnlyList<int> SkippedWeeks);

/// <summary>
/// Why a class write was refused. All 400 except the six 409s — <c>time_conflict</c>,
/// <c>has_bookings</c>, <c>capacity_below_bookings</c>, <c>conflict</c>, <c>class_started</c> and
/// <c>already_cancelled</c> — each a disagreement with existing state rather than bad input.
///
/// <para>
/// Reasons: <c>missing_field</c>, <c>invalid_capacity</c>, <c>invalid_duration</c>,
/// <c>starts_in_past</c>, <c>invalid_weeks</c>, <c>time_conflict</c>, <c>unknown_class_type</c>,
/// <c>inactive_class_type</c>, <c>class_type_immutable</c>, <c>unknown_instructor</c>,
/// <c>instructor_not_trainer</c>, <c>has_bookings</c>, <c>capacity_below_bookings</c>,
/// <c>conflict</c>, <c>class_started</c>, <c>already_cancelled</c>. Adding one here means adding it
/// to the SPA's ClassFailure union too — that type mirrors this one field for field.
/// </para>
///
/// <para>
/// S-09 ADDED THE LAST TWO, both 409s and both belonging to <see cref="CancelAsync"/>.
/// <c>class_started</c> reuses the name BookingEndpoints already gives the same disagreement — the
/// class is no longer in the future — so the API speaks one vocabulary rather than two; it is a
/// refusal here because telling members a class that already happened is cancelled is
/// disinformation, and there is no undo. <c>already_cancelled</c> keeps the transition one-way and,
/// with the stamp rotation, keeps it exactly-once: two admins cancelling the same class must not
/// send two rounds of email.
/// </para>
///
/// <para>
/// S-08 ADDED THE LAST THREE, ALL 409s. <c>has_bookings</c> and <c>capacity_below_bookings</c> are
/// the two ways an admin action would otherwise break the no-overbooking guarantee from the
/// management side; <c>conflict</c> means a booking committed between this request's check and its
/// write, so the admin is asked to look again rather than shown a 500.
/// </para>
///
/// <para>
/// ONE MORE REASON TRAVELS IN THIS SHAPE WITHOUT BELONGING TO THAT UNION: <c>invalid_range</c>,
/// returned by the two READ endpoints when the requested date window is inverted or too wide. The
/// record is reused because the wire shape is identical, but no write path can ever return it — so
/// the SPA models it as its own <c>ScheduleReadFailure</c> rather than widening <c>ClassFailure</c>,
/// which would force the class form to carry a message for a refusal it cannot receive.
/// </para>
/// </summary>
public record ClassFailure(string Reason);

/// <summary>
/// The class schedule (prd.md FR-007) and the admin's management of it (prd-v2 US-01, FR-008 –
/// FR-013).
///
/// Two groups with different policies: members read the schedule under ActiveMember, admins manage
/// under Admin. The policy is applied at each GROUP, not per endpoint, so an endpoint added here
/// later cannot accidentally ship unauthenticated.
///
/// <para>
/// THE ONE RULE THIS FILE EXISTS TO PROTECT (prd-v2 FR-007): the class type is loaded to be
/// VALIDATED, never to be read from. <see cref="CreateAsync"/> copies duration and capacity out of
/// the REQUEST — the client prefilled them from the type and the admin may have overridden them —
/// and <see cref="UpdateAsync"/> and <see cref="DuplicateAsync"/> never touch the type's defaults at
/// all. Reading <c>DefaultCapacity</c> here would let a later type edit change the capacity of a
/// class that already has bookings. ClassEndpointTests pins this; nothing in the compiler does.
/// </para>
///
/// CANCEL AND DELETE ARE DIFFERENT ACTIONS, and S-09 is where the difference became real. DELETE is
/// for a MISTAKE — a class created seconds ago, which S-08's guard refuses once anybody has ever
/// booked it. <see cref="CancelAsync"/> is FR-013's state transition: the class stays, its bookings
/// and history stay, and everyone holding a spot is emailed and pushed in the SAME unit of work that
/// performs the flip. An admin who meant "this is not happening" wants the second one.
/// </summary>
public static class ClassEndpoints
{
    /// <summary>
    /// How far ahead the member schedule reaches WHEN THE CALLER ASKS FOR NO RANGE. A fortnight is one
    /// round-trip of a few dozen rows for a single club, which is what keeps this inside the PRD's
    /// ~1 s perceived-response NFR.
    ///
    /// <para>
    /// Since S-07 this is the FALLBACK, not the only answer: the calendar asks for the window it is
    /// showing. Keeping the fallback exactly as it was is what makes the parameters additive — a
    /// client that sends nothing still gets the fortnight it got before.
    /// </para>
    /// </summary>
    private const int ScheduleWindowDays = 14;

    /// <summary>
    /// The widest window either read endpoint will answer for (prd-v2 FR-015, FR-016).
    ///
    /// <para>
    /// Two months. Comfortably above anything the calendar asks for — it requests one day or one week
    /// — and low enough that a malformed or hostile client cannot ask for a decade of rows in one
    /// round trip. The admin list was UNBOUNDED before this slice, so this is a tightening — and
    /// since 2026-09-03 it binds the admin's no-parameter fallback too: once the calendar shipped,
    /// every caller sends a window, so the unbounded path had no client left and was pure surface.
    /// </para>
    /// </summary>
    private const int MaxRangeDays = 62;

    /// <summary>Bounds on a duplicate batch. Above this it is a recurring series, which the PRD parks.</summary>
    private const int MaxDuplicateWeeks = 8;

    /// <summary>
    /// Bounds on an occurrence's own duration and capacity.
    ///
    /// <para>
    /// DUPLICATED FROM ClassTypeEndpoints ON PURPOSE, not shared through it. An occurrence may
    /// legitimately override its type's defaults (prd-v2 FR-008), so it cannot inherit the type's
    /// bounds by reference any more than it inherits its numbers — the whole point is that the two
    /// values are independent after creation. Keep the four constants in step by hand.
    /// </para>
    /// </summary>
    private const int MinDurationMinutes = 1;

    private const int MaxDurationMinutes = 480;

    private const int MinCapacity = 1;

    private const int MaxCapacity = 200;

    public static IEndpointRouteBuilder MapClassEndpoints(this IEndpointRouteBuilder app)
    {
        var schedule = app.MapGroup("/api/classes")
            .WithTags("Schedule")
            .RequireAuthorization(AuthorizationPolicyNames.ActiveMember);

        schedule.MapGet("/", GetScheduleAsync);

        var admin = app.MapGroup("/api/admin/classes")
            .WithTags("Schedule")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        admin.MapGet("/", GetAdminClassesAsync);
        admin.MapGet("/{id:guid}", GetByIdAsync);
        admin.MapPost("/", CreateAsync);
        admin.MapPut("/{id:guid}", UpdateAsync);
        admin.MapDelete("/{id:guid}", DeleteAsync);
        admin.MapPost("/{id:guid}/cancel", CancelAsync);
        admin.MapPost("/{id:guid}/duplicate", DuplicateAsync);

        return app;
    }

    /// <summary>
    /// The member's schedule for a window, time-ordered and flat.
    ///
    /// FLAT, deliberately — the SPA groups by the BROWSER's local date. Grouping here would mean the
    /// server picking a timezone, and this stack has been UTC-in / local-render throughout. The
    /// window boundaries arrive already converted to UTC instants for the same reason: the calendar
    /// computes "this week" in the member's own clock, and only the instants cross the wire.
    ///
    /// <para>
    /// Both parameters are optional and move together. Omitted, this answers exactly what it
    /// answered before S-07 — the next <see cref="ScheduleWindowDays"/> days — which is what keeps
    /// the change additive. Supplied, a <paramref name="from"/> in the PAST is legitimate: the
    /// calendar's backward navigation is the whole reason this parameter exists (prd-v2 FR-015).
    /// </para>
    /// </summary>
    private static async Task<IResult> GetScheduleAsync(
        IClassScheduleQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var now = timeProvider.GetUtcNow();

        var (rangeFailure, resolved) = ResolveRange(
            from, to, now, now.AddDays(ScheduleWindowDays));
        if (rangeFailure is not null)
        {
            return rangeFailure;
        }

        return Results.Ok(await query.GetScheduleAsync(
            resolved.From, resolved.To!.Value, cancellationToken));
    }

    /// <summary>
    /// The admin's management list for a window.
    ///
    /// <para>
    /// Its no-parameter fallback still reaches further than the member's — <see cref="MaxRangeDays"/>
    /// rather than <see cref="ScheduleWindowDays"/>, because an admin setting up a term looks further
    /// ahead than a member browsing next week. It is no longer UNBOUNDED: that was preserved through
    /// this slice for compatibility, and once the calendar shipped every caller began sending a
    /// window, so the unbounded path had no client left.
    /// </para>
    ///
    /// <para>
    /// With a range, the admin gets the same treatment as the member INCLUDING the past, which is new:
    /// this list used to start at now and the past was simply unreachable. Read-only-ness of a past
    /// week is a client concern (the admin screen withholds its actions), not an authorization one —
    /// the write endpoints already refuse a create in the past on their own.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetAdminClassesAsync(
        IClassScheduleQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var now = timeProvider.GetUtcNow();
        var (rangeFailure, resolved) = ResolveRange(
            from, to, now, now.AddDays(MaxRangeDays));
        if (rangeFailure is not null)
        {
            return rangeFailure;
        }

        return Results.Ok(await query.GetUpcomingForAdminAsync(
            resolved.From, resolved.To!.Value, cancellationToken));
    }

    /// <summary>
    /// Validates an optional [from, to) window and falls back to the endpoint's own default when it is
    /// absent.
    ///
    /// <para>
    /// PARTIAL RANGES ARE REFUSED rather than half-honoured. A caller sending only <c>from</c> has a
    /// bug, and silently pairing it with the default <c>to</c> would answer a window nobody asked for
    /// — the fortnight from an arbitrary date, or everything to the end of time. Both parameters or
    /// neither.
    /// </para>
    /// </summary>
    /// <param name="defaultTo">
    /// The upper bound when no range is supplied — null for the admin path, which is deliberately
    /// unbounded without one.
    /// </param>
    /// <returns>
    /// <c>Failure</c> set and the range meaningless when refused; the reverse when accepted. The
    /// accepted <c>To</c> is null only on the unbounded admin default.
    /// </returns>
    private static (IResult? Failure, (DateTimeOffset From, DateTimeOffset? To) Range) ResolveRange(
        DateTimeOffset? from,
        DateTimeOffset? to,
        DateTimeOffset defaultFrom,
        DateTimeOffset? defaultTo)
    {
        if (from is null && to is null)
        {
            return (null, (defaultFrom, defaultTo));
        }

        if (from is null || to is null)
        {
            return (InvalidRange(), default);
        }

        // An empty or inverted window is a client bug, not an empty schedule: answering it with [] would
        // render as "no classes this week" and hide the fault.
        if (to.Value <= from.Value)
        {
            return (InvalidRange(), default);
        }

        if ((to.Value - from.Value).TotalDays > MaxRangeDays)
        {
            return (InvalidRange(), default);
        }

        return (null, (from.Value, to.Value));
    }

    private static IResult InvalidRange() =>
        Results.Json(new ClassFailure("invalid_range"), statusCode: 400);

    /// <summary>
    /// One class, for the edit form.
    ///
    /// Exists so opening /admin/classes/:id directly - a bookmark, a refresh, a shared link - costs
    /// one row instead of the whole admin list. That list is deliberately unbounded, so filtering it
    /// client-side to find a single class would grow with every class the club ever schedules.
    /// </summary>
    private static async Task<IResult> GetByIdAsync(
        Guid id,
        IClassStore store,
        IBookingStore bookings,
        CancellationToken cancellationToken)
    {
        var found = await store.FindAsync(id, cancellationToken);

        // The one caller whose entity genuinely arrives with both navigations populated - FindAsync
        // Includes them - and the one place a null truly means "no such class".
        return found is null
            ? Results.NotFound()
            : Results.Ok(ToDto(
                found,
                found.ClassType,
                found.Instructor,
                await bookings.CountActiveAsync(id, cancellationToken)));
    }

    private static async Task<IResult> CreateAsync(
        ClassRequest request,
        IClassStore store,
        IClassTypeStore classTypes,
        UserManager<ApplicationUser> userManager,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        // Create-only. An admin must be able to correct a class that has already started, so
        // UpdateAsync deliberately does NOT apply this rule.
        if (request.StartsAt <= now)
        {
            return Results.Json(new ClassFailure("starts_in_past"), statusCode: 400);
        }

        var classType = await classTypes.FindAsync(request.ClassTypeId, cancellationToken);
        if (classType is null)
        {
            return Results.Json(new ClassFailure("unknown_class_type"), statusCode: 400);
        }

        // Active is checked HERE ONLY, not on edit. FR-006 promises that deactivating a type leaves
        // its existing occurrences intact - and an occurrence the admin cannot reschedule is not
        // intact. Since the type is immutable after creation (see UpdateAsync), create is the only
        // place a deactivated type could be newly attached to anything.
        if (!classType.IsActive)
        {
            return Results.Json(new ClassFailure("inactive_class_type"), statusCode: 400);
        }

        var (instructorFailure, instructor) =
            await ValidateInstructorAsync(request.InstructorUserId, userManager);
        if (instructorFailure is not null)
        {
            return instructorFailure;
        }

        if (await store.HasTimeConflictAsync(
                request.StartsAt, request.DurationMinutes, null, cancellationToken))
        {
            return Results.Json(new ClassFailure("time_conflict"), statusCode: 409);
        }

        var created = new Class
        {
            Id = Guid.NewGuid(),
            ClassTypeId = classType.Id,
            StartsAt = request.StartsAt,

            // FROM THE REQUEST, NOT FROM classType (prd-v2 FR-007). The client prefilled these from
            // the type's defaults and the admin may have overridden them; reading
            // classType.DefaultCapacity here instead would both ignore the override and re-open the
            // door to a type edit moving a booked class's capacity. classType is a VALIDATION result
            // on this path, nothing more.
            DurationMinutes = request.DurationMinutes,
            Capacity = request.Capacity,

            InstructorUserId = request.InstructorUserId,
            Status = ClassStatus.Scheduled,
            CreatedAt = now,
        };

        store.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Projected from what this handler already holds, NOT from a re-read.
        //
        // The freshly-constructed entity's navigations are null, so ToDto cannot take it alone - but
        // classType and instructor are both in hand from the validation above. Re-reading here used
        // to mean a second round-trip AND, when it came back null, a 404 for a row that had just been
        // committed: the client was told the write failed after it succeeded, and an admin retrying a
        // create would produce a duplicate class.
        // Zero bookings, by construction: the occurrence was created this instant, and there is no
        // route by which anything could have booked it before the response is written.
        return Results.Ok(ToDto(created, classType, instructor!, bookedCount: 0));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        ClassRequest request,
        IClassStore store,
        IBookingStore bookings,
        UserManager<ApplicationUser> userManager,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        var invalid = Validate(request);
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
            await ValidateInstructorAsync(request.InstructorUserId, userManager);
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
        existing.InstructorUserId = request.InstructorUserId;

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
        return Results.Ok(ToDto(existing, existing.ClassType, instructor!, bookedCount));
    }

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
    private static async Task<IResult> DeleteAsync(
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
    private static async Task<IResult> CancelAsync(
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
                existing.Instructor.DisplayName),
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
        return Results.Ok(ToDto(existing, existing.ClassType, existing.Instructor, recipients.Count));
    }

    /// <summary>
    /// Copies a class into the next N weeks (prd-v2 FR-013) — the deliberate MVP substitute for
    /// recurring series.
    ///
    /// PARTIAL SUCCESS IS THE POINT. A week whose time is already taken is skipped and reported, not
    /// fatal: one clash in week seven must not throw away six good copies and send the admin hunting
    /// for it. Every surviving copy lands in ONE save, so a batch that fails to commit leaves no
    /// half-created weeks behind.
    ///
    /// <para>
    /// The copies carry the SOURCE's type, instructor and numbers verbatim. No validation of the
    /// type's active state runs here: the source occurrence is already valid, and refusing to
    /// duplicate a class because its type was retired afterwards would contradict FR-006 exactly as
    /// refusing to edit it would.
    /// </para>
    /// </summary>
    private static async Task<IResult> DuplicateAsync(
        Guid id,
        DuplicateRequest request,
        IClassStore store,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var source = await store.FindAsync(id, cancellationToken);
        if (source is null)
        {
            return Results.NotFound();
        }

        if (request.Weeks < 1 || request.Weeks > MaxDuplicateWeeks)
        {
            return Results.Json(new ClassFailure("invalid_weeks"), statusCode: 400);
        }

        var now = timeProvider.GetUtcNow();
        var skipped = new List<int>();
        var created = 0;

        for (var week = 1; week <= request.Weeks; week++)
        {
            // Seven days added in the CLUB's local time, not on the UTC instant.
            //
            // DateTimeOffset.AddDays would preserve the instant, which is not what a timetable
            // means: a 22:34 class duplicated across the October DST transition would silently land
            // at 21:34: right instant, wrong wall clock, and nothing would fail. Members read a wall
            // clock, so the wall clock is what has to survive. See ClubTime.
            var startsAt = ClubTime.AddLocalDays(source.StartsAt, 7 * week);

            // Each copy is checked independently, against rows already in the database AND against
            // the copies queued earlier in this same batch — otherwise two weeks of a batch could
            // collide with each other and both be written.
            if (await store.HasTimeConflictAsync(
                    startsAt, source.DurationMinutes, null, cancellationToken))
            {
                skipped.Add(week);
                continue;
            }

            store.Add(new Class
            {
                Id = Guid.NewGuid(),
                ClassTypeId = source.ClassTypeId,
                StartsAt = startsAt,
                DurationMinutes = source.DurationMinutes,
                InstructorUserId = source.InstructorUserId,
                Capacity = source.Capacity,
                Status = ClassStatus.Scheduled,
                CreatedAt = now,
            });

            created++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(new DuplicateResult(created, skipped));
    }

    /// <summary>
    /// The rules shared by create and edit. Hand-rolled, like every other validation in this
    /// codebase — there is no validation library here and adding one for five fields is not
    /// warranted.
    /// </summary>
    private static IResult? Validate(ClassRequest request)
    {
        // A reference that is absent, not a field that is blank: the client picks these from two
        // lists, so the only way they arrive empty is a form submitted without a selection.
        if (request.ClassTypeId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.InstructorUserId))
        {
            return Results.Json(new ClassFailure("missing_field"), statusCode: 400);
        }

        // A class nobody can book is not a class. The ceiling is far above any room this club has and
        // exists only to catch a slipped digit.
        if (request.Capacity < MinCapacity || request.Capacity > MaxCapacity)
        {
            return Results.Json(new ClassFailure("invalid_capacity"), statusCode: 400);
        }

        // Zero-length would make every overlap check meaningless. The ceiling is eight hours: past
        // that it is a typo (600 for 60), not a class.
        if (request.DurationMinutes < MinDurationMinutes
            || request.DurationMinutes > MaxDurationMinutes)
        {
            return Results.Json(new ClassFailure("invalid_duration"), statusCode: 400);
        }

        return null;
    }

    /// <summary>
    /// Whether this account may be named as an instructor (prd-v2 FR-009): it must exist, be ACTIVE,
    /// and hold the Trainer role.
    ///
    /// <para>
    /// Through UserManager rather than a query seam, matching MemberAdminEndpoints.GrantTrainerAsync
    /// — <c>IsInRoleAsync</c> normalises its argument, so the role name is compared the way Identity
    /// stores it. A single lookup does not justify a third read seam.
    /// </para>
    ///
    /// <para>
    /// A blocked or pending account reports <c>unknown_instructor</c>, not a status of its own: the
    /// selection only ever offers active trainers, so an inactive id means the client is working from
    /// a stale list, and "pick someone else" is the whole of the useful advice. Telling the caller
    /// which accounts exist but are blocked would leak account state onto a scheduling surface.
    /// </para>
    ///
    /// <para>
    /// RETURNS THE ACCOUNT, not just a verdict. The caller needs its DisplayName to build the
    /// response, and this method has already fetched it - handing it back is what lets both write
    /// paths answer without a second round-trip to the database after they have committed.
    /// </para>
    ///
    /// <para>
    /// No CancellationToken: UserManager exposes no token overload for either call, so an aborted
    /// request still pays for both. A deliberate gap, not an omission.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>Failure</c> set and <c>Instructor</c> null when the account may not be assigned; the
    /// reverse when it may. Exactly one of the two is ever non-null.
    /// </returns>
    private static async Task<(IResult? Failure, ApplicationUser? Instructor)> ValidateInstructorAsync(
        string instructorUserId,
        UserManager<ApplicationUser> userManager)
    {
        var instructor = await userManager.FindByIdAsync(instructorUserId);

        if (instructor is null || instructor.Status != AccountStatus.Active)
        {
            return (Results.Json(new ClassFailure("unknown_instructor"), statusCode: 400), null);
        }

        if (!await userManager.IsInRoleAsync(instructor, ApplicationRoles.Trainer))
        {
            return (Results.Json(new ClassFailure("instructor_not_trainer"), statusCode: 400), null);
        }

        return (null, instructor);
    }

    /// <summary>
    /// Projects an occurrence onto the wire contract.
    ///
    /// <para>
    /// THE RESOLVED PARTS ARE PASSED IN, not read off the entity's navigations. The name,
    /// description and instructor name do not live on the occurrence (prd-v2 FR-007, FR-009,
    /// FR-010), and the caller is not always holding an entity whose navigations are populated: a
    /// freshly created one has none, and an edited one still points at the PREVIOUS instructor.
    /// Taking them as parameters makes the caller state where each came from.
    /// </para>
    /// </summary>
    /// <remarks>
    /// INTERNAL rather than private since S-08: BookingEndpoints answers with the class as it now
    /// stands, and two constructions of the same contract would drift the moment one of them learned
    /// about free spots and the other did not.
    /// </remarks>
    /// <param name="bookedCount">
    /// How many active bookings the occurrence has, which the caller must supply because this method
    /// has no query of its own — and must NOT reach through a navigation, because Class deliberately
    /// has no Bookings collection (see Booking.Class). A caller that has just created the occurrence
    /// passes 0; every other caller counts.
    /// </param>
    internal static ScheduledClass ToDto(
        Class entity, ClassType classType, ApplicationUser instructor, int bookedCount) =>
        new(entity.Id,
            entity.ClassTypeId,
            classType.Name,
            classType.Description,
            entity.StartsAt,
            entity.DurationMinutes,
            entity.InstructorUserId,
            instructor.DisplayName,
            entity.Capacity,
            // Same construction as the read query, and unclamped for the same reason - see
            // ClassScheduleQuery.
            entity.Capacity - bookedCount,
            entity.Status.ToString());
}

/// <summary>
/// Narrow read seam over the class table, so Application does not reference EF Core (AGENTS.md
/// layering). Implemented in Infrastructure.
/// </summary>
public interface IClassScheduleQuery
{
    /// <summary>Scheduled classes starting within [from, to), time-ordered. The member's window.</summary>
    Task<IReadOnlyList<ScheduledClass>> GetScheduleAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>
    /// The admin's list for the window [<paramref name="from"/>, <paramref name="to"/>).
    ///
    /// <para>
    /// Same shape as <see cref="GetScheduleAsync"/> and deliberately so: the bound is not optional.
    /// It was, briefly — the endpoint's fallback used to be unbounded — and nothing asks for that any
    /// more.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ScheduledClass>> GetUpcomingForAdminAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>
/// The write counterpart. Intention-revealing methods rather than a generic repository — this
/// codebase has no repository pattern and this slice does not introduce one.
///
/// Nothing here saves. The endpoint commits through <see cref="IUnitOfWork"/>, which is what lets a
/// whole duplicate batch land in one transaction.
/// </summary>
public interface IClassStore
{
    /// <summary>
    /// One occurrence WITH its ClassType and Instructor navigations loaded — ToDto resolves the name,
    /// description and display name through them, so a bare entity is not enough.
    /// </summary>
    Task<Class?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Class entity);

    void Remove(Class entity);

    /// <summary>
    /// Whether another class already occupies any part of
    /// [startsAt, startsAt + durationMinutes) — ANYWHERE in the club (prd-v2 FR-012).
    ///
    /// <para>
    /// This was <c>HasRoomConflictAsync</c> until S-06. The room disappeared, but the rule did not:
    /// it widened from "one room, one class at a time" to "one club, one class at a time". A
    /// single-room gym could never have two classes at once anyway, so removing the room made the
    /// real rule explicit rather than removing the protection.
    /// </para>
    /// </summary>
    /// <param name="excludingId">
    /// The class being edited, so it does not conflict with itself. Null when creating.
    /// </param>
    Task<bool> HasTimeConflictAsync(
        DateTimeOffset startsAt,
        int durationMinutes,
        Guid? excludingId,
        CancellationToken cancellationToken);
}
