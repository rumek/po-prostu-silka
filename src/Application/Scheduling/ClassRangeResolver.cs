using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Resolves and bounds the from/to window both schedule reads accept.
///
/// <para>
/// SHARED BY TWO HANDLERS, which is the whole reason it is extracted: the member schedule and
/// the admin list must bound the window identically, or one of them becomes a way to ask for
/// an unbounded range.
/// </para>
/// </summary>
public static class ClassRangeResolver
{
    /// <summary>
    /// How far ahead the member schedule reaches WHEN THE CALLER ASKS FOR NO RANGE. A fortnight is one
    /// round-trip of a few dozen rows for a single club, which is what keeps this inside the PRD's
    /// ~1 s perceived-response NFR.
    ///
    /// <para>
    /// Since S-07 this is the FALLBACK, not the only answer: the calendar asks for the window it is
    /// showing. Keeping the fallback exactly as it was is what makes the parameters additive — a
    /// client that sends nothing still gets the fortnight it got before.
    /// </para>
    /// </summary>
    public const int ScheduleWindowDays = 14;

    /// <summary>
    /// The widest window either read endpoint will answer for (prd-v2 FR-015, FR-016).
    ///
    /// <para>
    /// Two months. Comfortably above anything the calendar asks for — it requests one day or one week
    /// — and low enough that a malformed or hostile client cannot ask for a decade of rows in one
    /// round trip. The admin list was UNBOUNDED before this slice, so this is a tightening — and
    /// since 2026-09-03 it binds the admin's no-parameter fallback too: once the calendar shipped,
    /// every caller sends a window, so the unbounded path had no client left and was pure surface.
    /// </para>
    /// </summary>
    public const int MaxRangeDays = 62;

    public static IResult InvalidRange() =>
        Results.Json(new ClassFailure("invalid_range"), statusCode: 400);

    /// <summary>
    /// Validates an optional [from, to) window and falls back to the endpoint's own default when it is
    /// absent.
    ///
    /// <para>
    /// PARTIAL RANGES ARE REFUSED rather than half-honoured. A caller sending only <c>from</c> has a
    /// bug, and silently pairing it with the default <c>to</c> would answer a window nobody asked for
    /// — the fortnight from an arbitrary date, or everything to the end of time. Both parameters or
    /// neither.
    /// </para>
    /// </summary>
    /// <param name="defaultTo">
    /// The upper bound when no range is supplied — null for the admin path, which is deliberately
    /// unbounded without one.
    /// </param>
    /// <returns>
    /// <c>Failure</c> set and the range meaningless when refused; the reverse when accepted. The
    /// accepted <c>To</c> is null only on the unbounded admin default.
    /// </returns>
    public static (IResult? Failure, (DateTimeOffset From, DateTimeOffset? To) Range) ResolveRange(
        DateTimeOffset? from,
        DateTimeOffset? to,
        DateTimeOffset defaultFrom,
        DateTimeOffset? defaultTo)
    {
        if (from is null && to is null)
        {
            return (null, (defaultFrom, defaultTo));
        }

        if (from is null || to is null)
        {
            return (ClassRangeResolver.InvalidRange(), default);
        }

        // An empty or inverted window is a client bug, not an empty schedule: answering it with [] would
        // render as "no classes this week" and hide the fault.
        if (to.Value <= from.Value)
        {
            return (ClassRangeResolver.InvalidRange(), default);
        }

        if ((to.Value - from.Value).TotalDays > MaxRangeDays)
        {
            return (ClassRangeResolver.InvalidRange(), default);
        }

        return (null, (from.Value, to.Value));
    }
}
