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

    /// <summary>
    /// THE ONE DEFINITION of a makeup item's state. Every read (the staff list, the member's count) and
    /// every write that needs an open item (booking, closing) goes through it. First match wins:
    /// <list type="number">
    /// <item>closed by hand → not made up;</item>
    /// <item>a live makeup (active, on a class not cancelled): present → made up, forfeited → not made
    /// up, otherwise → planned;</item>
    /// <item>today past the deadline → not made up;</item>
    /// <item>otherwise → open.</item>
    /// </list>
    ///
    /// <para>
    /// A PLANNED MAKEUP OUTLIVES THE DEADLINE. The deadline bounds the makeup class's date, which the
    /// booking already checked; once booked in time, the item waits for its mark. Do not move rule 3
    /// above rule 2.
    /// </para>
    /// </summary>
    public static MakeupState StateOf(
        bool closedByHand,
        bool hasLiveMakeup,
        BookingAttendance? makeupAttendance,
        DateOnly deadline,
        DateOnly today)
    {
        if (closedByHand)
        {
            return MakeupState.NotMadeUp;
        }

        if (hasLiveMakeup)
        {
            return makeupAttendance switch
            {
                BookingAttendance.Present => MakeupState.MadeUp,
                BookingAttendance.Forfeited => MakeupState.NotMadeUp,
                _ => MakeupState.Planned,
            };
        }

        return today > deadline ? MakeupState.NotMadeUp : MakeupState.Open;
    }

    /// <summary>The wire spelling of a state, the one the SPA's union names.</summary>
    public static string WireName(MakeupState state) => state switch
    {
        MakeupState.Open => "open",
        MakeupState.Planned => "planned",
        MakeupState.MadeUp => "made_up",
        _ => "not_made_up",
    };
}

/// <summary>Where one makeup item stands (S-36). Derived on read — never stored.</summary>
public enum MakeupState
{
    /// <summary>"Do odrobienia": nothing booked, deadline not passed, not closed.</summary>
    Open,

    /// <summary>"Zaplanowane": a makeup is booked and not yet marked.</summary>
    Planned,

    /// <summary>"Odrobił": the makeup was marked present.</summary>
    MadeUp,

    /// <summary>"Nie odrobił": forfeited at the makeup, past the deadline, or closed by hand.</summary>
    NotMadeUp,
}
