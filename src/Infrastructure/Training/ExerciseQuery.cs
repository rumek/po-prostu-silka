using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Training;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Training;

/// <summary>
/// Infrastructure side of <see cref="IExerciseQuery"/>. Same shape as ClassTypeQuery: AsNoTracking
/// and projected in the database, with no in-memory pass because ExerciseSummary carries no enum
/// needing ToString().
/// </summary>
public class ExerciseQuery(AppDbContext db) : IExerciseQuery
{
    public async Task<IReadOnlyList<ExerciseSummary>> GetAllAsync(
        CancellationToken cancellationToken) =>
        // Active first, then alphabetical. Unfiltered on purpose: the admin screen's "show inactive"
        // toggle filters rows it already holds, so flicking it costs no round trip - and the form
        // reuses this same call to build its muscle-group and difficulty suggestions.
        await db.Exercises
            .AsNoTracking()
            .OrderByDescending(e => e.IsActive)
            .ThenBy(e => e.Name)
            .Select(e => new ExerciseSummary(
                e.Id,
                e.Name,
                e.Description,
                e.MuscleGroup,
                e.Difficulty,
                e.Equipment,
                e.Preparation,
                e.StartingPosition,
                e.Execution,
                e.VideoId,
                e.IsActive,
                e.CreatedAt))
            .ToListAsync(cancellationToken);
}
