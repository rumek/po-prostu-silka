using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Scheduling;

public class GroupRosterStore(AppDbContext db) : IGroupRosterStore
{
    public void Add(GroupRosterEntry entry) => db.GroupRosterEntries.Add(entry);

    public void Remove(GroupRosterEntry entry) => db.GroupRosterEntries.Remove(entry);

    public Task<GroupRosterEntry?> FindAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken) =>
        db.GroupRosterEntries.FirstOrDefaultAsync(
            e => e.ClassGroupId == groupId && e.MemberId == memberId, cancellationToken);

    public Task<int> CountAsync(Guid groupId, CancellationToken cancellationToken) =>
        db.GroupRosterEntries.CountAsync(e => e.ClassGroupId == groupId, cancellationToken);

    public async Task<IReadOnlySet<(Guid MemberId, Guid ClassId)>> ActiveBookingPairsAsync(
        IReadOnlyCollection<Guid> memberIds, IReadOnlyCollection<Guid> classIds, CancellationToken cancellationToken) =>
        (await db.Bookings
            .AsNoTracking()
            .Where(b => memberIds.Contains(b.MemberId)
                        && classIds.Contains(b.ClassId)
                        && b.Status == BookingStatus.Active)
            .Select(b => new { b.MemberId, b.ClassId })
            .ToListAsync(cancellationToken))
        .Select(b => (b.MemberId, b.ClassId))
        .ToHashSet();

    public Task<bool> IsInRosterAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken) =>
        db.GroupRosterEntries
            .AsNoTracking()
            .AnyAsync(e => e.ClassGroupId == groupId && e.MemberId == memberId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> MemberIdsAsync(Guid groupId, CancellationToken cancellationToken) =>
        await db.GroupRosterEntries
            .AsNoTracking()
            .Where(e => e.ClassGroupId == groupId)
            .OrderBy(e => e.AddedAt)
            .Select(e => e.MemberId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GroupIdsForMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
        await db.GroupRosterEntries
            .AsNoTracking()
            .Where(e => e.MemberId == memberId)
            .Select(e => e.ClassGroupId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RosterClass>> UpcomingClassesAsync(
        Guid groupId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        await Upcoming(groupId, asOf)
            .OrderBy(c => c.StartsAt)
            .Select(c => new RosterClass(c.Id, c.ClassGroupId, c.StartsAt, c.InstructorMemberId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RosterClass>> UpcomingClassesInRangeAsync(
        Guid groupId, DateOnly from, DateOnly to, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        // Club-local days as UTC instants: [start of `from`, start of the day after `to`). ClubTime does
        // the DST arithmetic; SQL compares instants only, so the range is a seek on StartsAt.
        var fromInstant = ClubTime.StartOfLocalDay(from);
        var toExclusive = ClubTime.StartOfLocalDay(to.AddDays(1));

        return await Upcoming(groupId, asOf)
            .Where(c => c.StartsAt >= fromInstant && c.StartsAt < toExclusive)
            .OrderBy(c => c.StartsAt)
            .Select(c => new RosterClass(c.Id, c.ClassGroupId, c.StartsAt, c.InstructorMemberId))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> IsInstructorOfUpcomingAsync(
        Guid groupId, Guid memberId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Upcoming(groupId, asOf).AnyAsync(c => c.InstructorMemberId == memberId, cancellationToken);

    public async Task<IReadOnlyList<Booking>> ReleasableBookingsAsync(
        Guid groupId,
        Guid memberId,
        DateTimeOffset asOf,
        Guid? instructorMemberId,
        CancellationToken cancellationToken) =>
        // Tracked: the caller cancels these. b.Class is a join here, not a load - the caller rotates the
        // class stamps through IClassStore.
        await db.Bookings
            .Where(b => b.MemberId == memberId
                        && b.Status == BookingStatus.Active
                        && b.MakeupForBookingId == null
                        && b.Class.ClassGroupId == groupId
                        && b.Class.Status == ClassStatus.Scheduled
                        && b.Class.StartsAt > asOf
                        && (instructorMemberId == null || b.Class.InstructorMemberId == instructorMemberId))
            .ToListAsync(cancellationToken);

    /// <summary>"Upcoming" as S-37 means it everywhere: scheduled, and not yet started.</summary>
    private IQueryable<Class> Upcoming(Guid groupId, DateTimeOffset asOf) =>
        db.Classes
            .AsNoTracking()
            .Where(c => c.ClassGroupId == groupId && c.Status == ClassStatus.Scheduled && c.StartsAt > asOf);
}
