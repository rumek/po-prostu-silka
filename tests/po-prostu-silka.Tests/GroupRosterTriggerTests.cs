using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// The automatic roster bookings (S-37, group-fixed-roster phase 2): creating or duplicating a class of a
/// group books its roster, and issuing or editing a karnet books its holder into their groups' classes.
///
/// <para>
/// Classes sit in 2047, a year no other suite uses. Each test's source class takes its own slot, spaced
/// so that its weekly copies never land on another test's class - the club-wide overlap rule would skip
/// the week, which is a duplication outcome these tests do not assert.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class GroupRosterTriggerTests(IntegrationTestFixture fixture)
{
    private sealed record GroupBody(Guid Id);

    private sealed record SkipBody(Guid MemberId, Guid ClassId, DateTimeOffset StartsAt, string Reason);

    private sealed record ReportBody(int Booked, List<SkipBody> Skipped);

    private sealed record CreatedClassBody(Guid Id, DateTimeOffset StartsAt, int Capacity, int FreeSpots, ReportBody Roster);

    private sealed record DuplicateBody(int Created, List<int> SkippedWeeks, ReportBody Roster);

    private sealed record PassBody(Guid Id, int EntriesUsed, int EntriesLeft, ReportBody Roster);

    private static int _slot;

    /// <summary>
    /// 13 hours apart: never a multiple of a week, so one test's weekly copies cannot meet another's
    /// source or copies within the tests this suite holds.
    /// </summary>
    private static DateTimeOffset NextSlot() =>
        new DateTimeOffset(2047, 1, 5, 6, 0, 0, TimeSpan.Zero).AddHours(13 * Interlocked.Increment(ref _slot));

    private static DateOnly ClubDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(ClubTime.ToClubLocal(instant).DateTime);

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    private Task<HttpClient> AdminAsync() => fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    private async Task<Guid> TrainerIdAsync(HttpClient admin)
    {
        var email = $"grt-trainer-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.Trainer);
        return await fixture.FindMemberIdAsync(admin, email);
    }

    private static async Task<Guid> GroupAsync(HttpClient admin, int capacity = 6)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/class-groups", new
        {
            name = $"Wyzwalacz-{Guid.NewGuid():N}",
            description = (string?)null,
            defaultDurationMinutes = 60,
            defaultCapacity = capacity,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupBody>())!.Id;
    }

    private static async Task<CreatedClassBody> CreateClassAsync(
        HttpClient admin, Guid groupId, Guid trainerId, DateTimeOffset startsAt, int capacity = 6)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/classes", new
        {
            classGroupId = groupId,
            startsAt,
            instructorMemberId = trainerId,
            durationMinutes = 60,
            capacity,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedClassBody>())!;
    }

    private static async Task AddToRosterAsync(HttpClient admin, Guid groupId, Guid memberId)
    {
        var response = await admin.PostAsJsonAsync($"/api/groups/{groupId}/roster", new { memberId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PassBody> IssuePassAsync(
        HttpClient admin, Guid memberId, DateOnly from, DateOnly to, int entries = 20)
    {
        var response = await admin.PostAsJsonAsync($"/api/admin/members/{memberId}/passes", new
        {
            typeName = "Karnet miesięczny",
            validFrom = from,
            validTo = to,
            entryCount = entries,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PassBody>())!;
    }

    private async Task<int> ActiveBookingCountAsync(Guid memberId, Guid groupId)
    {
        await using var db = NewContext();
        return await db.Bookings.CountAsync(b => b.MemberId == memberId
                                                 && b.Class.ClassGroupId == groupId
                                                 && b.Status == BookingStatus.Active);
    }

    [Fact]
    public async Task Creating_a_class_books_the_roster_and_reports_a_member_without_a_karnet()
    {
        var admin = await AdminAsync();
        var groupId = await GroupAsync(admin);
        var withPass = await fixture.CreateMemberAsync("Z karnetem");
        await fixture.IssuePassAsync(withPass);
        var withoutPass = await fixture.CreateMemberAsync("Bez karnetu");
        await AddToRosterAsync(admin, groupId, withPass);
        await AddToRosterAsync(admin, groupId, withoutPass);

        var created = await CreateClassAsync(admin, groupId, await TrainerIdAsync(admin), NextSlot());

        Assert.Equal(1, created.Roster.Booked);
        var skip = Assert.Single(created.Roster.Skipped);
        Assert.Equal(withoutPass, skip.MemberId);
        Assert.Equal("no_valid_pass", skip.Reason);
        Assert.Equal(created.Capacity - 1, created.FreeSpots);
    }

    [Fact]
    public async Task Creating_a_class_of_a_group_without_a_roster_books_nothing()
    {
        var admin = await AdminAsync();
        var groupId = await GroupAsync(admin);

        var created = await CreateClassAsync(admin, groupId, await TrainerIdAsync(admin), NextSlot());

        Assert.Equal(0, created.Roster.Booked);
        Assert.Empty(created.Roster.Skipped);
        Assert.Equal(created.Capacity, created.FreeSpots);
    }

    [Fact]
    public async Task Duplicating_books_the_covered_weeks_and_a_renewal_books_the_rest()
    {
        var admin = await AdminAsync();
        var groupId = await GroupAsync(admin);
        var start = NextSlot();
        var memberId = await fixture.CreateMemberAsync("Miesięczny");

        // A karnet covering the source and the first four weekly copies.
        await IssuePassAsync(admin, memberId, ClubDate(start), ClubDate(start).AddDays(28));
        await AddToRosterAsync(admin, groupId, memberId);

        var source = await CreateClassAsync(admin, groupId, await TrainerIdAsync(admin), start);
        Assert.Equal(1, source.Roster.Booked);

        var duplicate = await admin.PostAsJsonAsync($"/api/admin/classes/{source.Id}/duplicate", new { weeks = 8 });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var result = (await duplicate.Content.ReadFromJsonAsync<DuplicateBody>())!;

        Assert.Equal(8, result.Created);
        Assert.Empty(result.SkippedWeeks);
        Assert.Equal(4, result.Roster.Booked);
        Assert.Equal(4, result.Roster.Skipped.Count);
        Assert.All(result.Roster.Skipped, s => Assert.Equal("no_valid_pass", s.Reason));
        Assert.All(result.Roster.Skipped, s => Assert.True(ClubDate(s.StartsAt) > ClubDate(start).AddDays(28)));

        // The next karnet, from the day after the first ends: its weeks are booked without another click.
        var renewal = await IssuePassAsync(
            admin, memberId, ClubDate(start).AddDays(29), ClubDate(start).AddDays(70));

        Assert.Equal(4, renewal.Roster.Booked);
        Assert.Empty(renewal.Roster.Skipped);
        Assert.Equal(4, renewal.EntriesUsed);
        Assert.Equal(9, await ActiveBookingCountAsync(memberId, groupId));
    }

    [Fact]
    public async Task Moving_a_karnets_end_forward_books_the_newly_covered_classes()
    {
        var admin = await AdminAsync();
        var groupId = await GroupAsync(admin);
        var start = NextSlot();
        var memberId = await fixture.CreateMemberAsync("Przedłużony");

        var pass = await IssuePassAsync(admin, memberId, ClubDate(start), ClubDate(start));
        await AddToRosterAsync(admin, groupId, memberId);

        var source = await CreateClassAsync(admin, groupId, await TrainerIdAsync(admin), start);
        var duplicate = (await (await admin.PostAsJsonAsync(
                $"/api/admin/classes/{source.Id}/duplicate", new { weeks = 2 }))
            .Content.ReadFromJsonAsync<DuplicateBody>())!;
        Assert.Equal(2, duplicate.Roster.Skipped.Count);

        var edited = await admin.PutAsJsonAsync($"/api/admin/members/{memberId}/passes/{pass.Id}", new
        {
            typeName = "Karnet miesięczny",
            validFrom = ClubDate(start),
            validTo = ClubDate(start).AddDays(14),
            entryCount = 20,
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var body = (await edited.Content.ReadFromJsonAsync<PassBody>())!;

        Assert.Equal(2, body.Roster.Booked);
        Assert.Equal(3, body.EntriesUsed);
        Assert.Equal(3, await ActiveBookingCountAsync(memberId, groupId));
    }

    [Fact]
    public async Task Editing_only_a_karnets_name_books_nothing()
    {
        var admin = await AdminAsync();
        var groupId = await GroupAsync(admin);
        var start = NextSlot();
        var memberId = await fixture.CreateMemberAsync("Tylko nazwa");

        var pass = await IssuePassAsync(admin, memberId, ClubDate(start), ClubDate(start).AddDays(7));
        await AddToRosterAsync(admin, groupId, memberId);
        await CreateClassAsync(admin, groupId, await TrainerIdAsync(admin), start);

        // Released behind the API's back: a hook that ran on this edit would book it again.
        await using (var db = NewContext())
        {
            await db.Bookings.Where(b => b.MemberId == memberId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Cancelled));
        }

        var edited = await admin.PutAsJsonAsync($"/api/admin/members/{memberId}/passes/{pass.Id}", new
        {
            typeName = "Nowa nazwa",
            validFrom = ClubDate(start),
            validTo = ClubDate(start).AddDays(7),
            entryCount = 20,
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        Assert.Equal(0, (await edited.Content.ReadFromJsonAsync<PassBody>())!.Roster.Booked);
        Assert.Equal(0, await ActiveBookingCountAsync(memberId, groupId));
    }

    [Fact]
    public async Task A_class_with_fewer_spots_than_the_roster_books_up_to_capacity_and_reports_the_rest()
    {
        var admin = await AdminAsync();
        var groupId = await GroupAsync(admin);
        var first = await fixture.CreateMemberAsync("Pierwsza");
        var second = await fixture.CreateMemberAsync("Druga");
        await fixture.IssuePassAsync(first);
        await fixture.IssuePassAsync(second);
        await AddToRosterAsync(admin, groupId, first);
        await AddToRosterAsync(admin, groupId, second);

        var created = await CreateClassAsync(admin, groupId, await TrainerIdAsync(admin), NextSlot(), capacity: 1);

        Assert.Equal(1, created.Roster.Booked);
        var skip = Assert.Single(created.Roster.Skipped);
        Assert.Equal("class_full", skip.Reason);
        Assert.Equal(second, skip.MemberId);
        Assert.Equal(0, created.FreeSpots);
    }
}
