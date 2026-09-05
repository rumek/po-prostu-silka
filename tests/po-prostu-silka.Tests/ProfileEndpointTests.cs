using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Tests;

/// <summary>
/// The member's own profile surface (S-13, FR-006 as rewritten). The invariants that matter are that
/// a member can change their contact details and nothing else, that validation is identical to
/// registration's, and that an account created before this slice - with NULL columns and possibly
/// still awaiting approval - can complete its details.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class ProfileEndpointTests(IntegrationTestFixture fixture)
{
    private sealed record ProfileFailureBody(string Reason);

    private sealed record CurrentUserBody(
        string Id,
        string Email,
        string DisplayName,
        string Status,
        string[] Roles,
        string? PhoneNumber,
        string? Street,
        string? HouseNumber,
        string? PostalCode,
        string? City);

    private static object Profile(
        string phoneNumber = "123456789",
        string street = "Piłsudskiego",
        string houseNumber = "12A/3",
        string postalCode = "00-001",
        string city = "Warszawa") =>
        new { phoneNumber, street, houseNumber, postalCode, city };

    /// <summary>
    /// A fresh account per test. The seeded members are shared across the whole collection, and
    /// these tests WRITE - reusing one would make this file's results depend on execution order.
    /// Created through UserManager rather than /register so the contact columns start NULL, which is
    /// what an account registered before this slice actually looks like.
    /// </summary>
    private async Task<string> CreateMemberWithoutContactDetailsAsync(AccountStatus status)
    {
        var email = $"profile-{Guid.NewGuid():N}@test.local";

        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Anna Kowalska",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var created = await userManager.CreateAsync(user, TestUsers.Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, ApplicationRoles.User);

        return email;
    }

    private async Task<ApplicationUser> ReadBackAsync(string email)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await userManager.FindByEmailAsync(email);
        Assert.NotNull(stored);
        return stored;
    }

    [Fact]
    public async Task An_authenticated_member_updates_their_contact_details()
    {
        var email = await CreateMemberWithoutContactDetailsAsync(AccountStatus.Active);
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        var response = await client.PutAsJsonAsync(
            "/api/profile",
            Profile(phoneNumber: "+48 987 654 321", street: "  Długa  ", postalCode: "31-042",
                city: "  Kraków  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The response IS the new session state - the SPA replaces its user signal from it rather
        // than re-fetching, so the saved values have to come back on this response.
        var body = await response.Content.ReadFromJsonAsync<CurrentUserBody>();
        Assert.Equal("987654321", body!.PhoneNumber);
        Assert.Equal("Długa", body.Street);
        Assert.Equal("31-042", body.PostalCode);
        Assert.Equal("Kraków", body.City);
        Assert.Equal("Anna Kowalska", body.DisplayName);

        var stored = await ReadBackAsync(email);
        Assert.Equal("987654321", stored.PhoneNumber);
        Assert.Equal("Długa", stored.Street);
        Assert.Equal("12A/3", stored.HouseNumber);
        Assert.Equal("31-042", stored.PostalCode);
        Assert.Equal("Kraków", stored.City);
    }

    /// <summary>
    /// The gym owns the name on the membership. The request record simply has no field for it, so a
    /// member sending one is not refused - it never reaches the entity. If this ever fails, someone
    /// added DisplayName or Email to ProfileRequest; read the comment on that record first.
    /// </summary>
    [Fact]
    public async Task Display_name_and_email_sent_alongside_are_ignored()
    {
        var email = await CreateMemberWithoutContactDetailsAsync(AccountStatus.Active);
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        var response = await client.PutAsJsonAsync(
            "/api/profile",
            new
            {
                phoneNumber = "123456789",
                street = "Piłsudskiego",
                houseNumber = "12A/3",
                postalCode = "00-001",
                city = "Warszawa",
                displayName = "Podszywacz",
                email = "przejety@test.local",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await ReadBackAsync(email);
        Assert.Equal("Anna Kowalska", stored.DisplayName);
        Assert.Equal(email, stored.Email);
    }

    /// <summary>
    /// Bare RequireAuthorization, NOT the ActiveMember policy. An account created before this slice
    /// may still be awaiting approval, and the screen that asks it to complete its details would be
    /// useless if the save behind it answered 403.
    /// </summary>
    [Fact]
    public async Task A_pending_member_can_complete_their_contact_details()
    {
        var email = await CreateMemberWithoutContactDetailsAsync(AccountStatus.Pending);
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        var response = await client.PutAsJsonAsync("/api/profile", Profile());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CurrentUserBody>();
        Assert.Equal(nameof(AccountStatus.Pending), body!.Status);
        Assert.Equal("123456789", body.PhoneNumber);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        var client = fixture.CreateClient();

        var response = await client.PutAsJsonAsync("/api/profile", Profile());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Same helper as registration, so the same input must produce the same reason code. A member
    /// who can save what the registration form refuses is the drift ContactDetails exists to stop.
    /// </summary>
    [Theory]
    [InlineData("00001", null, null, "invalid_postal_code")]
    [InlineData(null, "12345678", null, "invalid_phone")]
    [InlineData(null, null, "   ", "invalid_city")]
    public async Task Contact_details_are_validated_exactly_as_at_registration(
        string? postalCode, string? phoneNumber, string? city, string expectedReason)
    {
        var email = await CreateMemberWithoutContactDetailsAsync(AccountStatus.Active);
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        var response = await client.PutAsJsonAsync(
            "/api/profile",
            Profile(
                phoneNumber: phoneNumber ?? "123456789",
                postalCode: postalCode ?? "00-001",
                city: city ?? "Warszawa"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            expectedReason,
            (await response.Content.ReadFromJsonAsync<ProfileFailureBody>())!.Reason);

        // A refused save leaves the row untouched - the member's previous details are not half-written.
        var stored = await ReadBackAsync(email);
        Assert.Null(stored.Street);
    }

    /// <summary>
    /// The profile form pre-fills from session state rather than from a GET of its own, so /me has
    /// to carry the contact details. An account without them reports nulls, which is what the
    /// screen's "complete your details" prompt keys off.
    /// </summary>
    [Fact]
    public async Task The_session_payload_carries_the_contact_details()
    {
        var email = await CreateMemberWithoutContactDetailsAsync(AccountStatus.Active);
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        var before = await client.GetFromJsonAsync<CurrentUserBody>("/api/auth/me");
        Assert.Null(before!.PhoneNumber);
        Assert.Null(before.Street);
        Assert.Null(before.City);

        await client.PutAsJsonAsync("/api/profile", Profile());

        var after = await client.GetFromJsonAsync<CurrentUserBody>("/api/auth/me");
        Assert.Equal("123456789", after!.PhoneNumber);
        Assert.Equal("Piłsudskiego", after.Street);
        Assert.Equal("Warszawa", after.City);
    }
}
