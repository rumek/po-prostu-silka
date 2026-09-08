using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Authorization;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Identity;

/// <summary>
/// Adds the claims the ActiveMember/Admin/TrainerOrAdmin policies check.
///
/// <para>
/// Identity calls this when a principal is created (at sign-in, and again whenever the security
/// stamp validator refreshes the cookie), so the claims track stored state without any per-request
/// database round-trip. That refresh interval - SecurityStampValidatorOptions.ValidationInterval,
/// set in Program.cs, which is the one place it is stated - is what bounds how long a just-blocked
/// member keeps their access.
/// </para>
///
/// <para>
/// SINCE S-14 THIS COSTS ONE INDEXED READ. The membership status lives on a different row from the
/// account, so unlike the other claims it cannot be taken from the entity Identity already loaded.
/// The read seeks IX_Members_UserId and happens at sign-in and once per validation interval per
/// signed-in user — the same order as the security-stamp check already running beside it, but worth
/// knowing about, because it is one of the few things in this app that scales with concurrent users
/// rather than with member count.
/// </para>
/// </summary>
public class AppUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options,
    AppDbContext db)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(AuthorizationPolicies.StatusClaimType, user.Status.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.GivenName, user.DisplayName));

        // Seeks IX_Members_UserId. Projected rather than materialised - nothing here needs the entity.
        var member = await db.Members
            .AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .Select(m => new { m.Id, m.Status })
            .FirstOrDefaultAsync();

        // NO FALLBACK WHEN THE ROW IS MISSING, and this is the deliberate half. Minting Active here
        // would turn a data-integrity failure into a silent authorization bypass that nothing would
        // ever surface; leaving the claims off means such an account fails every policy loudly, which
        // is recoverable. Three producers make the state unreachable - the AddMembers backfill,
        // AdminSeeder on every cold start, and RegisterAsync - so this branch is a tripwire, not a
        // case to handle.
        if (member is null)
        {
            return identity;
        }

        identity.AddClaim(
            new Claim(AuthorizationPolicies.MemberStatusClaimType, member.Status.ToString()));
        identity.AddClaim(
            new Claim(AuthorizationPolicies.MemberIdClaimType, member.Id.ToString()));

        return identity;
    }
}
