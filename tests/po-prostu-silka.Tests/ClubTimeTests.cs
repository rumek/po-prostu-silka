using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Tests;

/// <summary>
/// The wall clock a member reads in an email (S-09 phase 1; prd.md FR-021) — ClubTime's zone
/// arithmetic and MessageTime's rendering of it, which is the layer split those two exist to keep.
///
/// <para>
/// EXPECTED STRINGS ARE LITERAL HERE, deliberately. The obvious way to assert this — comparing a
/// message body against <c>MessageTime.ToClubWallClock(startsAt)</c> — calls the function under test to
/// compute the expectation, so a wrong zone, a wrong offset or a wrong culture passes. These are the
/// readings a Polish member of this club would actually see, written out by hand.
/// </para>
///
/// <para>
/// No fixture and no collection: two pure functions, so neither a database nor a container.
/// </para>
/// </summary>
public class ClubTimeTests
{
    /// <summary>
    /// June is CEST, UTC+2. An email saying 16:00 sends the member to the gym two hours early.
    /// </summary>
    [Fact]
    public void Summer_instants_read_two_hours_ahead_of_utc()
    {
        var instant = new DateTimeOffset(2026, 6, 16, 16, 0, 0, TimeSpan.Zero);

        Assert.Equal("wtorek, 16 czerwca 2026, 18:00", MessageTime.ToClubWallClock(instant));
    }

    /// <summary>
    /// January is CET, UTC+1 — the same UTC hour reads an hour earlier than it does in June. A
    /// renderer with the offset hardcoded would pass the summer case and fail this one.
    /// </summary>
    [Fact]
    public void Winter_instants_read_one_hour_ahead_of_utc()
    {
        var instant = new DateTimeOffset(2026, 1, 13, 16, 0, 0, TimeSpan.Zero);

        Assert.Equal("wtorek, 13 stycznia 2026, 17:00", MessageTime.ToClubWallClock(instant));
    }

    /// <summary>
    /// The culture is named explicitly rather than taken from CurrentCulture, so the month and the
    /// day name must be Polish on every host. Left to an invariant ambient culture this would render
    /// "Tuesday, 16 June".
    /// </summary>
    [Fact]
    public void The_reading_is_rendered_in_Polish_regardless_of_the_ambient_culture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        try
        {
            var instant = new DateTimeOffset(2026, 6, 16, 16, 0, 0, TimeSpan.Zero);

            Assert.Equal("wtorek, 16 czerwca 2026, 18:00", MessageTime.ToClubWallClock(instant));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    /// <summary>
    /// A week added across the October transition must keep the wall-clock hour, not the instant.
    /// This is the failure ClubTime was written for: 18:00 CEST is 16:00 UTC, and 18:00 CET a week
    /// later is 17:00 UTC — plain UTC arithmetic would leave the class at 17:00 local.
    /// </summary>
    [Fact]
    public void A_week_across_the_autumn_transition_keeps_the_wall_clock_hour()
    {
        // Sunday 25 October 2026 is the transition. This is the Tuesday before it, 18:00 local.
        var before = new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.Zero);

        var after = ClubTime.AddLocalDays(before, 7);

        Assert.Equal("wtorek, 27 października 2026, 18:00", MessageTime.ToClubWallClock(after));
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.Zero), after);
    }

    /// <summary>
    /// The same across the March transition, in the other direction: 18:00 CET is 17:00 UTC, and
    /// 18:00 CEST a week later is 16:00 UTC.
    /// </summary>
    [Fact]
    public void A_week_across_the_spring_transition_keeps_the_wall_clock_hour()
    {
        // Sunday 29 March 2026 is the transition. This is the Tuesday before it, 18:00 local.
        var before = new DateTimeOffset(2026, 3, 24, 17, 0, 0, TimeSpan.Zero);

        var after = ClubTime.AddLocalDays(before, 7);

        Assert.Equal("wtorek, 31 marca 2026, 18:00", MessageTime.ToClubWallClock(after));
        Assert.Equal(new DateTimeOffset(2026, 3, 31, 16, 0, 0, TimeSpan.Zero), after);
    }
}
