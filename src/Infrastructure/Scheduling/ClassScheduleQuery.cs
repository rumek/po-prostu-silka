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
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        // SAME STATUS FILTER as the member's query since S-09. A cancelled class is done: the
        // members were told, and leaving its tile on the admin's calendar leaves a slot that looks
        // occupied and is not - so the admin cannot reuse the hour without first working out that
        // the block in the way is dead. The row and its bookings survive, so who was signed up is
        // still on record and still readable through GET {id}/bookings; it is the CALENDAR the
        // cancellation leaves, not the database.
        //
        // The window is now the only difference between the two queries.
        ProjectAsync(
            db.Classes.Where(c =>
                c.Status == ClassStatus.Scheduled && c.StartsAt >= from && c.StartsAt < to),
            cancellationToken);

    private async Task<IReadOnlyList<ScheduledClass>> ProjectAsync(
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

                // A CORRELATED SUBQUERY, NOT A COLLECTION NAVIGATION. Class deliberately has no
                // Bookings collection - see Booking.Class for why: a collection hanging off the
                // aggregate is a standing invitation for a write path to count spots through it,
                // and a capacity check that does not also rotate ConcurrencyStamp is not a check.
                //
                // This costs nothing to avoid it. EF translates the subquery into the same single
                // statement a navigation would have produced, seeking
                // IX_Bookings_Class_Member_Active - whose filter is exactly this predicate's Status
                // term - so the whole window is still ONE round trip.
                BookedCount = db.Bookings.Count(
                    b => b.ClassId == c.Id && b.Status == BookingStatus.Active),
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
                // NOT CLAMPED AT ZERO. With the capacity_below_bookings guard on the edit path a
                // negative value is unreachable, and clamping would turn a broken invariant into a
                // plausible-looking number - the one failure nobody would notice.
                r.Capacity - r.BookedCount,
                r.Status.ToString()))
            .ToList();
    }
}
