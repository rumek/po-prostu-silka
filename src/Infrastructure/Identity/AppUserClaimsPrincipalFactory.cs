using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Authorization;

namespace po_prostu_silka.Infrastructure.Identity;

/// <summary>
/// Adds the account-status claim the ActiveMember/Admin policies check.
///
/// Identity calls this when a principal is created (at sign-in, and again whenever the security
/// stamp validator refreshes the cookie), so the claim tracks the stored status without any
/// per-request database round-trip. That refresh interval - 30 minutes, set in Program.cs - is what
/// bounds how long a just-blocked member keeps their access.
/// </summary>
public class AppUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(AuthorizationPolicies.StatusClaimType, user.Status.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.GivenName, user.DisplayName));

        return identity;
    }
}
