using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The write side of a group's fixed roster (S-37), and the narrow reads its writes decide on.
/// </summary>
public interface IGroupRosterStore
{
    void Add(GroupRosterEntry entry);

    void Remove(GroupRosterEntry entry);

    /// <summary>Tracked, because the removal path deletes what this returns.</summary>
    Task<GroupRosterEntry?> FindAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>The roster's member ids, in the order they joined.</summary>
    Task<IReadOnlyList<Guid>> MemberIdsAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>Every group whose roster holds this member - the karnet hook's starting point.</summary>
    Task<IReadOnlyList<Guid>> GroupIdsForMemberAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>
    /// The group's classes still to come: <see cref="ClassStatus.Scheduled"/> and starting after
    /// <paramref name="asOf"/>, ordered by start. "Upcoming" means exactly this everywhere in S-37.
    /// </summary>
    Task<IReadOnlyList<RosterClass>> UpcomingClassesAsync(
        Guid groupId, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="UpcomingClassesAsync"/> narrowed to classes whose CLUB-LOCAL date lies in
    /// [<paramref name="from"/>, <paramref name="to"/>], both inclusive - the karnet gate's own dating,
    /// so a 21:00 class on a karnet's last day is inside it.
    /// </summary>
    Task<IReadOnlyList<RosterClass>> UpcomingClassesInRangeAsync(
        Guid groupId, DateOnly from, DateOnly to, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>Whether this member instructs at least one upcoming class of the group.</summary>
    Task<bool> IsInstructorOfUpcomingAsync(
        Guid groupId, Guid memberId, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>
    /// TRACKED: the member's active, non-makeup bookings on the group's upcoming classes - what leaving
    /// the roster releases. Narrowed to classes <paramref name="instructorMemberId"/> instructs when it
    /// is set (a trainer's removal, S-16).
    /// </summary>
    Task<IReadOnlyList<Booking>> ReleasableBookingsAsync(
        Guid groupId,
        Guid memberId,
        DateTimeOffset asOf,
        Guid? instructorMemberId,
        CancellationToken cancellationToken);
}

/// <summary>An upcoming class of a group, as the roster batch needs it.</summary>
public record RosterClass(Guid Id, DateTimeOffset StartsAt, Guid InstructorMemberId);
