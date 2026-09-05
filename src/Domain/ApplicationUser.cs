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

    // CONTACT DETAILS (S-13). The phone number is NOT declared here on purpose: IdentityUser
    // already carries a PhoneNumber column, it was unused until this slice, and adding a second
    // phone property would leave two columns where one member has one number. Its length is
    // bounded explicitly in ApplicationUserConfiguration - inherited properties get no length from
    // Identity, so without that it stays nvarchar(max).
    //
    // PhoneNumberConfirmed stays deliberately unused: nothing in this milestone sends an SMS, and
    // confirming a number nobody verifies would be a lie in the schema.
    //
    // All four are nullable because accounts registered before this slice have no values. The
    // schema tolerates them; the API does not - both /register and PUT /api/profile require every
    // field through ContactDetails, and the profile screen prompts an incomplete account to fill
    // them in.

    /// <summary>Street name, without the house number. Required by the API, nullable in the schema.</summary>
    public string? Street { get; set; }

    /// <summary>House number, optionally with a flat number ("12A/3"). One field, because that is how a Polish address is written.</summary>
    public string? HouseNumber { get; set; }

    /// <summary>Polish postal code in NN-NNN form.</summary>
    public string? PostalCode { get; set; }

    /// <summary>Town or city.</summary>
    public string? City { get; set; }
}
