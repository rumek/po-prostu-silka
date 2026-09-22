using po_prostu_silka.Infrastructure.TestData;

namespace po_prostu_silka.Tests;

/// <summary>
/// The generator alone, without a database: it is pure, so what it promises about determinism can be
/// checked directly rather than through two seeds a few seconds apart.
/// </summary>
public class TestDataGeneratorTests
{
    [Fact]
    public void Same_club_local_day_produces_the_same_data_at_any_hour()
    {
        // 08:00 and 20:00 club-local (CEST) on the same day.
        var morning = TestDataGenerator.Generate(TestDataSeeder.Seed, new DateTimeOffset(2026, 9, 22, 6, 0, 0, TimeSpan.Zero));
        var evening = TestDataGenerator.Generate(TestDataSeeder.Seed, new DateTimeOffset(2026, 9, 22, 18, 0, 0, TimeSpan.Zero));

        Assert.Equal(morning.Bookings.Select(b => b.Id), evening.Bookings.Select(b => b.Id));
        Assert.Equal(morning.Exercises.Select(e => e.Id), evening.Exercises.Select(e => e.Id));
        Assert.Equal(morning.Plans.Select(p => p.Id), evening.Plans.Select(p => p.Id));
    }

    [Fact]
    public void No_pass_or_booking_predates_its_member()
    {
        var data = TestDataGenerator.Generate(TestDataSeeder.Seed, DateTimeOffset.UtcNow);
        var joined = data.Members.ToDictionary(m => m.Id, m => m.CreatedAt);

        Assert.All(data.Passes, p => Assert.True(p.IssuedAt >= joined[p.MemberId], "A pass predates its member."));
        Assert.All(data.Bookings, b => Assert.True(b.CreatedAt >= joined[b.MemberId], "A booking predates its member."));
    }
}
