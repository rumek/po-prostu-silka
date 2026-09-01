namespace po_prostu_silka.Domain.Scheduling;

/// <summary>
/// The club's wall clock.
///
/// <para>
/// This is the ONLY place a timezone is named in the application, and it exists for exactly one
/// reason: weekly duplication (FR-012) must preserve LOCAL clock time, not the UTC instant. A
/// timetable entry means "Tuesday at 18:00" - the members who show up read a wall clock. Adding
/// seven days to a UTC DateTimeOffset preserves the instant instead, so a class duplicated across
/// the October DST transition silently moves an hour earlier. Nothing fails; the class just happens
/// at the wrong time.
/// </para>
///
/// <para>
/// It is deliberately NOT used on any read path. The schedule endpoint still returns UTC instants
/// and the SPA still groups days by the browser's local date - that keeps a member who opens the app
/// abroad seeing times in their own clock, which is what every other timestamp in this app already
/// does. Only the duplicate arithmetic needs to know where the club is.
/// </para>
///
/// <para>
/// Single-club by design (PRD Non-Goals: "no multi-tenancy, no per-club admins"). If that ever
/// changes, this becomes a property of the club, not a constant - and that is the moment to move it.
/// </para>
/// </summary>
public static class ClubTime
{
    /// <summary>
    /// IANA id. Resolved via TimeZoneInfo.FindSystemTimeZoneById, which accepts IANA ids on Windows
    /// and Linux alike since .NET 6 - so this works on the dev machine and on Linux App Service
    /// without a per-platform lookup table.
    /// </summary>
    public const string TimeZoneId = "Europe/Warsaw";

    public static TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>
    /// Adds whole days in the club's local time, so the wall-clock time survives a DST transition.
    ///
    /// Converts to club-local, adds the days there, then converts back to UTC. The two conversions
    /// are the whole point: doing the arithmetic on the UTC instant is what loses the hour.
    /// </summary>
    public static DateTimeOffset AddLocalDays(DateTimeOffset instant, int days)
    {
        var zone = Zone;
        var local = TimeZoneInfo.ConvertTime(instant, zone);

        // DateTime, not DateTimeOffset: the offset itself is what must be recomputed on the far side
        // of a transition, so we carry only the wall-clock reading across and let the zone re-derive
        // the offset for that date.
        var shifted = local.DateTime.AddDays(days);

        // A wall-clock time can be invalid (the spring-forward gap) or ambiguous (the autumn
        // repeat). TimeZoneInfo would throw on the first and silently pick one on the second, so
        // both are handled explicitly rather than left to chance.
        if (zone.IsInvalidTime(shifted))
        {
            // That local time does not exist on that date. Step forward past the gap; an hour is the
            // transition size everywhere this app will run.
            shifted = shifted.AddHours(1);
        }

        var offset = zone.IsAmbiguousTime(shifted)
            // Repeated hour: take the FIRST occurrence (still on the pre-transition offset), which is
            // the one a member reading a timetable would arrive for.
            ? zone.GetAmbiguousTimeOffsets(shifted)[0]
            : zone.GetUtcOffset(shifted);

        return new DateTimeOffset(shifted, offset).ToUniversalTime();
    }
}
