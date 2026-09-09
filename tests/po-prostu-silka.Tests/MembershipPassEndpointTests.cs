using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// The karnet's admin surface (S-16, MP-04..MP-06).
///
/// <para>
/// What is worth testing here is what the handler is NOT obvious about: that ranges may abut but not
/// intersect, that the range is inclusive at both ends, and that two admins issuing overlapping passes
/// at the same instant produce one pass rather than two. The last one is the reason the insert rotates
/// <c>Member.ConcurrencyStamp</c> at all, and it is asserted against the database rather than against
/// the two responses — a lost race still returns a perfectly plausible 201.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class MembershipPassEndpointTests(IntegrationTestFixture fixture)
{
    private static readonly DateOnly Anchor = new(2026, 10, 1);

    private static object Request(DateOnly from, DateOnly to, int entries = 4, string name = "Karnet 4 wejścia") =>
        new { typeName = name, validFrom = from, validTo = to, entryCount = entries };

    private async Task<HttpClient> AdminAsync() =>
        await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    private static async Task<string?> ReasonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<MembershipPassFailure>())?.Reason;

    private async Task<int> PassCountAsync(Guid memberId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.MembershipPasses.AsNoTracking().CountAsync(p => p.MemberId == memberId);
    }

    [Fact]
    public async Task Issuing_a_pass_returns_every_field()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Pass Fields");

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes",
            Request(Anchor, Anchor.AddDays(29), entries: 8, name: "Karnet 8 wejść"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var view = await response.Content.ReadFromJsonAsync<MembershipPassView>();
        Assert.NotNull(view);
        Assert.Equal("Karnet 8 wejść", view.TypeName);
        Assert.Equal(Anchor, view.ValidFrom);
        Assert.Equal(Anchor.AddDays(29), view.ValidTo);
        Assert.Equal(8, view.EntryCount);

        // Freshly issued: nothing can have consumed an entry, and "left" is the derivation of that.
        Assert.Equal(0, view.EntriesUsed);
        Assert.Equal(8, view.EntriesLeft);
    }

    /// <summary>
    /// THE KARNET'S WHOLE POINT per the frame brief: entitlement to train hangs off the person, not
    /// off a login. A member the club recorded at the desk must be able to hold one.
    /// </summary>
    [Fact]
    public async Task Issuing_a_pass_to_a_member_with_no_account_succeeds()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Accountless Pass Holder");

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor.AddDays(9)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_overlapping_range_is_refused()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Overlap Target");

        var first = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor.AddDays(30)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Starts inside the first pass's range by a single day. One day of intersection is still an
        // intersection — passes are a history, not a stack.
        var second = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor.AddDays(30), Anchor.AddDays(60)));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("overlapping_pass", await ReasonAsync(second));
    }

    /// <summary>
    /// The boundary the overlap rule must NOT refuse: renewing a karnet the day the old one ends is
    /// the ordinary case, and an off-by-one in the intersection test would break it.
    /// </summary>
    [Fact]
    public async Task A_range_starting_the_day_after_the_previous_one_ends_is_accepted()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Abutting Ranges");

        var first = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor.AddDays(30)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor.AddDays(31), Anchor.AddDays(60)));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(2, await PassCountAsync(memberId));
    }

    [Fact]
    public async Task A_range_that_runs_backwards_is_refused()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Backwards Range");

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor.AddDays(10), Anchor));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_range", await ReasonAsync(response));
    }

    /// <summary>A one-day karnet is legal — the range is inclusive at both ends.</summary>
    [Fact]
    public async Task A_single_day_range_is_accepted()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Single Day Range");

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor, entries: 1));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Zero entries is not a pass, it is a data-entry mistake — see MembershipPassRules.MinEntryCount.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MembershipPassRules.MaxEntryCount + 1)]
    public async Task An_entry_count_outside_the_bounds_is_refused(int entries)
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync($"Entry Bound {entries}");

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor.AddDays(9), entries));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_entry_count", await ReasonAsync(response));
    }

    [Fact]
    public async Task A_blank_type_name_is_refused()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Blank Name");

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes",
            Request(Anchor, Anchor.AddDays(9), name: "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_type_name", await ReasonAsync(response));
    }

    /// <summary>
    /// A pass for a blocked member entitles them to nothing — the membership check refuses the booking
    /// long before the pass is consulted — so selling them one would be a receipt for nothing.
    /// </summary>
    [Fact]
    public async Task Issuing_to_a_blocked_member_is_refused()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Blocked Holder", MembershipStatus.Blocked);

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor.AddDays(9)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("member_blocked", await ReasonAsync(response));
    }

    [Fact]
    public async Task An_unknown_member_is_not_found()
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/members/{Guid.NewGuid()}/passes", Request(Anchor, Anchor.AddDays(9)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_non_admin_is_refused()
    {
        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);
        var memberId = await fixture.CreateMemberAsync("Not Yours");

        var response = await member.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor.AddDays(9)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_history_reads_back_newest_first()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("History Order");

        await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor.AddDays(30)));
        await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor.AddDays(31), Anchor.AddDays(60)));

        var history = await admin.GetFromJsonAsync<List<MembershipPassView>>(
            $"/api/admin/members/{memberId}/passes");

        Assert.NotNull(history);
        Assert.Equal(2, history.Count);
        Assert.Equal(Anchor.AddDays(31), history[0].ValidFrom);
        Assert.Equal(Anchor, history[1].ValidFrom);
    }

    /// <summary>
    /// A pass covering today is marked as such, so the screen can highlight it without a second
    /// request. Anchored on the club's calendar, which is what CoversToday is computed against.
    /// </summary>
    [Fact]
    public async Task The_pass_covering_today_is_flagged()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Covers Today");

        var today = DateOnly.FromDateTime(
            po_prostu_silka.Domain.Scheduling.ClubTime.ToClubLocal(DateTimeOffset.UtcNow).DateTime);

        await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(today.AddDays(-3), today.AddDays(3)));

        var history = await admin.GetFromJsonAsync<List<MembershipPassView>>(
            $"/api/admin/members/{memberId}/passes");

        Assert.NotNull(history);
        Assert.True(Assert.Single(history).CoversToday);
    }

    [Fact]
    public async Task A_pass_with_no_bookings_can_be_revoked()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Revoke Me");

        var created = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor.AddDays(9)));
        var pass = await created.Content.ReadFromJsonAsync<MembershipPassView>();
        Assert.NotNull(pass);

        var response = await admin.DeleteAsync($"/api/admin/members/{memberId}/passes/{pass.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await PassCountAsync(memberId));
    }

    /// <summary>
    /// The nesting is enforced rather than decorative: without the MemberId comparison in the handler,
    /// addressing somebody else's pass through your own member's URL would work.
    /// </summary>
    [Fact]
    public async Task A_pass_belonging_to_another_member_is_not_found()
    {
        var admin = await AdminAsync();
        var ownerId = await fixture.CreateMemberAsync("Pass Owner");
        var otherId = await fixture.CreateMemberAsync("Pass Bystander");

        var created = await admin.PostAsJsonAsync(
            $"/api/admin/members/{ownerId}/passes", Request(Anchor, Anchor.AddDays(9)));
        var pass = await created.Content.ReadFromJsonAsync<MembershipPassView>();
        Assert.NotNull(pass);

        var response = await admin.DeleteAsync($"/api/admin/members/{otherId}/passes/{pass.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Editing_a_pass_changes_its_fields()
    {
        var admin = await AdminAsync();
        var memberId = await fixture.CreateMemberAsync("Edit Me");

        var created = await admin.PostAsJsonAsync(
            $"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor.AddDays(9), entries: 4));
        var pass = await created.Content.ReadFromJsonAsync<MembershipPassView>();
        Assert.NotNull(pass);

        var response = await admin.PutAsJsonAsync(
            $"/api/admin/members/{memberId}/passes/{pass.Id}",
            Request(Anchor, Anchor.AddDays(20), entries: 10, name: "Karnet 10 wejść"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<MembershipPassView>();
        Assert.NotNull(updated);
        Assert.Equal("Karnet 10 wejść", updated.TypeName);
        Assert.Equal(10, updated.EntryCount);
        Assert.Equal(Anchor.AddDays(20), updated.ValidTo);
    }

    /// <summary>
    /// THE TEST THE STAMP ROTATION EXISTS FOR. Two admins issue overlapping passes at the same
    /// instant; both pass the overlap probe, because both read before either writes.
    ///
    /// <para>
    /// Asserted against the DATABASE, not against the two responses — a lost race returns a
    /// thoroughly plausible 201, so counting successes proves nothing on its own. Remove the
    /// <c>member.ConcurrencyStamp</c> rotation in IssuePassAsync and this test finds two rows.
    /// </para>
    ///
    /// <para>
    /// Separate HttpClients on purpose, like the booking race: separate cookies mean separate DI
    /// scopes and separate DbContexts, so nothing is shared through the change tracker.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Concurrent_overlapping_issues_leave_exactly_one_pass()
    {
        var memberId = await fixture.CreateMemberAsync("Overlap Racer");

        var one = await AdminAsync();
        var two = await AdminAsync();

        var responses = await Task.WhenAll(
            one.PostAsJsonAsync($"/api/admin/members/{memberId}/passes", Request(Anchor, Anchor.AddDays(30))),
            two.PostAsJsonAsync($"/api/admin/members/{memberId}/passes", Request(Anchor.AddDays(10), Anchor.AddDays(40))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));

        // The assertion that matters. Everything above could be satisfied by a coincidence of
        // scheduling; the row count cannot.
        Assert.Equal(1, await PassCountAsync(memberId));
    }
}
