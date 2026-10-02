using po_prostu_silka.Application.Paging;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The read side of makeups (S-36). Every status it reports comes from
/// <c>MakeupRules.StateOf</c>, so the list, the member's count and the write paths agree.
/// </summary>
public interface IMakeupQuery
{
    /// <summary>
    /// The staff list, nearest deadline first. Open and planned items only, unless
    /// <paramref name="includeClosed"/> — then made-up and not-made-up items follow too.
    /// </summary>
    Task<PagedResult<MakeupItem>> GetItemsAsync(
        bool includeClosed, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One item, or null when the booking is not an "odrobi" absence of a non-staff member.</summary>
    Task<MakeupItem?> GetItemAsync(Guid absenceBookingId, CancellationToken cancellationToken);

    /// <summary>The member's open items: their count and the nearest deadline.</summary>
    Task<MyMakeups> GetForMemberAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>
    /// The classes a makeup for <paramref name="memberId"/> may go into: scheduled, not started, on or
    /// before <paramref name="deadline"/> (club-local), with a free spot, without the member, and on a
    /// day one of the member's karnets covers. Any instructor. Ordered by start.
    /// </summary>
    Task<IReadOnlyList<ScheduledClass>> GetEligibleClassesAsync(
        Guid memberId, DateOnly deadline, CancellationToken cancellationToken);
}
