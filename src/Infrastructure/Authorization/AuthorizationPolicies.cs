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
/// THE NAMES THEMSELVES LIVE IN DOMAIN (<see cref="AuthorizationPolicyNames"/>), not here: endpoint
/// definitions in Application reference them, and Application may not reference Infrastructure. This
/// file keeps only the builder, which genuinely is infrastructure. The aliases below exist so
/// existing call sites keep compiling; prefer AuthorizationPolicyNames in new code.
/// </summary>
public static class AuthorizationPolicies
{
    /// <inheritdoc cref="AuthorizationPolicyNames.StatusClaimType"/>
    public const string StatusClaimType = AuthorizationPolicyNames.StatusClaimType;

    /// <inheritdoc cref="AuthorizationPolicyNames.ActiveMember"/>
    public const string ActiveMember = AuthorizationPolicyNames.ActiveMember;

    /// <inheritdoc cref="AuthorizationPolicyNames.Admin"/>
    public const string Admin = AuthorizationPolicyNames.Admin;

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
