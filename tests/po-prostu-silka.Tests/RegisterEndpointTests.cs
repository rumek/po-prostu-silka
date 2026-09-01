using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Tests;

/// <summary>
/// Registration (FR-001). The invariants that matter here are that a new account lands Pending with
/// a role, and that the failure vocabulary never leaks Identity's raw error text.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class RegisterEndpointTests(IntegrationTestFixture fixture)
{
    private sealed record RegisterFailureBody(string Reason);

    private sealed record CurrentUserBody(
        string Id, string Email, string DisplayName, string Status, string[] Roles);

    private static string NewEmail() => $"register-{Guid.NewGuid():N}@test.local";

    private static object Registration(string email, string password = TestUsers.Password,
        string displayName = "Nowy Członek") =>
        new { email, password, displayName };

    [Fact]
    public async Task Registration_creates_a_pending_member_in_the_User_role_and_signs_them_in()
    {
        var client = fixture.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", Registration(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Signed in immediately (D1): the member waits for approval inside a session, not outside it.
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.Contains("Identity.Application"));

        var body = await response.Content.ReadFromJsonAsync<CurrentUserBody>();
        Assert.Equal(email, body!.Email);
        Assert.Equal(nameof(AccountStatus.Pending), body.Status);
        Assert.Contains(ApplicationRoles.User, body.Roles);

        // A role-less account passes the ActiveMember status check and then fails its RequireRole,
        // with no admin surface to repair it — so assert the role landed in the database too, not
        // just in the response we built.
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await userManager.FindByEmailAsync(email);
        Assert.NotNull(stored);
        Assert.Equal(AccountStatus.Pending, stored.Status);
        Assert.Contains(ApplicationRoles.User, await userManager.GetRolesAsync(stored));
    }

    /// <summary>
    /// D3, deliberately asymmetric with /login's non-disclosure: silence would strand a real member
    /// who forgot they had signed up. If this test is ever "fixed" to expect a generic response,
    /// read the comment on RegisterAsync first.
    /// </summary>
    [Fact]
    public async Task Duplicate_email_is_disclosed_as_email_taken()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(TestUsers.ActiveMemberEmail));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterFailureBody>();
        Assert.Equal("email_taken", body!.Reason);
    }

    [Fact]
    public async Task Short_password_is_rejected()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(NewEmail(), password: "Krot1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterFailureBody>();
        Assert.Equal("invalid_password", body!.Reason);
    }

    [Fact]
    public async Task Blank_display_name_is_rejected()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(NewEmail(), displayName: "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterFailureBody>();
        Assert.Equal("invalid_display_name", body!.Reason);
    }

    [Fact]
    public async Task Display_name_is_trimmed_before_it_is_stored()
    {
        var client = fixture.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(email, displayName: "  Anna Kowalska  "));

        var body = await response.Content.ReadFromJsonAsync<CurrentUserBody>();
        Assert.Equal("Anna Kowalska", body!.DisplayName);
    }

    [Fact]
    public async Task Malformed_email_is_rejected_without_echoing_identity_error_text()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration("not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterFailureBody>();
        Assert.Equal("invalid_email", body!.Reason);
    }
}
