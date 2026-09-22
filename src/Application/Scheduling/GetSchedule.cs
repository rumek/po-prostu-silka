using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The schedule over a bounded window (FR-007), a STAFF read since S-25.
///
/// <para>
/// One endpoint, two answers, picked from the principal the way
/// <see cref="BookingAuthorization.MayActOn"/> picks: an admin reads every class, anyone else who
/// passed the group's TrainerOrAdmin policy reads only the classes they instruct. A member never gets
/// here — the policy refuses them.
/// </para>
/// </summary>
public static class GetSchedule
{
    /// <summary>
    /// The schedule for a window, time-ordered and flat.
    ///
    /// FLAT, deliberately — the SPA groups by the BROWSER's local date. Grouping here would mean the
    /// server picking a timezone, and this stack has been UTC-in / local-render throughout. The
    /// window boundaries arrive already converted to UTC instants for the same reason: the calendar
    /// computes "this week" in the member's own clock, and only the instants cross the wire.
    ///
    /// <para>
    /// Both parameters are optional and move together. Omitted, this answers exactly what it
    /// answered before S-07 — the next <see cref="ClassRangeResolver.ScheduleWindowDays"/> days — which is what keeps
    /// the change additive. Supplied, a <paramref name="from"/> in the PAST is legitimate: the
    /// calendar's backward navigation is the whole reason this parameter exists (prd-v2 FR-015).
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        IClassScheduleQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var now = timeProvider.GetUtcNow();

        var (rangeFailure, resolved) = ClassRangeResolver.ResolveRange(
            from, to, now, now.AddDays(ClassRangeResolver.ScheduleWindowDays));
        if (rangeFailure is not null)
        {
            return rangeFailure;
        }

        if (principal.IsInRole(ApplicationRoles.Admin))
        {
            return Results.Ok(await query.GetScheduleAsync(
                resolved.From, resolved.To!.Value, cancellationToken));
        }

        // A missing member id is an EMPTY list, never "all". The policy's status claims come from the
        // member row, so the case is unreachable - which is exactly why it must fail closed.
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Ok(Array.Empty<ScheduledClass>());
        }

        return Results.Ok(await query.GetForInstructorAsync(
            memberId.Value, resolved.From, resolved.To!.Value, cancellationToken));
    }
}
