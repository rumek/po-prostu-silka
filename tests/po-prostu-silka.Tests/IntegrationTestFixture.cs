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

    /// <summary>
    /// A client that does not follow redirects, so 401/403 assertions stay observable.
    ///
    /// <para>
    /// EACH CLIENT GETS ITS OWN CLIENT IP (S-16). Every request in this suite arrives over the same
    /// in-memory connection, so without this they all share one rate-limiter partition — and once
    /// /register became rate limited, the fourth registration ANY test performed started answering
    /// 429, which surfaces as two dozen unrelated failures that look nothing like a rate limit. A
    /// per-client X-Forwarded-For is what production sends anyway (App Service terminates at a
    /// reverse proxy), so this exercises the real partitioning rather than bypassing it. A test that
    /// wants to hit the limiter shares one address deliberately — see
    /// <see cref="CreateClientFromAddress"/>.
    /// </para>
    /// </summary>
    public HttpClient CreateClient() => CreateClientFromAddress(RandomClientAddress());

    /// <summary>
    /// A client whose requests all appear to come from <paramref name="clientAddress"/> — the handle
    /// a rate-limiter test needs, since the limiter partitions on exactly this.
    /// </summary>
    public HttpClient CreateClientFromAddress(string clientAddress)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,

            // https, not the default http. The auth cookie is issued with Secure=true (production is
            // HTTPS-only), and CookieContainer silently refuses to store a Secure cookie received over
            // http - every authenticated test would then fail as anonymous. TestServer does no real
            // TLS; this only sets the request scheme.
            BaseAddress = new Uri("https://localhost"),
        });

        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientAddress);

        return client;
    }

    /// <summary>
    /// A distinct address per client. 198.51.100.0/24 and 203.0.113.0/24 are the documentation
    /// ranges (RFC 5737) — they can never collide with anything real.
    /// </summary>
    private static string RandomClientAddress() =>
        $"198.51.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

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
        // Mirrors what RegisterAsync does in production. Membership follows the account only for
        // Blocked; the two statuses answer different questions and are deliberately not derived from
        // one another anywhere else.
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
    /// Issues a karnet wide enough that it never gets in the way of a test about something else
    /// (S-16).
    ///
    /// <para>
    /// WHY EVERY BOOKING TEST NEEDS THIS. Since S-16 no member may be booked into a class without a
    /// pass covering that class's club-local date with a free entry — so a suite testing capacity,
    /// cancellation notifications or the block cascade has to arrange one, or it measures the karnet
    /// gate instead of what it meant to measure. Written through the DbContext rather than the admin
    /// API, because arranging a fixture through the surface under test would make every one of those
    /// suites depend on the pass endpoints being correct.
    /// </para>
    ///
    /// <para>
    /// The range is deliberately absurd (the whole 21st century) and the entry count deliberately
    /// large. It has to be: the suites work in fabricated years — ClassEndpointTests in 2030,
    /// BookingEndpointTests in 2032, ClassCancellationTests in 2034 — and each slides its classes
    /// further out with every test in the file, so anything narrower starts refusing bookings partway
    /// through a run and only in a FULL run, which is the worst kind of flake to diagnose.
    /// A test that cares about the range or the pool arranges its OWN pass with real bounds; this one
    /// exists to be invisible. It is also why it must not be folded into
    /// <see cref="CreateMemberAsync"/> or <see cref="CreateUserAsync"/>: a fixture that silently gave
    /// everyone a karnet would make the gate untestable.
    /// </para>
    /// </summary>
    public async Task<Guid> IssuePassAsync(
        Guid memberId,
        int entryCount = 1000,
        DateOnly? validFrom = null,
        DateOnly? validTo = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pass = new MembershipPass
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            TypeName = "Test Karnet",
            ValidFrom = validFrom ?? new DateOnly(2000, 1, 1),
            ValidTo = validTo ?? new DateOnly(2099, 12, 31),
            EntryCount = entryCount,
            IssuedAt = DateTimeOffset.UtcNow,
        };

        db.MembershipPasses.Add(pass);
        await db.SaveChangesAsync();

        return pass.Id;
    }

    /// <summary>
    /// <see cref="IssuePassAsync"/> for a member addressed by their account's email — which is what
    /// every suite that seeds through <see cref="CreateUserAsync"/> actually holds.
    /// </summary>
    public async Task<Guid> IssuePassForAccountAsync(string email, int entryCount = 1000)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var memberId = await db.Members
            .AsNoTracking()
            .Where(m => m.Email == email)
            .Select(m => m.Id)
            .SingleAsync();

        return await IssuePassAsync(memberId, entryCount);
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
    /// A signed-in client whose session FAILS the ActiveMember policy (S-16).
    ///
    /// <para>
    /// WHY THIS HELPER HAD TO EXIST. Until S-16 that state was simply "a pending account", which the
    /// fixture seeded and any test could sign in as. Approval is gone, and the remaining way to fail
    /// the policy — being blocked — cannot be reached the same way: login refuses a blocked account
    /// outright, so there is no cookie to test with. The state still MATTERS, because a session
    /// issued before a block must keep being refused, and that is what these tests assert.
    /// </para>
    ///
    /// <para>
    /// So the sequence is: seed an active account, sign in, block the MEMBERSHIP straight in the
    /// database, and refresh the claims. Membership rather than the account, because blocking the
    /// account would invalidate the cookie itself and the client would stop being signed in at all —
    /// which is a different thing from a live session that is refused.
    /// </para>
    /// </summary>
    public async Task<HttpClient> CreateInactiveSessionAsync()
    {
        var email = $"inactive-session-{Guid.NewGuid():N}@test.local";
        await CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        var client = await CreateAuthenticatedClientAsync(email);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var member = await db.Members.SingleAsync(m => m.Email == email);
            member.Status = MembershipStatus.Blocked;

            await db.SaveChangesAsync();
        }

        // Re-mint, so the cookie carries the new membership rather than the one it was issued with.
        // Bare RequireAuthorization(), so a member who is about to fail every policy can still call it.
        var refreshed = await client.PostAsync("/api/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        return client;
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
    // PendingMemberEmail is GONE (S-16, MP-03). Nothing produces a pending account any more, so a
    // seeded one would be a state the product cannot reach — and every test that used it was really
    // asking "what does a session that fails ActiveMember do", which BlockedMemberEmail answers on
    // the one axis that still exists.
    public const string BlockedMemberEmail = "blocked-member@test.local";

    /// <summary>An approved member who also holds Trainer - what promoting a member actually produces.</summary>
    public const string ActiveTrainerEmail = "active-trainer@test.local";
}

[CollectionDefinition(nameof(IntegrationCollection))]
public class IntegrationCollection : ICollectionFixture<IntegrationTestFixture>;
