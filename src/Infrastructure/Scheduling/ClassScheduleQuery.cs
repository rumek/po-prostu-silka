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
        // S-09, but filtering on it now means S-09 adds a transition and not a rewrite of this query.
        ProjectAsync(
            db.Classes.Where(c =>
                c.Status == ClassStatus.Scheduled && c.StartsAt >= from && c.StartsAt < to),
            cancellationToken);

    public Task<IReadOnlyList<ScheduledClass>> GetUpcomingForAdminAsync(
        DateTimeOffset from, DateTimeOffset? to, CancellationToken cancellationToken) =>
        // Still NO STATUS FILTER, whether or not a window is given: the admin manages what they
        // scheduled, including anything S-09 later cancels. Only the upper bound is new, and it stays
        // optional — without one this is the unbounded list it has always been.
        ProjectAsync(
            db.Classes.Where(c => c.StartsAt >= from && (to == null || c.StartsAt < to)),
            cancellationToken);

    private static async Task<IReadOnlyList<ScheduledClass>> ProjectAsync(
        IQueryable<Class> query, CancellationToken cancellationToken)
    {
        // The name, description and instructor name are RESOLVED through the navigations, not read
        // off the occurrence - it holds none of the three (prd-v2 FR-007, FR-009, FR-010). That is
        // what makes correcting a typo on a class type correct it on every occurrence at once, past
        // ones included.
        //
        // Navigations inside a Select, NOT Include: this stays a projection, so EF emits joins and
        // returns exactly these columns. An Include added here would materialise whole ClassType and
        // ApplicationUser rows for every class - and, with query splitting ever enabled globally,
        // turn one statement into three.
        var rows = await query
            .AsNoTracking()
            .OrderBy(c => c.StartsAt)
            .Select(c => new
            {
                c.Id,
                c.ClassTypeId,
                ClassTypeName = c.ClassType.Name,
                ClassTypeDescription = c.ClassType.Description,
                c.StartsAt,
                c.DurationMinutes,
                c.InstructorUserId,
                InstructorDisplayName = c.Instructor.DisplayName,
                c.Capacity,
                c.Status,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ScheduledClass(
                r.Id,
                r.ClassTypeId,
                r.ClassTypeName,
                r.ClassTypeDescription,
                r.StartsAt,
                r.DurationMinutes,
                r.InstructorUserId,
                r.InstructorDisplayName,
                r.Capacity,
                // FREE SPOTS = CAPACITY, by construction: Booking does not exist until S-08, so
                // nothing can be booked. S-08 replaces this one expression with
                // "r.Capacity - <booked count>" and nothing else in the stack changes - not this
                // DTO, not the schedule template, not its spec.
                r.Capacity,
                r.Status.ToString()))
            .ToList();
    }
}
