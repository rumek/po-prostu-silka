using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.TestData;

namespace po_prostu_silka.Tests;

/// <summary>
/// The generator alone, without a database: it is pure, so what it promises about determinism and about
/// the app's rules can be checked directly rather than through two seeds a few seconds apart.
///
/// <para>
/// THE SEEDER BYPASSES EVERY APPLICATION RULE (it writes through the DbContext), so these tests are where
/// the club-shaped data set is held to them: no overlapping class, no overbooking, no booking without a
/// covering karnet and an entry, attendance only after the start, makeups within the deadline and under a
/// karnet. They run over ten consecutive club-local days, across the October DST change, because every
/// guarantee must hold whatever day a reseed happens on.
/// </para>
/// </summary>
public class TestDataGeneratorTests
{
    public static TheoryData<DateTimeOffset> TenDaysAcrossDst()
    {
        var days = new TheoryData<DateTimeOffset>();
        for (var i = 0; i < 10; i++)
        {
            // 10:00 UTC from 20 October 2026; the clocks go back on the 25th.
            days.Add(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero).AddDays(i));
        }

        return days;
    }

    [Fact]
    public void Same_club_local_day_produces_the_same_data_at_any_hour()
    {
        // 08:00 and 20:00 club-local (CEST) on the same day.
        var morning = TestDataGenerator.Generate(TestDataSeeder.Seed, new DateTimeOffset(2026, 9, 22, 6, 0, 0, TimeSpan.Zero));
        var evening = TestDataGenerator.Generate(TestDataSeeder.Seed, new DateTimeOffset(2026, 9, 22, 18, 0, 0, TimeSpan.Zero));

        Assert.Equal(morning.Bookings.Select(b => b.Id), evening.Bookings.Select(b => b.Id));
        Assert.Equal(morning.RosterEntries.Select(r => r.Id), evening.RosterEntries.Select(r => r.Id));
        Assert.Equal(morning.Exercises.Select(e => e.Id), evening.Exercises.Select(e => e.Id));
        Assert.Equal(morning.Plans.Select(p => p.Id), evening.Plans.Select(p => p.Id));
    }

    [Fact]
    public void Nothing_predates_its_member()
    {
        var data = TestDataGenerator.Generate(TestDataSeeder.Seed, DateTimeOffset.UtcNow);
        var joined = data.Members.ToDictionary(m => m.Id, m => m.CreatedAt);

        Assert.All(data.Passes, p => Assert.True(p.IssuedAt >= joined[p.MemberId], "A pass predates its member."));
        Assert.All(data.Bookings, b => Assert.True(b.CreatedAt >= joined[b.MemberId], "A booking predates its member."));
        Assert.All(data.RosterEntries, r => Assert.True(r.AddedAt >= joined[r.MemberId], "A roster entry predates its member."));
    }

    [Theory]
    [MemberData(nameof(TenDaysAcrossDst))]
    public void The_schedule_is_fixed_weekly_groups_that_never_overlap(DateTimeOffset now)
    {
        var data = TestDataGenerator.Generate(TestDataSeeder.Seed, now);

        // HasTimeConflictAsync's rule: half-open intervals, club-wide, cancelled classes included.
        var ordered = data.Classes.OrderBy(c => c.StartsAt).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.True(
                ordered[i - 1].StartsAt.AddMinutes(ordered[i - 1].DurationMinutes) <= ordered[i].StartsAt,
                $"Classes {ordered[i - 1].Id} and {ordered[i].Id} overlap.");
        }

        var active = data.ClassGroups.Where(g => g.IsActive).ToList();
        Assert.InRange(active.Count, 28, 32);
        Assert.Contains(data.ClassGroups, g => !g.IsActive);
        Assert.All(data.Classes, c => Assert.Equal(data.ClassGroups.Single(g => g.Id == c.ClassGroupId).DefaultCapacity, c.Capacity));

        // The four kinds of the spreadsheet.
        Assert.Equal([1, 2, 3, 6], active.Select(g => g.DefaultCapacity).Distinct().Order());

        // A substitution: an occurrence taught by someone other than its group's usual instructor.
        var usual = data.Classes.GroupBy(c => c.ClassGroupId)
            .ToDictionary(g => g.Key, g => g.GroupBy(c => c.InstructorMemberId).MaxBy(i => i.Count())!.Key);
        Assert.Contains(data.Classes, c => c.InstructorMemberId != usual[c.ClassGroupId]);
    }

    [Theory]
    [MemberData(nameof(TenDaysAcrossDst))]
    public void Rosters_fit_their_groups_and_hold_no_staff(DateTimeOffset now)
    {
        var data = TestDataGenerator.Generate(TestDataSeeder.Seed, now);
        var staff = StaffIds(data);

        foreach (var group in data.ClassGroups)
        {
            var roster = data.RosterEntries.Where(r => r.ClassGroupId == group.Id).ToList();
            Assert.InRange(roster.Count, 1, group.DefaultCapacity);
            Assert.Equal(roster.Count, roster.Select(r => r.MemberId).Distinct().Count());
        }

        Assert.DoesNotContain(data.RosterEntries, r => staff.Contains(r.MemberId));
        Assert.InRange(data.RosterEntries.Select(r => r.MemberId).Distinct().Count(), 70, 85);

        // Blocking does not remove anyone from a roster (S-37): one blocked member stays in one.
        var blocked = data.Members.Where(m => m.Status == MembershipStatus.Blocked).Select(m => m.Id).ToHashSet();
        Assert.Contains(data.RosterEntries, r => blocked.Contains(r.MemberId));
    }

    [Theory]
    [MemberData(nameof(TenDaysAcrossDst))]
    public void Every_booking_is_one_the_booking_protocol_would_make(DateTimeOffset now)
    {
        var data = TestDataGenerator.Generate(TestDataSeeder.Seed, now);
        var classes = data.Classes.ToDictionary(c => c.Id);
        var passes = data.Passes.ToDictionary(p => p.Id);
        var staff = StaffIds(data);
        var active = data.Bookings.Where(b => b.Status == BookingStatus.Active).ToList();

        Assert.All(classes.Values, c => Assert.True(
            active.Count(b => b.ClassId == c.Id) <= c.Capacity, $"Class {c.Id} is overbooked."));
        Assert.Contains(classes.Values, c =>
            c.StartsAt > now && c.Status == ClassStatus.Scheduled && active.Count(b => b.ClassId == c.Id) == c.Capacity);

        Assert.Equal(active.Count, active.Select(b => (b.ClassId, b.MemberId)).Distinct().Count());
        Assert.DoesNotContain(active, b => staff.Contains(b.MemberId));

        Assert.All(active, b =>
        {
            var pass = passes[b.MembershipPassId!.Value];
            var cls = classes[b.ClassId];

            Assert.Equal(b.MemberId, pass.MemberId);
            Assert.InRange(ClubDate(cls), pass.ValidFrom, pass.ValidTo);
            Assert.True(b.CreatedAt < cls.StartsAt, "A booking was made after its class started.");
            Assert.True(b.CreatedAt <= now, "A booking is dated in the future.");
        });

        // Entries used, by EntryConsumption's definition.
        Assert.All(data.Passes, p => Assert.True(
            active.Count(b => b.MembershipPassId == p.Id
                              && classes[b.ClassId].Status != ClassStatus.Cancelled
                              && b.MakeupForBookingId == null
                              && b.Attendance != BookingAttendance.Absent) <= p.EntryCount,
            $"Karnet {p.Id} is spent past its entry count."));

        // A blocked member keeps no future booking, as BlockMember's cascade would leave it.
        var blocked = data.Members.Where(m => m.Status == MembershipStatus.Blocked).Select(m => m.Id).ToHashSet();
        Assert.DoesNotContain(active, b => blocked.Contains(b.MemberId) && classes[b.ClassId].StartsAt > now);
    }

    [Theory]
    [MemberData(nameof(TenDaysAcrossDst))]
    public void Attendance_and_makeups_follow_the_app_rules_and_cover_every_state(DateTimeOffset now)
    {
        var data = TestDataGenerator.Generate(TestDataSeeder.Seed, now);
        var classes = data.Classes.ToDictionary(c => c.Id);
        var passes = data.Passes.ToDictionary(p => p.Id);
        var bookings = data.Bookings.ToDictionary(b => b.Id);
        var today = DateOnly.FromDateTime(ClubTime.ToClubLocal(now).DateTime);

        Assert.DoesNotContain(data.Bookings, b => b.Attendance == BookingAttendance.Absent);

        Assert.All(data.Bookings.Where(b => b.Attendance is not null), b =>
        {
            var cls = classes[b.ClassId];
            Assert.Equal(ClassStatus.Scheduled, cls.Status);
            Assert.True(b.AttendanceRecordedAt >= cls.StartsAt, "Attendance recorded before the class started.");
            Assert.True(b.AttendanceRecordedAt <= now, "Attendance recorded in the future.");
            Assert.NotNull(b.AttendanceRecordedBy);
        });

        var makeups = data.Bookings.Where(b => b.MakeupForBookingId is not null).ToList();
        Assert.Equal(makeups.Count, makeups.Select(m => m.MakeupForBookingId).Distinct().Count());

        Assert.All(makeups, m =>
        {
            var absence = bookings[m.MakeupForBookingId!.Value];
            var absenceDate = ClubDate(classes[absence.ClassId]);
            var makeupDate = ClubDate(classes[m.ClassId]);
            var pass = passes[m.MembershipPassId!.Value];

            Assert.Equal(BookingAttendance.Makeup, absence.Attendance);
            Assert.Equal(absence.MemberId, m.MemberId);
            Assert.NotEqual(absence.ClassId, m.ClassId);
            Assert.True(makeupDate > absenceDate && makeupDate <= MakeupRules.DeadlineFor(absenceDate));
            Assert.InRange(makeupDate, pass.ValidFrom, pass.ValidTo);
            Assert.True(m.CreatedAt > absence.AttendanceRecordedAt, "A makeup was booked before the absence was marked.");
        });

        var states = data.Bookings
            .Where(b => b.Attendance == BookingAttendance.Makeup)
            .Select(absence =>
            {
                var live = makeups.SingleOrDefault(m =>
                    m.MakeupForBookingId == absence.Id && classes[m.ClassId].Status != ClassStatus.Cancelled);
                return MakeupRules.StateOf(
                    absence.MakeupClosedAt is not null,
                    live is not null,
                    live?.Attendance,
                    MakeupRules.DeadlineFor(ClubDate(classes[absence.ClassId])),
                    today);
            })
            .ToHashSet();

        Assert.Equal(Enum.GetValues<MakeupState>().ToHashSet(), states);

        // Not made up for each of its three reasons: closed by hand, deadline passed, makeup forfeited.
        Assert.Contains(data.Bookings, b => b.MakeupClosedAt is not null);
        Assert.Contains(makeups, m => m.Attendance == BookingAttendance.Forfeited);
        Assert.Contains(data.Bookings, b =>
            b.Attendance == BookingAttendance.Makeup && b.MakeupClosedAt is null
            && MakeupRules.DeadlineFor(ClubDate(classes[b.ClassId])) < today
            && !makeups.Any(m => m.MakeupForBookingId == b.Id));

        // The last two days are not all marked yet; today is never marked.
        Assert.Contains(data.Bookings, b =>
            b.Attendance is null && b.MakeupForBookingId is null && classes[b.ClassId].Status == ClassStatus.Scheduled
            && ClubDate(classes[b.ClassId]) < today);
        Assert.DoesNotContain(data.Bookings, b => b.Attendance is not null && ClubDate(classes[b.ClassId]) >= today);
    }

    [Theory]
    [MemberData(nameof(TenDaysAcrossDst))]
    public void Karnety_cover_every_payment_state_and_type(DateTimeOffset now)
    {
        var data = TestDataGenerator.Generate(TestDataSeeder.Seed, now);
        var today = DateOnly.FromDateTime(ClubTime.ToClubLocal(now).DateTime);
        var staff = StaffIds(data);

        Assert.All(data.Passes, p => Assert.True(p.PaidAt is null || p.PaidAt <= today, "A payment is dated in the future."));
        Assert.DoesNotContain(data.Passes, p => staff.Contains(p.MemberId));

        // One member's karnety never overlap - the issue rule.
        foreach (var member in data.Passes.GroupBy(p => p.MemberId))
        {
            var ordered = member.OrderBy(p => p.ValidFrom).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                Assert.True(ordered[i - 1].ValidTo < ordered[i].ValidFrom, $"Member {member.Key} holds overlapping karnety.");
            }
        }

        var current = data.Passes.Where(p => p.ValidFrom <= today && today <= p.ValidTo).ToList();
        var renewed = data.Passes.Where(p => p.ValidFrom > today).Select(p => p.MemberId).ToHashSet();

        // "Kończą się karnety": ending within five days, not renewed.
        Assert.True(
            current.Count(p => p.ValidTo <= today.AddDays(5) && !renewed.Contains(p.MemberId)) >= TestDataGenerator.ExpiringCount,
            "Too few karnety end within five days.");
        Assert.True(current.Count(p => p.PaidAt is null) >= TestDataGenerator.UnpaidCurrentCount, "Too few current karnety unpaid.");
        Assert.True(data.Passes.Count(p => p.ValidTo < today && p.PaidAt is null) >= TestDataGenerator.UnpaidExpiredCount,
            "Too few expired karnety unpaid.");

        Assert.Equal(
            new[] { "Miesięczny", "Voucher", "Wejście jednorazowe" }.Order(),
            data.Passes.Select(p => p.TypeName).Distinct().Order());
    }

    [Theory]
    [MemberData(nameof(TenDaysAcrossDst))]
    public void The_manual_scripts_member_is_an_active_roster_member_with_a_live_karnet(DateTimeOffset now)
    {
        var data = TestDataGenerator.Generate(TestDataSeeder.Seed, now);
        var today = DateOnly.FromDateTime(ClubTime.ToClubLocal(now).DateTime);
        var member = data.Members.Single(m => m.Email == TestDataGenerator.ReferenceMemberEmail);

        Assert.Equal(MembershipStatus.Active, member.Status);
        Assert.Contains(data.RosterEntries, r => r.MemberId == member.Id);
        Assert.Contains(data.Passes, p => p.MemberId == member.Id && p.ValidFrom <= today && today <= p.ValidTo);
        Assert.Contains(data.Bookings, b => b.MemberId == member.Id);
    }

    private static HashSet<Guid> StaffIds(TestDataSet data)
    {
        var staffUsers = data.Accounts
            .Where(a => a.Roles.Contains(ApplicationRoles.Admin) || a.Roles.Contains(ApplicationRoles.Trainer))
            .Select(a => a.User.Id)
            .ToHashSet();

        return data.Members.Where(m => m.UserId is not null && staffUsers.Contains(m.UserId)).Select(m => m.Id).ToHashSet();
    }

    private static DateOnly ClubDate(Class cls) => DateOnly.FromDateTime(ClubTime.ToClubLocal(cls.StartsAt).DateTime);
}
