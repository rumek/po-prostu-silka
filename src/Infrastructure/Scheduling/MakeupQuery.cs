using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Paging;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Scheduling;

/// <summary>
/// Infrastructure side of <see cref="IMakeupQuery"/> (S-36).
///
/// <para>
/// STATUS IS DERIVED IN MEMORY, AFTER ONE PROJECTION. An item's deadline is its absence's CLUB-LOCAL
/// date plus thirty days, and the club-local date is a time-zone conversion SQL Server would have to be
/// taught; <see cref="MakeupRules.StateOf"/> is then the one definition every caller reads. That loads
/// every "odrobi" absence the club has - a handful a week for a club of dozens - which is cheaper than
/// a second definition of the rule in SQL drifting from the first. Revisit with a measured plan, not a
/// guess, if the list ever grows slow.
/// </para>
/// </summary>
public class MakeupQuery(AppDbContext db, TimeProvider timeProvider) : IMakeupQuery
{
    private sealed record Row(
        Guid AbsenceBookingId,
        Guid MemberId,
        string DisplayName,
        string ClassName,
        DateTimeOffset AbsenceStartsAt,
        string Instructor,
        DateTimeOffset? ClosedAt,
        Guid? MakeupClassId,
        Guid? MakeupBookingId,
        string? MakeupName,
        DateTimeOffset? MakeupStartsAt,
        string? MakeupInstructor,
        BookingAttendance? MakeupAttendance);

    public async Task<PagedResult<MakeupItem>> GetItemsAsync(
        bool includeClosed, int page, int pageSize, CancellationToken cancellationToken)
    {
        var today = ClubToday();
        var items = (await RowsAsync(Absences(), cancellationToken))
            .Select(r => ToItem(r, today))
            .Where(i => includeClosed || i.Status is "open" or "planned")
            // Nearest deadline first: the list is a to-do list. The absence's start breaks a tie, then
            // the id, so the 25th/26th cut is deterministic across pages.
            .OrderBy(i => i.Deadline)
            .ThenBy(i => i.AbsenceStartsAt)
            .ThenBy(i => i.AbsenceBookingId)
            .ToList();

        return new PagedResult<MakeupItem>(
            items.Skip((page - 1) * pageSize).Take(pageSize).ToList(), items.Count, page, pageSize);
    }

    public async Task<MakeupItem?> GetItemAsync(Guid absenceBookingId, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(Absences().Where(b => b.Id == absenceBookingId), cancellationToken);
        return rows.Count == 0 ? null : ToItem(rows[0], ClubToday());
    }

    public async Task<MyMakeups> GetForMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var today = ClubToday();
        var open = (await RowsAsync(Absences().Where(b => b.MemberId == memberId), cancellationToken))
            .Select(r => ToItem(r, today))
            .Where(i => i.Status == "open")
            .ToList();

        return new MyMakeups(open.Count, open.Count == 0 ? null : open.Min(i => i.Deadline));
    }

    public async Task<IReadOnlyList<ScheduledClass>> GetEligibleClassesAsync(
        Guid memberId, DateOnly deadline, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // The first instant AFTER the deadline day, club-local - an exclusive upper bound in UTC.
        var until = ClubTime.StartOfLocalDay(deadline.AddDays(1));

        var rows = await db.Classes
            .AsNoTracking()
            .Where(c => c.Status == ClassStatus.Scheduled && c.StartsAt > now && c.StartsAt < until)
            .Where(c => !db.Bookings.Any(
                b => b.ClassId == c.Id && b.MemberId == memberId && b.Status == BookingStatus.Active))
            .OrderBy(c => c.StartsAt)
            .Select(c => new
            {
                c.Id,
                c.ClassGroupId,
                Name = c.ClassGroup.Name,
                c.ClassGroup.Description,
                c.StartsAt,
                c.DurationMinutes,
                c.InstructorMemberId,
                Instructor = c.Instructor!.DisplayName,
                c.Capacity,
                c.Status,
                BookedCount = db.Bookings.Count(b => b.ClassId == c.Id && b.Status == BookingStatus.Active),
            })
            .ToListAsync(cancellationToken);

        // The karnet check in memory, against the member's few passes, with the booking loop's own
        // club-local date - so the picker never offers a class the booking would refuse no_valid_pass.
        var passes = await db.MembershipPasses
            .AsNoTracking()
            .Where(p => p.MemberId == memberId)
            .Select(p => new { p.ValidFrom, p.ValidTo })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.BookedCount < r.Capacity)
            .Where(r =>
            {
                var date = DateOnly.FromDateTime(ClubTime.ToClubLocal(r.StartsAt).DateTime);
                return passes.Any(p => p.ValidFrom <= date && date <= p.ValidTo);
            })
            .Select(r => new ScheduledClass(
                r.Id,
                r.ClassGroupId,
                r.Name,
                r.Description,
                r.StartsAt,
                r.DurationMinutes,
                r.InstructorMemberId,
                r.Instructor,
                r.Capacity,
                r.Capacity - r.BookedCount,
                r.Status.ToString()))
            .ToList();
    }

    /// <summary>
    /// Every "odrobi" absence that still stands: an active booking on a class that took place, of a
    /// member who is not staff (defence in depth - staff are never booked, S-25).
    /// </summary>
    private IQueryable<Booking> Absences()
    {
        var notStaff = StaffPredicate.IsNotStaff(db);

        return db.Bookings
            .Where(b => b.Attendance == BookingAttendance.Makeup
                        && b.Status == BookingStatus.Active
                        && b.Class.Status != ClassStatus.Cancelled)
            .Where(b => db.Members.Where(notStaff).Any(m => m.Id == b.MemberId));
    }

    private async Task<List<Row>> RowsAsync(IQueryable<Booking> absences, CancellationToken cancellationToken) =>
        await absences
            .AsNoTracking()
            .Select(b => new
            {
                Absence = b,
                // The LIVE makeup: active, on a class that still stands. A makeup on a cancelled class
                // leaves the item open again, which is why the class status is part of "live".
                Makeup = db.Bookings
                    .Where(m => m.MakeupForBookingId == b.Id
                                && m.Status == BookingStatus.Active
                                && m.Class.Status != ClassStatus.Cancelled)
                    .Select(m => new
                    {
                        m.ClassId,
                        m.Id,
                        Name = m.Class.ClassGroup.Name,
                        m.Class.StartsAt,
                        Instructor = m.Class.Instructor!.DisplayName,
                        m.Attendance,
                    })
                    .FirstOrDefault(),
            })
            .Select(x => new Row(
                x.Absence.Id,
                x.Absence.MemberId,
                x.Absence.Member!.DisplayName,
                x.Absence.Class.ClassGroup.Name,
                x.Absence.Class.StartsAt,
                x.Absence.Class.Instructor!.DisplayName,
                x.Absence.MakeupClosedAt,
                x.Makeup == null ? null : x.Makeup.ClassId,
                x.Makeup == null ? null : x.Makeup.Id,
                x.Makeup == null ? null : x.Makeup.Name,
                x.Makeup == null ? null : x.Makeup.StartsAt,
                x.Makeup == null ? null : x.Makeup.Instructor,
                x.Makeup == null ? null : x.Makeup.Attendance))
            .ToListAsync(cancellationToken);

    private static MakeupItem ToItem(Row r, DateOnly today)
    {
        var deadline = MakeupRules.DeadlineFor(
            DateOnly.FromDateTime(ClubTime.ToClubLocal(r.AbsenceStartsAt).DateTime));
        var hasMakeup = r.MakeupBookingId is not null;
        var state = MakeupRules.StateOf(r.ClosedAt is not null, hasMakeup, r.MakeupAttendance, deadline, today);

        return new MakeupItem(
            r.AbsenceBookingId,
            r.MemberId,
            r.DisplayName,
            r.ClassName,
            r.AbsenceStartsAt,
            r.Instructor,
            deadline,
            MakeupRules.WireName(state),
            r.ClosedAt is not null,
            hasMakeup
                ? new MakeupClass(
                    r.MakeupClassId!.Value, r.MakeupBookingId!.Value, r.MakeupName!, r.MakeupStartsAt!.Value,
                    r.MakeupInstructor!)
                : null);
    }

    private DateOnly ClubToday() =>
        DateOnly.FromDateTime(ClubTime.ToClubLocal(timeProvider.GetUtcNow()).DateTime);
}
