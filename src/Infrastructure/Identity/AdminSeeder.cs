using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Domain;

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
        logger.LogInformation("Seeded admin account.");
    }
}
