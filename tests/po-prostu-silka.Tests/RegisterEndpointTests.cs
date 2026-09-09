using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// Registration (FR-001, superseded in part by S-16 MP-03). The invariants that matter here are that
/// a new account lands ACTIVE with a role, that it can immediately use the app, that the endpoint is
/// rate limited — the control that replaced the approval gate — and that the failure vocabulary never
/// leaks Identity's raw error text.
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

    /// <summary>
    /// THE POINT OF MP-03: no approval step stands between registering and using the app. Asserted
    /// against the ActiveMember policy probe rather than against the status field, because the status
    /// is only interesting insofar as it opens the door — and this is the door.
    /// </summary>
    [Fact]
    public async Task A_freshly_registered_account_immediately_passes_the_ActiveMember_policy()
    {
        var client = fixture.CreateClient();

        var registered = await client.PostAsJsonAsync("/api/auth/register", Registration(NewEmail()));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        // The same client, carrying the cookie registration just issued. No refresh, no second login.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/active-member")).StatusCode);
    }

    /// <summary>
    /// THE CONTROL THAT REPLACED APPROVAL (S-16). One client — therefore one rate-limiter partition —
    /// registering in a burst is eventually refused with 429 rather than accepted.
    ///
    /// <para>
    /// Every other test in this suite gets its own client address from the fixture precisely so it
    /// does NOT hit this; here one address is shared on purpose, which is the only way to observe the
    /// limiter at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_burst_of_registrations_from_one_client_is_refused()
    {
        var client = fixture.CreateClientFromAddress("203.0.113.42");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/register", Registration(NewEmail()));
            statuses.Add(response.StatusCode);
        }

        // 429, not 503: the caller is being told to slow down, not that the service is unavailable.
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

        // And the cap is real rather than total — the first few genuinely went through, so a household
        // or the club's own wifi is not locked out by one person signing up.
        Assert.Contains(HttpStatusCode.OK, statuses);
    }

    [Fact]
    public async Task Registration_creates_exactly_one_member_record_linked_to_the_new_account()
    {
        var client = fixture.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", Registration(email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        // EXACTLY ONE, not "at least one". The whole guarantee the membership claim rests on is that an
        // account maps to a single member; two would make "whose bookings are these" ambiguous, and the
        // filtered unique index on UserId is what is being proven here.
        var members = await db.Members.Where(m => m.UserId == user!.Id).ToListAsync();
        var member = Assert.Single(members);

        Assert.Equal("Nowy Członek", member.DisplayName);
        Assert.Equal(email, member.Email);

        // BOTH ACTIVE since S-16 — and the two statuses still answer DIFFERENT questions, which is
        // why both are asserted rather than one. Approval used to make them disagree at registration;
        // blocking still can, and the pair must stay distinguishable for that reason alone.
        Assert.Equal(MembershipStatus.Active, member.Status);
        Assert.Equal(AccountStatus.Active, user!.Status);

        // Contact details are copied onto the member as well as the account, which is what lets the
        // read flip to Members in a later phase without a second migration.
        Assert.Equal("123456789", member.PhoneNumber);
        Assert.Equal("Warszawa", member.City);
    }

    [Fact]
    public async Task Registration_creates_an_active_member_in_the_User_role_and_signs_them_in()
    {
        var client = fixture.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", Registration(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Signed in immediately, and since S-16 there is nothing to wait for inside that session:
        // the account works the moment it exists. What the member still cannot do is train, because
        // being booked requires a karnet — the gate moved, it did not disappear.
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.Contains("Identity.Application"));

        var body = await response.Content.ReadFromJsonAsync<CurrentUserBody>();
        Assert.Equal(email, body!.Email);
        Assert.Equal(nameof(AccountStatus.Active), body.Status);
        Assert.Contains(ApplicationRoles.User, body.Roles);

        // A role-less account passes the ActiveMember status check and then fails its RequireRole,
        // with no admin surface to repair it — so assert the role landed in the database too, not
        // just in the response we built.
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await userManager.FindByEmailAsync(email);
        Assert.NotNull(stored);
        Assert.Equal(AccountStatus.Active, stored.Status);
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
    /// THE ADDRESS ON AN ACCOUNTLESS MEMBER IS TAKEN TOO, and the whole point is that Identity cannot
    /// see it. The admin records a walk-in with their email at the desk; that person later registers
    /// on their own, without a code. Before this was checked, the pre-check passed, the account was
    /// created, and IX_Members_Email rejected the member row - so the caller got a 500 and the
    /// compensating delete undid a real account on every retry, locking them out of their own
    /// address. Answer the same 409 the account case answers, which the SPA already renders with its
    /// "Zaloguj się" branch.
    /// </summary>
    [Fact]
    public async Task An_address_held_by_an_accountless_member_is_disclosed_as_email_taken()
    {
        var email = $"walkin-{Guid.NewGuid():N}@test.local";
        await fixture.CreateMemberAsync("Walk-in przy ladzie", email: email);

        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", Registration(email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterFailureBody>();
        Assert.Equal("email_taken", body!.Reason);

        // No account was created and rolled back - the refusal happens before CreateAsync, so there
        // is nothing to undo. This is the half that a 500 plus compensation got wrong.
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await userManager.FindByEmailAsync(email));
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

        // ON THE MEMBER since S-14 Phase 8. The account keeps only the phone number, which is
        // Identity's own column and survives the drop the address columns do not.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Email == email);

        Assert.NotNull(stored);
        Assert.Equal("123456789", stored.PhoneNumber);
        Assert.Equal("Piłsudskiego", stored.Street);
        Assert.Equal("12A/3", stored.HouseNumber);
        Assert.Equal("31-042", stored.PostalCode);
        Assert.Equal("Kraków", stored.City);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var account = await userManager.FindByEmailAsync(email);
        Assert.Equal("123456789", account!.PhoneNumber);
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
