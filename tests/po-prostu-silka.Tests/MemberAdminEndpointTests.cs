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
}
