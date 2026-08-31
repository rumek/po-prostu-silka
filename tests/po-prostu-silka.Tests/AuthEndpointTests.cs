using System.Net;
using System.Net.Http.Json;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Tests;

/// <summary>
/// Asserts the PRD's Access Control rules directly. These are the invariants whose silent breakage
/// would compromise every later slice: an unapproved account must not be able to act, and role
/// separation must hold at the HTTP boundary.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class AuthEndpointTests(IntegrationTestFixture fixture)
{
    private sealed record LoginFailureBody(string Reason);

    private sealed record CurrentUserBody(
        string Id, string Email, string DisplayName, string Status, string[] Roles);

    private static object Credentials(string email) =>
        new { email, password = TestUsers.Password };

    // --- login: status gating -------------------------------------------------

    [Fact]
    public async Task Active_user_can_log_in()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", Credentials(TestUsers.ActiveMemberEmail));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.Contains("Identity.Application"));
    }

    [Fact]
    public async Task Pending_user_is_refused_with_a_distinguishing_reason()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", Credentials(TestUsers.PendingMemberEmail));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginFailureBody>();
        Assert.Equal("pending_approval", body!.Reason);
    }

    [Fact]
    public async Task Blocked_user_is_refused_with_a_distinguishing_reason()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", Credentials(TestUsers.BlockedMemberEmail));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginFailureBody>();
        Assert.Equal("blocked", body!.Reason);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_are_indistinguishable()
    {
        var client = fixture.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login",
            new { email = TestUsers.ActiveMemberEmail, password = "NotThePassword1" });
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@test.local", password = "NotThePassword1" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        // Identical responses, so login cannot be used to enumerate registered addresses.
        Assert.Equal(
            (await wrongPassword.Content.ReadFromJsonAsync<LoginFailureBody>())!.Reason,
            (await unknownEmail.Content.ReadFromJsonAsync<LoginFailureBody>())!.Reason);
    }

    // --- /me ------------------------------------------------------------------

    [Fact]
    public async Task Me_is_401_when_anonymous_and_is_not_the_spa_shell()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Identity's default is a 302 to /Account/Login, which MapFallbackToFile would answer with
        // 200 text/html. If this ever regresses, the SPA silently receives the shell instead of 401.
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Me_returns_the_expected_claims_after_login()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var body = await client.GetFromJsonAsync<CurrentUserBody>("/api/auth/me");

        Assert.Equal(TestUsers.ActiveAdminEmail, body!.Email);
        Assert.Equal(nameof(AccountStatus.Active), body.Status);
        Assert.Contains(ApplicationRoles.Admin, body.Roles);
        Assert.NotEmpty(body.DisplayName);
    }

    [Fact]
    public async Task Logout_invalidates_the_session()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    // --- policies -------------------------------------------------------------

    [Fact]
    public async Task Admin_policy_returns_403_for_a_plain_member()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await client.GetAsync("/test/admin-only");

        // 403, not 401: the caller is authenticated but lacks the role. This distinction comes from
        // the OnRedirectToAccessDenied override in Program.cs.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_policy_admits_an_admin()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await client.GetAsync("/test/admin-only");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Policy_protected_route_is_401_when_anonymous()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/test/active-member");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ActiveMember_policy_admits_an_active_member()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await client.GetAsync("/test/active-member");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- seeding --------------------------------------------------------------

    [Fact]
    public async Task Seeded_admin_exists_and_can_log_in()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", Credentials(TestUsers.SeededAdminEmail));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CurrentUserBody>();
        Assert.Contains(ApplicationRoles.Admin, body!.Roles);
    }
}
