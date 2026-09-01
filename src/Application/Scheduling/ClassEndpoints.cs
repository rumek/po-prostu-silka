using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One class as the schedule and the admin list see it. This is a CONTRACT the SPA's class service
/// mirrors — renaming a field breaks both screens silently.
///
/// <para>
/// Status crosses the wire as the enum NAME ("Scheduled" / "Cancelled"), not its int, for the same
/// reason AccountStatus does: the numeric values exist for persistence stability, and a badge keyed
/// on 1 would break the day someone renumbers.
/// </para>
///
/// <para>
/// FreeSpots is <see cref="Capacity"/> in S-03 — see IClassScheduleQuery. It is present now, rather
/// than added in S-04, so the wire contract and both screens are final and that slice changes one
/// projection expression instead of a DTO, two templates and their specs.
/// </para>
/// </summary>
public record ScheduledClass(
    Guid Id,
    string Name,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string Room,
    string Instructor,
    int Capacity,
    int FreeSpots,
    string Status);

/// <summary>Create/edit payload. Same shape for both — an edit replaces every field.</summary>
public record ClassRequest(
    string Name,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string Room,
    string Instructor,
    int Capacity);

/// <summary>How many following weeks to copy a class into.</summary>
public record DuplicateRequest(int Weeks);

/// <summary>
/// What a duplicate actually did. NOT a bare success: a batch where some weeks collided is a partial
/// success, and reporting it as "done" would leave the admin believing in classes that were never
/// created.
/// </summary>
/// <param name="Created">How many copies were written.</param>
/// <param name="SkippedWeeks">1-based week offsets refused for a room conflict.</param>
public record DuplicateResult(int Created, IReadOnlyList<int> SkippedWeeks);

/// <summary>
/// Why a class write was refused. All 400 except <c>room_conflict</c>, which is a 409 — it is a
/// conflict with existing state, not bad input.
/// </summary>
public record ClassFailure(string Reason);

/// <summary>
/// The class schedule (FR-007) and the admin's management of it (FR-011, FR-012).
///
/// Two groups with different policies: members read the schedule under ActiveMember, admins manage
/// under Admin. The policy is applied at each GROUP, not per endpoint, so an endpoint added here
/// later cannot accidentally ship unauthenticated.
///
/// No cancel endpoint. FR-013 makes cancellation a state transition that must be accompanied by the
/// email and push to everyone booked — that lands whole in S-05. DELETE here is for a MISTAKE (a
/// class typed wrong and created seconds ago), not for cancelling a class members signed up for;
/// S-04 adds the guard that refuses once bookings exist.
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

        return found is null ? Results.NotFound() : Results.Ok(ToDto(found));
    }

    private static async Task<IResult> CreateAsync(
        ClassRequest request,
        IClassStore store,
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

        if (await store.HasRoomConflictAsync(
                request.Room, request.StartsAt, request.DurationMinutes, null, cancellationToken))
        {
            return Results.Json(new ClassFailure("room_conflict"), statusCode: 409);
        }

        var created = new Class
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            StartsAt = request.StartsAt,
            DurationMinutes = request.DurationMinutes,
            Room = request.Room.Trim(),
            Instructor = request.Instructor.Trim(),
            Capacity = request.Capacity,
            Status = ClassStatus.Scheduled,
            CreatedAt = now,
        };

        store.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(created));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        ClassRequest request,
        IClassStore store,
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

        // No starts_in_past check here — see CreateAsync. Correcting a class that already ran is a
        // legitimate thing for an admin to do; refusing it would leave a wrong record permanently
        // wrong.

        // Excluding its own id, or every edit that keeps the time would conflict with itself.
        if (await store.HasRoomConflictAsync(
                request.Room, request.StartsAt, request.DurationMinutes, id, cancellationToken))
        {
            return Results.Json(new ClassFailure("room_conflict"), statusCode: 409);
        }

        existing.Name = request.Name.Trim();
        existing.StartsAt = request.StartsAt;
        existing.DurationMinutes = request.DurationMinutes;
        existing.Room = request.Room.Trim();
        existing.Instructor = request.Instructor.Trim();
        existing.Capacity = request.Capacity;

        // SaveChangesAsync, not TrySaveChangesAsync, and Class carries no concurrency token - a
        // deliberate departure from the MemberAdminEndpoints pattern above, on the same grounds
        // ClassStore.HasRoomConflictAsync records: exactly one admin account is ever seeded
        // (AdminSeeder), so there is no second writer to lose a race against. Two admins would make
        // this last-write-wins, which is why ClassStore names a second admin as the trigger to
        // revisit - at which point Class needs a ConcurrencyStamp and both handlers need the 409.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(existing));
    }

    /// <summary>
    /// Deletes a class outright. For MISTAKES only — see the class doc comment.
    ///
    /// Nothing is booked in S-03 because Booking does not exist, so this always succeeds. S-04 adds
    /// the guard that refuses a delete once someone has booked, at which point cancelling (S-05) is
    /// the only correct way to take a class off the schedule.
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
    /// Copies a class into the next N weeks (FR-012) — the deliberate MVP substitute for recurring
    /// series.
    ///
    /// PARTIAL SUCCESS IS THE POINT. A week whose room is already taken is skipped and reported, not
    /// fatal: one clash in week seven must not throw away six good copies and send the admin hunting
    /// for it. Every surviving copy lands in ONE save, so a batch that fails to commit leaves no
    /// half-created weeks behind.
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
            if (await store.HasRoomConflictAsync(
                    source.Room, startsAt, source.DurationMinutes, null, cancellationToken))
            {
                skipped.Add(week);
                continue;
            }

            store.Add(new Class
            {
                Id = Guid.NewGuid(),
                Name = source.Name,
                StartsAt = startsAt,
                DurationMinutes = source.DurationMinutes,
                Room = source.Room,
                Instructor = source.Instructor,
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
    /// codebase — there is no validation library here and adding one for six fields is not warranted.
    /// </summary>
    private static IResult? Validate(ClassRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Room)
            || string.IsNullOrWhiteSpace(request.Instructor))
        {
            return Results.Json(new ClassFailure("missing_field"), statusCode: 400);
        }

        // A class nobody can book is not a class.
        if (request.Capacity < 1)
        {
            return Results.Json(new ClassFailure("invalid_capacity"), statusCode: 400);
        }

        // Zero-length would make every overlap check meaningless.
        if (request.DurationMinutes < 1)
        {
            return Results.Json(new ClassFailure("invalid_duration"), statusCode: 400);
        }

        return null;
    }

    private static ScheduledClass ToDto(Class entity) =>
        new(entity.Id,
            entity.Name,
            entity.StartsAt,
            entity.DurationMinutes,
            entity.Room,
            entity.Instructor,
            entity.Capacity,
            // Same construction as the read query: no bookings exist until S-04.
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
    Task<Class?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Class entity);

    void Remove(Class entity);

    /// <summary>
    /// Whether another class already occupies <paramref name="room"/> for any part of
    /// [startsAt, startsAt + durationMinutes).
    /// </summary>
    /// <param name="excludingId">
    /// The class being edited, so it does not conflict with itself. Null when creating.
    /// </param>
    Task<bool> HasRoomConflictAsync(
        string room,
        DateTimeOffset startsAt,
        int durationMinutes,
        Guid? excludingId,
        CancellationToken cancellationToken);
}
