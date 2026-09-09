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
/// S-14's authorization half: the membership status is part of every policy, and it can bar someone
/// whose ACCOUNT is untouched.
///
/// <para>
/// This is the riskiest thing the slice does, and the risk runs both ways. Too loose and a blocked
/// member keeps reading the schedule; too strict — specifically, an account with no member row — and
/// every policy refuses them, `Admin` included, which locks the club out of its own app. Both
/// directions are pinned here.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class MemberBlockPolicyTests(IntegrationTestFixture fixture)
{
    private sealed record CurrentUserBody(
        string Id,
        string Email,
        string DisplayName,
        string Status,
        string[] Roles,
        Guid? MemberId,
        string? MembershipStatus);

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    private async Task<string> UserIdOfAsync(string email)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return (await userManager.FindByEmailAsync(email))!.Id;
    }

    /// <summary>An approved member of their own, signed in, with the ids both halves are addressed by.</summary>
    private async Task<(HttpClient Client, string Email, string UserId, Guid MemberId)> NewMemberAsync()
    {
        var email = $"policy-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        var userId = await UserIdOfAsync(email);

        return (
            await fixture.CreateAuthenticatedClientAsync(email),
            email,
            userId,
            await fixture.MemberIdOfAsync(userId));
    }

    // --- the claims exist at all ----------------------------------------------

    [Fact]
    public async Task The_session_carries_the_member_id_and_membership_status()
    {
        var (client, _, _, memberId) = await NewMemberAsync();

        var me = await client.GetFromJsonAsync<CurrentUserBody>("/api/auth/me");

        Assert.Equal(memberId, me!.MemberId);
        Assert.Equal(nameof(MembershipStatus.Active), me.MembershipStatus);
    }

    // --- what a membership block actually does --------------------------------

    /// <summary>
    /// THE POINT OF PHASE 3. The account is left Active in the database and the member is refused
    /// anyway, which is only possible because the policy reads both statuses.
    /// </summary>
    [Fact]
    public async Task A_blocked_membership_is_refused_even_while_the_account_stays_active()
    {
        var (client, _, userId, memberId) = await NewMemberAsync();

        // Reachable before the block — otherwise the assertion below proves nothing.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/active-member")).StatusCode);

        // Blocked directly in the database, NOT through the admin endpoint: that one also blocks the
        // account, which would make this pass for the wrong reason. Here only the membership moves.
        await using (var db = NewContext())
        {
            var member = await db.Members.SingleAsync(m => m.Id == memberId);
            member.Status = MembershipStatus.Blocked;
            await db.SaveChangesAsync();
        }

        // The claim is still the one minted at sign-in, so the refusal has to be forced by re-minting
        // it — exactly what the security-stamp validation interval does on its own within minutes.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/test/active-member")).StatusCode);

        // The ACCOUNT never moved. This is what separates a membership block from an account block.
        await using (var db = NewContext())
        {
            Assert.Equal(
                AccountStatus.Active,
                (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).Status);
        }
    }

    /// <summary>
    /// The admin's block moves both statuses, and it bites on the SAME schedule an account block
    /// always has — not sooner.
    ///
    /// <para>
    /// A LIVE COOKIE KEEPS WORKING FOR UP TO ONE VALIDATION INTERVAL, and that is pre-existing
    /// behaviour rather than something S-14 introduced: the policies read claims, and the claims are
    /// re-minted only when the security-stamp validator next runs (SecurityStampValidatorOptions
    /// .ValidationInterval in Program.cs) or when the holder calls /api/auth/refresh. Rotating the
    /// stamp is what makes that re-mint fail the session; it does not reach back into a cookie already
    /// in flight. This test pins the bound rather than pretending there is none — asserting an
    /// immediate 401 here would be asserting something the product does not do.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_admin_block_bites_as_soon_as_the_session_is_revalidated()
    {
        var (client, _, _, memberId) = await NewMemberAsync();
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/active-member")).StatusCode);

        var blocked = await admin.PostAsync($"/api/admin/members/{memberId}/block", content: null);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);

        // Stands in for the validation interval elapsing. Refresh SUCCEEDS — its job is to re-mint the
        // claims from the current row, and it is called unconditionally by the awaiting-approval
        // screen — but what it mints now says Blocked on both statuses.
        var refreshed = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        var session = await refreshed.Content.ReadFromJsonAsync<CurrentUserBody>();
        Assert.Equal(nameof(AccountStatus.Blocked), session!.Status);
        Assert.Equal(nameof(MembershipStatus.Blocked), session.MembershipStatus);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/test/active-member")).StatusCode);
    }

    [Fact]
    public async Task Unblocking_lets_the_member_back_in()
    {
        var (_, email, _, memberId) = await NewMemberAsync();
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        await admin.PostAsync($"/api/admin/members/{memberId}/block", content: null);
        await admin.PostAsync($"/api/admin/members/{memberId}/unblock", content: null);

        // A fresh sign-in, because the block rotated the security stamp and killed the old cookie.
        var restored = await fixture.CreateAuthenticatedClientAsync(email);

        Assert.Equal(HttpStatusCode.OK, (await restored.GetAsync("/test/active-member")).StatusCode);
    }

    // --- the tripwire ---------------------------------------------------------

    /// <summary>
    /// AN ACCOUNT WITH NO MEMBER ROW FAILS EVERY POLICY, and that is deliberate rather than a bug to
    /// paper over. The alternative — treating an absent claim as Active — would be a permanent
    /// authorization bypass hiding behind a data-integrity assumption.
    ///
    /// <para>
    /// The state is unreachable in production: three producers create the row (the AddMembers
    /// backfill, AdminSeeder on every cold start, RegisterAsync). This test manufactures it by
    /// deleting the row, which nothing in the application can do, precisely to prove which way the
    /// failure falls.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_account_whose_member_row_is_missing_is_refused_rather_than_admitted()
    {
        var (client, _, _, memberId) = await NewMemberAsync();

        await using (var db = NewContext())
        {
            db.Members.Remove(await db.Members.SingleAsync(m => m.Id == memberId));
            await db.SaveChangesAsync();
        }

        await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/test/active-member")).StatusCode);

        // /me still answers — it reads the account, and the null membership is what tells the SPA the
        // session is signed in but unusable rather than leaving it to guess from a 403.
        var me = await client.GetFromJsonAsync<CurrentUserBody>("/api/auth/me");
        Assert.Null(me!.MemberId);
        Assert.Null(me.MembershipStatus);
    }

    /// <summary>
    /// The seeded admin has a member row on every cold start, which is what stops the claim from
    /// locking the club out of its own app. AdminSeeder ensures it even when the account already
    /// exists — the case nothing else would cover.
    /// </summary>
    [Fact]
    public async Task The_seeded_admin_has_a_member_row_and_reaches_the_admin_policy()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.SeededAdminEmail);

        var me = await admin.GetFromJsonAsync<CurrentUserBody>("/api/auth/me");
        Assert.NotNull(me!.MemberId);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/test/admin-only")).StatusCode);
    }
}
