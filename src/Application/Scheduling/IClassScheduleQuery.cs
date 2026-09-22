using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Narrow read seam over the class table, so Application does not reference EF Core (AGENTS.md
/// layering). Implemented in Infrastructure.
/// </summary>
public interface IClassScheduleQuery
{
    /// <summary>Scheduled classes starting within [from, to), time-ordered. The member's window.</summary>
    Task<IReadOnlyList<ScheduledClass>> GetScheduleAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>
    /// Scheduled classes starting within [from, to) that <paramref name="instructorMemberId"/>
    /// instructs, time-ordered (S-25). A trainer's schedule and every staff member's dashboard feed.
    /// The predicate is <see cref="BookingAuthorization.MayActOn"/>'s ownership half as SQL.
    /// </summary>
    Task<IReadOnlyList<ScheduledClass>> GetForInstructorAsync(
        Guid instructorMemberId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>
    /// The admin's list for the window [<paramref name="from"/>, <paramref name="to"/>).
    ///
    /// <para>
    /// Same shape as <see cref="GetScheduleAsync"/> and deliberately so: the bound is not optional.
    /// It was, briefly — the endpoint's fallback used to be unbounded — and nothing asks for that any
    /// more.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ScheduledClass>> GetUpcomingForAdminAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
