namespace po_prostu_silka.Domain.Scheduling;

/// <summary>
/// Whether the member came to a class that took place (S-27, AT-01–AT-03). A fact about the
/// OCCURRENCE, recorded by staff after its start — orthogonal to <see cref="BookingStatus"/>, which
/// says whether the booking still stands.
///
/// <para>
/// NOT A <see cref="BookingStatus"/> VALUE, deliberately. Every capacity, roster and uniqueness
/// predicate in the app reads <c>Status == Active</c>; an "attended" status would drop marked rows out
/// of all of them. A separate nullable column is invisible to every predicate except the entries-used
/// one, which is the only one AT-03 means to change.
/// </para>
///
/// <para>
/// Null on the booking means "not recorded", and that counts as SPENT — see EntryConsumption. The
/// numeric values are persisted as an int and must not be reordered; S-36 appended two and froze one.
/// </para>
/// </summary>
public enum BookingAttendance
{
    /// <summary>The member came. The entry stays spent.</summary>
    Present = 0,

    /// <summary>
    /// LEGACY, READ-ONLY (S-36). Before class-makeups, "did not come" returned the entry, and rows
    /// recorded then keep that meaning so no member's balance moved on deploy. Nothing writes it any
    /// more: the attendance PUT refuses <c>"absent"</c>, and re-marking such a row moves it to one of
    /// the values below, re-spending the entry it returned.
    /// </summary>
    Absent = 1,

    /// <summary>
    /// "Nie był – odrobi" (S-36). The entry stays SPENT on this class, and the booking becomes a makeup
    /// item: the right to ONE free makeup booking within <see cref="MakeupRules.DeadlineDays"/>.
    /// </summary>
    Makeup = 2,

    /// <summary>"Nie był – przepada" (S-36). The entry stays spent and nothing is owed.</summary>
    Forfeited = 3,
}
