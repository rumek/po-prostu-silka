namespace po_prostu_silka.Domain.Scheduling;

/// <summary>
/// One member of a group's fixed roster ("stały skład", S-37) - entered once, and booked into the
/// group's upcoming classes automatically.
///
/// <para>
/// A ROW SAYS "BELONGS", NOTHING MORE. It holds no bookings and no counters: what the member is booked
/// into is read from <see cref="Booking"/> rows, and what they are missing is derived on read. That is
/// what lets the roster screen stay current without anything to keep in step with the bookings.
/// </para>
///
/// <para>
/// THE ROSTER IS NOT A BOOKING PATH OF ITS OWN. Every booking it makes goes through the same
/// no-overbooking protocol as a staff booking, one transaction each, with the same karnet gate - so a
/// roster member without a karnet stays in the roster and is reported, never booked.
/// </para>
/// </summary>
public class GroupRosterEntry
{
    public Guid Id { get; set; }

    public Guid ClassGroupId { get; set; }

    public ClassGroup ClassGroup { get; set; } = null!;

    public Guid MemberId { get; set; }

    public Members.Member? Member { get; set; }

    /// <summary>When the member joined the roster; orders the roster screen.</summary>
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>
    /// The Identity user id of the staff account that added them. No FK, for the reason
    /// <see cref="Booking.AttendanceRecordedBy"/> has none: an attribution must outlive the account.
    /// </summary>
    public string? AddedBy { get; set; }
}
