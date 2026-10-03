using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Scheduling;

public class GroupRosterQuery(AppDbContext db) : IGroupRosterQuery
{
    public async Task<GroupRosterView?> GetViewAsync(
        Guid groupId, DateTimeOffset asOf, Guid? actingInstructorId, CancellationToken cancellationToken)
    {
        var group = await db.ClassGroups
            .AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new { g.Id, g.Name, g.IsActive, g.DefaultCapacity })
            .SingleOrDefaultAsync(cancellationToken);

        if (group is null)
        {
            return null;
        }

        var isStaff = StaffPredicate.IsStaff(db);

        var roster = await db.GroupRosterEntries
            .AsNoTracking()
            .Where(e => e.ClassGroupId == groupId)
            .OrderBy(e => e.AddedAt)
            .Select(e => new
            {
                e.MemberId,
                e.Member!.DisplayName,
                HasAccount = e.Member.UserId != null,
                e.Member.Status,
                IsStaff = db.Members.Where(isStaff).Any(m => m.Id == e.MemberId),
                e.AddedAt,
            })
            .ToListAsync(cancellationToken);

        var classes = await db.Classes
            .AsNoTracking()
            .Where(c => c.ClassGroupId == groupId && c.Status == ClassStatus.Scheduled && c.StartsAt > asOf)
            .OrderBy(c => c.StartsAt)
            .Select(c => new
            {
                c.Id,
                c.StartsAt,
                c.Capacity,
                c.InstructorMemberId,
                Booked = db.Bookings.Count(b => b.ClassId == c.Id && b.Status == BookingStatus.Active),
            })
            .ToListAsync(cancellationToken);

        var memberIds = roster.Select(r => r.MemberId).ToList();
        var classIds = classes.Select(c => c.Id).ToList();

        // Who holds an active booking on which upcoming class - a makeup counts, it is a seat all the same.
        var held = (await db.Bookings
                .AsNoTracking()
                .Where(b => memberIds.Contains(b.MemberId)
                            && classIds.Contains(b.ClassId)
                            && b.Status == BookingStatus.Active)
                .Select(b => new { b.MemberId, b.ClassId })
                .ToListAsync(cancellationToken))
            .Select(b => (b.MemberId, b.ClassId))
            .ToHashSet();

        // Karnets and their entries left, ONCE per member rather than per class. Entries used is the same
        // definition the protocol counts against (EntryConsumption).
        var passes = await db.MembershipPasses
            .AsNoTracking()
            .Where(p => memberIds.Contains(p.MemberId))
            .Select(p => new
            {
                p.MemberId,
                p.ValidFrom,
                p.ValidTo,
                Left = p.EntryCount - db.Bookings
                    .Where(b => b.MembershipPassId == p.Id)
                    .Where(EntryConsumption.ConsumesAnEntry)
                    .Count(),
            })
            .ToListAsync(cancellationToken);

        var members = roster.Select(r =>
        {
            var gaps = new List<RosterGap>();
            var booked = 0;

            foreach (var c in classes)
            {
                if (held.Contains((r.MemberId, c.Id)))
                {
                    booked++;
                    continue;
                }

                // The protocol's order: who, then the seat, then the karnet. The first that would refuse
                // is the reason; nothing refusing is "bookable".
                var date = DateOnly.FromDateTime(ClubTime.ToClubLocal(c.StartsAt).DateTime);

                // The protocol's covering karnet is the earliest-starting one covering the date.
                var pass = passes
                    .Where(p => p.MemberId == r.MemberId && p.ValidFrom <= date && date <= p.ValidTo)
                    .OrderBy(p => p.ValidFrom)
                    .FirstOrDefault();

                var reason =
                    r.Status != MembershipStatus.Active ? "member_blocked"
                    : r.IsStaff ? "member_is_staff"
                    : actingInstructorId is not null && c.InstructorMemberId != actingInstructorId
                        ? RosterBooking.NotYourClass
                    : c.Booked >= c.Capacity ? "class_full"
                    : pass is null ? "no_valid_pass"
                    : pass.Left <= 0 ? "no_entries_left"
                    : RosterGap.Bookable;

                gaps.Add(new RosterGap(c.Id, c.StartsAt, reason));
            }

            return new GroupRosterMember(r.MemberId, r.DisplayName, r.HasAccount, r.AddedAt, booked, gaps);
        }).ToList();

        return new GroupRosterView(group.Id, group.Name, group.IsActive, group.DefaultCapacity, classes.Count, members);
    }

    public async Task<IReadOnlyList<TrainerGroup>> GetTrainerGroupsAsync(
        Guid instructorMemberId, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var groups = await db.ClassGroups
            .AsNoTracking()
            .Where(g => db.Classes.Any(c => c.ClassGroupId == g.Id
                                            && c.InstructorMemberId == instructorMemberId
                                            && c.Status == ClassStatus.Scheduled
                                            && c.StartsAt > asOf))
            .Select(g => new
            {
                g.Id,
                g.Name,
                RosterCount = db.GroupRosterEntries.Count(e => e.ClassGroupId == g.Id),
                g.DefaultCapacity,
                NextClassAt = db.Classes
                    .Where(c => c.ClassGroupId == g.Id && c.Status == ClassStatus.Scheduled && c.StartsAt > asOf)
                    .Min(c => (DateTimeOffset?)c.StartsAt),
            })
            .ToListAsync(cancellationToken);

        return groups
            .OrderBy(g => g.NextClassAt)
            .ThenBy(g => g.Name)
            .Select(g => new TrainerGroup(g.Id, g.Name, g.RosterCount, g.DefaultCapacity, g.NextClassAt))
            .ToList();
    }
}
