using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// Registration (FR-001, superseded in part by S-16 MP-03 and again by S-17 IR-05). The invariants
/// that matter here are that a new account lands ACTIVE with a role, that it can immediately use the
/// app, that the endpoint is rate limited — the control that replaced the approval gate — and that
/// the failure vocabulary never leaks Identity's raw error text.
///
/// <para>
/// EVERY REGISTRATION HERE CARRIES AN INVITATION CODE, because since S-17 there is no other kind.
/// The refusal of a codeless one, and everything about what a claim inherits, lives in
/// <see cref="MemberClaimTests"/>; this file is about what registration still does once the door has
/// been opened. The display name and the five contact fields are deliberately absent from the
/// payload — their validation moved with them, to <c>PUT /api/profile</c> (ProfileEndpointTests) and
/// to the admin surface (MemberEndpointTests).
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class RegisterEndpointTests(IntegrationTestFixture fixture)
{
    private sealed record RegisterFailureBody(string Reason);

    private sealed record CurrentUserBody(
        string Id, string Email, string DisplayName, string Status, string[] Roles);

    private static string NewEmail() => $"register-{Guid.NewGuid():N}@test.local";

    /// <summary>
    /// THREE FIELDS. This is the whole request contract since S-17 — an address to sign in with, a
    /// password, and the code that says which record the account attaches to.
    /// </summary>
    private static object Registration(
        string? email, string code, string? password = TestUsers.Password) =>
        new { email, password, memberCode = code };

    /// <summary>
    /// An accountless member with a live code in somebody's hand — the state every registration now
    /// starts from.
    ///
    /// <para>
    /// Written straight through the DbContext rather than through the admin API on purpose: this
    /// suite is about the register endpoint, and arranging its fixture through a second HTTP surface
    /// would make every test here fail when the code-issuing endpoints break. The end-to-end path
    /// (admin issues, member claims) is <see cref="MemberClaimTests"/>'s subject.
    /// </para>
    /// </summary>
    private async Task<(Guid MemberId, string Code)> InvitedMemberAsync(
        string displayName = "Nowy Członek",
        string? email = null,
        bool withContactDetails = false)
    {
        var memberId = await fixture.CreateMemberAsync(displayName, email: email);
        var code = MemberAccessCode.Generate();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var member = await db.Members.SingleAsync(m => m.Id == memberId);
        member.AccessCode = code;
        member.AccessCodeExpiresAt = DateTimeOffset.UtcNow.Add(MemberAccessCode.Validity);

        if (withContactDetails)
        {
            member.PhoneNumber = "123456789";
            member.Street = "Piłsudskiego";
            member.HouseNumber = "12A/3";
            member.PostalCode = "00-001";
            member.City = "Warszawa";
        }

        await db.SaveChangesAsync();

        return (memberId, code);
    }

    /// <summary>
    /// THE POINT OF MP-03: no approval step stands between registering and using the app. Asserted
    /// against the ActiveMember policy probe rather than against the status field, because the status
    /// is only interesting insofar as it opens the door — and this is the door.
    /// </summary>
    [Fact]
    public async Task A_freshly_registered_account_immediately_passes_the_ActiveMember_policy()
    {
        var client = fixture.CreateClient();
        var (_, code) = await InvitedMemberAsync();

        var registered = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(NewEmail(), code));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        // The same client, carrying the cookie registration just issued. No refresh, no second login.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/active-member")).StatusCode);
    }

    /// <summary>
    /// THE CONTROL THAT REPLACED APPROVAL (S-16), and load-bearing again under S-17: the invitation
    /// code is the only credential standing between a stranger and an account, so the limiter is what
    /// keeps guessing at it uneconomic in practice as well as in arithmetic. One client — therefore
    /// one rate-limiter partition — registering in a burst is eventually refused with 429 rather than
    /// accepted.
    ///
    /// <para>
    /// Every other test in this suite gets its own client address from the fixture precisely so it
    /// does NOT hit this; here one address is shared on purpose, which is the only way to observe the
    /// limiter at all. Each attempt carries its own live code, so what is being measured is the
    /// limiter and not a code running out.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_burst_of_registrations_from_one_client_is_refused()
    {
        var client = fixture.CreateClientFromAddress("203.0.113.42");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var (_, code) = await InvitedMemberAsync();
            var response = await client.PostAsJsonAsync(
                "/api/auth/register", Registration(NewEmail(), code));
            statuses.Add(response.StatusCode);
        }

        // 429, not 503: the caller is being told to slow down, not that the service is unavailable.
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

        // And the cap is real rather than total — the first few genuinely went through, so a household
        // or the club's own wifi is not locked out by one person signing up.
        Assert.Contains(HttpStatusCode.OK, statuses);
    }

    /// <summary>
    /// Registration LINKS a member record, and since S-17 it can no longer create one — so "exactly
    /// one" is now also "exactly the one the code named".
    /// </summary>
    [Fact]
    public async Task Registration_links_exactly_one_member_record_to_the_new_account()
    {
        var client = fixture.CreateClient();
        var email = NewEmail();
        var (memberId, code) = await InvitedMemberAsync(
            "Nowy Członek", withContactDetails: true);

        var response = await client.PostAsJsonAsync("/api/auth/register", Registration(email, code));
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

        // And it is the INVITED record, not a fresh one beside it.
        Assert.Equal(memberId, member.Id);

        Assert.Equal("Nowy Członek", member.DisplayName);

        // The record was entered with no address, so the login address became its contact.
        Assert.Equal(email, member.Email);

        // BOTH ACTIVE since S-16 — and the two statuses still answer DIFFERENT questions, which is
        // why both are asserted rather than one. Approval used to make them disagree at registration;
        // blocking still can, and the pair must stay distinguishable for that reason alone.
        Assert.Equal(MembershipStatus.Active, member.Status);
        Assert.Equal(AccountStatus.Active, user!.Status);

        // THE CLUB'S CONTACT DETAILS, UNTOUCHED (S-17). The form stopped supplying them, so nothing
        // in registration may overwrite what the desk recorded.
        Assert.Equal("123456789", member.PhoneNumber);
        Assert.Equal("Warszawa", member.City);

        // The account carries Identity's own phone column in step with the record, as it always has —
        // sourced from the record now rather than from the form.
        Assert.Equal("123456789", user.PhoneNumber);
    }

    [Fact]
    public async Task Registration_creates_an_active_member_in_the_User_role_and_signs_them_in()
    {
        var client = fixture.CreateClient();
        var email = NewEmail();
        var (_, code) = await InvitedMemberAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", Registration(email, code));

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
    ///
    /// <para>
    /// It also pins the ORDERING S-17 depends on: the code is resolved before the address is checked,
    /// so a duplicate address refuses the registration without consuming the invitation. Getting this
    /// backwards would burn somebody's only way in on a typo.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Duplicate_email_is_disclosed_as_email_taken_without_consuming_the_code()
    {
        var client = fixture.CreateClient();
        var (memberId, code) = await InvitedMemberAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(TestUsers.ActiveMemberEmail, code));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterFailureBody>();
        Assert.Equal("email_taken", body!.Reason);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId);

        Assert.Equal(code, member.AccessCode);
        Assert.Null(member.UserId);
    }

    /// <summary>
    /// THE ADDRESS ON AN ACCOUNTLESS MEMBER IS TAKEN TOO, and the whole point is that Identity cannot
    /// see it. The admin records a walk-in with their email at the desk; that person is later invited
    /// under a DIFFERENT record and tries to register with the first one's address. Before this was
    /// checked, the pre-check passed, the account was created, and IX_Members_Email rejected the
    /// member row - so the caller got a 500 and the compensating delete undid a real account on every
    /// retry, locking them out of their own address. Answer the same 409 the account case answers,
    /// which the SPA already renders with its "Zaloguj się" branch.
    /// </summary>
    [Fact]
    public async Task An_address_held_by_an_accountless_member_is_disclosed_as_email_taken()
    {
        var email = $"walkin-{Guid.NewGuid():N}@test.local";
        await fixture.CreateMemberAsync("Walk-in przy ladzie", email: email);

        var (_, code) = await InvitedMemberAsync();
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", Registration(email, code));

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
    /// A member claiming the record that ALREADY holds their address is not a duplicate. The
    /// exceptMemberId argument is what tells the two apart, and without it the desk recording someone
    /// with their email would make that person unable to use it as their login.
    /// </summary>
    [Fact]
    public async Task A_records_own_address_is_not_a_duplicate_when_that_record_is_the_one_claimed()
    {
        var email = $"desk-{Guid.NewGuid():N}@test.local";
        var (_, code) = await InvitedMemberAsync("Klubowicz Z Adresem", email: email);

        var response = await fixture.CreateClient()
            .PostAsJsonAsync("/api/auth/register", Registration(email, code));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The record's strings are non-nullable, but that is a compile-time contract - a JSON null
    /// still arrives. Without a guard, FindByEmailAsync throws and an anonymous caller gets a 500.
    ///
    /// <para>
    /// A VALID CODE travels with each case, which is what proves the reason code comes from the
    /// credential guard rather than from the code check that now sits below it.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(null, TestUsers.Password, "invalid_email")]
    [InlineData("", TestUsers.Password, "invalid_email")]
    [InlineData("someone@test.local", null, "invalid_password")]
    public async Task Null_or_blank_credentials_are_rejected_without_a_500(
        string? email, string? password, string expectedReason)
    {
        var client = fixture.CreateClient();
        var (_, code) = await InvitedMemberAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(email, code, password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            expectedReason,
            (await response.Content.ReadFromJsonAsync<RegisterFailureBody>())!.Reason);
    }

    [Fact]
    public async Task Short_password_is_rejected()
    {
        var client = fixture.CreateClient();
        var (_, code) = await InvitedMemberAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(NewEmail(), code, password: "Krot1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterFailureBody>();
        Assert.Equal("invalid_password", body!.Reason);
    }

    /// <summary>
    /// Identity refuses the address, and the refusal must leave the invitation intact — a typo in the
    /// email is exactly the case where the member needs to try the same link again.
    /// </summary>
    [Fact]
    public async Task Malformed_email_is_rejected_without_echoing_identity_error_text()
    {
        var client = fixture.CreateClient();
        var (memberId, code) = await InvitedMemberAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", Registration("not-an-email", code));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterFailureBody>();
        Assert.Equal("invalid_email", body!.Reason);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId);
        Assert.Equal(code, member.AccessCode);
    }
}
