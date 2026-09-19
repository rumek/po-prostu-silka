using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The read counterpart. AsNoTracking projections for display, mirroring
/// <see cref="IClassScheduleQuery"/>; nothing here is ever fed back into a write.
/// </summary>
public interface IBookingQuery
{
    /// <summary>
    /// The member's active bookings on SCHEDULED classes starting at or after
    /// <paramref name="from"/>, ordered by the class's start.
    ///
    /// <para>
    /// BOTH STATUSES MATTER SINCE S-09. A cancelled class keeps its bookings Active on purpose, so
    /// the class's own status is the only thing that takes it off this list — see the implementation
    /// for why that predicate is load-bearing rather than defensive.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<MyBooking>> GetUpcomingForMemberAsync(
        Guid memberId, DateTimeOffset from, CancellationToken cancellationToken);

    /// <summary>
    /// Everyone actively signed up for a class, in the order they booked — so the club can see who
    /// was first.
    ///
    /// <para>
    /// TWO CALLERS SINCE S-09, and the second one is not a screen. The admin's "Zapisani" panel
    /// displays this; <see cref="ClassEndpoints"/>' cancel and edit paths use the SAME projection as
    /// the fan-out list for the email and push owed to everyone holding a spot. That is deliberate
    /// rather than convenient: a member the club can see on the list and a member the club must tell
    /// are by definition the same set, and a second query would let the two drift.
    /// </para>
    ///
    /// <para>
    /// ACTIVE ONLY, which is what makes it right for the fan-out: somebody who released their spot
    /// is owed nothing.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ClassBooking>> GetForClassAsync(
        Guid classId, CancellationToken cancellationToken);
}
