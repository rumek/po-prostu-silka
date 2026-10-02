namespace po_prostu_silka.Domain.Scheduling;

/// <summary>
/// The club's makeup rule (S-36, from the club's attendance spreadsheet): an absence marked
/// <see cref="BookingAttendance.Makeup"/> may be made up by one class taking place within
/// <see cref="DeadlineDays"/> club-local days of the absence.
///
/// <para>
/// THE DEADLINE GOVERNS THE MAKEUP CLASS'S DATE, not the day it is booked: a class on the deadline day
/// qualifies, one the day after does not. A constant rather than a setting, like the five-day window of
/// "Kończą się karnety".
/// </para>
/// </summary>
public static class MakeupRules
{
    public const int DeadlineDays = 30;

    /// <summary>The last club-local date a makeup class may take place on. Inclusive.</summary>
    public static DateOnly DeadlineFor(DateOnly absenceDate) => absenceDate.AddDays(DeadlineDays);
}
