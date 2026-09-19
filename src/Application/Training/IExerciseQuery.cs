using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Narrow read seam over the exercise table, so Application does not reference EF Core (AGENTS.md
/// layering). Implemented in Infrastructure.
/// </summary>
public interface IExerciseQuery
{
    /// <summary>Every exercise, active first and then by name. Unbounded — see GetAllAsync.</summary>
    Task<IReadOnlyList<ExerciseSummary>> GetAllAsync(CancellationToken cancellationToken);
}
