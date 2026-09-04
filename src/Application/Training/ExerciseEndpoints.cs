using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// One exercise as the admin's list, detail screen and form see it. This is a CONTRACT the SPA's
/// exercise service mirrors — renaming a field breaks three screens silently.
///
/// <para>
/// ONE SHAPE SERVES BOTH THE LIST AND THE DETAIL SCREEN. A trimmed list DTO would save a few
/// kilobytes on a library of dozens of rows and cost a second contract to keep in step with this
/// one; the list simply ignores the fields it does not render.
/// </para>
///
/// <para>
/// <see cref="VideoId"/> crosses the wire as the bare 11-character id, never a URL. The client
/// composes the thumbnail (img.youtube.com) and the player (youtube-nocookie.com) from it, so the
/// API carries no derived URLs that could drift from each other.
/// </para>
/// </summary>
public record ExerciseSummary(
    Guid Id,
    string Name,
    string? Description,
    string? MuscleGroup,
    string? Difficulty,
    string? Equipment,
    string? Preparation,
    string? StartingPosition,
    string? Execution,
    string? VideoId,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>
/// Create/edit payload. Same shape for both — an edit replaces every field.
///
/// <para>
/// It carries <c>VideoUrl</c>, not a video id: the client sends whatever the admin pasted and the
/// SERVER owns the parsing (<see cref="YouTubeVideoId"/>). Accepting an id here instead would move
/// that parse into the browser, where a second implementation would eventually disagree with this
/// one.
/// </para>
///
/// <para>
/// <c>IsActive</c> is deliberately ABSENT, exactly as in <see cref="Scheduling.ClassTypeRequest"/>:
/// activation has its own two endpoints, so a careless edit cannot resurrect a retired exercise.
/// </para>
/// </summary>
public record ExerciseRequest(
    string Name,
    string? Description,
    string? MuscleGroup,
    string? Difficulty,
    string? Equipment,
    string? Preparation,
    string? StartingPosition,
    string? Execution,
    string? VideoUrl);

/// <summary>
/// Why an exercise write was refused. All 400 except <c>name_taken</c>, which is a 409 — it is a
/// conflict with existing state rather than bad input.
///
/// <para>
/// Reasons: <c>missing_field</c>, <c>name_too_long</c>, <c>description_too_long</c>,
/// <c>muscle_group_too_long</c>, <c>difficulty_too_long</c>, <c>equipment_too_long</c>,
/// <c>preparation_too_long</c>, <c>starting_position_too_long</c>, <c>execution_too_long</c>,
/// <c>invalid_video_url</c>, <c>name_taken</c>. Adding one here means adding it to the SPA's
/// ExerciseFailure union too — that type mirrors this one field for field, and the form maps each
/// reason onto the control that owns it.
/// </para>
/// </summary>
public record ExerciseFailure(string Reason);

/// <summary>
/// The admin's exercise library (prd.md FR-018, FR-019) — the first surface in the training context
/// and the vocabulary S-11's training plans will be assembled from.
///
/// <para>
/// Admin-only, with the policy applied at the GROUP rather than per endpoint, so an endpoint added
/// here later cannot accidentally ship unauthenticated. Members never read this surface: FR-020
/// reaches an exercise from inside an assigned plan, which is S-11, and prd.md explicitly cuts
/// standalone library browsing for members.
/// </para>
///
/// <para>
/// NO DELETE, by design — deactivation instead, for the reason FR-006 gives class types and which
/// applies harder here: S-11's plans will reference exercises, so a deleted row would either orphan
/// a plan or be blocked by a foreign key.
/// </para>
/// </summary>
public static class ExerciseEndpoints
{
    /// <summary>
    /// Every bound below matches a HasMaxLength in ExerciseConfiguration. Keep the two in step.
    ///
    /// NOT optional to check. Without a guard, a longer value reaches SQL Server, which refuses the
    /// INSERT with "String or binary data would be truncated" - an unhandled DbUpdateException, i.e.
    /// a 500 for what is ordinary bad input. This is the single most repeated finding in this repo's
    /// review history, so every column with a length has its own guard and its own reason.
    /// </summary>
    private const int MaxNameLength = 200;

    private const int MaxDescriptionLength = 1000;

    private const int MaxMuscleGroupLength = 100;

    private const int MaxDifficultyLength = 50;

    private const int MaxEquipmentLength = 200;

    private const int MaxPreparationLength = 2000;

    private const int MaxStartingPositionLength = 2000;

    private const int MaxExecutionLength = 4000;

    /// <summary>
    /// The one bound here that mirrors no column, because <c>VideoUrl</c> is never stored - only the
    /// id it parses to is. 2048 is the conventional URL ceiling; a real YouTube link is under 100
    /// characters, so this refuses only input that could not have parsed anyway.
    /// </summary>
    private const int MaxVideoUrlLength = 2048;

    public static IEndpointRouteBuilder MapExerciseEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/exercises")
            .WithTags("Training")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        admin.MapGet("/", GetAllAsync);
        admin.MapGet("/{id:guid}", GetByIdAsync);
        admin.MapPost("/", CreateAsync);
        admin.MapPut("/{id:guid}", UpdateAsync);

        // Two verbs rather than a boolean on the edit payload, matching the class-type surface: the
        // action the admin took is legible in the request, and an edit cannot perform it by accident.
        admin.MapPost("/{id:guid}/deactivate", DeactivateAsync);
        admin.MapPost("/{id:guid}/activate", ActivateAsync);

        return app;
    }

    /// <summary>
    /// Every exercise, active and inactive, active first and then by name.
    ///
    /// UNFILTERED, deliberately — same reasoning as the class-type list: the screen's "pokaż
    /// nieaktywne" toggle filters rows it already holds. It is also what makes the form's
    /// muscle-group and difficulty suggestions free: the form reuses this call and derives the
    /// distinct values client-side, so no second endpoint exists to keep in step.
    /// </summary>
    private static async Task<IResult> GetAllAsync(
        IExerciseQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetAllAsync(cancellationToken));

    /// <summary>
    /// One exercise, for the detail screen and the edit form — so opening /admin/exercises/:id
    /// directly costs one row instead of the whole library.
    /// </summary>
    private static async Task<IResult> GetByIdAsync(
        Guid id,
        IExerciseStore store,
        CancellationToken cancellationToken)
    {
        var found = await store.FindAsync(id, cancellationToken);

        return found is null ? Results.NotFound() : Results.Ok(ToDto(found));
    }

    private static async Task<IResult> CreateAsync(
        ExerciseRequest request,
        IExerciseStore store,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var name = request.Name.Trim();

        if (await store.IsNameTakenAsync(name, null, cancellationToken))
        {
            return Results.Json(new ExerciseFailure("name_taken"), statusCode: 409);
        }

        var created = new Exercise
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        Apply(created, request);

        store.Add(created);

        if (await unitOfWork.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return NameTaken(unitOfWork);
        }

        return Results.Ok(ToDto(created));
    }

    /// <summary>
    /// Replaces every field of an exercise. Nothing references an exercise yet, so an edit
    /// propagates nowhere; once S-11 lands, a plan will resolve name and instructions BY REFERENCE,
    /// which is what makes correcting a typo here fix it in every plan at once.
    /// </summary>
    private static async Task<IResult> UpdateAsync(
        Guid id,
        ExerciseRequest request,
        IExerciseStore store,
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

        var name = request.Name.Trim();

        // Excluding its own id, or every edit that keeps the name would collide with itself.
        if (await store.IsNameTakenAsync(name, id, cancellationToken))
        {
            return Results.Json(new ExerciseFailure("name_taken"), statusCode: 409);
        }

        existing.Name = name;
        Apply(existing, request);

        // No concurrency token on Exercise: with exactly one admin account ever seeded (AdminSeeder)
        // there is no second writer, so two edits of the same row cannot race. A second admin makes
        // this last-write-wins, at which point Exercise needs a ConcurrencyStamp.
        //
        // The NAME collision is a different matter and is handled - see NameTaken.
        if (await unitOfWork.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return NameTaken(unitOfWork);
        }

        return Results.Ok(ToDto(existing));
    }

    /// <summary>
    /// Retires an exercise: it leaves the library's active set while everything referencing it stays
    /// intact. No uniqueness check is needed — deactivating only ever RELEASES a name.
    ///
    /// Idempotent. Deactivating an already-inactive exercise is a 200, not a refusal.
    /// </summary>
    private static async Task<IResult> DeactivateAsync(
        Guid id,
        IExerciseStore store,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        existing.IsActive = false;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(existing));
    }

    /// <summary>
    /// Puts a retired exercise back into circulation.
    ///
    /// <para>
    /// THE UNIQUENESS CHECK HERE IS NOT DECORATION. Deactivating released the name, so another
    /// exercise may have claimed it since. Reactivating blindly would violate
    /// IX_Exercises_Name_Active and surface as an unhandled DbUpdateException — a 500 for what is
    /// really a conflict the admin can resolve. The request carries no name, which is exactly why
    /// this is easy to miss; it was found in review on the class-type surface, not in planning.
    /// </para>
    /// </summary>
    private static async Task<IResult> ActivateAsync(
        Guid id,
        IExerciseStore store,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (await store.IsNameTakenAsync(existing.Name, id, cancellationToken))
        {
            return Results.Json(new ExerciseFailure("name_taken"), statusCode: 409);
        }

        existing.IsActive = true;

        if (await unitOfWork.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return NameTaken(unitOfWork);
        }

        return Results.Ok(ToDto(existing));
    }

    /// <summary>
    /// The refusal for a name collision that the pre-check missed.
    ///
    /// <para>
    /// THE PRE-CHECK NARROWS THE WINDOW; THIS CLOSES IT. Two concurrent writes can both pass
    /// <c>IsNameTakenAsync</c>, and only one of them can satisfy IX_Exercises_Name_Active. With the
    /// plain <see cref="IUnitOfWork.SaveChangesAsync"/> the loser raises an unhandled
    /// DbUpdateException - a 500 for what the admin should see as the same clean 409 the ordinary
    /// path returns. <see cref="SaveOutcome.UniqueViolation"/> exists precisely for this shape, and
    /// its doc comment records that three earlier implementation reviews found this hole and deferred
    /// it because catching it needed EF Core types in Application. It does not any more.
    /// </para>
    ///
    /// <para>
    /// Any non-Saved outcome lands here, and that is correct rather than sloppy: Exercise carries no
    /// concurrency token and no row is ever deleted, so <see cref="SaveOutcome.ConcurrencyConflict"/>
    /// is unreachable and the name collision is the only way a commit can fail without throwing.
    /// Discarding matters because nothing was written either way - leaving the rejected insert in the
    /// tracked graph would poison any later save on the same request.
    /// </para>
    /// </summary>
    private static IResult NameTaken(IUnitOfWork unitOfWork)
    {
        unitOfWork.DiscardChanges();

        return Results.Json(new ExerciseFailure("name_taken"), statusCode: 409);
    }

    /// <summary>
    /// The rules shared by create and edit. Hand-rolled, like every other validation in this
    /// codebase — there is no validation library here and adding one would be the deviation.
    ///
    /// <para>
    /// Only the name is required (FR-018: the fields are optional by design). Every other rule is a
    /// ceiling mirroring a column, plus the video link, which is refused rather than silently
    /// dropped so the admin learns immediately that the link they pasted is not one we can use.
    /// </para>
    /// </summary>
    private static IResult? Validate(ExerciseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Json(new ExerciseFailure("missing_field"), statusCode: 400);
        }

        // Every length is measured on the TRIMMED value, which is what gets stored - otherwise
        // trailing whitespace could be refused for a value that fits.
        if (request.Name.Trim().Length > MaxNameLength)
        {
            return Results.Json(new ExerciseFailure("name_too_long"), statusCode: 400);
        }

        var tooLong = TooLong(request.Description, MaxDescriptionLength, "description_too_long")
            ?? TooLong(request.MuscleGroup, MaxMuscleGroupLength, "muscle_group_too_long")
            ?? TooLong(request.Difficulty, MaxDifficultyLength, "difficulty_too_long")
            ?? TooLong(request.Equipment, MaxEquipmentLength, "equipment_too_long")
            ?? TooLong(request.Preparation, MaxPreparationLength, "preparation_too_long")
            ?? TooLong(request.StartingPosition, MaxStartingPositionLength, "starting_position_too_long")
            ?? TooLong(request.Execution, MaxExecutionLength, "execution_too_long");

        if (tooLong is not null)
        {
            return tooLong;
        }

        // Blank is "no video", not a bad link. Anything else must be of a sane size AND must parse.
        //
        // The length check comes FIRST and is the one guard here whose bound does not mirror a
        // column: VideoUrl is never stored - only the 11-character id it parses to is - so without
        // it an arbitrarily long paste would reach Uri.TryCreate and the regex on every request.
        // The ceiling is the conventional URL limit, orders of magnitude above any real YouTube
        // link, so it can only ever refuse something that was never going to parse anyway.
        if (!string.IsNullOrWhiteSpace(request.VideoUrl)
            && (request.VideoUrl.Trim().Length > MaxVideoUrlLength
                || !YouTubeVideoId.TryParse(request.VideoUrl, out _)))
        {
            return Results.Json(new ExerciseFailure("invalid_video_url"), statusCode: 400);
        }

        return null;
    }

    private static IResult? TooLong(string? value, int max, string reason) =>
        Normalize(value) is { } normalized && normalized.Length > max
            ? Results.Json(new ExerciseFailure(reason), statusCode: 400)
            : null;

    /// <summary>
    /// Copies the optional fields onto the entity. Shared by create and edit so the two cannot drift
    /// - a field added to the request and wired into only one of them is a bug that reads as working
    /// code. The name is NOT set here: it is trimmed and uniqueness-checked by each caller first.
    /// </summary>
    private static void Apply(Exercise entity, ExerciseRequest request)
    {
        entity.Description = Normalize(request.Description);
        entity.MuscleGroup = Normalize(request.MuscleGroup);
        entity.Difficulty = Normalize(request.Difficulty);
        entity.Equipment = Normalize(request.Equipment);
        entity.Preparation = Normalize(request.Preparation);
        entity.StartingPosition = Normalize(request.StartingPosition);
        entity.Execution = Normalize(request.Execution);
        entity.VideoId = ParseVideoId(request.VideoUrl);
    }

    /// <summary>
    /// Trims, and collapses "absent" to a single representation. A whitespace-only value and a
    /// missing one mean the same thing to a reader, so they must not be two different values in the
    /// database — otherwise every screen has to test for both.
    /// </summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The stored form of the video: an id, or nothing. Safe to call unconditionally because
    /// <see cref="Validate"/> has already refused anything that would not parse.
    /// </summary>
    private static string? ParseVideoId(string? videoUrl) =>
        YouTubeVideoId.TryParse(videoUrl, out var videoId) ? videoId : null;

    private static ExerciseSummary ToDto(Exercise entity) =>
        new(entity.Id,
            entity.Name,
            entity.Description,
            entity.MuscleGroup,
            entity.Difficulty,
            entity.Equipment,
            entity.Preparation,
            entity.StartingPosition,
            entity.Execution,
            entity.VideoId,
            entity.IsActive,
            entity.CreatedAt);
}

/// <summary>
/// Narrow read seam over the exercise table, so Application does not reference EF Core (AGENTS.md
/// layering). Implemented in Infrastructure.
/// </summary>
public interface IExerciseQuery
{
    /// <summary>Every exercise, active first and then by name. Unbounded — see GetAllAsync.</summary>
    Task<IReadOnlyList<ExerciseSummary>> GetAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The write counterpart. Intention-revealing methods rather than a generic repository — this
/// codebase has no repository pattern and this slice does not introduce one.
///
/// No Remove. Deactivation is a field on the entity, not an operation on the store.
///
/// Nothing here saves. The endpoint commits through <see cref="IUnitOfWork"/>.
/// </summary>
public interface IExerciseStore
{
    Task<Exercise?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Exercise entity);

    /// <summary>
    /// Whether another ACTIVE exercise already holds <paramref name="name"/>. Inactive rows are
    /// invisible here, which is what lets a retired name be reused.
    /// </summary>
    /// <param name="excludingId">
    /// The exercise being edited or activated, so it does not collide with itself. Null when creating.
    /// </param>
    Task<bool> IsNameTakenAsync(string name, Guid? excludingId, CancellationToken cancellationToken);
}
