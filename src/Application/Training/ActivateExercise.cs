using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Brings a retired exercise back.
/// </summary>
public static class ActivateExercise
{
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

        if (await store.IsNameTakenAsync(existing.Name, id, cancellationToken))
        {
            return Results.Json(new ExerciseFailure("name_taken"), statusCode: 409);
        }

        existing.IsActive = true;

        if (await unitOfWork.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return ExerciseValidator.NameTaken(unitOfWork);
        }

        return Results.Ok(ExerciseProjection.ToDto(existing));
    }
}
