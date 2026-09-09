using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// S-14's own surface: the club's record of a person who has no account (AM-001, AM-002).
///
/// The invariants worth pinning here are the ones that only exist because the member and the account
/// came apart — that a record can be created and blocked with no Identity row anywhere near it, that
/// the two statuses move together when there IS an account, and that the account-shaped actions say
/// so rather than 404-ing or, worse, half-succeeding.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class MemberEndpointTests(IntegrationTestFixture fixture)
{
    private const string Endpoint = "/api/admin/members";

    private sealed record MemberSummaryBody(
        Guid Id,
        string? UserId,
        string DisplayName,
        string? Email,
        string MembershipStatus,
        string? AccountStatus,
        string[] Roles,
        bool HasAccessCode,
        DateTimeOffset CreatedAt);

    private sealed record MemberDetailBody(
        Guid Id,
        string? UserId,
        string DisplayName,
        string? Email,
        string MembershipStatus,
        string? AccountStatus,
        string[] Roles,
        string? PhoneNumber,
        string? Street,
        string? HouseNumber,
        string? PostalCode,
        string? City,
        DateTimeOffset CreatedAt);

    private sealed record CreatedBody(Guid Id);

    private sealed record FailureBody(string Reason);

    private static object Request(
        string displayName = "Jan Kowalski",
        string? email = null,
        string? phoneNumber = null,
        string? street = null,
        string? houseNumber = null,
        string? postalCode = null,
        string? city = null) =>
        new { displayName, email, phoneNumber, street, houseNumber, postalCode, city };

    private static object FullContact(string displayName, string? email = null) =>
        new
        {
            displayName,
            email,
            phoneNumber = "601202303",
            street = "Polna",
            houseNumber = "7/2",
            postalCode = "00-002",
            city = "Kraków",
        };

    private Task<HttpClient> AdminAsync() =>
        fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    private async Task<Guid> CreateAsync(HttpClient admin, object request)
    {
        var response = await admin.PostAsJsonAsync(Endpoint, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreatedBody>())!.Id;
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    // --- who may reach the surface --------------------------------------------

    [Fact]
    public async Task Creating_a_member_requires_an_admin()
    {
        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await member.PostAsJsonAsync(Endpoint, Request());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- create ---------------------------------------------------------------

    /// <summary>
    /// THE POINT OF THE WHOLE SLICE: a person exists in the club's records, and Identity knows
    /// nothing about them. The assertion that no account was created is the load-bearing half — a
    /// version of this that quietly minted a passwordless account would pass every other test here.
    /// </summary>
    [Fact]
    public async Task A_member_can_be_created_with_no_account_at_all()
    {
        var admin = await AdminAsync();
        var name = $"Bez Konta {Guid.NewGuid():N}";

        var id = await CreateAsync(admin, Request(displayName: name));

        var detail = await admin.GetFromJsonAsync<MemberDetailBody>($"{Endpoint}/{id}");

        Assert.Equal(name, detail!.DisplayName);
        Assert.Null(detail.UserId);
        Assert.Null(detail.AccountStatus);
        Assert.Equal(nameof(MembershipStatus.Active), detail.MembershipStatus);
        Assert.Empty(detail.Roles);

        await using var db = NewContext();
        var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == id);
        Assert.Null(member.UserId);

        // No Identity row was created for them, under any address.
        Assert.Equal(0, await db.Users.CountAsync(u => u.DisplayName == name));
    }

    [Fact]
    public async Task A_member_can_be_created_with_full_contact_details()
    {
        var admin = await AdminAsync();
        var email = $"desk-{Guid.NewGuid():N}@test.local";

        var id = await CreateAsync(admin, FullContact("Anna Nowak", email));

        var detail = await admin.GetFromJsonAsync<MemberDetailBody>($"{Endpoint}/{id}");

        Assert.Equal(email, detail!.Email);
        Assert.Equal("601202303", detail.PhoneNumber);
        Assert.Equal("Polna", detail.Street);
        Assert.Equal("7/2", detail.HouseNumber);
        Assert.Equal("00-002", detail.PostalCode);
        Assert.Equal("Kraków", detail.City);
    }

    /// <summary>
    /// The all-or-nothing rule. Contact details are OPTIONAL for an admin-created record — demanding
    /// a full postal address before the club may write down that someone trains here would defeat the
    /// slice — but half an address is refused, through the same validator /register uses.
    /// </summary>
    [Fact]
    public async Task Partial_contact_details_are_refused_with_the_shared_vocabulary()
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync(
            Endpoint, Request(displayName: "Pół Adresu", city: "Warszawa"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // invalid_phone, not a bespoke "incomplete_address" - ContactDetails reports the first field
        // that fails in form order, and this surface deliberately speaks its vocabulary unchanged.
        Assert.Equal("invalid_phone", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    [Fact]
    public async Task A_blank_display_name_is_refused()
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync(Endpoint, Request(displayName: "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_display_name",
            (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>
    /// An address already held by an ACCOUNT is taken too, not just one held by another member row.
    /// Without this an admin could record an address that a later claim could never attach to.
    /// </summary>
    [Fact]
    public async Task An_address_an_account_already_holds_is_refused()
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync(
            Endpoint, Request(displayName: "Duplikat", email: TestUsers.ActiveMemberEmail));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("email_taken", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    [Fact]
    public async Task An_address_another_member_already_holds_is_refused()
    {
        var admin = await AdminAsync();
        var email = $"first-{Guid.NewGuid():N}@test.local";

        await CreateAsync(admin, Request(displayName: "Pierwszy", email: email));

        var response = await admin.PostAsJsonAsync(
            Endpoint, Request(displayName: "Drugi", email: email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- edit -----------------------------------------------------------------

    [Fact]
    public async Task Editing_updates_the_record()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin, Request(displayName: "Przed"));

        var response = await admin.PutAsJsonAsync($"{Endpoint}/{id}", FullContact("Po"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await admin.GetFromJsonAsync<MemberDetailBody>($"{Endpoint}/{id}");
        Assert.Equal("Po", detail!.DisplayName);
        Assert.Equal("Kraków", detail.City);
    }

    [Fact]
    public async Task Editing_keeps_an_unchanged_address_rather_than_colliding_with_itself()
    {
        var admin = await AdminAsync();
        var email = $"same-{Guid.NewGuid():N}@test.local";
        var id = await CreateAsync(admin, Request(displayName: "Ten Sam", email: email));

        var response = await admin.PutAsJsonAsync(
            $"{Endpoint}/{id}", Request(displayName: "Ten Sam Poprawiony", email: email));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// Editing a member who HAS an account carries the display name and the phone number onto it,
    /// and leaves the login address alone.
    ///
    /// <para>
    /// THE ADDRESS IS NOT COPIED ANY MORE (S-14): it lives on the member, and the account's four
    /// address columns are gone. The phone number still is, because it is Identity's own column and
    /// survives.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Editing_a_member_with_an_account_updates_the_accounts_copies()
    {
        var admin = await AdminAsync();
        var email = $"linked-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        var userId = await UserIdOfAsync(email);
        var memberId = await fixture.MemberIdOfAsync(userId);

        var response = await admin.PutAsJsonAsync($"{Endpoint}/{memberId}", FullContact("Nowa Nazwa"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = NewContext();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);

        Assert.Equal("Nowa Nazwa", user.DisplayName);
        Assert.Equal("601202303", user.PhoneNumber);

        // The LOGIN ADDRESS IS UNTOUCHED. It is the username, Identity indexes a normalised copy of
        // it, and renaming somebody's login as a side effect of fixing a phone number is not what the
        // admin asked for.
        Assert.Equal(email, user.Email);
    }

    [Fact]
    public async Task Editing_a_member_that_does_not_exist_is_404()
    {
        var admin = await AdminAsync();

        var response = await admin.PutAsJsonAsync($"{Endpoint}/{Guid.NewGuid()}", Request());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- block and unblock ----------------------------------------------------

    /// <summary>
    /// Blocking works with no account in sight — the case the pre-S-14 handler could not express at
    /// all, because it flipped a column on an Identity row that does not exist here.
    /// </summary>
    [Fact]
    public async Task An_accountless_member_can_be_blocked_and_unblocked()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin, Request(displayName: "Do Zablokowania"));

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsync($"{Endpoint}/{id}/block", content: null)).StatusCode);

        var blocked = await admin.GetFromJsonAsync<MemberDetailBody>($"{Endpoint}/{id}");
        Assert.Equal(nameof(MembershipStatus.Blocked), blocked!.MembershipStatus);
        Assert.Null(blocked.AccountStatus);

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsync($"{Endpoint}/{id}/unblock", content: null)).StatusCode);

        var restored = await admin.GetFromJsonAsync<MemberDetailBody>($"{Endpoint}/{id}");
        Assert.Equal(nameof(MembershipStatus.Active), restored!.MembershipStatus);
    }

    [Fact]
    public async Task Blocking_twice_is_not_an_error()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin, Request(displayName: "Dwa Razy"));

        await admin.PostAsync($"{Endpoint}/{id}/block", content: null);
        var second = await admin.PostAsync($"{Endpoint}/{id}/block", content: null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>
    /// Idempotent, exactly like block. Membership has two states, so "not blocked" is "already
    /// active" — a no-op, and reporting an error for a no-op would have the admin's double-click look
    /// like a failure.
    /// </summary>
    [Fact]
    public async Task Unblocking_a_member_who_is_not_blocked_is_a_no_op()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin, Request(displayName: "Nie Zablokowany"));

        var response = await admin.PostAsync($"{Endpoint}/{id}/unblock", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// When there IS an account the two statuses have to move together: a person barred from the club
    /// whose login still worked would reach every screen the ActiveMember policy guards.
    /// </summary>
    [Fact]
    public async Task Blocking_a_member_with_an_account_blocks_the_account_too()
    {
        var admin = await AdminAsync();
        var email = $"both-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        var userId = await UserIdOfAsync(email);
        var memberId = await fixture.MemberIdOfAsync(userId);

        await admin.PostAsync($"{Endpoint}/{memberId}/block", content: null);

        await using (var db = NewContext())
        {
            Assert.Equal(
                AccountStatus.Blocked,
                (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).Status);
            Assert.Equal(
                MembershipStatus.Blocked,
                (await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId)).Status);
        }

        await admin.PostAsync($"{Endpoint}/{memberId}/unblock", content: null);

        await using (var db = NewContext())
        {
            Assert.Equal(
                AccountStatus.Active,
                (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).Status);
            Assert.Equal(
                MembershipStatus.Active,
                (await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId)).Status);
        }
    }

    /// <summary>
    /// THE GUARD THAT STOPS THE CLUB LOCKING ITSELF OUT. Re-keying every route by member id moved
    /// this check to a new lookup path, which is exactly when a guard gets dropped by accident.
    /// </summary>
    [Fact]
    public async Task Blocking_an_admin_is_refused()
    {
        var admin = await AdminAsync();
        var userId = await UserIdOfAsync(TestUsers.ActiveAdminEmail);
        var memberId = await fixture.MemberIdOfAsync(userId);

        var response = await admin.PostAsync($"{Endpoint}/{memberId}/block", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("is_admin", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    // --- account-shaped actions on a record with no account -------------------

    /// <summary>
    /// Roles live in Identity, so an accountless record cannot hold one. S-14 makes an accountless
    /// INSTRUCTOR representable — the class's instructor becomes a member — and still refuses it here;
    /// see roadmap Open Question 3.
    /// </summary>
    [Fact]
    public async Task Granting_trainer_to_a_member_with_no_account_is_409_no_account()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin, Request(displayName: "Nie Trener"));

        var response = await admin.PostAsync($"{Endpoint}/{id}/roles/trainer", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_account", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    // --- the list -------------------------------------------------------------

    [Fact]
    public async Task The_list_includes_members_with_no_account()
    {
        var admin = await AdminAsync();
        var name = $"Na Liście {Guid.NewGuid():N}";
        var id = await CreateAsync(admin, Request(displayName: name));

        var rows = await admin.GetFromJsonAsync<MemberSummaryBody[]>(Endpoint);

        var row = Assert.Single(rows!, r => r.Id == id);
        Assert.Equal(name, row.DisplayName);
        Assert.Null(row.UserId);
        Assert.Null(row.AccountStatus);
        Assert.False(row.HasAccessCode);
    }

    [Fact]
    public async Task The_without_account_filter_returns_only_records_with_no_login()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin, Request(displayName: $"Filtr {Guid.NewGuid():N}"));

        var rows = await admin.GetFromJsonAsync<MemberSummaryBody[]>($"{Endpoint}?filter=WithoutAccount");

        Assert.Contains(rows!, r => r.Id == id);
        Assert.All(rows!, r => Assert.Null(r.UserId));
    }

    /// <summary>
    /// "Active" has to mean the same thing for a person with a login and a person without one, which
    /// is why the filter is not a projection of either status on its own.
    /// </summary>
    [Fact]
    public async Task The_active_filter_spans_both_kinds_of_member()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin, Request(displayName: $"Aktywny {Guid.NewGuid():N}"));

        var rows = await admin.GetFromJsonAsync<MemberSummaryBody[]>($"{Endpoint}?filter=Active");

        Assert.Contains(rows!, r => r.Id == id);
        Assert.Contains(rows!, r => r.Email == TestUsers.ActiveMemberEmail);
        Assert.DoesNotContain(rows!, r => r.Email == TestUsers.BlockedMemberEmail);
    }

    [Fact]
    public async Task The_blocked_filter_includes_a_blocked_accountless_member()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin, Request(displayName: $"Zablokowany {Guid.NewGuid():N}"));
        await admin.PostAsync($"{Endpoint}/{id}/block", content: null);

        var rows = await admin.GetFromJsonAsync<MemberSummaryBody[]>($"{Endpoint}?filter=Blocked");

        Assert.Contains(rows!, r => r.Id == id);
        Assert.All(rows!, r => Assert.Equal(nameof(MembershipStatus.Blocked), r.MembershipStatus));
    }

    /// <summary>
    /// An unparseable filter must be a broken request, not a silent fall-through to "no filter" — a
    /// typo in the SPA has to surface rather than quietly showing the admin everyone.
    /// </summary>
    [Fact]
    public async Task An_unknown_filter_value_is_400()
    {
        var admin = await AdminAsync();

        var response = await admin.GetAsync($"{Endpoint}?filter=Nonsense");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reading_a_member_that_does_not_exist_is_404()
    {
        var admin = await AdminAsync();

        var response = await admin.GetAsync($"{Endpoint}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> UserIdOfAsync(string email)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return (await userManager.FindByEmailAsync(email))!.Id;
    }
}
