using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The admin occurrence list over a bounded window.
/// </summary>
public static class GetAdminClasses
{
    /// <summary>
    /// The admin's management list for a window.
    ///
    /// <para>
    /// Its no-parameter fallback still reaches further than the member's — <see cref="ClassRangeResolver.MaxRangeDays"/>
    /// rather than <see cref="ClassRangeResolver.ScheduleWindowDays"/>, because an admin setting up a term looks further
    /// ahead than a member browsing next week. It is no longer UNBOUNDED: that was preserved through
    /// this slice for compatibility, and once the calendar shipped every caller began sending a
    /// window, so the unbounded path had no client left.
    /// </para>
    ///
    /// <para>
    /// With a range, the admin gets the same treatment as the member INCLUDING the past, which is new:
    /// this list used to start at now and the past was simply unreachable. Read-only-ness of a past
    /// week is a client concern (the admin screen withholds its actions), not an authorization one —
    /// the write endpoints already refuse a create in the past on their own.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        IClassScheduleQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var now = timeProvider.GetUtcNow();
        var (rangeFailure, resolved) = ClassRangeResolver.ResolveRange(
            from, to, now, now.AddDays(ClassRangeResolver.MaxRangeDays));
        if (rangeFailure is not null)
        {
            return rangeFailure;
        }

        return Results.Ok(await query.GetUpcomingForAdminAsync(
            resolved.From, resolved.To!.Value, cancellationToken));
    }
}
