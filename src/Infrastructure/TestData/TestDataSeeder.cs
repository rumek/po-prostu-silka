using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Identity;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.TestData;

/// <summary>
/// Fills a development or staging database with the realistic club <see cref="TestDataGenerator"/>
/// builds (S-24), when configuration asks for it. Shaped like <see cref="AdminSeeder"/>: runs at
/// startup, logs rather than throws for a configuration problem, and is idempotent.
///
/// <para>
/// FAILS CLOSED. Three conditions must all hold - the environment is Development or Staging,
/// <c>TestDataSeed:Enabled</c> is true, and a password is configured - or nothing is touched. The
/// environment check is what protects the one Azure database on the day it becomes production: the
/// flags alone would not, since a forgotten app setting survives any deploy.
/// </para>
///
/// <para>
/// NOT A MIGRATION, deliberately. deploy.yml applies migrations on every merge to main, so seed rows in
/// a migration would sit in its history forever and reach the future production database.
/// </para>
/// </summary>
public static class TestDataSeeder
{
    /// <summary>Fixed, so a reseed on the same club-local day produces the same people and ids.</summary>
    public const int Seed = 20260922;

    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger)
    {
        var options = configuration.GetSection(TestDataSeedOptions.SectionName).Get<TestDataSeedOptions>()
            ?? new TestDataSeedOptions();

        if (!options.Enabled)
        {
            logger.LogInformation("Test data seeding is off ({Section}:Enabled is not set).", TestDataSeedOptions.SectionName);
            return;
        }

        // Never Production, and never Testing either: the integration suite owns that database.
        if (!environment.IsDevelopment() && !environment.IsStaging())
        {
            logger.LogError(
                "Test data seeding refused: it runs only in Development and Staging, and this is {Environment}.",
                environment.EnvironmentName);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            logger.LogError(
                "Test data seeding refused: {Section}:Password is not configured.", TestDataSeedOptions.SectionName);
            return;
        }

        var db = services.GetRequiredService<AppDbContext>();
        var normalizer = services.GetRequiredService<ILookupNormalizer>();

        if (options.Reset && !await WipeAsync(db, normalizer, configuration[AdminSeeder.EmailKey], logger))
        {
            return;
        }

        // Guarded on the sentinel account, not on "is the table empty": the AdminSeed account and its
        // member row are always there, and hand-made test data must not be mistaken for the seed.
        var sentinel = normalizer.NormalizeEmail(TestDataGenerator.SentinelEmail);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == sentinel))
        {
            logger.LogInformation("Test data already present; seeding skipped.");
            return;
        }

        var roleIds = await db.Roles
            .Where(r => r.Name != null)
            .ToDictionaryAsync(r => r.Name!, r => r.Id);

        if (ApplicationRoles.All.Any(role => !roleIds.ContainsKey(role)))
        {
            // AdminSeeder creates the roles and runs first; this is the case where it failed.
            logger.LogError("Test data seeding refused: the Identity roles are missing.");
            return;
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var data = TestDataGenerator.Generate(Seed, now);

        // ONE hash for every account. UserManager.CreateAsync would run PBKDF2 once per account - about
        // 164 times, tens of seconds of cold start on B1 - for a password they all share anyway.
        var hasher = services.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        var passwordHash = hasher.HashPassword(data.Accounts[0].User, options.Password);

        foreach (var (user, roles) in data.Accounts)
        {
            // Everything CreateAsync would have filled in, normalised by the SAME normalizer login uses
            // to look the account up - a mismatch here is an account nobody can sign into.
            user.NormalizedEmail = normalizer.NormalizeEmail(user.Email);
            user.NormalizedUserName = normalizer.NormalizeName(user.UserName);
            user.PasswordHash = passwordHash;
            user.SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant();
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
            user.LockoutEnabled = true;

            db.Users.Add(user);
            db.UserRoles.AddRange(roles.Select(role => new IdentityUserRole<string>
            {
                UserId = user.Id,
                RoleId = roleIds[role],
            }));
        }

        db.Members.AddRange(data.Members);
        db.MembershipPasses.AddRange(data.Passes);
        db.ClassTypes.AddRange(data.ClassTypes);
        db.Classes.AddRange(data.Classes);
        db.Bookings.AddRange(data.Bookings);
        db.Exercises.AddRange(data.Exercises);
        db.TrainingPlans.AddRange(data.Plans);

        // One SaveChangesAsync is one transaction, and EnableRetryOnFailure retries it as a unit - so a
        // failed seed never leaves the sentinel behind without the rest of the club.
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seeded test data: {Accounts} accounts, {Members} members, {Passes} passes, {Classes} classes, "
            + "{Bookings} bookings, {Exercises} exercises, {Plans} plans.",
            data.Accounts.Count, data.Members.Count, data.Passes.Count, data.Classes.Count,
            data.Bookings.Count, data.Exercises.Count, data.Plans.Count);
    }

    /// <summary>
    /// Deletes every piece of domain data except the AdminSeed account, its member row and the roles.
    /// Children before parents, because every foreign key between domain tables is Restrict.
    /// </summary>
    private static async Task<bool> WipeAsync(
        AppDbContext db,
        ILookupNormalizer normalizer,
        string? adminSeedEmail,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(adminSeedEmail))
        {
            logger.LogError("Test data reset refused: {Key} is not configured.", AdminSeeder.EmailKey);
            return false;
        }

        var normalizedAdminEmail = normalizer.NormalizeEmail(adminSeedEmail);

        // An explicit transaction under EnableRetryOnFailure must run inside the execution strategy, or
        // EF throws. The whole wipe retries as one unit.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            var adminUserId = await db.Users
                .Where(u => u.NormalizedEmail == normalizedAdminEmail)
                .Select(u => u.Id)
                .SingleOrDefaultAsync();

            if (adminUserId is null)
            {
                // Without the one account the wipe keeps, it would delete every admin there is and lock
                // the environment out of itself.
                logger.LogError("Test data reset refused: the AdminSeed account does not exist.");
                return false;
            }

            await db.Bookings.ExecuteDeleteAsync();
            await db.TrainingPlanItems.ExecuteDeleteAsync();
            await db.TrainingPlans.ExecuteDeleteAsync();
            await db.Classes.ExecuteDeleteAsync();
            await db.ClassTypes.ExecuteDeleteAsync();
            await db.MembershipPasses.ExecuteDeleteAsync();
            await db.Exercises.ExecuteDeleteAsync();
            await db.OutboxMessages.ExecuteDeleteAsync();
            await db.PushSubscriptions.ExecuteDeleteAsync();
            await db.Members.Where(m => m.UserId != adminUserId).ExecuteDeleteAsync();
            await db.UserRoles.Where(r => r.UserId != adminUserId).ExecuteDeleteAsync();
            await db.UserClaims.Where(c => c.UserId != adminUserId).ExecuteDeleteAsync();
            await db.UserLogins.Where(l => l.UserId != adminUserId).ExecuteDeleteAsync();
            await db.UserTokens.Where(t => t.UserId != adminUserId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id != adminUserId).ExecuteDeleteAsync();

            await transaction.CommitAsync();

            logger.LogWarning("Test data reset: every member, class, booking, pass and plan was deleted.");
            return true;
        });
    }
}
