using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Scheduling;

/// <summary>
/// Infrastructure side of <see cref="IClassGroupQuery"/>. Same shape as ClassScheduleQuery:
/// AsNoTracking and projected in the database — but with no in-memory pass, because ClassGroupSummary
/// carries no enum needing ToString().
/// </summary>
public class ClassGroupQuery(AppDbContext db) : IClassGroupQuery
{
    public async Task<IReadOnlyList<ClassGroupSummary>> GetAllAsync(
        CancellationToken cancellationToken) =>
        // Active first, then alphabetical. Unfiltered on purpose: the admin screen's "show inactive"
        // toggle filters rows it already holds, so flicking it costs no round trip.
        await db.ClassGroups
            .AsNoTracking()
            .OrderByDescending(t => t.IsActive)
            .ThenBy(t => t.Name)
            .Select(t => new ClassGroupSummary(
                t.Id,
                t.Name,
                t.Description,
                t.DefaultDurationMinutes,
                t.DefaultCapacity,
                t.IsActive,
                t.CreatedAt))
            .ToListAsync(cancellationToken);
}
