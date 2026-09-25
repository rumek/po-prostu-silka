using po_prostu_silka.Infrastructure.Notifications;

namespace po_prostu_silka.Tests;

/// <summary>
/// How long a throttled email waits. Both RFC 9110 forms, and the clamp that keeps a missing or
/// absurd header from retrying every pass or silencing mail for a day.
/// </summary>
public class AcsRetryAfterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("120", 120)]
    [InlineData(null, 60)]
    [InlineData("soon", 60)]
    [InlineData("0", 15)]
    [InlineData("86400", 3600)]
    [InlineData("Fri, 25 Sep 2026 12:05:00 GMT", 300)]
    [InlineData("Fri, 25 Sep 2026 11:00:00 GMT", 15)]
    public void Retry_after_is_read_and_clamped(string? header, int expectedSeconds) =>
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            AcsEmailSender.ParseRetryAfter(header, Now));
}
