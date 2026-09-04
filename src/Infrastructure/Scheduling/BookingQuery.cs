using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Scheduling;

/// <summary>
/// Infrastructure side of <see cref="IBookingQuery"/>. Same shape as ClassScheduleQuery:
/// AsNoTracking, projected in the database, related rows reached by referencing navigations INSIDE
/// Select rather than by Include — so EF emits joins and returns exactly these columns.
/// </summary>
public class BookingQuery(AppDbContext db) : IBookingQuery
{
    public async Task<IReadOnlyList<MyBooking>> GetUpcomingForMemberAsync(
        string memberUserId, DateTimeOffset from, CancellationToken cancellationToken) =>
        // Ordered by the CLASS's start, not by when the booking was made: this is a list of what the
        // member still has to attend, so chronology means the gym's clock.
        //
        // The name, description and instructor are resolved two navigations deep (Booking -> Class ->
        // ClassType / Instructor) because a booking stores none of them - the same resolution the
        // schedule performs, and the same reason: a typo corrected on a class type is corrected here.
        //
        // TWO STATUSES ARE CHECKED, AND THAT IS THE MODEL S-09 CHOSE, NOT AN EXTRA SAFETY NET.
        // Cancelling a class deliberately leaves every Booking row Active — cascading would record
        // that the MEMBER cancelled, which is false and rewrites the club's own history — so the
        // class's own status is the ONLY thing keeping a cancelled class out of "Moje zajęcia".
        // This predicate is therefore the single point where that model can be silently broken: a
        // future query that copies this one and forgets `b.Class.Status` puts cancelled classes back
        // in front of members who were emailed that they are not happening. See
        // ClassEndpoints.CancelAsync.
        //
        // Seeks IX_Bookings_Member_Status for the member's rows, then joins.
        await db.Bookings
            .AsNoTracking()
            .Where(b => b.MemberUserId == memberUserId
                        && b.Status == BookingStatus.Active
                        && b.Class.Status == ClassStatus.Scheduled
                        && b.Class.StartsAt >= from)
            .OrderBy(b => b.Class.StartsAt)
            .Select(b => new MyBooking(
                b.Id,
                b.ClassId,
                b.Class.ClassType.Name,
                b.Class.ClassType.Description,
                b.Class.StartsAt,
                b.Class.DurationMinutes,
                b.Class.Instructor.DisplayName,
                b.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ClassBooking>> GetForClassAsync(
        Guid classId, CancellationToken cancellationToken) =>
        // Active only, ordered by when they booked, so the admin sees who was first - which is the
        // club's own tie-breaker when a class has to shrink.
        //
        // Email is nullable on ApplicationUser because Identity allows it; every account this app
        // creates has one, since registration is by email. The coalesce is a contract detail rather
        // than a real case - the SPA renders a string, and a null crossing the wire as an absent
        // field would break its type for a row that cannot exist.
        await db.Bookings
            .AsNoTracking()
            .Where(b => b.ClassId == classId && b.Status == BookingStatus.Active)
            .OrderBy(b => b.CreatedAt)
            .Select(b => new ClassBooking(
                b.Id,
                b.MemberUserId,
                b.Member.DisplayName,
                b.Member.Email ?? string.Empty,
                b.CreatedAt))
            .ToListAsync(cancellationToken);
}
