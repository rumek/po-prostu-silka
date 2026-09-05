using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;

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
}
