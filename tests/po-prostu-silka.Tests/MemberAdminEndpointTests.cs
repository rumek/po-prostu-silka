using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// The admin's member surface (FR-004, FR-005) and the Trainer role that lives on it — the first
/// production consumer of the Admin policy.
///
/// <para>
/// THE APPROVAL HALF IS GONE (S-16, MP-03). This file was built around <c>GET /pending</c> and
/// <c>POST /{id}/approve</c>, and every one of those tests went with the routes. What is left is the
/// group's policy, the member list and its filters, block/unblock, the Trainer role and access
/// codes — all untouched by the retirement.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class MemberAdminEndpointTests(IntegrationTestFixture fixture)
{
    private sealed record TrainerRoleFailureBody(string Reason);

    /// <summary>
    /// Mirrors MemberSummary — the Roles field S-04 added, and S-14's split of one status into two.
    /// </summary>
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

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    /// <summary>
    /// Seeds an account and returns BOTH ids. Since S-14 the two are different things and the tests
    /// need both: every route on this surface is addressed by <c>MemberId</c>, while the assertions
    /// that read a status back out of Identity need the <c>UserId</c>.
    /// </summary>
    private async Task<(Guid MemberId, string UserId, string Email)> CreateMemberAsync(
        AccountStatus status)
    {
        var email = $"admin-target-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, status, ApplicationRoles.User);

        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.Members.AsNoTracking().SingleAsync(m => m.UserId == user!.Id);

        return (member.Id, user!.Id, email);
    }

    // --- who may reach the group ----------------------------------------------

    /// <summary>
    /// The GROUP's policy, probed through the member list.
    ///
    /// <para>
    /// It used to be probed through <c>GET /pending</c>, which S-16 removed — and the swap matters
    /// more than it looks: an unmapped path falls through to <c>MapFallbackToFile</c> and answers 200
    /// with index.html, so a policy test left pointing at a deleted route passes nothing while
    /// LOOKING like it failed loudly. The route named here must always be one that exists.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Anonymous_is_401()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/admin/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Active_non_admin_is_403()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await client.GetAsync("/api/admin/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The Admin policy requires an Active account AND the Admin role. A blocked member holds no
    /// session at all — login refuses them — so the case worth pinning is the one this asserts: an
    /// ordinary signed-in member gets nothing here from the session alone.
    /// </summary>
    [Fact]
    public async Task A_trainer_is_403_on_the_admin_surface()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);

        var response = await client.GetAsync("/api/admin/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Trainer role (S-04, prd-v2 FR-001/FR-002/FR-003) ----------------------

    private async Task<bool> HoldsTrainerAsync(string userId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);

        return await userManager.IsInRoleAsync(user!, ApplicationRoles.Trainer);
    }

    private static async Task<IList<string>> RolesOfAsync(IntegrationTestFixture fixture, string email)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);

        return await userManager.GetRolesAsync(user!);
    }

    [Fact]
    public async Task Granting_trainer_to_an_active_member_succeeds()
    {
        var (id, userId, _) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await HoldsTrainerAsync(userId));
    }

    /// <summary>
    /// Additive, per FR-002: the grant must not cost the account the User role it registered with.
    /// </summary>
    [Fact]
    public async Task Granting_trainer_keeps_the_member_role()
    {
        var (id, userId, email) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        var roles = await RolesOfAsync(fixture, email);
        Assert.Contains(ApplicationRoles.User, roles);
        Assert.Contains(ApplicationRoles.Trainer, roles);
    }

    /// <summary>
    /// FR-003 — the owner who teaches. This is the case the member list stopped excluding admins
    /// for; without it the grant surface could never reach an admin account.
    /// </summary>
    [Fact]
    public async Task Granting_trainer_to_an_admin_succeeds()
    {
        var email = $"admin-trainer-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.Admin);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var target = await userManager.FindByEmailAsync(email);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var member = await db.Members.AsNoTracking().SingleAsync(m => m.UserId == target!.Id);

            var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
            var response = await admin.PostAsync(
                $"/api/admin/members/{member.Id}/roles/trainer", content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var roles = await RolesOfAsync(fixture, email);
        Assert.Contains(ApplicationRoles.Admin, roles);
        Assert.Contains(ApplicationRoles.Trainer, roles);
    }

    [Fact]
    public async Task Revoking_trainer_removes_the_role()
    {
        var (id, userId, _) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        var response = await admin.DeleteAsync($"/api/admin/members/{id}/roles/trainer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await HoldsTrainerAsync(userId));
    }

    [Fact]
    public async Task Granting_trainer_twice_is_idempotent()
    {
        var (id, userId, email) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);
        var second = await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(await RolesOfAsync(fixture, email), ApplicationRoles.Trainer);
    }

    [Fact]
    public async Task Revoking_a_role_the_member_does_not_hold_is_idempotent()
    {
        var (id, userId, _) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.DeleteAsync($"/api/admin/members/{id}/roles/trainer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await HoldsTrainerAsync(userId));
    }

    [Theory]
    [InlineData(AccountStatus.Pending)]
    [InlineData(AccountStatus.Blocked)]
    public async Task Granting_trainer_to_a_non_active_account_is_409(AccountStatus status)
    {
        var (id, userId, _) = await CreateMemberAsync(status);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "not_active",
            (await response.Content.ReadFromJsonAsync<TrainerRoleFailureBody>())!.Reason);
        Assert.False(await HoldsTrainerAsync(userId));
    }

    /// <summary>
    /// The revoke side of the status guard — Phase 1's adaptation #3, and the one direction that is
    /// not an obvious mirror of the other. A member who was granted the role and later blocked KEEPS
    /// it: revoking is refused, and S-06 filters the instructor selection by status instead.
    /// </summary>
    [Fact]
    public async Task Revoking_trainer_from_a_non_active_account_is_409()
    {
        var (id, userId, _) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);
        await admin.PostAsync($"/api/admin/members/{id}/block", content: null);

        var response = await admin.DeleteAsync($"/api/admin/members/{id}/roles/trainer");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "not_active",
            (await response.Content.ReadFromJsonAsync<TrainerRoleFailureBody>())!.Reason);

        // The role survives the refusal — that is the documented consequence, not a side effect.
        Assert.True(await HoldsTrainerAsync(userId));
    }

    [Fact]
    public async Task Granting_trainer_to_an_unknown_id_is_404()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsync(
            $"/api/admin/members/{Guid.NewGuid()}/roles/trainer", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoking_trainer_from_an_unknown_id_is_404()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.DeleteAsync(
            $"/api/admin/members/{Guid.NewGuid()}/roles/trainer");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- the member list after the admin exclusion was lifted -----------------

    /// <summary>
    /// The list used to filter admins out structurally. S-04 lifted that so FR-003's grant can
    /// reach them; the protection now lives solely in BlockAsync's is_admin check. That check is
    /// covered by MANUAL verification (plan step 1.6), not by a test here — a deliberate scoping
    /// decision recorded in the plan's Open Risks.
    /// </summary>
    [Fact]
    public async Task Member_list_includes_admins_with_their_roles()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var members = await admin.GetFromJsonAsync<MemberSummaryBody[]>("/api/admin/members");

        var adminRow = Assert.Single(members!, m => m.Email == TestUsers.ActiveAdminEmail);
        Assert.Contains(ApplicationRoles.Admin, adminRow.Roles);

        var memberRow = Assert.Single(members!, m => m.Email == TestUsers.ActiveMemberEmail);
        Assert.Contains(ApplicationRoles.User, memberRow.Roles);
        Assert.DoesNotContain(ApplicationRoles.Admin, memberRow.Roles);
    }

    [Fact]
    public async Task Member_list_reports_a_granted_trainer_role()
    {
        var (id, userId, email) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        var members = await admin.GetFromJsonAsync<MemberSummaryBody[]>("/api/admin/members");

        var row = Assert.Single(members!, m => m.Email == email);
        Assert.Contains(ApplicationRoles.Trainer, row.Roles);
    }
}
