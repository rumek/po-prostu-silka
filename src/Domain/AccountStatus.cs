namespace po_prostu_silka.Domain;

/// <summary>
/// The account lifecycle locked by the PRD: register → pending → active, with block/unblock for
/// enforcement. There is deliberately no "rejected" state — blocking covers bad actors.
///
/// The numeric values are explicit and must not be reordered: the column is persisted as an int,
/// so renumbering would silently reinterpret every existing row. <see cref="Pending"/> is 0 so it
/// is the default for any row inserted without an explicit value — a new account is never
/// accidentally active.
/// </summary>
public enum AccountStatus
{
    /// <summary>Awaiting admin approval. Can log in, but reaches only the awaiting-approval screen.</summary>
    Pending = 0,

    /// <summary>Approved by an admin. Full access to app content.</summary>
    Active = 1,

    /// <summary>Blocked by an admin. Refused at login; retains their data and history.</summary>
    Blocked = 2,
}
