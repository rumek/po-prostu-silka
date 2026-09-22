using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Domain.Training;
using po_prostu_silka.Infrastructure.Persistence;
using po_prostu_silka.Infrastructure.TestData;

namespace po_prostu_silka.Tests;

/// <summary>
/// A collection of its own, and therefore a SQL Server container of its own: xUnit gives every
/// collection a separate fixture instance. The reset wipes every member but the AdminSeed account, so
/// on the shared IntegrationCollection container it would destroy the rows the other tests create.
/// </summary>
[CollectionDefinition(nameof(TestDataSeederCollection))]
public class TestDataSeederCollection : ICollectionFixture<IntegrationTestFixture>;

/// <summary>
/// S-24's seeder against a real SQL Server. Called directly, with an in-memory configuration and a
/// stub environment, because the host the fixture boots runs as Testing - where the seeder refuses.
///
/// <para>
/// Every test that needs the data set starts from a reset seed, so none depends on the order xUnit
/// happens to run them in.
/// </para>
/// </summary>
[Collection(nameof(TestDataSeederCollection))]
public class TestDataSeederTests(IntegrationTestFixture fixture)
{
    private const string SeededDomain = "@" + TestDataGenerator.EmailDomain;

    private static readonly string[] SuppliedVideoIds = ["FR8qYjA8pKQ", "qsmtzydAS_U", "C-tRbRSBAoE"];

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task Refuses_in_Production_and_Testing(string environment)
    {
        await fixture.CreateMemberAsync($"Stray {Guid.NewGuid():N}");
        var before = await CountsAsync();

        await SeedAsync(environment, reset: true);

        // Neither the wipe nor the insert ran: the stray member survives and nothing was added.
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Refuses_without_password()
    {
        await fixture.CreateMemberAsync($"Stray {Guid.NewGuid():N}");
        var before = await CountsAsync();

        await SeedAsync("Development", reset: true, password: "");

        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Seeds_the_expected_population()
    {
        await SeedAsync("Staging", reset: true);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var seededAccounts = await db.Users.CountAsync(u => u.Email!.EndsWith(SeededDomain));
        var seededAccountMembers = await db.Members.CountAsync(m => m.UserId != null && m.Email!.EndsWith(SeededDomain));
        var accountless = await db.Members.CountAsync(m => m.UserId == null);

        Assert.Equal(164, seededAccounts);
        Assert.Equal(164, seededAccountMembers);
        Assert.Equal(40, accountless);

        // 204 seeded plus the AdminSeed account's member, which the reset keeps.
        Assert.Equal(205, await db.Members.CountAsync());

        Assert.Equal(2, await CountSeededInRoleAsync(db, ApplicationRoles.Admin));
        Assert.Equal(2, await CountSeededInRoleAsync(db, ApplicationRoles.Trainer));
        Assert.Equal(8, await db.TrainingPlans.CountAsync(p => p.Status == TrainingPlanStatus.Active));

        // About half the accountless members hold a live invitation code.
        var liveCodes = await db.Members.CountAsync(m => m.AccessCode != null && m.AccessCodeExpiresAt > DateTimeOffset.UtcNow);
        Assert.InRange(liveCodes, 15, 25);
    }

    [Fact]
    public async Task Bookings_obey_capacity_and_passes()
    {
        await SeedAsync("Development", reset: true);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        var classes = await db.Classes
            .Select(c => new
            {
                c.Id,
                c.StartsAt,
                c.Capacity,
                c.Status,
                Booked = db.Bookings.Count(b => b.ClassId == c.Id && b.Status == BookingStatus.Active),
            })
            .ToListAsync();

        Assert.All(classes, c => Assert.True(c.Booked <= c.Capacity, $"Class {c.Id} is overbooked."));
        Assert.Contains(classes, c => c.StartsAt > now && c.Status == ClassStatus.Scheduled && c.Booked == c.Capacity);

        var bookings = await db.Bookings
            .Where(b => b.Status == BookingStatus.Active)
            .Select(b => new
            {
                b.MemberId,
                b.MembershipPassId,
                b.Class.StartsAt,
                PassMemberId = (Guid?)b.MembershipPass!.MemberId,
                ValidFrom = (DateOnly?)b.MembershipPass!.ValidFrom,
                ValidTo = (DateOnly?)b.MembershipPass!.ValidTo,
                MemberStatus = b.Member!.Status,
            })
            .ToListAsync();

        Assert.NotEmpty(bookings);
        Assert.All(bookings, b =>
        {
            Assert.NotNull(b.MembershipPassId);
            Assert.Equal(b.MemberId, b.PassMemberId);

            // The CLASS'S club-local date, as BookingProtocol reads it - not UTC's.
            var classDate = DateOnly.FromDateTime(ClubTime.ToClubLocal(b.StartsAt).DateTime);
            Assert.InRange(classDate, b.ValidFrom!.Value, b.ValidTo!.Value);
        });

        // A blocked member keeps no future booking, as BlockMember's cascade would leave it.
        Assert.DoesNotContain(bookings, b => b.MemberStatus == MembershipStatus.Blocked && b.StartsAt > now);

        var passes = await db.MembershipPasses
            .Select(p => new
            {
                p.EntryCount,
                Used = db.Bookings.Count(b => b.MembershipPassId == p.Id && b.Status == BookingStatus.Active),
            })
            .ToListAsync();

        Assert.All(passes, p => Assert.True(p.Used <= p.EntryCount, "A pass is spent past its entry count."));
        Assert.Contains(passes, p => p.Used == p.EntryCount);
    }

    [Fact]
    public async Task Second_run_without_reset_changes_nothing()
    {
        await SeedAsync("Development", reset: true);
        var first = await CountsAsync();

        await SeedAsync("Development", reset: false);

        Assert.Equal(first, await CountsAsync());
    }

    [Fact]
    public async Task Reset_reproduces_the_same_data_and_keeps_the_admin_seed_account()
    {
        await SeedAsync("Development", reset: true);
        var firstIds = await SeededMemberIdsAsync();

        var strayId = await fixture.CreateMemberAsync($"Stray {Guid.NewGuid():N}");

        await SeedAsync("Development", reset: true);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Members.AnyAsync(m => m.Id == strayId));

        var adminSeed = await db.Users.SingleAsync(u => u.Email == TestUsers.SeededAdminEmail);
        Assert.True(await db.Members.AnyAsync(m => m.UserId == adminSeed.Id));

        Assert.Equal(firstIds, await SeededMemberIdsAsync());
    }

    [Fact]
    public async Task Exercises_carry_a_supplied_video()
    {
        await SeedAsync("Development", reset: true);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var videoIds = await db.Exercises.Select(e => e.VideoId).ToListAsync();

        Assert.NotEmpty(videoIds);
        Assert.All(videoIds, id => Assert.Contains(id, SuppliedVideoIds));
    }

    [Fact]
    public async Task Seeded_account_can_log_in()
    {
        await SeedAsync("Development", reset: true);

        // Proves the shared-hash shortcut and the hand-set normalised fields produce an account
        // Identity actually accepts - the one thing the row counts above cannot show.
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = $"trener1{SeededDomain}", password = TestUsers.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SeedAsync(string environment, bool reset, string password = TestUsers.Password)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TestDataSeed:Enabled"] = "true",
                ["TestDataSeed:Reset"] = reset ? "true" : "false",
                ["TestDataSeed:Password"] = password,
                ["AdminSeed:Email"] = TestUsers.SeededAdminEmail,
            })
            .Build();

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        await TestDataSeeder.SeedAsync(
            scope.ServiceProvider, configuration, new StubEnvironment(environment), NullLogger.Instance);
    }

    private async Task<(int Users, int Members, int Passes, int ClassTypes, int Classes, int Bookings, int Exercises, int Plans)> CountsAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (
            await db.Users.CountAsync(),
            await db.Members.CountAsync(),
            await db.MembershipPasses.CountAsync(),
            await db.ClassTypes.CountAsync(),
            await db.Classes.CountAsync(),
            await db.Bookings.CountAsync(),
            await db.Exercises.CountAsync(),
            await db.TrainingPlans.CountAsync());
    }

    private async Task<List<Guid>> SeededMemberIdsAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Members
            .Where(m => m.Email == null || m.Email != TestUsers.SeededAdminEmail)
            .OrderBy(m => m.Id)
            .Select(m => m.Id)
            .ToListAsync();
    }

    private static Task<int> CountSeededInRoleAsync(AppDbContext db, string role) =>
        (from userRole in db.UserRoles
         join r in db.Roles on userRole.RoleId equals r.Id
         join user in db.Users on userRole.UserId equals user.Id
         where r.Name == role && user.Email!.EndsWith(SeededDomain)
         select user.Id).CountAsync();

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "po-prostu-silka";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
