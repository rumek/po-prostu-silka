using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// Group rosters (S-37, group-fixed-roster): add, remove, sync, the batch's refusals, and who may
/// manage a roster.
///
/// <para>
/// EVERY TEST OWNS ITS GROUP, so "the group's upcoming classes" are exactly the ones the test made.
/// Classes sit in 2046, a year no other suite uses, one slot each so the club-wide overlap rule never
/// fires. A class that has to be in the PAST is moved there in the database, to 2019 - creation refuses
/// a past start.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class GroupRosterEndpointTests(IntegrationTestFixture fixture)
{
    private sealed record GroupBody(Guid Id, string Name, int RosterCount);

    private sealed record ClassBody(Guid Id, DateTimeOffset StartsAt);

    private sealed record FailureBody(string Reason);

    private sealed record SkipBody(Guid MemberId, string MemberName, Guid ClassId, DateTimeOffset StartsAt, string Reason);

    private sealed record ReportBody(int Booked, List<SkipBody> Skipped);

    private sealed record GapBody(Guid ClassId, DateTimeOffset StartsAt, string Reason);

    private sealed record RosterMemberBody(
        Guid MemberId, string DisplayName, bool HasAccount, int BookedUpcoming, List<GapBody> Gaps);

    private sealed record RosterBody(
        Guid GroupId, string Name, bool IsActive, int Capacity, int UpcomingClassCount, List<RosterMemberBody> Members);

    private sealed record ChangeBody(RosterBody Roster, ReportBody Report);

    private sealed record TrainerGroupBody(Guid Id, string Name, int RosterCount, int Capacity, DateTimeOffset? NextClassAt);

    private static int _slot;

    private static DateTimeOffset NextSlot() =>
        new DateTimeOffset(2046, 1, 1, 6, 0, 0, TimeSpan.Zero).AddHours(3 * Interlocked.Increment(ref _slot));

    private static int _pastSlot;

    private static DateTimeOffset NextPastSlot() =>
        new DateTimeOffset(2019, 1, 1, 6, 0, 0, TimeSpan.Zero).AddHours(3 * Interlocked.Increment(ref _pastSlot));

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    private Task<HttpClient> AdminAsync() => fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    // --- arrangement -----------------------------------------------------------

    private async Task<(HttpClient Client, Guid MemberId)> TrainerAsync(HttpClient admin)
    {
        var email = $"gr-trainer-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.Trainer);

        return (await fixture.CreateAuthenticatedClientAsync(email), await fixture.FindMemberIdAsync(admin, email));
    }

    private async Task<Guid> GroupAsync(HttpClient admin, int capacity = 6)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/class-groups", new
        {
            name = $"Skład-{Guid.NewGuid():N}",
            description = (string?)null,
            defaultDurationMinutes = 60,
            defaultCapacity = capacity,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<GroupBody>())!.Id;
    }

    private static async Task<ClassBody> ClassAsync(
        HttpClient admin, Guid groupId, Guid instructorMemberId, int capacity = 6, DateTimeOffset? startsAt = null)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/classes", new
        {
            classGroupId = groupId,
            startsAt = startsAt ?? NextSlot(),
            instructorMemberId,
            durationMinutes = 60,
            capacity,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ClassBody>())!;
    }

    /// <summary>A group with <paramref name="count"/> upcoming classes, all instructed by one new trainer.</summary>
    private async Task<(Guid GroupId, List<ClassBody> Classes, Guid TrainerId, HttpClient Trainer)> GroupWithClassesAsync(
        HttpClient admin, int count, int groupCapacity = 6, int classCapacity = 6)
    {
        var groupId = await GroupAsync(admin, groupCapacity);
        var (trainer, trainerId) = await TrainerAsync(admin);

        var classes = new List<ClassBody>();
        for (var i = 0; i < count; i++)
        {
            classes.Add(await ClassAsync(admin, groupId, trainerId, classCapacity));
        }

        return (groupId, classes, trainerId, trainer);
    }

    private async Task<Guid> MemberWithPassAsync(int entries = 1000)
    {
        var memberId = await fixture.CreateMemberAsync($"Skład {Guid.NewGuid():N}"[..20]);
        await fixture.IssuePassAsync(memberId, entryCount: entries);
        return memberId;
    }

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, Guid groupId, Guid memberId) =>
        client.PostAsJsonAsync($"/api/groups/{groupId}/roster", new { memberId });

    private static async Task<ChangeBody> AddOkAsync(HttpClient client, Guid groupId, Guid memberId)
    {
        var response = await AddAsync(client, groupId, memberId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChangeBody>())!;
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string reason)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(reason, (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    private async Task<List<Booking>> ActiveBookingsAsync(Guid memberId, IEnumerable<Guid> classIds)
    {
        var ids = classIds.ToList();
        await using var db = NewContext();
        return await db.Bookings.AsNoTracking()
            .Where(b => b.MemberId == memberId && ids.Contains(b.ClassId) && b.Status == BookingStatus.Active)
            .ToListAsync();
    }

    // --- add -------------------------------------------------------------------

    [Fact]
    public async Task Adding_a_member_books_every_upcoming_class_of_the_group()
    {
        var admin = await AdminAsync();
        var (groupId, classes, _, _) = await GroupWithClassesAsync(admin, 3);
        var memberId = await MemberWithPassAsync();

        var change = await AddOkAsync(admin, groupId, memberId);

        Assert.Equal(3, change.Report.Booked);
        Assert.Empty(change.Report.Skipped);

        var row = Assert.Single(change.Roster.Members);
        Assert.Equal(memberId, row.MemberId);
        Assert.Equal(3, row.BookedUpcoming);
        Assert.Empty(row.Gaps);
        Assert.Equal(3, change.Roster.UpcomingClassCount);

        Assert.Equal(3, (await ActiveBookingsAsync(memberId, classes.Select(c => c.Id))).Count);
    }

    [Fact]
    public async Task A_member_without_a_karnet_joins_the_roster_and_every_class_is_reported()
    {
        var admin = await AdminAsync();
        var (groupId, classes, _, _) = await GroupWithClassesAsync(admin, 2);
        var memberId = await fixture.CreateMemberAsync("Bez karnetu");

        var change = await AddOkAsync(admin, groupId, memberId);

        Assert.Equal(0, change.Report.Booked);
        Assert.Equal(2, change.Report.Skipped.Count);
        Assert.All(change.Report.Skipped, s =>
        {
            Assert.Equal("no_valid_pass", s.Reason);
            Assert.Equal("Bez karnetu", s.MemberName);
        });

        var row = Assert.Single(change.Roster.Members);
        Assert.Equal(0, row.BookedUpcoming);
        Assert.Equal(classes.Select(c => c.Id), row.Gaps.Select(g => g.ClassId));
        Assert.All(row.Gaps, g => Assert.Equal("no_valid_pass", g.Reason));
    }

    [Fact]
    public async Task A_karnet_with_fewer_entries_than_classes_books_the_earliest_and_reports_the_later()
    {
        var admin = await AdminAsync();
        var (groupId, classes, _, _) = await GroupWithClassesAsync(admin, 3);
        var memberId = await MemberWithPassAsync(entries: 2);

        var change = await AddOkAsync(admin, groupId, memberId);

        Assert.Equal(2, change.Report.Booked);
        var skip = Assert.Single(change.Report.Skipped);
        Assert.Equal("no_entries_left", skip.Reason);
        Assert.Equal(classes[2].Id, skip.ClassId);

        var booked = await ActiveBookingsAsync(memberId, classes.Select(c => c.Id));
        Assert.Equal(
            new[] { classes[0].Id, classes[1].Id }.OrderBy(id => id),
            booked.Select(b => b.ClassId).OrderBy(id => id));
    }

    [Fact]
    public async Task The_roster_is_capped_at_the_group_default_capacity()
    {
        var admin = await AdminAsync();
        var (groupId, _, _, _) = await GroupWithClassesAsync(admin, 1, groupCapacity: 1);

        await AddOkAsync(admin, groupId, await MemberWithPassAsync());

        await AssertRefusedAsync(await AddAsync(admin, groupId, await MemberWithPassAsync()), "roster_full");
    }

    [Fact]
    public async Task A_member_already_in_the_roster_is_refused()
    {
        var admin = await AdminAsync();
        var (groupId, _, _, _) = await GroupWithClassesAsync(admin, 1);
        var memberId = await MemberWithPassAsync();

        await AddOkAsync(admin, groupId, memberId);

        await AssertRefusedAsync(await AddAsync(admin, groupId, memberId), "already_in_roster");
    }

    [Fact]
    public async Task Staff_and_blocked_members_are_refused_at_the_door()
    {
        var admin = await AdminAsync();
        var (groupId, _, trainerId, _) = await GroupWithClassesAsync(admin, 1);
        var blocked = await fixture.CreateMemberAsync("Zablokowany", MembershipStatus.Blocked);

        await AssertRefusedAsync(await AddAsync(admin, groupId, trainerId), "member_is_staff");
        await AssertRefusedAsync(await AddAsync(admin, groupId, blocked), "member_blocked");
    }

    [Fact]
    public async Task An_unknown_member_is_a_404()
    {
        var admin = await AdminAsync();
        var groupId = await GroupAsync(admin);

        Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(admin, groupId, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_group_takes_no_new_members()
    {
        var admin = await AdminAsync();
        var groupId = await GroupAsync(admin);
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsync($"/api/admin/class-groups/{groupId}/deactivate", null)).StatusCode);

        await AssertRefusedAsync(await AddAsync(admin, groupId, await MemberWithPassAsync()), "inactive_class_group");
    }

    [Fact]
    public async Task The_admin_group_list_carries_the_roster_count()
    {
        var admin = await AdminAsync();
        var groupId = await GroupAsync(admin);
        await AddOkAsync(admin, groupId, await MemberWithPassAsync());
        await AddOkAsync(admin, groupId, await MemberWithPassAsync());

        var groups = await admin.GetFromJsonAsync<List<GroupBody>>("/api/admin/class-groups");

        Assert.Equal(2, groups!.Single(g => g.Id == groupId).RosterCount);
    }

    // --- remove ----------------------------------------------------------------

    [Fact]
    public async Task Removing_releases_future_bookings_but_keeps_a_makeup_and_past_classes()
    {
        var admin = await AdminAsync();
        var (groupId, classes, trainerId, _) = await GroupWithClassesAsync(admin, 3);
        var memberId = await MemberWithPassAsync();
        var past = await ClassAsync(admin, groupId, trainerId);

        await AddOkAsync(admin, groupId, memberId);

        // The fourth class becomes one that already happened, with its booking as history.
        await using (var db = NewContext())
        {
            var at = NextPastSlot();
            await db.Classes.Where(c => c.Id == past.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.StartsAt, at));
        }

        // The third class's booking becomes a makeup: released and replaced by one pointing at an absence
        // on another group's class. Leaving the roster must not take it.
        var otherGroup = await GroupAsync(admin);
        var absenceClass = await ClassAsync(admin, otherGroup, trainerId);
        await using (var db = NewContext())
        {
            var absence = new Booking
            {
                Id = Guid.NewGuid(),
                ClassId = absenceClass.Id,
                MemberId = memberId,
                Status = BookingStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                Attendance = BookingAttendance.Makeup,
            };
            db.Bookings.Add(absence);

            await db.Bookings
                .Where(b => b.MemberId == memberId && b.ClassId == classes[2].Id && b.Status == BookingStatus.Active)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Cancelled));

            db.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                ClassId = classes[2].Id,
                MemberId = memberId,
                Status = BookingStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                MakeupForBookingId = absence.Id,
            });
            await db.SaveChangesAsync();
        }

        var response = await admin.DeleteAsync($"/api/groups/{groupId}/roster/{memberId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<RosterBody>())!.Members);

        var remaining = await ActiveBookingsAsync(memberId, classes.Select(c => c.Id).Append(past.Id));
        Assert.Equal(
            new[] { classes[2].Id, past.Id }.OrderBy(id => id),
            remaining.Select(b => b.ClassId).OrderBy(id => id));
        Assert.NotNull(remaining.Single(b => b.ClassId == classes[2].Id).MakeupForBookingId);

        // The released bookings gave their entries back: only the past class still spends one.
        await using (var db = NewContext())
        {
            var consuming = await db.Bookings.AsNoTracking()
                .CountAsync(b => b.MemberId == memberId
                                 && b.MembershipPassId != null
                                 && b.Status == BookingStatus.Active
                                 && b.MakeupForBookingId == null);
            Assert.Equal(1, consuming);
        }
    }

    [Fact]
    public async Task Removing_a_member_not_in_the_roster_is_a_404()
    {
        var admin = await AdminAsync();
        var groupId = await GroupAsync(admin);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.DeleteAsync($"/api/groups/{groupId}/roster/{Guid.NewGuid()}")).StatusCode);
    }

    // --- sync ------------------------------------------------------------------

    [Fact]
    public async Task Sync_fills_the_gaps_a_new_karnet_opened_and_a_second_run_does_nothing()
    {
        var admin = await AdminAsync();
        var (groupId, classes, _, _) = await GroupWithClassesAsync(admin, 2);
        var memberId = await fixture.CreateMemberAsync("Później karnet");

        await AddOkAsync(admin, groupId, memberId);

        // Issued behind the API's back, so no hook runs: only the sync can fill the gaps.
        await fixture.IssuePassAsync(memberId);

        var before = await admin.GetFromJsonAsync<RosterBody>($"/api/groups/{groupId}/roster");
        Assert.All(Assert.Single(before!.Members).Gaps, g => Assert.Equal("bookable", g.Reason));

        var first = await admin.PostAsync($"/api/groups/{groupId}/roster/sync", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = (await first.Content.ReadFromJsonAsync<ChangeBody>())!;
        Assert.Equal(2, firstBody.Report.Booked);
        Assert.Empty(firstBody.Report.Skipped);
        Assert.Empty(Assert.Single(firstBody.Roster.Members).Gaps);

        var second = (await (await admin.PostAsync($"/api/groups/{groupId}/roster/sync", null))
            .Content.ReadFromJsonAsync<ChangeBody>())!;
        Assert.Equal(0, second.Report.Booked);
        Assert.Empty(second.Report.Skipped);

        Assert.Equal(2, (await ActiveBookingsAsync(memberId, classes.Select(c => c.Id))).Count);
    }

    [Fact]
    public async Task A_class_with_fewer_spots_than_the_roster_is_filled_and_the_rest_reported_full()
    {
        var admin = await AdminAsync();
        var (groupId, classes, _, _) = await GroupWithClassesAsync(admin, 1, classCapacity: 1);
        var first = await MemberWithPassAsync();
        var second = await MemberWithPassAsync();

        await AddOkAsync(admin, groupId, first);
        var change = await AddOkAsync(admin, groupId, second);

        var skip = Assert.Single(change.Report.Skipped);
        Assert.Equal("class_full", skip.Reason);
        Assert.Equal(second, skip.MemberId);
        Assert.Equal(
            "class_full",
            Assert.Single(change.Roster.Members.Single(m => m.MemberId == second).Gaps).Reason);
        Assert.Single(await ActiveBookingsAsync(first, [classes[0].Id]));
    }

    [Fact]
    public async Task Concurrent_roster_adds_never_overbook_a_class()
    {
        var admin = await AdminAsync();
        var (groupId, classes, _, _) = await GroupWithClassesAsync(admin, 1, groupCapacity: 10, classCapacity: 2);
        var members = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            members.Add(await MemberWithPassAsync());
        }

        var responses = await Task.WhenAll(members.Select(async m =>
            await AddAsync(await AdminAsync(), groupId, m)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        await using var db = NewContext();
        var active = await db.Bookings.CountAsync(b => b.ClassId == classes[0].Id && b.Status == BookingStatus.Active);
        Assert.Equal(2, active);
    }

    // --- who may manage --------------------------------------------------------

    [Fact]
    public async Task A_trainer_instructing_an_upcoming_class_manages_the_roster_and_others_are_refused()
    {
        var admin = await AdminAsync();
        var (groupId, _, _, trainer) = await GroupWithClassesAsync(admin, 1);
        var (otherTrainer, _) = await TrainerAsync(admin);
        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        Assert.Equal(HttpStatusCode.OK, (await trainer.GetAsync($"/api/groups/{groupId}/roster")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTrainer.GetAsync($"/api/groups/{groupId}/roster")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await AddAsync(otherTrainer, groupId, await MemberWithPassAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/api/groups/{groupId}/roster")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/groups/{Guid.NewGuid()}/roster")).StatusCode);

        await AddOkAsync(trainer, groupId, await MemberWithPassAsync());
    }

    [Fact]
    public async Task A_trainer_books_and_releases_only_on_the_classes_they_instruct()
    {
        var admin = await AdminAsync();
        var (groupId, classes, _, trainer) = await GroupWithClassesAsync(admin, 1);
        var (_, otherTrainerId) = await TrainerAsync(admin);
        var others = await ClassAsync(admin, groupId, otherTrainerId);
        var memberId = await MemberWithPassAsync();

        var change = await AddOkAsync(trainer, groupId, memberId);

        Assert.Equal(1, change.Report.Booked);
        var skip = Assert.Single(change.Report.Skipped);
        Assert.Equal("not_your_class", skip.Reason);
        Assert.Equal(others.Id, skip.ClassId);
        Assert.Equal("not_your_class", Assert.Single(Assert.Single(change.Roster.Members).Gaps).Reason);

        // The admin fills the other trainer's class; the trainer's removal must leave it.
        var synced = (await (await admin.PostAsync($"/api/groups/{groupId}/roster/sync", null))
            .Content.ReadFromJsonAsync<ChangeBody>())!;
        Assert.Equal(1, synced.Report.Booked);

        Assert.Equal(
            HttpStatusCode.OK,
            (await trainer.DeleteAsync($"/api/groups/{groupId}/roster/{memberId}")).StatusCode);

        var remaining = await ActiveBookingsAsync(memberId, [classes[0].Id, others.Id]);
        Assert.Equal(others.Id, Assert.Single(remaining).ClassId);
    }

    [Fact]
    public async Task The_trainer_group_list_holds_exactly_the_groups_the_trainer_may_manage()
    {
        var admin = await AdminAsync();
        var (groupId, classes, _, trainer) = await GroupWithClassesAsync(admin, 2);
        var (otherTrainer, _) = await TrainerAsync(admin);
        await AddOkAsync(admin, groupId, await MemberWithPassAsync());

        var mine = await trainer.GetFromJsonAsync<List<TrainerGroupBody>>("/api/trainer/groups");
        var group = Assert.Single(mine!);
        Assert.Equal(groupId, group.Id);
        Assert.Equal(1, group.RosterCount);
        Assert.Equal(6, group.Capacity);
        Assert.Equal(classes[0].StartsAt, group.NextClassAt);

        var theirs = await otherTrainer.GetFromJsonAsync<List<TrainerGroupBody>>("/api/trainer/groups");
        Assert.DoesNotContain(theirs!, g => g.Id == groupId);

        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/trainer/groups")).StatusCode);
    }
}
