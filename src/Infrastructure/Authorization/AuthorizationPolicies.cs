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
/// <see cref="Identity.AppUserClaimsPrincipalFactory"/>; staleness is bounded by the security-stamp
/// validation interval configured in Program.cs, which is the one place that number is stated.
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
            // MemberFacing, not All: the two are different sets since Trainer arrived. All is what
            // the seeder creates; this is what may pass. Granting a role must never widen access as
            // a side effect - see ApplicationRoles for which array a new role belongs in.
            .AddPolicy(ActiveMember, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(StatusClaimType, nameof(AccountStatus.Active))
                .RequireRole(ApplicationRoles.MemberFacing))
            .AddPolicy(Admin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(StatusClaimType, nameof(AccountStatus.Active))
                .RequireRole(ApplicationRoles.Admin))
            // The first policy here that admits a UNION of roles. RequireRole with several arguments
            // is OR in ASP.NET Core - the same semantics ActiveMember already leans on by passing the
            // MemberFacing array - so this reads "Trainer or Admin", not "Trainer and Admin".
            //
            // A role granted to a signed-in account does not reach that session's cookie immediately:
            // the claims are re-minted on POST /api/auth/refresh, or when the security-stamp
            // validation interval configured in Program.cs elapses. A freshly promoted trainer
            // therefore keeps seeing 403 for up to that interval, which is expected rather than a bug.
            .AddPolicy(AuthorizationPolicyNames.TrainerOrAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(StatusClaimType, nameof(AccountStatus.Active))
                .RequireRole(ApplicationRoles.Trainer, ApplicationRoles.Admin));
}
