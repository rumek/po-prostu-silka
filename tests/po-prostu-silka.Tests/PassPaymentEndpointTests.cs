using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// <c>PUT /api/passes/{passId}/paid</c> (pass-paid-flag) — the one write path for payment on an
/// existing karnet, shared by admin and trainer.
///
/// <para>
/// What is worth pinning is who may write (a trainer yes, a member no), what is refused (staff, a
/// future or absurdly old date) and what is deliberately NOT refused (a blocked member), plus the two
/// invisible properties: the recorder is the latest caller, and the pass's stamp is left alone.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class PassPaymentEndpointTests(IntegrationTestFixture fixture)
{
    private static string Route(Guid passId) => $"/api/passes/{passId}/paid";

    private static DateOnly ClubToday() =>
        DateOnly.FromDateTime(po_prostu_silka.Domain.Scheduling.ClubTime.ToClubLocal(DateTimeOffset.UtcNow).DateTime);

    private static async Task<string?> ReasonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<MembershipPassFailure>())?.Reason;

    private async Task<MembershipPass> PassRowAsync(Guid passId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.MembershipPasses.AsNoTracking().SingleAsync(p => p.Id == passId);
    }

    private async Task<string> UserIdAsync(string email)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Users.AsNoTracking().Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    private async Task<Guid> UnpaidPassAsync(string name)
    {
        var memberId = await fixture.CreateMemberAsync(name);

        return await fixture.IssuePassAsync(memberId);
    }

    [Theory]
    [InlineData(TestUsers.ActiveAdminEmail)]
    [InlineData(TestUsers.ActiveTrainerEmail)]
    public async Task Staff_mark_a_karnet_paid_and_clear_it(string email)
    {
        var passId = await UnpaidPassAsync($"Płatność {Guid.NewGuid():N}");
        var client = await fixture.CreateAuthenticatedClientAsync(email);
        var paidOn = ClubToday().AddDays(-3);

        var marked = await client.PutAsJsonAsync(Route(passId), new { paidAt = paidOn });

        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        Assert.Equal(paidOn, (await marked.Content.ReadFromJsonAsync<MembershipPassView>())!.PaidAt);
        Assert.Equal(paidOn, (await PassRowAsync(passId)).PaidAt);

        var cleared = await client.PutAsJsonAsync(Route(passId), new { paidAt = (DateOnly?)null });

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<MembershipPassView>())!.PaidAt);
        Assert.Null((await PassRowAsync(passId)).PaidAt);
    }

    [Fact]
    public async Task A_member_is_refused()
    {
        var passId = await UnpaidPassAsync($"Członek płaci {Guid.NewGuid():N}");
        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await member.PutAsJsonAsync(Route(passId), new { paidAt = ClubToday() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null((await PassRowAsync(passId)).PaidAt);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        var response = await fixture.CreateClient().PutAsJsonAsync(Route(Guid.NewGuid()), new { paidAt = ClubToday() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_pass_is_404()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PutAsJsonAsync(Route(Guid.NewGuid()), new { paidAt = ClubToday() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The recorder is the LATEST caller — on a mark and on a clear alike.</summary>
    [Fact]
    public async Task The_latest_caller_is_recorded_even_when_clearing()
    {
        var passId = await UnpaidPassAsync($"Kto zapisał {Guid.NewGuid():N}");
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        var trainer = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);

        await admin.PutAsJsonAsync(Route(passId), new { paidAt = ClubToday() });
        Assert.Equal(await UserIdAsync(TestUsers.ActiveAdminEmail), (await PassRowAsync(passId)).PaidRecordedBy);

        await trainer.PutAsJsonAsync(Route(passId), new { paidAt = (DateOnly?)null });
        Assert.Equal(await UserIdAsync(TestUsers.ActiveTrainerEmail), (await PassRowAsync(passId)).PaidRecordedBy);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-MembershipPassRules.MaxPaidAtAgeDays - 1)]
    public async Task A_future_or_too_old_date_is_refused(int offsetDays)
    {
        var passId = await UnpaidPassAsync($"Zła data {offsetDays} {Guid.NewGuid():N}");
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PutAsJsonAsync(Route(passId), new { paidAt = ClubToday().AddDays(offsetDays) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_paid_at", await ReasonAsync(response));
        Assert.Null((await PassRowAsync(passId)).PaidAt);
    }

    /// <summary>
    /// A member granted Trainer after holding a karnet keeps it, but staff hold no member data — so the
    /// payment write refuses it, as issuing does.
    /// </summary>
    [Fact]
    public async Task A_staff_holders_karnet_is_refused()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        var staffId = await fixture.FindMemberIdAsync(admin, TestUsers.ActiveTrainerEmail);
        var passId = await fixture.IssuePassAsync(staffId, validFrom: new DateOnly(1990, 1, 1), validTo: new DateOnly(1990, 1, 31));

        var response = await admin.PutAsJsonAsync(Route(passId), new { paidAt = ClubToday() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("member_is_staff", await ReasonAsync(response));
    }

    /// <summary>Payment is about money, not access: a blocked member's debt can still be settled.</summary>
    [Fact]
    public async Task A_blocked_members_karnet_can_be_marked_paid()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        var memberId = await fixture.CreateMemberAsync($"Zablokowany płaci {Guid.NewGuid():N}");
        var passId = await fixture.IssuePassAsync(memberId);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/members/{memberId}/block", null)).StatusCode);

        var response = await admin.PutAsJsonAsync(Route(passId), new { paidAt = ClubToday() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// THE STAMP GUARDS THE ENTRY POOL, which payment does not touch. Rotating it would make a payment
    /// race a concurrent booking for nothing. ADD A ROTATION TO SetPassPaid AND THIS TEST FAILS.
    /// </summary>
    [Fact]
    public async Task Marking_paid_leaves_the_pass_stamp_alone()
    {
        var passId = await UnpaidPassAsync($"Stempel {Guid.NewGuid():N}");
        var before = (await PassRowAsync(passId)).ConcurrencyStamp;
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(Route(passId), new { paidAt = ClubToday() })).StatusCode);

        Assert.Equal(before, (await PassRowAsync(passId)).ConcurrencyStamp);
    }
}
