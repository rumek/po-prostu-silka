using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// The in-session password change (S-13). Two invariants carry this endpoint: the password actually
/// changes, and the caller is still signed in afterwards.
///
/// <para>
/// The second one is the one that breaks silently. ChangePasswordAsync rotates the security stamp,
/// which invalidates every cookie for the user — including the caller's — and the validator only
/// re-checks on an interval. Without RefreshSignInAsync in the handler the member is logged out
/// minutes after being told the change worked, and nothing in a build or a lint run notices.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class PasswordEndpointTests(IntegrationTestFixture fixture)
{
    private const string NewPassword = "NoweHaslo_456";

    private sealed record ChangePasswordFailureBody(string Reason);

    /// <summary>
    /// A fresh account per test. These tests CHANGE THE PASSWORD of the account they run against,
    /// and the seeded members are shared by the whole collection — reusing one would break every
    /// other test file that logs in as it.
    /// </summary>
    private async Task<string> CreateMemberAsync()
    {
        var email = $"password-{Guid.NewGuid():N}@test.local";

        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Anna Kowalska",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var created = await userManager.CreateAsync(user, TestUsers.Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, ApplicationRoles.User);

        return email;
    }

    private static object Change(string currentPassword, string newPassword) =>
        new { currentPassword, newPassword };

    private async Task<HttpStatusCode> LoginStatusAsync(string email, string password)
    {
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        return response.StatusCode;
    }

    [Fact]
    public async Task A_valid_change_replaces_the_password()
    {
        var email = await CreateMemberAsync();
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password", Change(TestUsers.Password, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(email, NewPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(email, TestUsers.Password));
    }

    /// <summary>
    /// THE REGRESSION TEST FOR RefreshSignInAsync. The stamp rotation kills every cookie for this
    /// user; the handler re-issues the caller's against the new stamp. If this ever fails, the
    /// refresh call was moved or removed — read the comment on ChangePasswordAsync before "fixing"
    /// the test.
    /// </summary>
    [Fact]
    public async Task The_acting_session_survives_the_change()
    {
        var email = await CreateMemberAsync();
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        await client.PostAsJsonAsync(
            "/api/auth/change-password", Change(TestUsers.Password, NewPassword));

        // The same client, so the same cookie. A stale stamp answers 401 here.
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task A_wrong_current_password_is_refused_and_changes_nothing()
    {
        var email = await CreateMemberAsync();
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password", Change("NieToHaslo_999", NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_current_password",
            (await response.Content.ReadFromJsonAsync<ChangePasswordFailureBody>())!.Reason);

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(email, TestUsers.Password));
    }

    /// <summary>
    /// The policy in Program.cs is length-only (8), so a short password is the one refusal it
    /// produces. Mapped to our own code rather than forwarding Identity's English description.
    /// </summary>
    [Fact]
    public async Task A_new_password_below_the_policy_length_is_refused()
    {
        var email = await CreateMemberAsync();
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password", Change(TestUsers.Password, "Krot1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_new_password",
            (await response.Content.ReadFromJsonAsync<ChangePasswordFailureBody>())!.Reason);
    }

    /// <summary>
    /// A JSON null reaches the handler despite the non-nullable record — the same compile-time-only
    /// contract /login and /register guard against. Without the guard ChangePasswordAsync throws and
    /// the member gets a 500 instead of a message on a field.
    /// </summary>
    [Theory]
    [InlineData(null, NewPassword, "invalid_current_password")]
    [InlineData(TestUsers.Password, null, "invalid_new_password")]
    public async Task Null_passwords_are_rejected_without_a_500(
        string? currentPassword, string? newPassword, string expectedReason)
    {
        var email = await CreateMemberAsync();
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password", new { currentPassword, newPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            expectedReason,
            (await response.Content.ReadFromJsonAsync<ChangePasswordFailureBody>())!.Reason);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password", Change(TestUsers.Password, NewPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Bare RequireAuthorization, not the ActiveMember policy: a member awaiting approval owns their
    /// password like anyone else, and nothing about changing it depends on being approved.
    /// </summary>
    [Fact]
    public async Task A_pending_member_can_change_their_password()
    {
        var email = $"password-pending-{Guid.NewGuid():N}@test.local";

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Anna Kowalska",
                Status = AccountStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            var created = await userManager.CreateAsync(user, TestUsers.Password);
            Assert.True(created.Succeeded);
            await userManager.AddToRoleAsync(user, ApplicationRoles.User);
        }

        var client = await fixture.CreateAuthenticatedClientAsync(email);

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password", Change(TestUsers.Password, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(email, NewPassword));
    }
    // ---------------------------------------------------------------------------
    // Forgot / reset (S-13, Phase 4).
    //
    // The invariant that carries this flow is NON-DISCLOSURE: /forgot-password must answer
    // identically whatever the address is, and /reset-password must not distinguish "no such
    // account" from "bad token". Several tests below exist only to pin that, and a change that makes
    // one of them fail is almost certainly reintroducing an account-enumeration oracle.
    // ---------------------------------------------------------------------------

    private sealed record ResetFailureBody(string Reason);

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(fixture.ConnectionString).Options);

    /// <summary>
    /// The reset emails queued for one address. Read from the outbox rather than from a fake sender:
    /// the endpoint ENQUEUES, and whether the worker later delivers is a different test's business.
    /// </summary>
    private async Task<List<OutboxMessage>> ResetMailFor(string email)
    {
        await using var db = NewContext();

        return await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Channel == NotificationChannel.Email && m.Recipient == email)
            .ToListAsync();
    }

    /// <summary>
    /// Pulls the token back out of the queued link, exactly as a member clicking it would. Asserting
    /// on a token generated separately would prove nothing about the link that was actually sent.
    /// </summary>
    private static string TokenFromBody(string body)
    {
        var start = body.IndexOf("token=", StringComparison.Ordinal);
        Assert.True(start >= 0, "No token in the reset body:\n" + body);

        var raw = body[(start + "token=".Length)..].Split('\n')[0].Trim();
        return Uri.UnescapeDataString(raw);
    }

    private static int forgotClients;

    /// <summary>
    /// A client with an X-Forwarded-For of its own, so it lands in its own rate-limiter partition.
    ///
    /// <para>
    /// WITHOUT THIS THE TESTS FIGHT THE LIMITER, NOT THE ENDPOINT. /forgot-password is capped at
    /// five requests a minute per client IP, every test in the collection shares one loopback
    /// address, and the file makes more than five requests in well under a minute - so the later
    /// ones answer 429 and prove nothing about the flow. Giving each client a distinct forwarded
    /// address exercises exactly the partition key Program.cs uses; the cap itself is asserted by
    /// <see cref="Requests_beyond_the_per_client_cap_are_refused_with_429"/>.
    /// </para>
    /// </summary>
    private HttpClient CreateForgotClient()
    {
        var client = fixture.CreateClient();

        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For", $"203.0.113.{Interlocked.Increment(ref forgotClients) % 250 + 1}");

        return client;
    }

    private static async Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email) =>
        await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

    [Fact]
    public async Task A_registered_address_is_queued_one_reset_email_with_a_usable_link()
    {
        var email = await CreateMemberAsync();
        var client = CreateForgotClient();

        var response = await ForgotAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mail = Assert.Single(await ResetMailFor(email));

        // The link's origin comes from App:BaseUrl, NOT from the request's Host header - the outbox
        // renders at enqueue and a worker retry has no request, and a Host header is attacker
        // controlled. See AppOptions.
        Assert.Contains(
            IntegrationTestFixture.TestAppBaseUrl + "/reset-password?email=", mail.Body);
        Assert.Contains("token=", mail.Body);

        // Email only. A reset must reach the mailbox being recovered, never a push subscription on
        // a device that may not belong to the person asking.
        await using var db = NewContext();
        Assert.Empty(await db.OutboxMessages
            .Where(m => m.Channel == NotificationChannel.Push && m.Recipient == email)
            .ToListAsync());
    }

    /// <summary>
    /// THE NON-DISCLOSURE TEST. An unregistered address must produce a byte-identical answer and
    /// leave nothing behind. If this fails, an anonymous caller can enumerate which addresses belong
    /// to members - read the comment on ForgotPasswordAsync before changing the endpoint.
    /// </summary>
    [Fact]
    public async Task An_unregistered_address_answers_identically_and_queues_nothing()
    {
        var registered = await CreateMemberAsync();
        var unknown = $"nobody-{Guid.NewGuid():N}@test.local";
        var client = CreateForgotClient();

        var knownResponse = await ForgotAsync(client, registered);
        var unknownResponse = await ForgotAsync(client, unknown);

        Assert.Equal(knownResponse.StatusCode, unknownResponse.StatusCode);
        Assert.Equal(
            await knownResponse.Content.ReadAsStringAsync(),
            await unknownResponse.Content.ReadAsStringAsync());

        Assert.Empty(await ResetMailFor(unknown));
    }

    /// <summary>
    /// Blocked is treated exactly like Active. Branching on status would reintroduce the enumeration
    /// oracle from the other direction, and a blocked account is refused at /login regardless - a new
    /// password gets them nothing.
    /// </summary>
    [Fact]
    public async Task A_blocked_account_is_treated_the_same_as_an_active_one()
    {
        var email = $"password-blocked-{Guid.NewGuid():N}@test.local";

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Anna Kowalska",
                Status = AccountStatus.Blocked,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            Assert.True((await userManager.CreateAsync(user, TestUsers.Password)).Succeeded);
            await userManager.AddToRoleAsync(user, ApplicationRoles.User);
        }

        var response = await ForgotAsync(CreateForgotClient(), email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await ResetMailFor(email));
    }

    [Fact]
    public async Task The_emailed_token_sets_a_new_password()
    {
        var email = await CreateMemberAsync();
        var client = CreateForgotClient();

        await ForgotAsync(client, email);
        var token = TokenFromBody(Assert.Single(await ResetMailFor(email)).Body);

        var response = await client.PostAsJsonAsync(
            "/api/auth/reset-password", new { email, token, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Deliberately NOT signed in by the reset: the member is sent to /login, which is what
        // proves the new password works.
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.Contains("Identity.Application")));

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(email, NewPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(email, TestUsers.Password));
    }

    /// <summary>
    /// Single use comes from the security stamp, which ResetPasswordAsync rotates - the token was
    /// minted against the old one, so a replay fails validation. Nothing marks the token as used.
    /// </summary>
    [Fact]
    public async Task The_same_token_cannot_be_used_twice()
    {
        var email = await CreateMemberAsync();
        var client = CreateForgotClient();

        await ForgotAsync(client, email);
        var token = TokenFromBody(Assert.Single(await ResetMailFor(email)).Body);

        var first = await client.PostAsJsonAsync(
            "/api/auth/reset-password", new { email, token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "/api/auth/reset-password", new { email, token, newPassword = "JeszczeInne_789" });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(
            "invalid_token",
            (await second.Content.ReadFromJsonAsync<ResetFailureBody>())!.Reason);

        // The second attempt changed nothing.
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(email, NewPassword));
    }

    /// <summary>
    /// One code for every way a link can be dead, including an address that does not exist. Splitting
    /// them apart would tell an anonymous caller which addresses are registered.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_bad_token_and_an_unknown_address_answer_the_same(bool unknownAddress)
    {
        var email = unknownAddress
            ? $"nobody-{Guid.NewGuid():N}@test.local"
            : await CreateMemberAsync();

        var response = await CreateForgotClient().PostAsJsonAsync(
            "/api/auth/reset-password",
            new { email, token = "to-nie-jest-token", newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_token",
            (await response.Content.ReadFromJsonAsync<ResetFailureBody>())!.Reason);
    }

    /// <summary>
    /// The password policy is the one thing a member CAN act on, so it gets its own code even though
    /// everything else collapses into invalid_token.
    /// </summary>
    [Fact]
    public async Task A_new_password_below_the_policy_length_is_refused_on_reset()
    {
        var email = await CreateMemberAsync();
        var client = CreateForgotClient();

        await ForgotAsync(client, email);
        var token = TokenFromBody(Assert.Single(await ResetMailFor(email)).Body);

        var response = await client.PostAsJsonAsync(
            "/api/auth/reset-password", new { email, token, newPassword = "Krot1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_new_password",
            (await response.Content.ReadFromJsonAsync<ResetFailureBody>())!.Reason);

        // Refused, so the old password still works.
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(email, TestUsers.Password));
    }

    /// <summary>
    /// The per-address throttle collapses impatient repeats into ONE email - and, critically, does
    /// not change the response. A throttled request that answered differently would be the same
    /// oracle by another route.
    /// </summary>
    [Fact]
    public async Task Repeated_requests_for_one_address_send_once_and_answer_identically()
    {
        var email = await CreateMemberAsync();
        var client = CreateForgotClient();

        var first = await ForgotAsync(client, email);
        var second = await ForgotAsync(client, email);
        var third = await ForgotAsync(client, email);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(first.StatusCode, third.StatusCode);

        Assert.Single(await ResetMailFor(email));
    }

    /// <summary>
    /// A null email is a JSON null reaching a non-nullable record, the same compile-time-only gap the
    /// other handlers guard. It must answer 200 like everything else here, not 500 and not 400 -
    /// a distinguishable answer for ANY input is a disclosure.
    /// </summary>
    [Fact]
    public async Task A_null_email_answers_200_like_everything_else()
    {
        var response = await CreateForgotClient().PostAsJsonAsync(
            "/api/auth/forgot-password", new { email = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The cap itself. Five in a minute is generous for a member who mistypes their address; the
    /// sixth is refused - and refused with 429 rather than the framework's default 503, because the
    /// caller is being told to slow down, not that the service is down.
    /// </summary>
    [Fact]
    public async Task Requests_beyond_the_per_client_cap_are_refused_with_429()
    {
        var email = await CreateMemberAsync();
        var client = CreateForgotClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await ForgotAsync(client, email)).StatusCode);
        }

        var refused = await ForgotAsync(client, email);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }
}
