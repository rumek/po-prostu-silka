namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// What an automatic roster booking did (S-37): how many bookings it made, and every one it could not.
///
/// <para>
/// NOT PERSISTED. The roster screen derives the same gaps on read, so a report only has to describe
/// the action that produced it; once dismissed, nothing is lost.
/// </para>
/// </summary>
public record RosterReport(int Booked, IReadOnlyList<RosterSkip> Skipped)
{
    public static readonly RosterReport Empty = new(0, []);

    public RosterReport Plus(RosterReport other) =>
        other.Booked == 0 && other.Skipped.Count == 0
            ? this
            : new RosterReport(Booked + other.Booked, [.. Skipped, .. other.Skipped]);
}

/// <summary>
/// One booking that was not made. <paramref name="Reason"/> is a <see cref="BookingFailure"/> reason
/// (or <c>not_your_class</c>), so the SPA reads it through the booking failure table.
/// </summary>
public record RosterSkip(Guid MemberId, string MemberName, Guid ClassId, DateTimeOffset StartsAt, string Reason);
