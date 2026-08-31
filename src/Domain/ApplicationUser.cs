using Microsoft.AspNetCore.Identity;

namespace po_prostu_silka.Domain;

/// <summary>
/// The application's user. Extends ASP.NET Core Identity with the three fields the milestone needs,
/// so S-01 (registration), S-02 (member list) and S-09 (profile edit) inherit them rather than each
/// adding another Identity migration.
///
/// Default string keys: conventional, best-documented, and what every Identity sample assumes.
///
/// LAYERING NOTE - do not "fix" this. <see cref="IdentityUser"/> lives in
/// Microsoft.AspNetCore.Identity, NOT Microsoft.EntityFrameworkCore. AGENTS.md forbids EF Core in
/// Domain; it names EF Core specifically, and this type does not reference it. Moving this type to
/// Infrastructure would put the domain's central entity outside the domain for no reason.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Shown to the member and in the admin's member list. Editable by the member (S-09, FR-006).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gates everything else (PRD Business Logic). Defined here in F-02 and enforced at login and
    /// in the ActiveMember authorization policy; the admin actions that change it land with S-01
    /// (approve) and S-02 (block/unblock).
    /// </summary>
    public AccountStatus Status { get; set; } = AccountStatus.Pending;

    /// <summary>When the account was registered. The admin's pending list (FR-005) orders by it.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
