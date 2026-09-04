using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Training;
using po_prostu_silka.Domain.Training;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Training;

/// <summary>
/// Infrastructure side of <see cref="IExerciseStore"/>. Nothing here saves — the endpoint commits
/// through IUnitOfWork.
/// </summary>
public class ExerciseStore(AppDbContext db) : IExerciseStore
{
    public async Task<Exercise?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        // Tracked, not AsNoTracking: the update, deactivate and activate handlers all mutate what
        // this returns and expect the change tracker to notice.
        await db.Exercises.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public void Add(Exercise entity) => db.Exercises.Add(entity);

    /// <summary>
    /// Whether another ACTIVE exercise holds this name.
    ///
    /// <para>
    /// A plain <c>==</c>, deliberately, exactly as ClassTypeStore documents: SQL Server's default
    /// collation is case-insensitive, so this already refuses "przysiad" against an active
    /// "Przysiad". Calling ToLower() would express the same intent while making the predicate
    /// non-sargable, so it could no longer use IX_Exercises_Name_Active.
    /// </para>
    ///
    /// <para>
    /// This is a read-then-write race: the caller checks here and writes after. The filtered unique
    /// index is the real backstop — a loser of the race gets a DbUpdateException rather than two
    /// active exercises sharing a name. The check exists so the ordinary case returns a clean 409.
    /// </para>
    /// </summary>
    public Task<bool> IsNameTakenAsync(
        string name,
        Guid? excludingId,
        CancellationToken cancellationToken) =>
        db.Exercises
            .AsNoTracking()
            .AnyAsync(
                e => e.IsActive
                     && e.Name == name
                     && (excludingId == null || e.Id != excludingId),
                cancellationToken);
}
