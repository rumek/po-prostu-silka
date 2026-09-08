namespace po_prostu_silka.Domain;

/// <summary>
/// The names of the application's authorization policies, and the claim type they read.
///
/// These live in Domain, not beside the policy builder in Infrastructure, because they are consumed
/// by endpoint definitions in Application — and Application may not reference Infrastructure. The
/// builder that turns these names into ASP.NET policies IS infrastructure and stays there
/// (<c>Infrastructure/Authorization/AuthorizationPolicies.cs</c>); only the names are a contract the
/// upper layers need. <see cref="ApplicationRoles"/> sits here for the same reason.
///
/// THESE NAMES ARE A CONTRACT later slices depend on. Do not rename them.
/// </summary>
public static class AuthorizationPolicyNames
{
    /// <summary>Claim type carrying <see cref="AccountStatus"/> as its string name.</summary>
    public const string StatusClaimType = "account_status";

    /// <summary>
    /// Claim type carrying <see cref="Members.MembershipStatus"/> as its string name (S-14).
    ///
    /// <para>
    /// A SECOND STATUS CLAIM, not a replacement. The two answer different questions and every policy
    /// checks both: the account status says whether this LOGIN may be used, this one says whether the
    /// PERSON may use the club. They can legitimately disagree — an approved account whose membership
    /// was blocked, or a pending account whose membership is active from the day it was created.
    /// </para>
    ///
    /// <para>
    /// Like the account status, it is read from the COOKIE rather than the database, so it is re-minted
    /// on the security-stamp validation interval (Program.cs) or on POST /api/auth/refresh. A block
    /// rotates the linked account's security stamp, so it bites on exactly the same schedule an
    /// account block always has.
    /// </para>
    /// </summary>
    public const string MemberStatusClaimType = "member_status";

    /// <summary>
    /// Claim type carrying the caller's member id.
    ///
    /// <para>
    /// Minted alongside the status because the read that produces one produces the other for free, and
    /// it is what lets endpoints resolve "who is asking" as a member without a query per request. It
    /// keeps the rule the booking and plan surfaces have always followed: the caller's identity comes
    /// from the cookie, never from the request body.
    /// </para>
    /// </summary>
    public const string MemberIdClaimType = "member_id";

    /// <summary>Authenticated, approved, and holding any application role. The default for member-facing endpoints.</summary>
    public const string ActiveMember = "ActiveMember";

    /// <summary>Everything ActiveMember requires, plus the Admin role.</summary>
    public const string Admin = "Admin";

    /// <summary>
    /// Active, and holding EITHER the Trainer role or the Admin role. The authoring side of training
    /// plans (S-11), and the first capability the Trainer role has ever carried.
    ///
    /// <para>
    /// The union is deliberate rather than a convenience. prd.md FR-015 gives plan authoring to the
    /// admin; the S-11 decision widened it to trainers without taking it away, which is exactly what
    /// prd-v2's additive role model asks for - an owner who teaches holds both roles and must not
    /// have to pick one.
    /// </para>
    ///
    /// <para>
    /// NOT a superset of <see cref="ActiveMember"/> and not a substitute for it. An account holding
    /// only Trainer passes this and fails ActiveMember by design (see ApplicationRoles.MemberFacing).
    /// Member-facing reads keep using ActiveMember.
    /// </para>
    /// </summary>
    public const string TrainerOrAdmin = "TrainerOrAdmin";
}
