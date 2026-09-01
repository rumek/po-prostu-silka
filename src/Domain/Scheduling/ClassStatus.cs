namespace po_prostu_silka.Domain.Scheduling;

/// <summary>
/// A class's lifecycle. FR-013 makes cancellation a visible STATE, not a delete: a cancelled class
/// keeps its bookings and its history so the members who signed up can be told (S-05).
///
/// The numeric values are explicit and must not be reordered: the column is persisted as an int, so
/// renumbering would silently reinterpret every existing row. <see cref="Scheduled"/> is 0 so it is
/// the default for any row inserted without an explicit value - a class is never accidentally
/// cancelled.
///
/// S-03 DEFINES this enum but never sets <see cref="Cancelled"/>. The transition, and the email and
/// push that must accompany it, land together in S-05 - the roadmap pairs them deliberately, because
/// cancelling a class without telling the booked members is a half-feature. Defining the state now
/// means S-05 adds a transition rather than a migration plus a rewrite of every read path this slice
/// creates.
/// </summary>
public enum ClassStatus
{
    /// <summary>On the schedule and bookable. The only state S-03 ever writes.</summary>
    Scheduled = 0,

    /// <summary>Cancelled by an admin. Still visible, still holds its bookings and history (S-05).</summary>
    Cancelled = 1,
}
