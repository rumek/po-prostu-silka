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
    // RequireAuthorization() rather than the ActiveMember policy — under ActiveMember, the one member
    // who most needs a push subscription would be the one who could not create one.
    //
    // Reachable since S-01 (D1) gave pending members a session.
    [Fact]
    public async Task Pending_member_can_also_subscribe()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.PendingMemberEmail);
        var endpoint = NewEndpoint();

        var response = await client.PostAsJsonAsync("/api/push/subscribe", Subscription(endpoint));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = NewContext();
        Assert.True(await db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint));
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

    /// <summary>
    /// A DEVICE BELONGS TO A LOGIN, NOT TO A MEMBER, and S-14 deliberately left it that way while it
    /// moved every other club-shaped foreign key onto <c>Member</c>. Push is the one relationship
    /// that genuinely keys on the account: an accountless member has no browser to send to, which is
    /// why <c>ClassChangeNotification</c> skips them rather than looking up subscriptions that cannot
    /// exist.
    ///
    /// <para>
    /// This is the regression pin the plan asked for and the slice never wrote. The invariant is
    /// invisible — nothing fails today if somebody "tidies" the column onto <c>MemberId</c> for
    /// consistency with its neighbours — so it is asserted here rather than left to the schema.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_subscription_is_stored_against_the_account_not_the_member()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);
        var endpoint = NewEndpoint();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/push/subscribe", Subscription(endpoint))).StatusCode);

        await using var db = NewContext();

        var stored = await db.PushSubscriptions
            .AsNoTracking()
            .SingleAsync(s => s.Endpoint == endpoint);

        // The account id, and an account id it is: it resolves through Identity.
        var account = await db.Users.AsNoTracking().SingleAsync(u => u.Id == stored.UserId);
        Assert.Equal(TestUsers.ActiveMemberEmail, account.Email);

        // And NOT the member id, which is the value a well-meaning refactor would put here. Both are
        // strings holding a Guid — Identity's default id generator is Guid.NewGuid().ToString() — so
        // the shape proves nothing and only the identity does: this member HAS a member id, it is a
        // different one, and the column holds the account's.
        var memberId = await fixture.MemberIdOfAsync(stored.UserId);
        Assert.NotEqual(memberId.ToString(), stored.UserId);
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
