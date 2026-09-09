namespace po_prostu_silka.Domain;

/// <summary>
/// Whether a LOGIN may be used. Two live states since S-16 — active, and blocked for enforcement.
/// There is deliberately no "rejected" state; blocking covers bad actors.
///
/// <para>
/// The numeric values are explicit and must not be reordered: the column is persisted as an int, so
/// renumbering would silently reinterpret every existing row. That constraint is why
/// <see cref="Pending"/> is still declared below rather than deleted.
/// </para>
///
/// <para>
/// See <see cref="Members.MembershipStatus"/> for why this is not the same question as whether a
/// PERSON may use the club, and why both have to exist.
/// </para>
/// </summary>
public enum AccountStatus
{
    /// <summary>
    /// RETIRED. Nothing has produced this value since S-16 (2026-09-09), which removed admin approval
    /// of new accounts: registration creates an <see cref="Active"/> account immediately, and what
    /// decides whether somebody may train is the karnet rather than a flag on their login.
    ///
    /// <para>
    /// IT STAYS DECLARED, AND REMOVING IT IS A SEPARATE CHANGE. Two reasons, and the first is the
    /// binding one: 0 is the persisted default, so a row inserted without an explicit value carries
    /// it, and the enum must be able to read back anything the column can hold. The second is the
    /// rollback rule in AGENTS.md — rolling back redeploys the previous artifact without rolling back
    /// schema, and that artifact reads this value. Delete it only once no deployed build does, and
    /// only after a migration has established that no row holds 0.
    /// </para>
    ///
    /// <para>
    /// The <c>ActivatePendingAccounts</c> migration (S-16) flipped every existing row off this value.
    /// </para>
    /// </summary>
    Pending = 0,

    /// <summary>Usable. Full access to app content, subject to membership and the karnet.</summary>
    Active = 1,

    /// <summary>Blocked by an admin. Refused at login; retains their data and history.</summary>
    Blocked = 2,
}
