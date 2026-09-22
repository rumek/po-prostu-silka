using System.Security.Claims;
using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// "Twoje zajęcia" — the classes the caller instructs over a window (S-25).
///
/// <para>
/// The staff dashboard's feed, for trainers and admins ALIKE: unlike the schedule, an admin is NOT
/// widened to every class here. The question is "which classes do I teach", so an Admin+Trainer gets
/// their own classes and an admin who teaches nothing gets an empty list. The club-wide view is the
/// schedule, one tap away.
/// </para>
/// </summary>
public static class GetInstructedClasses
{
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

        // Fails closed, as in GetSchedule: no member id, no classes.
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Ok(Array.Empty<ScheduledClass>());
        }

        return Results.Ok(await query.GetForInstructorAsync(
            memberId.Value, resolved.From, resolved.To!.Value, cancellationToken));
    }
}
