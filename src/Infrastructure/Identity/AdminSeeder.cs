using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Identity;

/// <summary>
/// Guarantees the roles and the admin account exist, in every environment, without anyone ever
/// self-registering an admin (PRD Access Control: "admin accounts are seeded at setup").
///
/// Runs on every start. App Service recycles without warning and Always On restarts the app on its
/// own schedule, so this MUST be idempotent - see the notes on SeedAsync.
/// </summary>
public static class AdminSeeder
{
    public const string EmailKey = "AdminSeed:Email";
    public const string PasswordKey = "AdminSeed:Password";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in ApplicationRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                logger.LogInformation("Seeded missing role {Role}.", role);
            }
        }

        var email = configuration[EmailKey];
        var password = configuration[PasswordKey];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            // Log and continue rather than throw: a missing app setting must not take the site
            // down. The consequence is a running app with no admin, which /health cannot detect -
            // so this line is the signal to look for.
            logger.LogError(
                "Admin seeding skipped: {EmailKey} and/or {PasswordKey} is not configured.",
                EmailKey, PasswordKey);
            return;
        }

        // Guard on "does this user exist", never on "is the table empty" - the latter would create a
        // second admin the moment any other account is deleted.
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Deliberately does NOT reset the password. If it did, rotating the admin credential
            // would be silently reverted on the next App Service recycle.
            //
            // The member record IS still ensured, on every start, even though the account is untouched.
            // That is not belt-and-braces: an admin without one fails the membership claim and loses the
            // Admin policy, which locks the club out of its own app, and the account existing is exactly
            // the case where nothing else would create it.
            await EnsureMemberAsync(services, existing, logger);

            logger.LogInformation("Admin account already present; seeding skipped.");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Administrator",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var created = await userManager.CreateAsync(admin, password);
        if (!created.Succeeded)
        {
            // Never log the password, and never log the result's raw description for a password
            // failure - descriptions can echo policy details but not the value itself.
            logger.LogError(
                "Admin seeding failed: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Code)));
            return;
        }

        await userManager.AddToRoleAsync(admin, ApplicationRoles.Admin);
        await EnsureMemberAsync(services, admin, logger);
        logger.LogInformation("Seeded admin account.");
    }

    /// <summary>
    /// Gives an account the club record every account must have (S-14), if it does not have one.
    ///
    /// <para>
    /// ONE OF THREE PRODUCERS, and the one that covers the account nothing else creates. The others are
    /// the backfill in the AddMembers migration (every account that predates the split) and
    /// AuthEndpoints.RegisterAsync (every account created since). Together they are what makes "an
    /// account with no member" unreachable — which matters because the membership claim refuses such an
    /// account everywhere, the Admin policy included.
    /// </para>
    ///
    /// <para>
    /// Idempotent by the same rule the rest of this class follows: guarded on "does this account have a
    /// member", never on "is the table empty".
    /// </para>
    /// </summary>
    private static async Task EnsureMemberAsync(
        IServiceProvider services,
        ApplicationUser user,
        ILogger logger)
    {
        var db = services.GetRequiredService<AppDbContext>();

        // Seeks IX_Members_UserId.
        if (await db.Members.AnyAsync(m => m.UserId == user.Id))
        {
            return;
        }

        db.Members.Add(new Member
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,

            // No address: the seeded admin never had one to copy, and since S-14 dropped the four
            // account columns there is nowhere left to copy one from.

            // Active regardless of the account's status, for the reason the migration's backfill gives:
            // Pending is a fact about a login awaiting approval and stays on the account. An admin is
            // seeded Active anyway.
            Status = MembershipStatus.Active,
            CreatedAt = user.CreatedAt,
            ClaimedAt = user.CreatedAt,
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded the member record for the admin account.");
    }
}
