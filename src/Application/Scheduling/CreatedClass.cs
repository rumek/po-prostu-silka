namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The answer to creating a class (S-37): the class as every other route returns it, plus what booking
/// the group's fixed roster into it did. Derived rather than a wrapper, so the existing fields stay at the
/// top level and a reader that ignores <see cref="Roster"/> sees no change.
/// </summary>
public sealed record CreatedClass : ScheduledClass
{
    public CreatedClass(ScheduledClass dto, RosterReport roster)
        : base(dto)
    {
        Roster = roster;
    }

    public RosterReport Roster { get; init; }
}
