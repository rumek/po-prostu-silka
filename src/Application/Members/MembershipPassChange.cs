using po_prostu_silka.Application.Scheduling;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// The answer to issuing or editing a karnet (S-37): the karnet as every other route returns it, plus
/// what booking its holder into their groups' classes did. Derived rather than a wrapper, so the existing
/// fields stay at the top level.
/// </summary>
public sealed record MembershipPassChange : MembershipPassView
{
    public MembershipPassChange(MembershipPassView view, RosterReport roster)
        : base(view)
    {
        Roster = roster;
    }

    public RosterReport Roster { get; init; }
}
