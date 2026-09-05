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
        string displayName = "Nowy Członek", string phoneNumber = "123456789",
        string street = "Piłsudskiego", string houseNumber = "12A/3",
        string postalCode = "00-001", string city = "Warszawa") =>
        new { email, password, displayName, phoneNumber, street, houseNumber, postalCode, city };

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

    /// <summary>
    /// The record's strings are non-nullable, but that is a compile-time contract - a JSON null
    /// still arrives. Without a guard, FindByEmailAsync throws and an anonymous caller gets a 500.
    /// </summary>
    [Theory]
    [InlineData(null, TestUsers.Password, "invalid_email")]
    [InlineData("", TestUsers.Password, "invalid_email")]
    [InlineData("someone@test.local", null, "invalid_password")]
    public async Task Null_or_blank_credentials_are_rejected_without_a_500(
        string? email, string? password, string expectedReason)
    {
        var client = fixture.CreateClient();

        // Email and password are guarded BEFORE the contact details, so a complete address here
        // proves the reason code comes from the credential guard and not from ContactDetails.
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email,
                password,
                displayName = "Ktoś",
                phoneNumber = "123456789",
                street = "Piłsudskiego",
                houseNumber = "12A/3",
                postalCode = "00-001",
                city = "Warszawa",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            expectedReason,
            (await response.Content.ReadFromJsonAsync<RegisterFailureBody>())!.Reason);
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

    /// <summary>
    /// S-13. The contact fields are required by the API even though their columns are nullable, and
    /// the phone number is stored normalised - so "+48 123 456 789" and "123456789" are one value in
    /// the database, not two that look different to every future comparison.
    /// </summary>
    [Fact]
    public async Task Contact_details_are_stored_with_the_phone_number_normalised()
    {
        var client = fixture.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            Registration(
                email,
                phoneNumber: "+48 123 456 789",
                street: "  Piłsudskiego  ",
                houseNumber: " 12A/3 ",
                postalCode: "31-042",
                city: "  Kraków  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await userManager.FindByEmailAsync(email);

        Assert.NotNull(stored);
        Assert.Equal("123456789", stored.PhoneNumber);
        Assert.Equal("Piłsudskiego", stored.Street);
        Assert.Equal("12A/3", stored.HouseNumber);
        Assert.Equal("31-042", stored.PostalCode);
        Assert.Equal("Kraków", stored.City);
    }

    /// <summary>
    /// Each contact field answers with its own reason code, because the SPA maps every code onto a
    /// specific control - a shared "invalid_contact" would put the error on the wrong field.
    /// </summary>
    [Theory]
    [InlineData("00001", "invalid_postal_code")]
    [InlineData("00-0001", "invalid_postal_code")]
    [InlineData("ab-cde", "invalid_postal_code")]
    public async Task Malformed_postal_code_is_rejected(string postalCode, string expectedReason)
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(NewEmail(), postalCode: postalCode));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            expectedReason,
            (await response.Content.ReadFromJsonAsync<RegisterFailureBody>())!.Reason);
    }

    [Theory]
    [InlineData("12345678", "invalid_phone")]
    [InlineData("1234567890", "invalid_phone")]
    [InlineData("nie-numer", "invalid_phone")]
    public async Task Malformed_phone_number_is_rejected(string phoneNumber, string expectedReason)
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(NewEmail(), phoneNumber: phoneNumber));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            expectedReason,
            (await response.Content.ReadFromJsonAsync<RegisterFailureBody>())!.Reason);
    }

    [Fact]
    public async Task Blank_city_is_rejected()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(NewEmail(), city: "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_city",
            (await response.Content.ReadFromJsonAsync<RegisterFailureBody>())!.Reason);
    }

    /// <summary>
    /// The contact details are validated before CreateAsync, so a refused registration must leave
    /// nothing behind - otherwise the member retries and is told the address is taken.
    /// </summary>
    [Fact]
    public async Task A_registration_refused_for_contact_details_creates_no_account()
    {
        var client = fixture.CreateClient();
        var email = NewEmail();

        await client.PostAsJsonAsync("/api/auth/register", Registration(email, street: ""));

        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await userManager.FindByEmailAsync(email));
    }
}
