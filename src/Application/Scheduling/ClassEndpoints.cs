using Microsoft.AspNetCore.Identity;
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
/// Why a class write was refused. All 400 except <c>time_conflict</c>, which is a 409 — it is a
/// conflict with existing state, not bad input.
///
/// <para>
/// Reasons: <c>missing_field</c>, <c>invalid_capacity</c>, <c>invalid_duration</c>,
/// <c>starts_in_past</c>, <c>invalid_weeks</c>, <c>time_conflict</c>, <c>unknown_class_type</c>,
/// <c>inactive_class_type</c>, <c>class_type_immutable</c>, <c>unknown_instructor</c>,
/// <c>instructor_not_trainer</c>. Adding one here means adding it to the SPA's ClassFailure union
/// too — that type mirrors this one field for field.
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
/// No cancel endpoint. FR-013 makes cancellation a state transition that must be accompanied by the
/// email and push to everyone booked — that lands whole in S-09. DELETE here is for a MISTAKE (a
/// class created seconds ago), not for cancelling a class members signed up for; S-08 adds the guard
/// that refuses once bookings exist.
/// </summary>
public static class ClassEndpoints
{
    /// <summary>
    /// How far ahead the member schedule reaches. A fortnight is one round-trip of a few dozen rows
    /// for a single club, which is what keeps this inside the PRD's ~1 s perceived-response NFR.
    /// </summary>
    private const int ScheduleWindowDays = 14;

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
        admin.MapPost("/{id:guid}/duplicate", DuplicateAsync);

        return app;
    }

    /// <summary>
    /// The member's schedule: the next fortnight, time-ordered and flat.
    ///
    /// FLAT, deliberately — the SPA groups into day headings by the BROWSER's local date. Grouping
    /// here would mean the server picking a timezone, and this stack has been UTC-in / local-render
    /// throughout. It also takes no parameters: a fixed window is one fewer thing a caller can get
    /// wrong.
    /// </summary>
    private static async Task<IResult> GetScheduleAsync(
        IClassScheduleQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        return Results.Ok(await query.GetScheduleAsync(
            now, now.AddDays(ScheduleWindowDays), cancellationToken));
    }

    /// <summary>
    /// Everything upcoming, for the admin's management list. Not window-bounded: an admin setting up
    /// a term needs to see what they have already scheduled, however far out.
    /// </summary>
    private static async Task<IResult> GetAdminClassesAsync(
        IClassScheduleQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetUpcomingForAdminAsync(
            timeProvider.GetUtcNow(), cancellationToken));

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
        CancellationToken cancellationToken)
    {
        var found = await store.FindAsync(id, cancellationToken);

        // The one caller whose entity genuinely arrives with both navigations populated - FindAsync
        // Includes them - and the one place a null truly means "no such class".
        return found is null
            ? Results.NotFound()
            : Results.Ok(ToDto(found, found.ClassType, found.Instructor));
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
        return Results.Ok(ToDto(created, classType, instructor!));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        ClassRequest request,
        IClassStore store,
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

        existing.StartsAt = request.StartsAt;
        existing.DurationMinutes = request.DurationMinutes;
        existing.Capacity = request.Capacity;
        existing.InstructorUserId = request.InstructorUserId;

        // SaveChangesAsync, not TrySaveChangesAsync, and Class carries no concurrency token - a
        // deliberate departure from the MemberAdminEndpoints pattern, on the same grounds
        // ClassStore.HasTimeConflictAsync records: exactly one admin account is ever seeded
        // (AdminSeeder), so there is no second writer to lose a race against. Two admins would make
        // this last-write-wins, which is why ClassStore names a second admin as the trigger to
        // revisit - at which point Class needs a ConcurrencyStamp and both handlers need the 409.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Projected from what this handler already holds, NOT from a re-read - see CreateAsync for
        // why the re-read was wrong. The type is immutable on an edit, so existing.ClassType (loaded
        // by FindAsync) is still correct; the instructor may have just changed, which is exactly why
        // the validated account is used rather than the tracked entity's navigation - that one still
        // points at the PREVIOUS account and would render a stale display name.
        return Results.Ok(ToDto(existing, existing.ClassType, instructor!));
    }

    /// <summary>
    /// Deletes a class outright. For MISTAKES only — see the class doc comment.
    ///
    /// Nothing is booked until S-08 because Booking does not exist, so this always succeeds. S-08
    /// adds the guard that refuses a delete once someone has booked, at which point cancelling (S-09)
    /// is the only correct way to take a class off the schedule.
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        Guid id,
        IClassStore store,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        store.Remove(existing);

        // No concurrency token here either - same single-admin reasoning as UpdateAsync. The race
        // this leaves open is delete-while-editing, which would surface as an unhandled
        // DbUpdateConcurrencyException rather than a clean 409; acceptable only while one admin
        // exists.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
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
    private static ScheduledClass ToDto(
        Class entity, ClassType classType, ApplicationUser instructor) =>
        new(entity.Id,
            entity.ClassTypeId,
            classType.Name,
            classType.Description,
            entity.StartsAt,
            entity.DurationMinutes,
            entity.InstructorUserId,
            instructor.DisplayName,
            entity.Capacity,
            // Same construction as the read query: no bookings exist until S-08.
            entity.Capacity,
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

    /// <summary>Everything from <paramref name="from"/> onward, unbounded. The admin's list.</summary>
    Task<IReadOnlyList<ScheduledClass>> GetUpcomingForAdminAsync(
        DateTimeOffset from, CancellationToken cancellationToken);
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
