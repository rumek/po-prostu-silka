using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// One exercise in full (FR-019).
/// </summary>
public static class GetExercise
{
    /// <summary>
    /// One exercise, for the detail screen and the edit form — so opening /admin/exercises/:id
    /// directly costs one row instead of the whole library.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid id,
        IExerciseStore store,
        CancellationToken cancellationToken)
    {
        var found = await store.FindAsync(id, cancellationToken);

        return found is null ? Results.NotFound() : Results.Ok(ExerciseProjection.ToDto(found));
    }
}
