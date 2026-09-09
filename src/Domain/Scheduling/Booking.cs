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

    /// <summary>
    /// Who holds the spot (S-14). A MEMBER, not an account: a spot belongs to a person, and a person
    /// may train here without ever logging in.
    /// </summary>
    public Guid MemberId { get; set; }

    /// <summary>
    /// The member. READ SIDE ONLY — it exists so the admin's booking list can project
    /// <c>DisplayName</c> and <c>Email</c> in one statement.
    /// </summary>
    public Domain.Members.Member? Member { get; set; }

    /// <summary>
    /// Which karnet paid for this booking, or null when none did (S-16).
    ///
    /// <para>
    /// THIS IS WHAT KEEPS THE DERIVED ENTRY COUNT STABLE. Entries left on a pass is the issued count
    /// minus the number of ACTIVE bookings carrying that pass's id — so it is attribution, recorded at
    /// the moment of booking, rather than a re-derivation from dates. Without it, editing a pass's
    /// validity range would silently move history between passes: a booking would stop being paid for
    /// by the pass that actually covered it and start counting against whichever pass happens to cover
    /// its date today.
    /// </para>
    ///
    /// <para>
    /// NULL MEANS "BOOKED BEFORE THE KARNET EXISTED", and such a booking CONSUMES NO ENTRY. That is a
    /// stated consequence, not a gap: back-filling these would mean inventing a pass that was never
    /// issued, so a member's first karnet is simply not retroactively debited for classes they
    /// attended before S-16 shipped. The column is nullable for that reason and for one more — it had
    /// to be addable to a live table without a backfill.
    /// </para>
    /// </summary>
    public Guid? MembershipPassId { get; set; }

    /// <summary>
    /// The karnet, when one paid. READ SIDE ONLY, same contract as <see cref="Member"/> above.
    /// </summary>
    public Domain.Members.MembershipPass? MembershipPass { get; set; }

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
