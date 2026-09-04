using System.Globalization;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// How a moment is WRITTEN in a message a member reads (S-09).
///
/// <para>
/// Separate from <see cref="ClubTime"/> on purpose. That class answers where the club is and what an
/// instant reads as there — a scheduling fact, and the reason it can live in the Domain. Which
/// culture names the month and what order the parts come in are presentation decisions, and they
/// belong to the layer that renders the text. Collapsing the two would make the first render-shaped
/// code in the Domain layer, and the next three messages would follow it there.
/// </para>
/// </summary>
public static class MessageTime
{
    /// <summary>
    /// The culture every notification body is rendered in. Static readonly rather than a per-call
    /// lookup: GetCultureInfo caches, but a fan-out to a full class renders this once per recipient
    /// per channel and there is no reason to pay for the lookup each time.
    /// </summary>
    private static readonly CultureInfo PolishCulture = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>
    /// An instant as a member reads it in an email or on a lock screen. Example:
    /// "wtorek, 3 września 2026, 18:00".
    ///
    /// <para>
    /// The Polish culture is named explicitly rather than taken from CurrentCulture. This is a
    /// background-adjacent code path — a request thread today, the outbox worker's thread if a later
    /// change moves it — and the server's ambient culture is invariant on Linux App Service. Left to
    /// CurrentCulture the same message would render "Tuesday, 03 September" on one host and
    /// "wtorek, 3 września" on another, for an app whose every other string is Polish.
    /// </para>
    /// </summary>
    public static string ToClubWallClock(DateTimeOffset instant) =>
        ClubTime.ToClubLocal(instant).ToString("dddd, d MMMM yyyy, HH:mm", PolishCulture);
}
