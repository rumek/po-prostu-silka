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
    /// Gates everything else (PRD Business Logic). Defined here in F-02 and enforced at login and in
    /// the ActiveMember authorization policy; the admin action that changes it is S-02
    /// (block/unblock). Approval is GONE (S-16) - there is no transition into this field's retired
    /// third value any more.
    ///
    /// <para>
    /// THE DEFAULT IS ACTIVE, AND THE COLUMN'S IS NOT. Registration sets this explicitly, so the
    /// initializer only decides what a caller who FORGOT to gets. Before S-16 that was
    /// <see cref="AccountStatus.Pending"/>, which an admin would then approve; with approval removed
    /// it would be an account that logs in - Blocked is the only refusal left - and then fails every
    /// ActiveMember policy, with the screen that used to explain the wait deleted. Active is the
    /// answer that at least matches what registration produces. Note the SQL default stays 0, so a
    /// row inserted without going through this initializer is still Pending on read.
    /// </para>
    /// </summary>
    public AccountStatus Status { get; set; } = AccountStatus.Active;

    /// <summary>When the account was registered. The admin's member list (FR-005) can order by it; the
    /// pending queue that originally did was removed with approval (S-16).</summary>
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
    // THE ADDRESS IS NOT HERE. It moved to Member in S-14 and the four columns were dropped, because
    // a person may train at this club without ever having a login for an address to hang off. The
    // phone number stays only because it is Identity's own inherited column; Member carries the copy
    // everything actually reads.
}
