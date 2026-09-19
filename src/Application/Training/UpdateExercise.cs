using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Edits an exercise (FR-018).
/// </summary>
public static class UpdateExercise
{
    /// <summary>
    /// Replaces every field of an exercise. Nothing references an exercise yet, so an edit
    /// propagates nowhere; once S-11 lands, a plan will resolve name and instructions BY REFERENCE,
    /// which is what makes correcting a typo here fix it in every plan at once.
    /// </summary>
    public static async Task<IResult> HandleAsync(
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

        var invalid = ExerciseValidator.Validate(request);
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
        ExerciseProjection.Apply(existing, request);

        // No concurrency token on Exercise: with exactly one admin account ever seeded (AdminSeeder)
        // there is no second writer, so two edits of the same row cannot race. A second admin makes
        // this last-write-wins, at which point Exercise needs a ConcurrencyStamp.
        //
        // The NAME collision is a different matter and is handled - see ExerciseValidator.NameTaken.
        if (await unitOfWork.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return ExerciseValidator.NameTaken(unitOfWork);
        }

        return Results.Ok(ExerciseProjection.ToDto(existing));
    }
}
