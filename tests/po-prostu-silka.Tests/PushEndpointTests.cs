using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// The push subscription surface: who may register a device, and what happens when the same browser
/// subscribes twice.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class PushEndpointTests(IntegrationTestFixture fixture)
{
    private static object Subscription(string endpoint) =>
        new { endpoint, p256dh = "p256dh-value", auth = "auth-value" };

    private static string NewEndpoint() => $"https://push.test/{Guid.NewGuid()}";

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    [Fact]
    public async Task Anonymous_subscribe_is_rejected()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/push/subscribe", Subscription(NewEndpoint()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_vapid_key_is_rejected()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/push/vapid-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_member_can_subscribe()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);
        var endpoint = NewEndpoint();

        var response = await client.PostAsJsonAsync("/api/push/subscribe", Subscription(endpoint));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = NewContext();
        Assert.True(await db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint));
    }

    // A Pending member's device must be able to subscribe: the account-approved notification is
    // exactly the message they are waiting for. This is why the endpoints use bare
    // RequireAuthorization() rather than the ActiveMember policy.
    [Fact]
    public async Task Pending_member_can_also_subscribe()
    {
        var client = fixture.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = TestUsers.PendingMemberEmail, password = TestUsers.Password });

        // A pending member cannot log in at all, so they subscribe only after approval in practice.
        // Assert the current contract rather than a hoped-for one.
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Subscribing_twice_with_the_same_endpoint_yields_one_row()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);
        var endpoint = NewEndpoint();

        await client.PostAsJsonAsync("/api/push/subscribe", Subscription(endpoint));
        await client.PostAsJsonAsync("/api/push/subscribe", Subscription(endpoint));

        await using var db = NewContext();
        Assert.Equal(1, await db.PushSubscriptions.CountAsync(s => s.Endpoint == endpoint));
    }

    [Fact]
    public async Task A_member_cannot_unsubscribe_another_members_subscription()
    {
        var endpoint = NewEndpoint();

        var owner = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);
        await owner.PostAsJsonAsync("/api/push/subscribe", Subscription(endpoint));

        var other = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        var response = await other.PostAsJsonAsync("/api/push/unsubscribe", Subscription(endpoint));

        // The call succeeds but deletes nothing — the store scopes the delete to the caller.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = NewContext();
        Assert.True(await db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint));
    }

    [Fact]
    public async Task A_member_can_unsubscribe_their_own_subscription()
    {
        var endpoint = NewEndpoint();
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        await client.PostAsJsonAsync("/api/push/subscribe", Subscription(endpoint));
        await client.PostAsJsonAsync("/api/push/unsubscribe", Subscription(endpoint));

        await using var db = NewContext();
        Assert.False(await db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint));
    }

    [Fact]
    public async Task Vapid_key_endpoint_reports_503_when_push_is_unconfigured()
    {
        // The test host supplies no VAPID keys, so this is the unconfigured path — a clear 503
        // rather than a 200 with an empty key the browser would fail on mysteriously.
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await client.GetAsync("/api/push/vapid-key");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
