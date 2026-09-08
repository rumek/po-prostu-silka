using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace po_prostu_silka.Tests;

/// <summary>
/// Boots one real SQL Server container and one real app host for the whole test run.
///
/// A real engine rather than SQLite or the in-memory provider: the behaviour under test is
/// Identity's, against an actual schema with actual filtered unique indexes. It is also what S-04's
/// no-overbooking concurrency tests will need, so the cost is paid once here.
///
/// The container is created by Testcontainers with its own random password and published port, so
/// nothing here touches the developer's docker-compose database.
/// </summary>
public class IntegrationTestFixture : IAsyncLifetime
{
    // Same image tag as docker-compose.yml, so tests and local development exercise one engine version.
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private WebApplicationFactory<Program>? _factory;

    /// <summary>
    /// The container's connection string, so tests that need their own DI graph (the outbox worker
    /// tests build one with fake channels) can share this container instead of starting a second.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialised.");

    /// <summary>A client that does not follow redirects, so 401/403 assertions stay observable.</summary>
    public HttpClient CreateClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,

        // https, not the default http. The auth cookie is issued with Secure=true (production is
        // HTTPS-only), and CookieContainer silently refuses to store a Secure cookie received over
        // http - every authenticated test would then fail as anonymous. TestServer does no real
        // TLS; this only sets the request scheme.
        BaseAddress = new Uri("https://localhost"),
    });

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Migrate BEFORE the host is built, not after.
        //
        // Program.cs runs the admin seeder at startup, and the seeder touches AspNetRoles - so a
        // host booted against an empty database throws before any test runs. This ordering also
        // mirrors production, where deploy.yml applies migrations in an earlier step than the
        // deploy itself.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        await using (var migrationContext = new AppDbContext(options))
        {
            await migrationContext.Database.MigrateAsync();
        }

        _factory = new TestAppFactory(_container.GetConnectionString());

        await SeedTestUsersAsync();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _container.DisposeAsync();
    }

    private async Task SeedTestUsersAsync()
    {
        await CreateUserAsync(TestUsers.ActiveAdminEmail, AccountStatus.Active, ApplicationRoles.Admin);
        await CreateUserAsync(TestUsers.ActiveMemberEmail, AccountStatus.Active, ApplicationRoles.User);
        await CreateUserAsync(TestUsers.PendingMemberEmail, AccountStatus.Pending, ApplicationRoles.User);
        await CreateUserAsync(TestUsers.BlockedMemberEmail, AccountStatus.Blocked, ApplicationRoles.User);

        // BOTH ROLES, and that is the point rather than belt-and-braces. A real trainer is an
        // approved member the admin promoted, so they keep User - and Trainer alone deliberately
        // fails the ActiveMember policy (see ApplicationRoles.MemberFacing). Seeding this account
        // with Trainer only would test a state the product cannot produce, and would quietly make
        // every "a trainer may also read their own plan" assertion unreachable.
        await CreateUserAsync(
            TestUsers.ActiveTrainerEmail,
            AccountStatus.Active,
            ApplicationRoles.User,
            displayName: "Test Active Trainer",
            additionalRole: ApplicationRoles.Trainer);
    }

    /// <summary>
    /// Creates a user through Identity's UserManager rather than raw SQL, so password hashing,
    /// normalisation and role wiring match production exactly.
    /// </summary>
    /// <param name="displayName">
    /// Defaults to "Test {role}". Pass an explicit one when a test needs to tell two accounts of the
    /// same role apart by name — asserting that a resolved display name is the RIGHT one is
    /// meaningless while every trainer is called "Test Trainer".
    /// </param>
    /// <param name="additionalRole">
    /// A second role granted alongside <paramref name="role"/>. Roles are additive in this product,
    /// and the only account that needs two is a trainer who is also a member - which is what every
    /// real trainer is.
    /// </param>
    public async Task CreateUserAsync(
        string email,
        AccountStatus status,
        string role,
        string? displayName = null,
        string? additionalRole = null)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName ?? $"Test {role}",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var created = await userManager.CreateAsync(user, TestUsers.Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, role);

        if (additionalRole is not null)
        {
            await userManager.AddToRoleAsync(user, additionalRole);
        }

        // THE CLUB RECORD EVERY ACCOUNT MUST HAVE (S-14). This is the single chokepoint every test
        // seeds accounts through, which is exactly why it belongs here: an account seeded without one
        // fails the membership claim and every policy built on it, and the failure would look like a
        // broken policy rather than a broken fixture.
        //
        // Mirrors what RegisterAsync does in production, including Active membership for a Pending
        // account - approval gates the login, not the membership.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Members.Add(new Member
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Status = status == AccountStatus.Blocked ? MembershipStatus.Blocked : MembershipStatus.Active,
            CreatedAt = user.CreatedAt,
            ClaimedAt = user.CreatedAt,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a member with NO account — the case S-14 exists for, and one no other helper can
    /// produce, because every other path starts from Identity.
    /// </summary>
    /// <returns>The new member's id, which is how every S-14 endpoint addresses them.</returns>
    public async Task<Guid> CreateMemberAsync(
        string displayName,
        MembershipStatus status = MembershipStatus.Active,
        string? email = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var member = new Member
        {
            Id = Guid.NewGuid(),
            UserId = null,
            DisplayName = displayName,
            Email = email,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Members.Add(member);
        await db.SaveChangesAsync();

        return member.Id;
    }

    /// <summary>
    /// The member id behind an account. Since S-14 the admin surface is addressed by member, while
    /// most of these tests still hold an account id — this is the bridge, rather than each test
    /// growing its own DbContext to look one up.
    /// </summary>
    public async Task<Guid> MemberIdOfAsync(string userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Members.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.Id).SingleAsync();
    }

    /// <summary>
    /// The account behind a member. The reverse of <see cref="MemberIdOfAsync"/>, and needed only while
    /// the legacy account columns are still NOT NULL: a test that inserts an entity directly has to
    /// populate both keys, because the database still enforces the old foreign key.
    ///
    /// <para>
    /// GOES AWAY WITH THOSE COLUMNS. If this is still here after the contract migration, something was
    /// missed.
    /// </para>
    /// </summary>
    public async Task<string> UserIdOfMemberAsync(Guid memberId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (await db.Members.AsNoTracking().Where(m => m.Id == memberId).Select(m => m.UserId)
            .SingleAsync())!;
    }

    /// <summary>Logs in and returns a client carrying the resulting auth cookie.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email, password = TestUsers.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    /// <summary>The App:BaseUrl the test host runs with. Reset-link assertions match against it.</summary>
    public const string TestAppBaseUrl = "https://test.poprostusilka.local";

    private sealed class TestAppFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // "Testing" is what enables the policy probe endpoints in Program.cs.
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = connectionString,

                    // The production seeder runs at startup; give it real values so its path is
                    // exercised rather than skipped.
                    ["AdminSeed:Email"] = TestUsers.SeededAdminEmail,
                    ["AdminSeed:Password"] = TestUsers.Password,

                    // The origin password-reset links are built from (S-13). Set, so the tests
                    // exercise the configured path rather than the "logs an error and sends
                    // nothing" fallback.
                    ["App:BaseUrl"] = TestAppBaseUrl,
                }));
        }
    }
}

public static class TestUsers
{
    public const string Password = "TestPass_123";
    public const string SeededAdminEmail = "seeded-admin@test.local";
    public const string ActiveAdminEmail = "active-admin@test.local";
    public const string ActiveMemberEmail = "active-member@test.local";
    public const string PendingMemberEmail = "pending-member@test.local";
    public const string BlockedMemberEmail = "blocked-member@test.local";

    /// <summary>An approved member who also holds Trainer - what promoting a member actually produces.</summary>
    public const string ActiveTrainerEmail = "active-trainer@test.local";
}

[CollectionDefinition(nameof(IntegrationCollection))]
public class IntegrationCollection : ICollectionFixture<IntegrationTestFixture>;
