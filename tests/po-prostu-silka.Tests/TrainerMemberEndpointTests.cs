using System.Net;
using System.Net.Http.Json;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Tests;

/// <summary>
/// The trainer's way into a member's plan (S-22, UX-07/UX-08): <c>GET /api/trainer/members</c> and
/// <c>GET /api/trainer/members/{memberId}/plan</c>.
///
/// <para>
/// WHAT THIS SURFACE MUST NOT BECOME is the admin's member list with a looser policy. Two pins carry
/// that: the list never carries an e-mail address (asserted on the raw body, as the picker's pin was),
/// and its search never matches one — otherwise the result count alone would answer "does anyone's
/// address contain x".
/// </para>
///
/// <para>
/// The database is SHARED across the collection, so every test seeds its members under a marker
/// nobody else holds and searches for it. That is what makes the totals below exact.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class TrainerMemberEndpointTests(IntegrationTestFixture fixture)
{
    /// <summary>Mirrors TrainerMemberSummary.</summary>
    private sealed record TrainerMemberRow(Guid Id, string DisplayName, bool HasAccount, string? PlanName);

    /// <summary>Mirrors AssignableMember.</summary>
    private sealed record MemberBody(Guid Id, string DisplayName, bool HasAccount);

    /// <summary>Mirrors the fields of TrainingPlanDetail these tests read.</summary>
    private sealed record PlanBody(Guid Id, string Name, Guid MemberId);

    /// <summary>Mirrors MemberPlan.</summary>
    private sealed record MemberPlanBody(MemberBody Member, PlanBody? Plan);

    private sealed record ExerciseBody(Guid Id);

    private sealed record FailureBody(string Reason);

    private const string Endpoint = "/api/trainer/members";

    private static string NewMarker() => Guid.NewGuid().ToString("N")[..12];

    private static string Search(string term) => $"search={Uri.EscapeDataString(term)}";

    private Task<HttpClient> TrainerAsync() =>
        fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);

    private Task<HttpClient> AdminAsync() =>
        fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    private static async Task<MemberPageBody<TrainerMemberRow>> PageAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<MemberPageBody<TrainerMemberRow>>($"{Endpoint}?{query}"))!;

    private static async Task<MemberPlanBody> MemberPlanAsync(HttpClient client, Guid memberId) =>
        (await client.GetFromJsonAsync<MemberPlanBody>($"{Endpoint}/{memberId}/plan"))!;

    /// <summary>Assigns a plan through the real write path, so the read below sees what production writes.</summary>
    private async Task<PlanBody> AssignAsync(Guid memberId, string name)
    {
        var admin = await AdminAsync();

        var exercise = await admin.PostAsJsonAsync(
            "/api/admin/exercises", new { name = $"Przysiad-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.OK, exercise.StatusCode);
        var exerciseId = (await exercise.Content.ReadFromJsonAsync<ExerciseBody>())!.Id;

        var trainer = await TrainerAsync();
        var response = await trainer.PostAsJsonAsync(
            "/api/trainer/plans",
            new { name, memberId, items = new[] { new { exerciseId, sets = 3 } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlanBody>())!;
    }

    // --- who may reach the group ----------------------------------------------

    public static TheoryData<string> EveryRoute => new()
    {
        Endpoint,
        $"{Endpoint}/{Guid.Empty}/plan",
    };

    [Theory]
    [MemberData(nameof(EveryRoute))]
    public async Task Anonymous_is_401_on_every_route(string url)
    {
        var response = await fixture.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(EveryRoute))]
    public async Task Plain_member_is_403_on_every_route(string url)
    {
        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await member.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(TestUsers.ActiveTrainerEmail)]
    [InlineData(TestUsers.ActiveAdminEmail)]
    public async Task Trainer_and_admin_may_read_both_routes(string email)
    {
        var memberId = await fixture.CreateMemberAsync($"Dostęp {NewMarker()}");
        var client = await fixture.CreateAuthenticatedClientAsync(email);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Endpoint}/{memberId}/plan")).StatusCode);
    }

    // --- what a trainer learns about a member -----------------------------------

    /// <summary>
    /// THE LIST CARRIES NO E-MAIL, on either route. prd.md's privacy NFR keeps member data between the
    /// admin and the member, and this surface exists precisely because /api/admin/members could not be
    /// opened to trainers without handing them the club's contact list.
    ///
    /// <para>
    /// Asserted on the RAW body, against a value rather than a field name: deserializing into today's
    /// record would only prove the fields it declares.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_member_list_never_carries_an_email_address()
    {
        var marker = NewMarker();
        var email = $"trainer-list-privacy-{marker}@test.local";
        var displayName = $"Prywatność {marker}";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User, displayName);

        var admin = await AdminAsync();
        var memberId = await fixture.FindMemberIdAsync(admin, email);

        var trainer = await TrainerAsync();
        var list = await trainer.GetStringAsync($"{Endpoint}?{Search(marker)}");
        var plan = await trainer.GetStringAsync($"{Endpoint}/{memberId}/plan");

        // Listed — otherwise the absence below would prove nothing.
        Assert.Contains(displayName, list, StringComparison.Ordinal);
        Assert.Contains(displayName, plan, StringComparison.Ordinal);

        Assert.DoesNotContain(email, list, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, plan, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE SEARCH IS NAME-ONLY, and this is the test that notices if it stops being. A search that
    /// matched addresses would be an e-mail oracle: the count alone answers "does anyone's address
    /// contain x". The admin's list is asked the same question first, to prove the phrase really is
    /// in an address.
    /// </summary>
    [Fact]
    public async Task A_phrase_found_only_in_an_email_matches_nobody()
    {
        var marker = NewMarker();
        await fixture.CreateMemberAsync("Szukany Po Adresie", email: $"oracle-{marker}@example.test");

        var admin = await AdminAsync();
        var adminPage = await admin.GetFromJsonAsync<MemberPageBody<MemberBody>>(
            $"/api/admin/members?{Search(marker)}");
        Assert.Equal(1, adminPage!.Total);

        var page = await PageAsync(await TrainerAsync(), Search(marker));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    /// <summary>
    /// A DECISION, NOT AN ACCIDENT: the list has no role filter. Every account has a member row and
    /// the eligibility rule does not look at roles, so trainers and admins are here — the same set
    /// the picker offered, and admins train too.
    /// </summary>
    [Fact]
    public async Task The_list_includes_trainers_and_admins_who_are_active_members()
    {
        var marker = NewMarker();
        var adminEmail = $"list-admin-{marker}@test.local";
        var trainerEmail = $"list-trainer-{marker}@test.local";
        await fixture.CreateUserAsync(
            adminEmail, AccountStatus.Active, ApplicationRoles.Admin, $"Admin {marker}");
        await fixture.CreateUserAsync(
            trainerEmail, AccountStatus.Active, ApplicationRoles.User, $"Trener {marker}",
            additionalRole: ApplicationRoles.Trainer);

        var admin = await AdminAsync();
        var adminId = await fixture.FindMemberIdAsync(admin, adminEmail);
        var trainerId = await fixture.FindMemberIdAsync(admin, trainerEmail);

        var page = await PageAsync(await TrainerAsync(), Search(marker));

        Assert.Equal(2, page.Total);
        Assert.Contains(page.Items, m => m.Id == adminId);
        Assert.Contains(page.Items, m => m.Id == trainerId);
    }

    /// <summary>
    /// The same fold as the admin's search, through the same helper. <c>ł</c> is the case worth
    /// pinning: no accent-insensitive collation folds it, so the query does it by hand.
    /// </summary>
    [Theory]
    [InlineData("Łukasz", "lukasz")]
    [InlineData("Michał", "MICHAL")]
    [InlineData("Żaneta", "ZANETA")]
    [InlineData("Gęślicka", "geslicka")]
    public async Task A_name_search_ignores_case_and_polish_diacritics(string stored, string typed)
    {
        var marker = NewMarker();
        var id = await fixture.CreateMemberAsync($"{stored} {marker}");

        var page = await PageAsync(await TrainerAsync(), Search($"{typed} {marker}"));

        Assert.Equal(id, Assert.Single(page.Items).Id);
    }

    /// <summary>
    /// ACTIVE MEMBERS ONLY — the rule a plan may be created under, so a blocked member drops off this
    /// list exactly as they dropped off the picker. A member with no account is listed and says so.
    /// </summary>
    [Fact]
    public async Task The_list_offers_active_members_only()
    {
        var marker = NewMarker();
        var accountless = await fixture.CreateMemberAsync($"Aktywny {marker} bez konta");
        var blocked = await fixture.CreateMemberAsync($"Zablokowany {marker}", MembershipStatus.Blocked);

        var withAccountEmail = $"list-active-{marker}@test.local";
        await fixture.CreateUserAsync(
            withAccountEmail, AccountStatus.Active, ApplicationRoles.User, $"Aktywny {marker} z kontem");
        var blockedAccountEmail = $"list-blocked-{marker}@test.local";
        await fixture.CreateUserAsync(
            blockedAccountEmail, AccountStatus.Blocked, ApplicationRoles.User, $"Zablokowany {marker} z kontem");

        var admin = await AdminAsync();
        var withAccount = await fixture.FindMemberIdAsync(admin, withAccountEmail);

        var page = await PageAsync(await TrainerAsync(), Search(marker));

        Assert.Equal(2, page.Total);
        Assert.False(Assert.Single(page.Items, m => m.Id == accountless).HasAccount);
        Assert.True(Assert.Single(page.Items, m => m.Id == withAccount).HasAccount);
        Assert.DoesNotContain(page.Items, m => m.Id == blocked);
    }

    /// <summary>Only the ACTIVE plan counts: a replaced plan's name must not linger on the row.</summary>
    [Fact]
    public async Task A_row_names_the_active_plan_only()
    {
        var marker = NewMarker();
        var withPlan = await fixture.CreateMemberAsync($"Plan {marker} A");
        var replaced = await fixture.CreateMemberAsync($"Plan {marker} B");
        var withoutPlan = await fixture.CreateMemberAsync($"Plan {marker} C");

        await AssignAsync(withPlan, $"Masa {marker}");
        await AssignAsync(replaced, $"Stary {marker}");
        await AssignAsync(replaced, $"Nowy {marker}");

        var page = await PageAsync(await TrainerAsync(), Search($"Plan {marker}"));

        Assert.Equal($"Masa {marker}", page.Items.Single(m => m.Id == withPlan).PlanName);
        Assert.Equal($"Nowy {marker}", page.Items.Single(m => m.Id == replaced).PlanName);
        Assert.Null(page.Items.Single(m => m.Id == withoutPlan).PlanName);
    }

    // --- paging ---------------------------------------------------------------

    [Fact]
    public async Task The_list_pages_and_orders_by_name()
    {
        var marker = NewMarker();
        var c = await fixture.CreateMemberAsync($"Strona {marker} C");
        var a = await fixture.CreateMemberAsync($"Strona {marker} A");
        var b = await fixture.CreateMemberAsync($"Strona {marker} B");
        var trainer = await TrainerAsync();

        var first = await PageAsync(trainer, $"{Search(marker)}&pageSize=2");
        var second = await PageAsync(trainer, $"{Search(marker)}&pageSize=2&page=2");

        Assert.Equal(3, first.Total);
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.PageSize);
        Assert.Equal([a, b], first.Items.Select(m => m.Id));

        Assert.Equal(3, second.Total);
        Assert.Equal(2, second.Page);
        Assert.Equal([c], second.Items.Select(m => m.Id));
    }

    /// <summary>The same bounds and the same reasons as the admin's list — one validator serves both.</summary>
    public static TheoryData<string, string> InvalidListQueries => new()
    {
        { "page=0", "invalid_page" },
        { "pageSize=0", "invalid_page" },
        { $"pageSize={GetMembers.MaxPageSize + 1}", "invalid_page" },
        { $"page={int.MaxValue}&pageSize={GetMembers.MaxPageSize}", "invalid_page" },
        { $"search={new string('a', GetMembers.MaxSearchLength + 1)}", "invalid_search" },
    };

    [Theory]
    [MemberData(nameof(InvalidListQueries))]
    public async Task The_list_rejects_invalid_paging(string query, string reason)
    {
        var trainer = await TrainerAsync();

        var response = await trainer.GetAsync($"{Endpoint}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(reason, (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    // --- one member's plan ----------------------------------------------------

    /// <summary>No plan is an ordinary state, not a failure: 200, the member, and a null plan.</summary>
    [Fact]
    public async Task A_member_without_a_plan_reads_as_200_with_no_plan()
    {
        var name = $"Bez planu {NewMarker()}";
        var memberId = await fixture.CreateMemberAsync(name);

        var body = await MemberPlanAsync(await TrainerAsync(), memberId);

        Assert.Equal(memberId, body.Member.Id);
        Assert.Equal(name, body.Member.DisplayName);
        Assert.False(body.Member.HasAccount);
        Assert.Null(body.Plan);
    }

    [Fact]
    public async Task A_member_with_a_plan_reads_as_200_with_the_active_plan()
    {
        var memberId = await fixture.CreateMemberAsync($"Z planem {NewMarker()}");
        await AssignAsync(memberId, "Stary");
        var active = await AssignAsync(memberId, "Nowy");

        var body = await MemberPlanAsync(await TrainerAsync(), memberId);

        Assert.Equal(active.Id, body.Plan!.Id);
        Assert.Equal("Nowy", body.Plan.Name);
        Assert.Equal(memberId, body.Plan.MemberId);
    }

    /// <summary>
    /// Status is NOT filtered here: an admin must reach a blocked member's plan to read or edit it,
    /// even though the list no longer offers that member.
    /// </summary>
    [Fact]
    public async Task A_blocked_members_plan_is_readable_by_an_admin()
    {
        var memberId = await fixture.CreateMemberAsync($"Zablokowany z planem {NewMarker()}");
        var plan = await AssignAsync(memberId, "Masa");

        var admin = await AdminAsync();
        var blocked = await admin.PostAsync($"/api/admin/members/{memberId}/block", null);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);

        var body = await MemberPlanAsync(admin, memberId);

        Assert.Equal(plan.Id, body.Plan!.Id);
    }

    /// <summary>
    /// A decision, not an accident: a trainer who reaches a blocked member by id gets the name AND the
    /// active plan — no wider than the retired plan list, which showed every active plan regardless
    /// of the member's status. Pinned so narrowing or widening it is a visible change.
    /// </summary>
    [Fact]
    public async Task A_blocked_members_plan_is_readable_by_a_trainer_who_knows_the_id()
    {
        var memberId = await fixture.CreateMemberAsync($"Zablokowany dla trenera {NewMarker()}");
        var plan = await AssignAsync(memberId, "Siła");

        var blocked = await (await AdminAsync()).PostAsync($"/api/admin/members/{memberId}/block", null);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);

        var body = await MemberPlanAsync(await TrainerAsync(), memberId);

        Assert.Equal(plan.Id, body.Plan!.Id);
    }

    [Fact]
    public async Task An_unknown_member_is_404()
    {
        var trainer = await TrainerAsync();

        var response = await trainer.GetAsync($"{Endpoint}/{Guid.NewGuid()}/plan");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
