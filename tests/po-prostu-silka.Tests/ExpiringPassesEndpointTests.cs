using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// A collection of its own, and therefore a SQL Server container of its own: the card is CLUB-WIDE —
/// it has no search to isolate behind — so on the shared IntegrationCollection container every other
/// suite's karnets would land in its top five and its total.
/// </summary>
[CollectionDefinition(nameof(ExpiringPassesCollection))]
public class ExpiringPassesCollection : ICollectionFixture<IntegrationTestFixture>;

/// <summary>
/// The admin's Start card "Kończą się karnety" (expiring-passes-dashboard). WHICH members are ending
/// is pinned in <c>MemberEndpointTests</c> through the list's <c>expiring</c> filter; this suite pins
/// what only the card does — the cut at five, its order, <c>daysLeft</c>, and that its total IS the
/// list's.
///
/// <para>
/// Every test starts by deleting every karnet in this container, so none depends on the order xUnit
/// runs them in.
/// </para>
/// </summary>
[Collection(nameof(ExpiringPassesCollection))]
public class ExpiringPassesEndpointTests(IntegrationTestFixture fixture)
{
    private const string Endpoint = "/api/admin/members/expiring-passes";

    private sealed record ExpiringPassBody(Guid MemberId, string DisplayName, DateOnly ValidTo, int DaysLeft);

    private sealed record ExpiringPassesBody(List<ExpiringPassBody> Items, int Total);

    private sealed record MemberRow(Guid Id);

    [Fact]
    public async Task An_empty_club_answers_an_empty_card()
    {
        await ClearPassesAsync();
        var admin = await AdminAsync();

        var card = await admin.GetFromJsonAsync<ExpiringPassesBody>(Endpoint);

        Assert.Equal(0, card!.Total);
        Assert.Empty(card.Items);
    }

    /// <summary>
    /// Six ending: five on the card, nearest first, total six — and at the cut the 5th and 6th share a
    /// day, so the NAME decides, never the insertion order.
    /// </summary>
    [Fact]
    public async Task The_card_takes_five_by_end_then_name_and_counts_them_all()
    {
        await ClearPassesAsync();
        var admin = await AdminAsync();
        var today = ClubToday();
        var run = Guid.NewGuid().ToString("N")[..8];

        // Created in an order that is NOT the expected one, so a tiebreak by insertion would show.
        var zofia = await MemberEndingAsync($"Zofia {run}", today.AddDays(4));
        var anna = await MemberEndingAsync($"Anna {run}", today.AddDays(4));
        var dzis = await MemberEndingAsync($"Dziś {run}", today);
        var jutro = await MemberEndingAsync($"Jutro {run}", today.AddDays(1));
        var piec = await MemberEndingAsync($"Pięć {run}", today.AddDays(5));
        var dwa = await MemberEndingAsync($"Dwa {run}", today.AddDays(2));

        var card = await admin.GetFromJsonAsync<ExpiringPassesBody>(Endpoint);

        Assert.Equal(6, card!.Total);
        Assert.Equal([dzis, jutro, dwa, anna, zofia], card.Items.Select(i => i.MemberId));
        Assert.DoesNotContain(piec, card.Items.Select(i => i.MemberId));

        Assert.Equal([0, 1, 2, 4, 4], card.Items.Select(i => i.DaysLeft));
        Assert.Equal(today, card.Items[0].ValidTo);
        Assert.Equal($"Dziś {run}", card.Items[0].DisplayName);
    }

    /// <summary>The promise behind "Zobacz wszystkich (N)": the list it opens counts the same N.</summary>
    [Fact]
    public async Task The_card_total_equals_the_expiring_list_total()
    {
        await ClearPassesAsync();
        var admin = await AdminAsync();
        var today = ClubToday();
        var run = Guid.NewGuid().ToString("N")[..8];

        for (var i = 0; i < 7; i++)
        {
            await MemberEndingAsync($"Lista {run} {i}", today.AddDays(i % 6));
        }

        // Not ending: renewed, and far off.
        var renewed = await MemberEndingAsync($"Lista {run} odnowiony", today.AddDays(2));
        await fixture.IssuePassAsync(renewed, validFrom: today.AddDays(3), validTo: today.AddDays(32));
        await MemberEndingAsync($"Lista {run} daleko", today.AddDays(20));

        var card = await admin.GetFromJsonAsync<ExpiringPassesBody>(Endpoint);
        var list = await admin.GetFromJsonAsync<MemberPageBody<MemberRow>>("/api/admin/members?expiring=true");

        Assert.Equal(7, card!.Total);
        Assert.Equal(card.Total, list!.Total);
    }

    [Fact]
    public async Task Only_an_admin_may_read_the_card()
    {
        var trainer = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);
        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);
        var anonymous = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await trainer.GetAsync(Endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(Endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Endpoint)).StatusCode);
    }

    private Task<HttpClient> AdminAsync() =>
        fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    private static DateOnly ClubToday() =>
        DateOnly.FromDateTime(ClubTime.ToClubLocal(DateTimeOffset.UtcNow).DateTime);

    private async Task<Guid> MemberEndingAsync(string name, DateOnly validTo)
    {
        var id = await fixture.CreateMemberAsync(name);
        await fixture.IssuePassAsync(id, validFrom: validTo.AddDays(-29), validTo: validTo);
        return id;
    }

    /// <summary>Bookings first: they carry the karnet's id.</summary>
    private async Task ClearPassesAsync()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

        await db.Bookings.Where(b => b.MembershipPassId != null).ExecuteDeleteAsync();
        await db.MembershipPasses.ExecuteDeleteAsync();
    }
}
