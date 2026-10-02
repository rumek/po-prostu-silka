using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
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
        DateTimeOffset CreatedAt,
        DateOnly? PassValidTo = null,
        int? PassEntriesLeft = null,
        bool HasUnpaidPass = false);

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

    /// <summary>
    /// NO EMAIL FIELD since S-17 — the admin surface stopped accepting one, and the payload here IS
    /// the contract. An address reaches a member row exactly once, from RegisterAsync.
    /// </summary>
    private static object Request(
        string displayName = "Jan Kowalski",
        string? phoneNumber = null,
        string? street = null,
        string? houseNumber = null,
        string? postalCode = null,
        string? city = null) =>
        new { displayName, phoneNumber, street, houseNumber, postalCode, city };

    private static object FullContact(string displayName) =>
        new
        {
            displayName,
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

        var id = await CreateAsync(admin, FullContact("Anna Nowak"));

        var detail = await admin.GetFromJsonAsync<MemberDetailBody>($"{Endpoint}/{id}");

        // NO ADDRESS, however complete the rest is (S-17). "Full contact details" now means the phone
        // and the postal address; the email is not the desk's to give.
        Assert.Null(detail!.Email);
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
    /// THE DESK CANNOT SET AN ADDRESS, even by sending one (S-17). The field left the contract, so a
    /// caller that still supplies it is not refused — it is simply ignored, which is what a removed
    /// field means over JSON. Worth pinning: a stale client, or a curl, must not be able to write a
    /// login address into somebody's record from the admin surface.
    /// </summary>
    [Fact]
    public async Task An_address_sent_to_the_admin_surface_is_ignored_rather_than_stored()
    {
        var admin = await AdminAsync();
        var email = $"smuggled-{Guid.NewGuid():N}@test.local";

        var created = await admin.PostAsJsonAsync(
            Endpoint,
            new { displayName = "Przemycony Adres", email, phoneNumber = (string?)null });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedBody>())!.Id;

        var detail = await admin.GetFromJsonAsync<MemberDetailBody>($"{Endpoint}/{id}");
        Assert.Null(detail!.Email);
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

    /// <summary>
    /// THE SHARPEST EDGE OF S-17. The admin form stopped sending an address, so an edit that assigned
    /// the request's value would null the login address the member registered with — losing it to an
    /// admin correcting a typo in a phone number. UpdateAsync deliberately does not touch Email at
    /// all, and this is what says so.
    /// </summary>
    [Fact]
    public async Task Editing_leaves_the_login_address_the_member_registered_with_untouched()
    {
        var admin = await AdminAsync();
        var email = $"registered-{Guid.NewGuid():N}@test.local";

        // A record whose address came from registration, which is the only writer of one now.
        var memberId = await fixture.CreateMemberAsync("Zarejestrowany", email: email);

        var response = await admin.PutAsJsonAsync(
            $"{Endpoint}/{memberId}", FullContact("Zarejestrowany Poprawiony"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await admin.GetFromJsonAsync<MemberDetailBody>($"{Endpoint}/{memberId}");
        Assert.Equal("Zarejestrowany Poprawiony", detail!.DisplayName);
        Assert.Equal("Kraków", detail.City);

        // The address survived the edit that never mentioned it.
        Assert.Equal(email, detail.Email);
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

        var rows = await ListAsync(admin, $"search={Uri.EscapeDataString(name)}");

        var row = Assert.Single(rows, r => r.Id == id);
        Assert.Equal(name, row.DisplayName);
        Assert.Null(row.UserId);
        Assert.Null(row.AccountStatus);
        Assert.False(row.HasAccessCode);
    }

    [Fact]
    public async Task The_without_account_filter_returns_only_records_with_no_login()
    {
        var admin = await AdminAsync();
        var name = $"Filtr {Guid.NewGuid():N}";
        var id = await CreateAsync(admin, Request(displayName: name));

        // The unique name, not a shared word: a generic phrase only "works" while every match fits on
        // one page, which is the assumption S-21 removed. The exclusion is asked for directly instead.
        var rows = await ListAsync(admin, $"filter=WithoutAccount&search={Uri.EscapeDataString(name)}");

        Assert.Contains(rows, r => r.Id == id);
        Assert.All(rows, r => Assert.Null(r.UserId));
        Assert.Empty(await ListAsync(admin, $"filter=WithoutAccount&search={TestUsers.ActiveMemberEmail}"));
    }

    /// <summary>
    /// "Active" has to mean the same thing for a person with a login and a person without one, which
    /// is why the filter is not a projection of either status on its own.
    /// </summary>
    [Fact]
    public async Task The_active_filter_spans_both_kinds_of_member()
    {
        var admin = await AdminAsync();
        var name = $"Aktywny {Guid.NewGuid():N}";
        var id = await CreateAsync(admin, Request(displayName: name));

        // Searched, one person at a time: the list is paged since S-21, so "is X in the whole
        // filtered list" is only answerable by asking for X.
        Assert.Contains(await ListAsync(admin, $"filter=Active&search={Uri.EscapeDataString(name)}"), r => r.Id == id);
        Assert.Contains(
            await ListAsync(admin, $"filter=Active&search={TestUsers.ActiveMemberEmail}"),
            r => r.Email == TestUsers.ActiveMemberEmail);
        Assert.DoesNotContain(
            await ListAsync(admin, $"filter=Active&search={TestUsers.BlockedMemberEmail}"),
            r => r.Email == TestUsers.BlockedMemberEmail);
    }

    [Fact]
    public async Task The_blocked_filter_includes_a_blocked_accountless_member()
    {
        var admin = await AdminAsync();
        var name = $"Zablokowany {Guid.NewGuid():N}";
        var id = await CreateAsync(admin, Request(displayName: name));
        await admin.PostAsync($"{Endpoint}/{id}/block", content: null);

        // The unique name — see The_without_account_filter_returns_only_records_with_no_login.
        var rows = await ListAsync(admin, $"filter=Blocked&search={Uri.EscapeDataString(name)}");

        Assert.Contains(rows, r => r.Id == id);
        Assert.All(rows, r => Assert.Equal(nameof(MembershipStatus.Blocked), r.MembershipStatus));
        Assert.Empty(await ListAsync(admin, $"filter=Blocked&search={TestUsers.ActiveMemberEmail}"));
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

    /// <summary>The page's rows for a query string. Page size at the maximum, so a marker search is never cut short.</summary>
    /// <summary>
    /// The role filter reads the PERSONA (S-25), so the positions partition the list: an
    /// Admin+Trainer account is an admin and only an admin, and a record with no login is a member.
    /// </summary>
    [Theory]
    [InlineData("Member", TestUsers.ActiveMemberEmail, true)]
    [InlineData("Member", TestUsers.ActiveTrainerEmail, false)]
    [InlineData("Member", TestUsers.ActiveAdminEmail, false)]
    [InlineData("Trainer", TestUsers.ActiveTrainerEmail, true)]
    [InlineData("Trainer", TestUsers.ActiveMemberEmail, false)]
    [InlineData("Trainer", TestUsers.ActiveAdminTrainerEmail, false)]
    [InlineData("Admin", TestUsers.ActiveAdminEmail, true)]
    [InlineData("Admin", TestUsers.ActiveAdminTrainerEmail, true)]
    [InlineData("Admin", TestUsers.ActiveTrainerEmail, false)]
    public async Task The_role_filter_selects_by_persona(string role, string email, bool listed)
    {
        var admin = await AdminAsync();

        var rows = await ListAsync(admin, $"role={role}&search={Uri.EscapeDataString(email)}");

        Assert.Equal(listed, rows.Any(r => r.Email == email));
    }

    [Fact]
    public async Task The_member_role_filter_includes_a_record_without_an_account()
    {
        var admin = await AdminAsync();
        var name = $"Rola {Guid.NewGuid():N}";
        var id = await CreateAsync(admin, Request(displayName: name));

        Assert.Contains(await ListAsync(admin, $"role=Member&search={Uri.EscapeDataString(name)}"), r => r.Id == id);
        Assert.Empty(await ListAsync(admin, $"role=Trainer&search={Uri.EscapeDataString(name)}"));
    }

    [Fact]
    public async Task An_unknown_role_filter_is_refused()
    {
        var admin = await AdminAsync();

        var response = await admin.GetAsync($"{Endpoint}?role=Owner");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The list's karnet column reads the pass covering TODAY: its last day and the entries it has
    /// left. A pass that ended, or none at all, is the same answer — null in both.
    /// </summary>
    [Fact]
    public async Task The_list_carries_the_pass_covering_today()
    {
        var admin = await AdminAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var current = await fixture.CreateMemberAsync($"Karnet {Guid.NewGuid():N}");
        await fixture.IssuePassAsync(current, entryCount: 8, validFrom: today.AddDays(-3), validTo: today.AddDays(20));

        var expired = await fixture.CreateMemberAsync($"Karnet {Guid.NewGuid():N}");
        await fixture.IssuePassAsync(expired, entryCount: 8, validFrom: today.AddDays(-40), validTo: today.AddDays(-10));

        var none = await fixture.CreateMemberAsync($"Karnet {Guid.NewGuid():N}");

        var rows = await ListAsync(admin, "search=Karnet");

        var withPass = Assert.Single(rows, r => r.Id == current);
        Assert.Equal(today.AddDays(20), withPass.PassValidTo);
        Assert.Equal(8, withPass.PassEntriesLeft);

        Assert.All(rows.Where(r => r.Id == expired || r.Id == none), r =>
        {
            Assert.Null(r.PassValidTo);
            Assert.Null(r.PassEntriesLeft);
        });
        Assert.Equal(2, rows.Count(r => r.Id == expired || r.Id == none));
    }

    /// <summary>
    /// "Nieopłacony" means ANY unpaid karnet (pass-paid-flag): a month that expired unpaid beside a
    /// paid current one still marks the member — the debt outlives the validity.
    /// </summary>
    [Fact]
    public async Task The_list_marks_a_member_with_any_unpaid_karnet()
    {
        var admin = await AdminAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var marker = $"Dług {Guid.NewGuid():N}";

        var owing = await fixture.CreateMemberAsync($"{marker} A");
        await fixture.IssuePassAsync(owing, validFrom: today.AddDays(-60), validTo: today.AddDays(-31));
        await fixture.IssuePassAsync(owing, validFrom: today.AddDays(-30), validTo: today.AddDays(10), paidAt: today);

        var settled = await fixture.CreateMemberAsync($"{marker} B");
        await fixture.IssuePassAsync(settled, validFrom: today.AddDays(-30), validTo: today.AddDays(10), paidAt: today);

        var none = await fixture.CreateMemberAsync($"{marker} C");

        var rows = await ListAsync(admin, $"search={Uri.EscapeDataString(marker)}");

        Assert.True(rows.Single(r => r.Id == owing).HasUnpaidPass);
        Assert.False(rows.Single(r => r.Id == settled).HasUnpaidPass);
        Assert.False(rows.Single(r => r.Id == none).HasUnpaidPass);
    }

    /// <summary>
    /// The unpaid filter is ORTHOGONAL to the status filter: "active and unpaid" is expressible, and the
    /// total counts the filtered set.
    /// </summary>
    [Fact]
    public async Task The_unpaid_filter_combines_with_the_status_filter()
    {
        var admin = await AdminAsync();
        var marker = $"Filtr {Guid.NewGuid():N}";

        var activeOwing = await fixture.CreateMemberAsync($"{marker} A");
        await fixture.IssuePassAsync(activeOwing);

        var blockedOwing = await fixture.CreateMemberAsync($"{marker} B");
        await fixture.IssuePassAsync(blockedOwing);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Endpoint}/{blockedOwing}/block", null)).StatusCode);

        var activePaid = await fixture.CreateMemberAsync($"{marker} C");
        await fixture.IssuePassAsync(activePaid, paidAt: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        var search = $"search={Uri.EscapeDataString(marker)}";

        var unpaid = await admin.GetFromJsonAsync<MemberPageBody<MemberSummaryBody>>($"{Endpoint}?{search}&unpaid=true");
        Assert.Equal(2, unpaid!.Total);
        Assert.Equal(
            new HashSet<Guid> { activeOwing, blockedOwing },
            unpaid.Items.Select(r => r.Id).ToHashSet());

        var activeUnpaid = await admin.GetFromJsonAsync<MemberPageBody<MemberSummaryBody>>($"{Endpoint}?{search}&unpaid=true&filter=Active");
        Assert.Equal(activeOwing, Assert.Single(activeUnpaid!.Items).Id);
        Assert.Equal(1, activeUnpaid.Total);

        var everyone = await admin.GetFromJsonAsync<MemberPageBody<MemberSummaryBody>>($"{Endpoint}?{search}");
        Assert.Equal(3, everyone!.Total);
    }

    /// <summary>
    /// A member granted Trainer keeps their karnets (S-25), but the payment route refuses a staff holder
    /// - so an unpaid karnet would mark them a debtor nobody can settle. Staff are never unpaid: neither
    /// the marker nor the filter counts them.
    /// </summary>
    [Fact]
    public async Task A_member_promoted_to_staff_is_neither_marked_nor_filtered_as_unpaid()
    {
        var admin = await AdminAsync();
        var marker = $"Awans {Guid.NewGuid():N}";
        var email = $"promoted-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User, displayName: marker);
        var memberId = await fixture.MemberIdOfAsync(await UserIdOfAsync(email));
        await fixture.IssuePassAsync(memberId);

        var search = $"search={Uri.EscapeDataString(marker)}";
        Assert.True((await ListAsync(admin, search)).Single(r => r.Id == memberId).HasUnpaidPass);

        var grant = await admin.PostAsync($"{Endpoint}/{memberId}/roles/trainer", content: null);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        Assert.False((await ListAsync(admin, search)).Single(r => r.Id == memberId).HasUnpaidPass);
        var unpaid = await admin.GetFromJsonAsync<MemberPageBody<MemberSummaryBody>>($"{Endpoint}?{search}&unpaid=true");
        Assert.Equal(0, unpaid!.Total);
    }

    // --- expiring karnets (expiring-passes-dashboard) ---------------------------

    /// <summary>
    /// The window is the club spreadsheet's: last day between today and today + 5, BOTH inclusive.
    /// Yesterday's end is expired, not ending; today + 6 is not yet ending.
    /// </summary>
    [Fact]
    public async Task The_expiring_filter_takes_ends_from_today_to_today_plus_five_inclusive()
    {
        var admin = await AdminAsync();
        var today = ClubToday();
        var marker = $"Okno {Guid.NewGuid():N}";

        var endsToday = await MemberWithPassAsync($"{marker} A", today.AddDays(-29), today);
        var endsInFive = await MemberWithPassAsync($"{marker} B", today.AddDays(-20), today.AddDays(5));
        var endsInSix = await MemberWithPassAsync($"{marker} C", today.AddDays(-20), today.AddDays(6));
        var endedYesterday = await MemberWithPassAsync($"{marker} D", today.AddDays(-30), today.AddDays(-1));

        var expiring = await ExpiringIdsAsync(admin, marker);

        Assert.Contains(endsToday, expiring);
        Assert.Contains(endsInFive, expiring);
        Assert.DoesNotContain(endsInSix, expiring);
        Assert.DoesNotContain(endedYesterday, expiring);
    }

    /// <summary>
    /// A renewal issued in advance is what the app knows and the spreadsheet cannot: ANY later karnet
    /// takes the member off — back-to-back or after a gap.
    /// </summary>
    [Fact]
    public async Task A_member_holding_a_later_karnet_is_not_expiring()
    {
        var admin = await AdminAsync();
        var today = ClubToday();
        var marker = $"Odnowa {Guid.NewGuid():N}";

        var backToBack = await MemberWithPassAsync($"{marker} A", today.AddDays(-25), today.AddDays(2));
        await fixture.IssuePassAsync(backToBack, validFrom: today.AddDays(3), validTo: today.AddDays(32));

        var afterGap = await MemberWithPassAsync($"{marker} B", today.AddDays(-25), today.AddDays(2));
        await fixture.IssuePassAsync(afterGap, validFrom: today.AddDays(20), validTo: today.AddDays(49));

        var notRenewed = await MemberWithPassAsync($"{marker} C", today.AddDays(-25), today.AddDays(2));

        Assert.Equal([notRenewed], await ExpiringIdsAsync(admin, marker));
    }

    /// <summary>
    /// Tied to the end date ONLY: an unpaid karnet is still ending (unpaid is pass-paid-flag's filter),
    /// and so is one with no entries left. A karnet that has not started is never ending — there is
    /// nothing current to renew.
    /// </summary>
    [Fact]
    public async Task Payment_and_entries_do_not_matter_and_a_future_karnet_is_not_expiring()
    {
        var admin = await AdminAsync();
        var today = ClubToday();
        var marker = $"Oś {Guid.NewGuid():N}";

        var unpaid = await MemberWithPassAsync($"{marker} A", today.AddDays(-25), today.AddDays(3));

        var spent = await fixture.CreateMemberAsync($"{marker} B");
        var spentPass = await fixture.IssuePassAsync(
            spent, entryCount: 1, validFrom: today.AddDays(-25), validTo: today.AddDays(3), paidAt: today.AddDays(-25));
        await SpendAnEntryAsync(spent, spentPass);

        var futureOnly = await fixture.CreateMemberAsync($"{marker} C");
        await fixture.IssuePassAsync(futureOnly, validFrom: today.AddDays(1), validTo: today.AddDays(3));

        var expiring = await ExpiringIdsAsync(admin, marker);

        Assert.Contains(unpaid, expiring);
        Assert.Contains(spent, expiring);
        Assert.DoesNotContain(futureOnly, expiring);

        // The spent one really is spent — otherwise this test proves nothing about entries.
        var rows = await ListAsync(admin, $"search={Uri.EscapeDataString(marker)}");
        Assert.Equal(0, rows.Single(r => r.Id == spent).PassEntriesLeft);
    }

    /// <summary>
    /// The card means "to renew": a blocked member is not, and neither is staff — the pass routes refuse
    /// a staff holder, so it would be a renewal nobody could issue.
    /// </summary>
    [Fact]
    public async Task Blocked_members_and_staff_are_not_expiring()
    {
        var admin = await AdminAsync();
        var today = ClubToday();
        var marker = $"Wyjątek {Guid.NewGuid():N}";

        var blocked = await MemberWithPassAsync($"{marker} A", today.AddDays(-25), today.AddDays(1));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Endpoint}/{blocked}/block", null)).StatusCode);

        var email = $"expiring-staff-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User, displayName: $"{marker} B");
        var promoted = await fixture.MemberIdOfAsync(await UserIdOfAsync(email));
        await fixture.IssuePassAsync(promoted, validFrom: today.AddDays(-25), validTo: today.AddDays(1));
        Assert.Equal([promoted], await ExpiringIdsAsync(admin, marker));

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Endpoint}/{promoted}/roles/trainer", null)).StatusCode);

        Assert.Empty(await ExpiringIdsAsync(admin, marker));
    }

    /// <summary>Orthogonal to the other filters, and the total counts the filtered set.</summary>
    [Fact]
    public async Task The_expiring_filter_combines_with_the_unpaid_and_status_filters()
    {
        var admin = await AdminAsync();
        var today = ClubToday();
        var marker = $"Kombi {Guid.NewGuid():N}";

        var unpaid = await MemberWithPassAsync($"{marker} A", today.AddDays(-25), today.AddDays(4));
        var paid = await fixture.CreateMemberAsync($"{marker} B");
        await fixture.IssuePassAsync(paid, validFrom: today.AddDays(-25), validTo: today.AddDays(4), paidAt: today.AddDays(-25));
        await MemberWithPassAsync($"{marker} C", today.AddDays(-5), today.AddDays(25));

        var search = $"search={Uri.EscapeDataString(marker)}";

        var expiring = await admin.GetFromJsonAsync<MemberPageBody<MemberSummaryBody>>($"{Endpoint}?{search}&expiring=true");
        Assert.Equal(2, expiring!.Total);

        var expiringUnpaid = await admin.GetFromJsonAsync<MemberPageBody<MemberSummaryBody>>(
            $"{Endpoint}?{search}&expiring=true&unpaid=true&filter=Active");
        Assert.Equal(unpaid, Assert.Single(expiringUnpaid!.Items).Id);
        Assert.Equal(1, expiringUnpaid.Total);
    }

    /// <summary>Club-local, as MemberQuery reads it.</summary>
    private static DateOnly ClubToday() =>
        DateOnly.FromDateTime(ClubTime.ToClubLocal(DateTimeOffset.UtcNow).DateTime);

    private async Task<Guid> MemberWithPassAsync(string name, DateOnly validFrom, DateOnly validTo)
    {
        var id = await fixture.CreateMemberAsync(name);
        await fixture.IssuePassAsync(id, validFrom: validFrom, validTo: validTo);
        return id;
    }

    private async Task<List<Guid>> ExpiringIdsAsync(HttpClient admin, string marker) =>
        (await ListAsync(admin, $"search={Uri.EscapeDataString(marker)}&expiring=true")).Select(r => r.Id).ToList();

    /// <summary>
    /// Slot allocator for <see cref="SpendAnEntryAsync"/>, 2045 rather than "now + something": the
    /// no-overlap rule is club-wide and this container is shared, so a near-now class could refuse
    /// another file's with time_conflict depending on execution order. ClassEndpointTests (2030) and
    /// BookingEndpointTests (2032) keep bases of their own for the same reason. The class's date does
    /// not matter to entry consumption.
    /// </summary>
    private static int _spendSlot;

    private static DateTimeOffset NextSpendSlot() =>
        new DateTimeOffset(2045, 1, 1, 10, 0, 0, TimeSpan.Zero).AddDays(Interlocked.Increment(ref _spendSlot));

    /// <summary>
    /// An active booking carrying the karnet, so its one entry is used. Written directly: the booking
    /// route's own rules are not what this suite tests. The member instructs their own class only
    /// because the instructor foreign key needs somebody.
    /// </summary>
    private async Task SpendAnEntryAsync(Guid memberId, Guid passId)
    {
        await using var db = NewContext();

        var group = new ClassGroup
        {
            Id = Guid.NewGuid(),
            Name = $"Expiring {Guid.NewGuid():N}",
            DefaultDurationMinutes = 60,
            DefaultCapacity = 5,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var cls = new Class
        {
            Id = Guid.NewGuid(),
            ClassGroupId = group.Id,
            InstructorMemberId = memberId,
            StartsAt = NextSpendSlot(),
            DurationMinutes = 60,
            Capacity = 5,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.ClassGroups.Add(group);
        db.Classes.Add(cls);
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            ClassId = cls.Id,
            MemberId = memberId,
            MembershipPassId = passId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<MemberSummaryBody>> ListAsync(HttpClient admin, string query) =>
        (await admin.GetFromJsonAsync<MemberPageBody<MemberSummaryBody>>($"{Endpoint}?{query}&pageSize=100"))!.Items;

    private async Task<string> UserIdOfAsync(string email)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return (await userManager.FindByEmailAsync(email))!.Id;
    }
}
