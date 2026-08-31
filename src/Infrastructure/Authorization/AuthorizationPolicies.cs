using Microsoft.AspNetCore.Authorization;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Infrastructure.Authorization;

/// <summary>
/// The application's authorization contract.
///
/// The PRD's real access rule is "active account AND role", not role alone (Business Logic: "every
/// account must pass admin approval before it can act"). Stating it once here means the nine
/// downstream slices annotate endpoints instead of re-deriving the rule - and cannot forget the
/// status half, which is how a pending member would otherwise reach the schedule.
///
/// Account status is read from a claim rather than the database, so a policy check costs no query
/// on a 5-DTU Basic tier. The claim is minted at sign-in by
/// <see cref="Identity.AppUserClaimsPrincipalFactory"/>; staleness is bounded by the 30-minute
/// security-stamp validation interval configured in Program.cs.
///
/// THESE NAMES ARE A CONTRACT later slices depend on. Do not rename them.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Claim type carrying <see cref="AccountStatus"/> as its string name.</summary>
    public const string StatusClaimType = "account_status";

    /// <summary>Authenticated, approved, and holding any application role. The default for member-facing endpoints.</summary>
    public const string ActiveMember = "ActiveMember";

    /// <summary>Everything ActiveMember requires, plus the Admin role.</summary>
    public const string Admin = "Admin";

    public static AuthorizationBuilder AddApplicationPolicies(this AuthorizationBuilder builder) =>
        builder
            .AddPolicy(ActiveMember, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(StatusClaimType, nameof(AccountStatus.Active))
                .RequireRole(ApplicationRoles.All))
            .AddPolicy(Admin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(StatusClaimType, nameof(AccountStatus.Active))
                .RequireRole(ApplicationRoles.Admin));
}
