using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Retires an exercise without deleting it - plans that prescribe it still read.
/// </summary>
public static class DeactivateExercise
{
    /// <summary>
    /// Retires an exercise: it leaves the library's active set while everything referencing it stays
    /// intact. No uniqueness check is needed — deactivating only ever RELEASES a name.
    ///
    /// Idempotent. Deactivating an already-inactive exercise is a 200, not a refusal.
    /// </summary>
    public static async Task<IResult> HandleAsync(
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

        return Results.Ok(ExerciseProjection.ToDto(existing));
    }
}
