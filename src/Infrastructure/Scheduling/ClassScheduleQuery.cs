using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Scheduling;

/// <summary>
/// Infrastructure side of <see cref="IClassScheduleQuery"/>. Same shape as MemberQuery:
/// AsNoTracking, projected in the database, enum mapped to its name in memory after materialising
/// because ToString() has no SQL translation.
/// </summary>
public class ClassScheduleQuery(AppDbContext db) : IClassScheduleQuery
{
    public Task<IReadOnlyList<ScheduledClass>> GetScheduleAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        // Cancelled classes are excluded from the member schedule. Nothing sets that status until
        // S-05, but filtering on it now means S-05 adds a transition and not a rewrite of this query.
        ProjectAsync(
            db.Classes.Where(c =>
                c.Status == ClassStatus.Scheduled && c.StartsAt >= from && c.StartsAt < to),
            cancellationToken);

    public Task<IReadOnlyList<ScheduledClass>> GetUpcomingForAdminAsync(
        DateTimeOffset from, CancellationToken cancellationToken) =>
        // No upper bound and no status filter: the admin manages what they scheduled, including
        // anything S-05 later cancels.
        ProjectAsync(db.Classes.Where(c => c.StartsAt >= from), cancellationToken);

    private static async Task<IReadOnlyList<ScheduledClass>> ProjectAsync(
        IQueryable<Class> query, CancellationToken cancellationToken)
    {
        var rows = await query
            .AsNoTracking()
            .OrderBy(c => c.StartsAt)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.StartsAt,
                c.DurationMinutes,
                c.Room,
                c.Instructor,
                c.Capacity,
                c.Status,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ScheduledClass(
                r.Id,
                r.Name,
                r.StartsAt,
                r.DurationMinutes,
                r.Room,
                r.Instructor,
                r.Capacity,
                // FREE SPOTS = CAPACITY, by construction: Booking does not exist until S-04, so
                // nothing can be booked. S-04 replaces this one expression with
                // "r.Capacity - <booked count>" and nothing else in the stack changes - not this
                // DTO, not the schedule template, not its spec.
                r.Capacity,
                r.Status.ToString()))
            .ToList();
    }
}
