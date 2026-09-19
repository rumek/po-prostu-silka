using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// The single construction of <see cref="ExerciseSummary"/>, and the write-side field copy.
/// </summary>
public static class ExerciseProjection
{
    public static ExerciseSummary ToDto(Exercise entity) =>
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

    /// <summary>
    /// Copies the optional fields onto the entity. Shared by create and edit so the two cannot drift
    /// - a field added to the request and wired into only one of them is a bug that reads as working
    /// code. The name is NOT set here: it is trimmed and uniqueness-checked by each caller first.
    /// </summary>
    public static void Apply(Exercise entity, ExerciseRequest request)
    {
        entity.Description = ExerciseValidator.Normalize(request.Description);
        entity.MuscleGroup = ExerciseValidator.Normalize(request.MuscleGroup);
        entity.Difficulty = ExerciseValidator.Normalize(request.Difficulty);
        entity.Equipment = ExerciseValidator.Normalize(request.Equipment);
        entity.Preparation = ExerciseValidator.Normalize(request.Preparation);
        entity.StartingPosition = ExerciseValidator.Normalize(request.StartingPosition);
        entity.Execution = ExerciseValidator.Normalize(request.Execution);
        entity.VideoId = ExerciseValidator.ParseVideoId(request.VideoUrl);
    }
}
