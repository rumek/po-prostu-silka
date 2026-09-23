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
/// numeric values are persisted as an int and must not be reordered.
/// </para>
/// </summary>
public enum BookingAttendance
{
    /// <summary>The member came. The entry stays spent.</summary>
    Present = 0,

    /// <summary>The member did not come. The entry goes back to the karnet.</summary>
    Absent = 1,
}
