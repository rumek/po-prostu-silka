namespace po_prostu_silka.Domain.Scheduling;

/// <summary>
/// One member's claim on one class occurrence (prd.md US-01, FR-008, FR-009).
///
/// <para>
/// THE ROW IS THE SPOT. There is no counter anywhere; how full a class is, is the number of
/// <see cref="BookingStatus.Active"/> rows pointing at it. That is a deliberate continuation of the
/// read-time projection the scheduling context has used since S-03 — see IClassScheduleQuery — and it
/// is why <see cref="Class.ConcurrencyStamp"/> exists: counting rows and inserting one is a
/// read-then-write sequence, and the stamp is what makes it atomic.
/// </para>
///
/// <para>
/// CANCELLING DOES NOT DELETE. FR-009 keeps the cancelled booking in history, so cancellation moves
/// <see cref="Status"/> and stamps <see cref="CancelledAt"/>. A member may book the same class again
/// afterwards, which is why the uniqueness index is filtered to active rows rather than plain.
/// </para>
///
/// Anemic on purpose, like <see cref="Class"/>: the invariants live in BookingEndpoints, which is
/// where the capacity check, the time rule and the stamp rotation have to sit together to be atomic.
/// </summary>
public class Booking
{
    public Guid Id { get; set; }

    /// <summary>The occurrence being booked. Immutable — moving a booking between classes is not a thing.</summary>
    public Guid ClassId { get; set; }

    /// <summary>
    /// The occurrence. READ SIDE ONLY, same contract as <see cref="Class.ClassType"/>.
    ///
    /// <para>
    /// It exists so the member's upcoming-bookings query can project the class's time and its type's
    /// name in one statement. NO WRITE PATH MAY READ <see cref="Class.Capacity"/> THROUGH IT without
    /// also rotating <see cref="Class.ConcurrencyStamp"/> — a capacity check that does not rotate the
    /// stamp is not a check, it is a guess that happens to be right most of the time.
    /// </para>
    ///
    /// <para>
    /// Note the reverse navigation deliberately does NOT exist: <see cref="Class"/> has no
    /// <c>Bookings</c> collection. A collection hanging off the aggregate is a standing invitation for
    /// a write path to count through it, and the read projection uses a correlated subquery instead,
    /// which produces the same single SQL statement without the hazard.
    /// </para>
    /// </summary>
    public Class Class { get; set; } = null!;

    /// <summary>Who holds the spot. Identity's default string key, like <see cref="Class.InstructorUserId"/>.</summary>
    public string MemberUserId { get; set; } = string.Empty;

    /// <summary>
    /// The member's account. READ SIDE ONLY — it exists so the admin's booking list can project
    /// <c>DisplayName</c> and <c>Email</c> in one statement.
    /// </summary>
    public ApplicationUser Member { get; set; } = null!;

    /// <summary>
    /// Whether this booking still holds the spot. Only <see cref="BookingStatus.Active"/> counts
    /// against <see cref="Class.Capacity"/>.
    /// </summary>
    public BookingStatus Status { get; set; } = BookingStatus.Active;

    /// <summary>When the member booked. Orders the admin's list, so the club can see who was first.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the spot was released, or null while the booking is active. Kept because FR-009 wants the
    /// history to be readable, not merely present.
    /// </summary>
    public DateTimeOffset? CancelledAt { get; set; }
}
