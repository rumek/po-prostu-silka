using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Adds an exercise to the library (FR-018).
/// </summary>
public static class CreateExercise
{
    public static async Task<IResult> HandleAsync(
        ExerciseRequest request,
        IExerciseStore store,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var invalid = ExerciseValidator.Validate(request);
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

        ExerciseProjection.Apply(created, request);

        store.Add(created);

        if (await unitOfWork.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return ExerciseValidator.NameTaken(unitOfWork);
        }

        return Results.Ok(ExerciseProjection.ToDto(created));
    }
}
