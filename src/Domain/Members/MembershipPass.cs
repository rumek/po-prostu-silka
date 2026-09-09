namespace po_prostu_silka.Domain.Members;

/// <summary>
/// A karnet: one member's time-bounded, counted entitlement to train (S-16, MP-04..MP-06).
///
/// <para>
/// THE THIRD ACCESS AXIS. <see cref="Domain.AccountStatus"/> answers "may this LOGIN be used" and
/// <see cref="MembershipStatus"/> answers "may this PERSON use the club" — see
/// <see cref="MembershipStatus"/> for why those two are not the same question. Neither answers "is
/// this person entitled to be in THIS class, on THIS day". That is what a pass answers, and it is why
/// it exists as a row rather than as another flag on <see cref="Member"/>: an entitlement that starts,
/// ends and runs out cannot be a status.
/// </para>
///
/// <para>
/// <see cref="EntryCount"/> IS THE NUMBER ISSUED, NOT THE NUMBER REMAINING. There is deliberately no
/// remaining-balance column. Entries left is derived — the issued count minus the number of active
/// bookings carrying this pass's id — for the same reason a class's free spots are derived from
/// booking rows rather than from a counter (see <see cref="Scheduling.Booking"/>): a stored counter is
/// a second source of truth, and the first time a cancel path forgets to decrement it, the club has a
/// pass that says two entries left and a database that says none. Deriving costs one indexed count on
/// the write path; drifting costs correctness.
/// </para>
///
/// <para>
/// THE RANGE IS INCLUSIVE AT BOTH ENDS, and <see cref="DateOnly"/> rather than
/// <see cref="DateTimeOffset"/> on purpose: a karnet is valid for a DAY, not from an instant. Whether
/// a class falls inside it is decided on the club-local date of the class's start
/// (<see cref="Scheduling.ClubTime"/>), so a 21:00 class on the last valid day is covered and a 06:00
/// class the morning after is not.
/// </para>
///
/// <para>
/// Passes for one member MAY NOT OVERLAP — they are a history, not a stack. That is a between-rows
/// range rule no index can express, so it is checked in the endpoint and made atomic by rotating
/// <see cref="Member.ConcurrencyStamp"/>, the same protocol claiming a code and blocking already use.
/// </para>
///
/// <para>
/// Anemic, like <see cref="Member"/> and <see cref="Scheduling.Class"/>: both invariants here need to
/// be atomic with a write happening elsewhere, so they live in the endpoints where they can share a
/// unit of work. NO <c>Bookings</c> COLLECTION, for the reason <see cref="Scheduling.Booking"/> spells
/// out — a collection on the aggregate is an invitation for a write path to count through it instead
/// of through the correlated subquery that is guarded by a stamp.
/// </para>
/// </summary>
public class MembershipPass
{
    public Guid Id { get; set; }

    /// <summary>Whose pass this is. Immutable — a karnet is never transferred between people.</summary>
    public Guid MemberId { get; set; }

    /// <summary>
    /// The member. READ SIDE ONLY, the same contract every other navigation in this codebase carries:
    /// it exists so a projection can reach the person in one statement, never so a write path can
    /// mutate them through it.
    /// </summary>
    public Member? Member { get; set; }

    /// <summary>
    /// What the club calls this pass — "Karnet 8 wejść", "Open miesięczny". FREE TEXT, not an enum:
    /// the club invents pass names faster than the code could ship them, and nothing in the system
    /// branches on the name. Bounded by <c>MembershipPassRules.TypeNameMaxLength</c>.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>First day the pass covers. Inclusive.</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Last day the pass covers. INCLUSIVE — a pass valid to the 30th covers the 30th.</summary>
    public DateOnly ValidTo { get; set; }

    /// <summary>
    /// How many entries the pass was ISSUED with. Always required and always at least one (MP-04):
    /// there is no unlimited pass in this model, because "unlimited" would need a second gate shape
    /// and the club does not sell one.
    /// </summary>
    public int EntryCount { get; set; }

    /// <summary>When the admin issued it. Orders the member's pass history newest-first.</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token, guarding the entry pool.
    ///
    /// <para>
    /// A SECOND STAMP BESIDE <see cref="Scheduling.Class.ConcurrencyStamp"/>, rotated in the same
    /// <c>SaveChangesAsync</c>. The class's stamp serializes "at most Capacity rows against this
    /// class"; this one serializes "at most <see cref="EntryCount"/> active bookings against this
    /// pass". They are different pools — one member's last entry spent on two DIFFERENT classes races
    /// on this stamp and on nothing else.
    /// </para>
    ///
    /// <para>
    /// It must also be rotated when a booking is RELEASED. Freeing a class spot skips the class stamp
    /// because it cannot cause an overbooking; returning an entry is the opposite case — it makes a
    /// booking possible that was refused a moment ago, which is a state another writer races for.
    /// </para>
    /// </summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
}
