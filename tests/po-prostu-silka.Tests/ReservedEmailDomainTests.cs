using po_prostu_silka.Infrastructure.Notifications;

namespace po_prostu_silka.Tests;

/// <summary>
/// The test-data club's addresses must never reach ACS: they cannot arrive, and sending them spent
/// the quota until the throttle stalled delivery for real members too.
/// </summary>
public class ReservedEmailDomainTests
{
    [Theory]
    [InlineData("member1@example.test")]
    [InlineData("MEMBER@Example.Test")]
    [InlineData("a@club.example")]
    [InlineData("a@nowhere.invalid")]
    [InlineData("a@localhost")]
    [InlineData("a@example.com")]
    [InlineData("a@mail.example.org")]
    [InlineData("a@example.test.")]
    public void Reserved_addresses_are_recognised(string address) =>
        Assert.True(AcsEmailSender.IsReservedDomain(address));

    [Theory]
    [InlineData("karol@gmail.com")]
    [InlineData("a@contest.pl")]
    [InlineData("a@myexample.com")]
    [InlineData("a@example.com.pl")]
    [InlineData("not-an-address")]
    public void Real_addresses_are_not(string address) =>
        Assert.False(AcsEmailSender.IsReservedDomain(address));
}
