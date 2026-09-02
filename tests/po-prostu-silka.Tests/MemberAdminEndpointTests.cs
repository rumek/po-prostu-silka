using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// The admin's approval surface (FR-003, FR-005) — the first production consumer of the Admin
/// policy, and the first production caller of F-03's outbox.
///
/// Every test that approves someone creates its own member: the fixture's seeded pending member is
/// shared, and flipping it to Active would silently break the tests that assert pending behaviour.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class MemberAdminEndpointTests(IntegrationTestFixture fixture)
{
    private sealed record PendingMemberBody(
        string Id, string Email, string DisplayName, DateTimeOffset CreatedAt);

    private sealed record ApproveFailureBody(string Reason);

    private sealed record TrainerRoleFailureBody(string Reason);

    /// <summary>Mirrors MemberSummary, including the Roles field S-04 added.</summary>
    private sealed record MemberSummaryBody(
        string Id,
        string Email,
        string DisplayName,
        string Status,
        string[] Roles,
        DateTimeOffset CreatedAt);

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    private async Task<(string Id, string Email)> CreateMemberAsync(AccountStatus status)
    {
        var email = $"admin-target-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, status, ApplicationRoles.User);

        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);

        return (user!.Id, email);
    }

    private static Task<int> CountApprovalEmailsAsync(AppDbContext db, string email) =>
        db.OutboxMessages.CountAsync(m =>
            m.Channel == NotificationChannel.Email && m.Recipient == email);

    // --- who may reach the group ----------------------------------------------

    [Fact]
    public async Task Anonymous_is_401()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/admin/members/pending");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Active_non_admin_is_403()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await client.GetAsync("/api/admin/members/pending");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The Admin policy requires Active AND the Admin role. A pending member now holds a session
    /// (S-01 D1), so this asserts that the session alone buys them nothing here.
    /// </summary>
    [Fact]
    public async Task Pending_member_is_403()
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.PendingMemberEmail);

        var response = await client.GetAsync("/api/admin/members/pending");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- the pending list -----------------------------------------------------

    [Fact]
    public async Task Admin_sees_pending_members_oldest_first()
    {
        var (_, email) = await CreateMemberAsync(AccountStatus.Pending);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var pending = await admin.GetFromJsonAsync<PendingMemberBody[]>("/api/admin/members/pending");

        Assert.Contains(pending!, p => p.Email == email);
        Assert.DoesNotContain(pending!, p => p.Email == TestUsers.ActiveMemberEmail);
        Assert.DoesNotContain(pending!, p => p.Email == TestUsers.BlockedMemberEmail);

        // The admin works a queue: whoever has waited longest is at the top.
        var createdAt = pending!.Select(p => p.CreatedAt).ToArray();
        Assert.Equal(createdAt.OrderBy(t => t).ToArray(), createdAt);
    }

    // --- approve --------------------------------------------------------------

    [Fact]
    public async Task Approve_activates_the_member_and_queues_exactly_one_email()
    {
        var (id, email) = await CreateMemberAsync(AccountStatus.Pending);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsync($"/api/admin/members/{id}/approve", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = NewContext();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        Assert.Equal(AccountStatus.Active, user.Status);

        // The status flip and the outbox row share one SaveChangesAsync — if the transaction had
        // been split, this is where a half-applied approval would show up.
        Assert.Equal(1, await CountApprovalEmailsAsync(db, email));
    }

    [Fact]
    public async Task Approving_twice_queues_exactly_one_email()
    {
        var (id, email) = await CreateMemberAsync(AccountStatus.Pending);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var first = await admin.PostAsync($"/api/admin/members/{id}/approve", content: null);
        var second = await admin.PostAsync($"/api/admin/members/{id}/approve", content: null);

        // Both succeed — two admins clicking the same row must not see an error — but only the
        // first enqueues. This is the whole point of the already-Active early return.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var db = NewContext();
        Assert.Equal(1, await CountApprovalEmailsAsync(db, email));
    }

    /// <summary>
    /// The same guarantee as the test above, but with the two approvals genuinely OVERLAPPING.
    ///
    /// The serialised case passes on the status check alone. This one does not: both requests read
    /// Pending before either writes, so without the concurrency-stamp rotation in ApproveAsync both
    /// updates match and the member is emailed twice. Each request gets its own HttpClient, so they
    /// run on separate scopes and separate DbContexts, which is what makes the race real.
    /// </summary>
    [Fact]
    public async Task Concurrent_approves_still_queue_exactly_one_email()
    {
        var (id, email) = await CreateMemberAsync(AccountStatus.Pending);

        var first = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        var second = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var responses = await Task.WhenAll(
            first.PostAsync($"/api/admin/members/{id}/approve", content: null),
            second.PostAsync($"/api/admin/members/{id}/approve", content: null));

        // Both callers are told it worked - the loser's answer is still true, because the member IS
        // approved. What must not double is the email.
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        await using var db = NewContext();
        Assert.Equal(AccountStatus.Active, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == id)).Status);
        Assert.Equal(1, await CountApprovalEmailsAsync(db, email));
    }

    [Fact]
    public async Task Approving_a_blocked_member_is_409_and_queues_nothing()
    {
        var (id, email) = await CreateMemberAsync(AccountStatus.Blocked);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsync($"/api/admin/members/{id}/approve", content: null);

        // Unblocking is S-02's action, and it has to answer what happens to the member's old
        // bookings — an open PRD question this endpoint must not quietly decide.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_pending", (await response.Content.ReadFromJsonAsync<ApproveFailureBody>())!.Reason);

        await using var db = NewContext();
        Assert.Equal(AccountStatus.Blocked, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == id)).Status);
        Assert.Equal(0, await CountApprovalEmailsAsync(db, email));
    }

    [Fact]
    public async Task Approving_an_unknown_id_is_404()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsync(
            $"/api/admin/members/{Guid.NewGuid()}/approve", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Approved_member_leaves_the_pending_list()
    {
        var (id, email) = await CreateMemberAsync(AccountStatus.Pending);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        await admin.PostAsync($"/api/admin/members/{id}/approve", content: null);

        var pending = await admin.GetFromJsonAsync<PendingMemberBody[]>("/api/admin/members/pending");
        Assert.DoesNotContain(pending!, p => p.Email == email);
    }

    // --- Trainer role (S-04, prd-v2 FR-001/FR-002/FR-003) ----------------------

    private async Task<bool> HoldsTrainerAsync(string id)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(id);

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
        var (id, _) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await HoldsTrainerAsync(id));
    }

    /// <summary>
    /// Additive, per FR-002: the grant must not cost the account the User role it registered with.
    /// </summary>
    [Fact]
    public async Task Granting_trainer_keeps_the_member_role()
    {
        var (id, email) = await CreateMemberAsync(AccountStatus.Active);
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

            var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
            var response = await admin.PostAsync(
                $"/api/admin/members/{target!.Id}/roles/trainer", content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var roles = await RolesOfAsync(fixture, email);
        Assert.Contains(ApplicationRoles.Admin, roles);
        Assert.Contains(ApplicationRoles.Trainer, roles);
    }

    [Fact]
    public async Task Revoking_trainer_removes_the_role()
    {
        var (id, _) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        var response = await admin.DeleteAsync($"/api/admin/members/{id}/roles/trainer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await HoldsTrainerAsync(id));
    }

    [Fact]
    public async Task Granting_trainer_twice_is_idempotent()
    {
        var (id, email) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);
        var second = await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(await RolesOfAsync(fixture, email), ApplicationRoles.Trainer);
    }

    [Fact]
    public async Task Revoking_a_role_the_member_does_not_hold_is_idempotent()
    {
        var (id, _) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.DeleteAsync($"/api/admin/members/{id}/roles/trainer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await HoldsTrainerAsync(id));
    }

    [Theory]
    [InlineData(AccountStatus.Pending)]
    [InlineData(AccountStatus.Blocked)]
    public async Task Granting_trainer_to_a_non_active_account_is_409(AccountStatus status)
    {
        var (id, _) = await CreateMemberAsync(status);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "not_active",
            (await response.Content.ReadFromJsonAsync<TrainerRoleFailureBody>())!.Reason);
        Assert.False(await HoldsTrainerAsync(id));
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
        var (id, email) = await CreateMemberAsync(AccountStatus.Active);
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        await admin.PostAsync($"/api/admin/members/{id}/roles/trainer", content: null);

        var members = await admin.GetFromJsonAsync<MemberSummaryBody[]>("/api/admin/members");

        var row = Assert.Single(members!, m => m.Email == email);
        Assert.Contains(ApplicationRoles.Trainer, row.Roles);
    }
}
