using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// Asserts the PRD's Access Control rules directly. These are the invariants whose silent breakage
/// would compromise every later slice: an account the club has barred must not be able to act, and
/// role separation must hold at the HTTP boundary.
///
/// <para>
/// The pending half of this file went with approval (S-16, MP-03). What survives is the axis that
/// still exists — blocked — plus the claim-staleness mechanism, which S-16 rewrote around a block
/// because that is now the only status change there is.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class AuthEndpointTests(IntegrationTestFixture fixture)
{
    private sealed record LoginFailureBody(string Reason);

    private sealed record CurrentUserBody(
        string Id, string Email, string DisplayName, string Status, string[] Roles);

    private sealed record PendingMemberBody(
        Guid MemberId, string UserId, string Email, string DisplayName, DateTimeOffset CreatedAt);

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

    // Inverted by S-01 (D1). The PRD's Access Control section and roadmap S-01 both say a pending
    // member logs in and sees an awaiting-approval screen; F-02 refused them instead. Content is
    // gated by the ActiveMember policy, which the next test pins.

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

    [Fact]
    public async Task Null_credentials_are_refused_without_a_500()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = (string?)null, password = (string?)null });

        // The same non-disclosing answer a wrong address gets - a null body must not be
        // distinguishable from a bad guess, and must not be a 500.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "invalid_credentials",
            (await response.Content.ReadFromJsonAsync<LoginFailureBody>())!.Reason);
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

    // --- /refresh -------------------------------------------------------------

    [Fact]
    public async Task Refresh_is_401_when_anonymous()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsync("/api/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// /refresh must sit behind bare RequireAuthorization(), never the ActiveMember policy - a
    /// member has to be able to call it whatever their claims currently say.
    /// </summary>
    [Fact]
    public async Task Refresh_succeeds_for_an_ordinary_member()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await client.PostAsync("/api/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CurrentUserBody>();
        Assert.Equal(nameof(AccountStatus.Active), body!.Status);
    }

    /// <summary>
    /// THE CLAIM-STALENESS MECHANISM, and the reason POST /api/auth/refresh exists.
    ///
    /// <para>
    /// Authorization reads the COOKIE, not the database, so a status change does not bite until the
    /// claims are re-minted — on the security-stamp validation interval (Program.cs) or on this
    /// endpoint. Asserting the still-permitted step in the middle pins the mechanism rather than the
    /// symptom: without it, this test would pass even if the claim were never stale.
    /// </para>
    ///
    /// <para>
    /// WRITTEN AROUND A BLOCK SINCE S-16. It used to run the same sequence through approval —
    /// Pending, approve, still refused, refresh, admitted — and approval is gone. Blocking is the
    /// remaining status change, and it runs the sequence in the more dangerous direction: the stale
    /// claim here is PERMISSIVE, so what refresh fixes is a session still being let in.
    /// </para>
    ///
    /// <para>
    /// The status is written straight to the database rather than through POST /{id}/block, and that
    /// is the whole arrangement: the endpoint rotates the security stamp, which makes Identity
    /// re-validate and re-mint on its own — closing the window this test needs to observe. Going
    /// around it is what keeps the stale claim stale.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_status_change_does_not_reach_the_cookie_until_refresh_is_called()
    {
        var email = $"claim-refresh-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        var member = await fixture.CreateAuthenticatedClientAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/test/active-member")).StatusCode);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = await db.Users.SingleAsync(u => u.Email == email);
            user.Status = AccountStatus.Blocked;

            await db.SaveChangesAsync();
        }

        // The database says Blocked...
        Assert.Equal(
            nameof(AccountStatus.Blocked),
            (await member.GetFromJsonAsync<CurrentUserBody>("/api/auth/me"))!.Status);

        // ...but the cookie still says Active, so the ActiveMember probe keeps admitting them.
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/test/active-member")).StatusCode);

        var refresh = await member.PostAsync("/api/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(
            nameof(AccountStatus.Blocked),
            (await refresh.Content.ReadFromJsonAsync<CurrentUserBody>())!.Status);

        Assert.Equal(
            HttpStatusCode.Forbidden, (await member.GetAsync("/test/active-member")).StatusCode);
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
