using System.Globalization;
using System.Security.Claims;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The member's own past classes, a page of three months at a time (S-27, AT-04).
/// </summary>
public static class GetMyAttendanceHistory
{
    /// <summary>How many club-local calendar months one page covers.</summary>
    public const int MonthsPerPage = 3;

    /// <summary>
    /// One page of the caller's history.
    ///
    /// <para>
    /// THE WINDOW IS CLUB-LOCAL CALENDAR MONTHS, computed through <see cref="ClubTime"/> and never in
    /// UTC: a class at 23:30 UTC on the last day of a month is the next month at the gym, and the
    /// member groups their history by the gym's calendar. Without <paramref name="before"/> the page
    /// ends now and covers the current month and the two before it; with it, the page ends at that
    /// club-local date (exclusive, and it must be the first day of a month) and covers the three
    /// months before it.
    /// </para>
    ///
    /// <para>
    /// THE MEMBER IS THE PRINCIPAL, never a parameter, like <c>/mine</c>: there is no id here to
    /// tamper with, so nobody reads anyone else's history (AT-04, PRD privacy NFR).
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        string? before,
        ClaimsPrincipal principal,
        IBookingQuery query,
        IMembershipPassQuery passes,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(ClubTime.ToClubLocal(now).DateTime);

        DateOnly lastMonth;
        DateTimeOffset to;

        if (before is null)
        {
            lastMonth = new DateOnly(today.Year, today.Month, 1);
            to = now.AddTicks(1);
        }
        else
        {
            if (!DateOnly.TryParseExact(
                    before, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                || parsed.Day != 1)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["before"] = ["Expected the first day of a month as YYYY-MM-DD."],
                });
            }

            lastMonth = parsed.AddMonths(-1);
            to = ClubTime.StartOfLocalDay(parsed);
        }

        var firstMonth = lastMonth.AddMonths(-(MonthsPerPage - 1));
        var from = ClubTime.StartOfLocalDay(firstMonth);

        var items = await query.GetHistoryForMemberAsync(memberId.Value, from, to, now, cancellationToken);

        // The next page ends just after the club-local month of the newest older class, not simply at
        // this page's start: a member back from a break would otherwise page through empty windows,
        // pressing "Pokaż wcześniejsze" and seeing nothing arrive.
        DateOnly? earlierBefore = null;
        if (await query.LatestHistoryStartBeforeAsync(memberId.Value, from, cancellationToken) is { } latest)
        {
            var local = ClubTime.ToClubLocal(latest);
            earlierBefore = new DateOnly(local.Year, local.Month, 1).AddMonths(1);
        }

        MyAttendanceSummary? summary = null;
        if (before is null
            && await passes.FindCoveringAsync(memberId.Value, today, cancellationToken) is { } pass)
        {
            var counts = await query.CountAttendanceForPassAsync(pass.Id, now, cancellationToken);
            summary = new MyAttendanceSummary(
                pass.TypeName,
                pass.ValidFrom,
                pass.ValidTo,
                pass.EntryCount,
                counts.Present,
                counts.Absent,
                counts.Unrecorded);
        }

        return Results.Ok(new MyAttendanceHistory(summary, items, earlierBefore));
    }
}
