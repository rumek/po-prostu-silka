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
        Guid memberId, DateTimeOffset from, CancellationToken cancellationToken) =>
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
        // Seeks IX_Bookings_MemberId_Status for the member's rows, then joins.
        await db.Bookings
            .AsNoTracking()
            .Where(b => b.MemberId == memberId
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
                b.Class.Instructor!.DisplayName,
                b.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ClassBooking>> GetForClassAsync(
        Guid classId, CancellationToken cancellationToken) =>
        // Active only, ordered by when they booked, so the admin sees who was first - which is the
        // club's own tie-breaker when a class has to shrink.
        //
        // The email is the MEMBER's, not the account's, and since S-14 a null one is a real case: a
        // person recorded at the desk may never have given an address. The coalesce turns that into
        // the blank string ClassChangeNotification skips, so nobody without an address is enqueued a
        // message that could only fail - and the SPA's roster still receives a string rather than an
        // absent field. Pinned by ClassCancellationTests' accountless-member cases.
        await db.Bookings
            .AsNoTracking()
            .Where(b => b.ClassId == classId && b.Status == BookingStatus.Active)
            .OrderBy(b => b.CreatedAt)
            .Select(b => new ClassBooking(
                b.Id,
                b.MemberId,
                b.Member!.UserId,
                b.Member!.DisplayName,
                b.Member!.Email ?? string.Empty,
                b.CreatedAt,
                // Lower-case words rather than the enum's names, the same spelling the PUT accepts.
                b.Attendance == BookingAttendance.Present ? "present"
                : b.Attendance == BookingAttendance.Absent ? "absent"
                : null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MyAttendanceEntry>> GetHistoryForMemberAsync(
        Guid memberId,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        // Seeks IX_Bookings_MemberId_Status, then joins; bounded to three months per call.
        //
        // NO b.Class.Status FILTER, deliberately - the opposite of GetUpcomingForMemberAsync. A
        // cancelled class is exactly the row that explains an entry coming back.
        await db.Bookings
            .AsNoTracking()
            .Where(b => b.MemberId == memberId
                        && b.Status == BookingStatus.Active
                        && b.Class.StartsAt >= from
                        && b.Class.StartsAt < to
                        && b.Class.StartsAt <= now)
            .OrderByDescending(b => b.Class.StartsAt)
            .Select(b => new MyAttendanceEntry(
                b.Id,
                b.ClassId,
                b.Class.ClassType.Name,
                b.Class.StartsAt,
                b.Class.DurationMinutes,
                b.Class.Instructor!.DisplayName,
                b.Class.Status == ClassStatus.Cancelled ? "cancelled"
                : b.Attendance == BookingAttendance.Present ? "present"
                : b.Attendance == BookingAttendance.Absent ? "absent"
                : "unrecorded"))
            .ToListAsync(cancellationToken);

    public Task<bool> HasHistoryBeforeAsync(
        Guid memberId, DateTimeOffset before, CancellationToken cancellationToken) =>
        db.Bookings.AnyAsync(
            b => b.MemberId == memberId
                 && b.Status == BookingStatus.Active
                 && b.Class.StartsAt < before,
            cancellationToken);

    public async Task<AttendanceCounts> CountAttendanceForPassAsync(
        Guid passId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // One grouped statement rather than three counts. A pass holds tens of rows.
        var groups = await db.Bookings
            .AsNoTracking()
            .Where(b => b.MembershipPassId == passId
                        && b.Status == BookingStatus.Active
                        && b.Class.Status != ClassStatus.Cancelled
                        && b.Class.StartsAt <= now)
            .GroupBy(b => b.Attendance)
            .Select(g => new { Attendance = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(BookingAttendance? a) => groups.FirstOrDefault(g => g.Attendance == a)?.Count ?? 0;

        return new AttendanceCounts(
            CountOf(BookingAttendance.Present), CountOf(BookingAttendance.Absent), CountOf(null));
    }
}
