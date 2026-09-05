using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Training;
using po_prostu_silka.Domain.Training;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Training;

/// <summary>
/// Write side of the training-plan surface. Everything here returns TRACKED entities - callers
/// mutate what comes back and rely on the change tracker - and nothing here saves; the endpoint
/// commits through IUnitOfWork.
/// </summary>
public class TrainingPlanStore(AppDbContext db) : ITrainingPlanStore
{
    /// <summary>
    /// Tracked, WITH items: the edit path replaces the whole list, so the tracker has to know which
    /// rows exist in order to delete them.
    /// </summary>
    public Task<TrainingPlan?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.TrainingPlans
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    /// <summary>
    /// Tracked, WITHOUT items. The assignment path only flips this row's status and rotates its
    /// stamp; loading its items would be fetching rows to ignore them.
    /// </summary>
    public Task<TrainingPlan?> FindActiveForMemberAsync(
        string memberUserId,
        CancellationToken cancellationToken) =>
        db.TrainingPlans.FirstOrDefaultAsync(
            x => x.MemberUserId == memberUserId && x.Status == TrainingPlanStatus.Active,
            cancellationToken);

    public void Add(TrainingPlan entity) => db.TrainingPlans.Add(entity);

    /// <summary>
    /// Swaps the plan's item rows for the given ones, addressing both sides through the DbSet.
    ///
    /// <para>
    /// THE COLLECTION NAVIGATION IS DELIBERATELY NOT TOUCHED. Assigning a fresh list to a tracked
    /// parent's navigation makes EF resolve the incoming children against the entries it already
    /// holds for that parent, and it emitted an UPDATE against a row the same SaveChanges had just
    /// deleted - "expected to affect 1 row(s), but actually affected 0". Deleting the old rows and
    /// adding the new ones with their foreign key already set leaves collection fixup nothing to
    /// guess at, and the projection reads items back from the table anyway.
    /// </para>
    /// </summary>
    public void ReplaceItems(TrainingPlan entity, IReadOnlyList<TrainingPlanItem> items)
    {
        db.TrainingPlanItems.RemoveRange(entity.Items);

        foreach (var item in items)
        {
            item.TrainingPlanId = entity.Id;
        }

        db.TrainingPlanItems.AddRange(items);
    }

    public async Task<IReadOnlyDictionary<Guid, bool>> FindExerciseStatesAsync(
        IReadOnlyCollection<Guid> exerciseIds,
        CancellationToken cancellationToken)
    {
        // One statement for the whole payload rather than a lookup per item: a fifty-exercise plan
        // would otherwise be fifty round trips on a Basic-tier database. Ids with no row are simply
        // absent from the dictionary, which is what lets the caller tell "unknown" from "retired".
        var rows = await db.Exercises
            .AsNoTracking()
            .Where(x => exerciseIds.Contains(x.Id))
            .Select(x => new { x.Id, x.IsActive })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id, x => x.IsActive);
    }
}
