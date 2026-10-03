namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The read side of rosters (S-37): the roster screen, and the trainer's list of groups.
/// </summary>
public interface IGroupRosterQuery
{
    /// <summary>
    /// The roster with each member's gaps, evaluated read-only as of <paramref name="asOf"/>. Null when
    /// the group does not exist.
    ///
    /// <para>
    /// <paramref name="actingInstructorId"/> is the trainer looking (null for an admin): a gap on a class
    /// they do not instruct says so (<see cref="RosterBooking.NotYourClass"/>) rather than "bookable",
    /// because their "Uzupełnij zapisy" will not fill it.
    /// </para>
    /// </summary>
    Task<GroupRosterView?> GetViewAsync(
        Guid groupId, DateTimeOffset asOf, Guid? actingInstructorId, CancellationToken cancellationToken);

    /// <summary>
    /// The groups this member instructs at least one upcoming class of - exactly the set
    /// <see cref="RosterAuthorization"/> lets them manage. Ordered by the next class.
    /// </summary>
    Task<IReadOnlyList<TrainerGroup>> GetTrainerGroupsAsync(
        Guid instructorMemberId, DateTimeOffset asOf, CancellationToken cancellationToken);
}

public record GroupRosterView(
    Guid GroupId,
    string Name,
    bool IsActive,
    int Capacity,
    int UpcomingClassCount,
    IReadOnlyList<GroupRosterMember> Members);

public record GroupRosterMember(
    Guid MemberId,
    string DisplayName,
    bool HasAccount,
    DateTimeOffset AddedAt,
    int BookedUpcoming,
    IReadOnlyList<RosterGap> Gaps);

/// <summary>
/// An upcoming class of the group the member holds no active booking on. <paramref name="Reason"/> is
/// what would refuse a booking NOW (a <see cref="BookingFailure"/> reason or
/// <see cref="RosterBooking.NotYourClass"/>), or <see cref="Bookable"/>.
///
/// <para>
/// Evaluated per class independently, so it describes the present, not a sequence: two gaps can both
/// say "bookable" against a karnet with one entry left. "Uzupełnij zapisy" is the authority.
/// </para>
/// </summary>
public record RosterGap(Guid ClassId, DateTimeOffset StartsAt, string Reason)
{
    public const string Bookable = "bookable";
}

public record TrainerGroup(Guid Id, string Name, int RosterCount, int Capacity, DateTimeOffset? NextClassAt);

/// <summary>The answer of every roster write that books: the refreshed roster, and what the booking did.</summary>
public record RosterChange(GroupRosterView Roster, RosterReport Report);
