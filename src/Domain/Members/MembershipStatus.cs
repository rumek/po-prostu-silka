namespace po_prostu_silka.Domain.Members;

/// <summary>
/// Whether a person may use the club. This is the MEMBERSHIP lifecycle, and it works for someone who
/// has never registered — which is the whole reason <see cref="Member"/> exists apart from
/// <see cref="ApplicationUser"/>.
///
/// <para>
/// DO NOT CONFUSE IT WITH <see cref="AccountStatus"/>. The two answer different questions and both
/// are load-bearing:
/// </para>
///
/// <list type="bullet">
///   <item><see cref="AccountStatus"/> — may this LOGIN be used? Owns registration approval
///   (pending → active) and the refusal at sign-in. Meaningless without an account.</item>
///   <item><see cref="MembershipStatus"/> — may this PERSON use the club? Owns bookings and plans.
///   Meaningful whether or not an account exists.</item>
/// </list>
///
/// <para>
/// NOTE THE DEFAULT IS THE OPPOSITE WAY ROUND FROM <see cref="AccountStatus"/>, and deliberately so.
/// There, <c>Pending = 0</c> so an account inserted without an explicit value is never accidentally
/// active — self-registration is open, so a new row is by definition unvetted. Here a row only exists
/// because an admin typed it in or because registration created one alongside an account, so the
/// person is vetted by construction and blocking has to be an explicit act. There is no membership
/// <c>Pending</c>: "registered but not yet approved" is a fact about a login, and it already has a
/// home.
/// </para>
///
/// The numeric values are explicit and must not be reordered — the column is persisted as an int, and
/// the filtered indexes in MemberConfiguration name them as literals.
/// </summary>
public enum MembershipStatus
{
    /// <summary>A member of the club. The normal state, and the default for an inserted row.</summary>
    Active = 0,

    /// <summary>
    /// Barred by an admin. Future bookings are released when this is set (the same cascade an account
    /// block runs), history is kept, and any linked account is signed out.
    /// </summary>
    Blocked = 1,
}
