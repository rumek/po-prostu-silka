using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Validation and normalisation for the exercise write paths, with the lengths it enforces.
///
/// <para>
/// MaxNameLength and MaxDescriptionLength match ClassTypeValidator's by parallel derivation
/// from the same column widths, NOT by sharing a rule. Do not consolidate them.
/// </para>
/// </summary>
internal static class ExerciseValidator
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

    private const int MaxEquipmentLength = 200;

    private const int MaxDifficultyLength = 50;

    private const int MaxPreparationLength = 2000;

    private const int MaxExecutionLength = 4000;

    private const int MaxStartingPositionLength = 2000;

    /// <summary>
    /// The one bound here that mirrors no column, because <c>VideoUrl</c> is never stored - only the
    /// id it parses to is. 2048 is the conventional URL ceiling; a real YouTube link is under 100
    /// characters, so this refuses only input that could not have parsed anyway.
    /// </summary>
    private const int MaxVideoUrlLength = 2048;

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
    public static IResult? Validate(ExerciseRequest request)
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

    /// <summary>
    /// Trims, and collapses "absent" to a single representation. A whitespace-only value and a
    /// missing one mean the same thing to a reader, so they must not be two different values in the
    /// database — otherwise every screen has to test for both.
    /// </summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IResult? TooLong(string? value, int max, string reason) =>
        Normalize(value) is { } normalized && normalized.Length > max
            ? Results.Json(new ExerciseFailure(reason), statusCode: 400)
            : null;

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
    public static IResult NameTaken(IUnitOfWork unitOfWork)
    {
        unitOfWork.DiscardChanges();

        return Results.Json(new ExerciseFailure("name_taken"), statusCode: 409);
    }

    /// <summary>
    /// The stored form of the video: an id, or nothing. Safe to call unconditionally because
    /// <see cref="Validate"/> has already refused anything that would not parse.
    /// </summary>
    public static string? ParseVideoId(string? videoUrl) =>
        YouTubeVideoId.TryParse(videoUrl, out var videoId) ? videoId : null;
}
