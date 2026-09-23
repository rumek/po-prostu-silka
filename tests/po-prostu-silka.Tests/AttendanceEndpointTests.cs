using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// Attendance and the entry rule it changes (S-27, AT-01–AT-03).
///
/// <para>
/// THE RULE UNDER TEST: a booking with a pass consumes an entry unless it was released, its class was
/// cancelled, or it was marked absent. Unrecorded counts as spent. The gate (the booking route) and
/// both read paths (the admin's pass list and the member's <c>/api/passes/mine</c>) share one
/// definition, and the tests below read all three so a drift between them fails here.
/// </para>
///
/// <para>
/// A STARTED CLASS CANNOT BE MADE THROUGH THE API — booking refuses <c>class_started</c> — so the
/// arrangement books a future class and then moves its start into the past directly in the database.
/// Future slots are in 2042 and past ones in 2018, which no other suite uses; the overlap rule is
/// club-wide and every file shares one database.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class AttendanceEndpointTests(IntegrationTestFixture fixture)
{
    /// <summary>Mirrors ScheduledClass — only what these tests read from it.</summary>
    private sealed record ClassBody(Guid Id, DateTimeOffset StartsAt);

    /// <summary>Mirrors ClassTypeSummary — only what these tests read from it.</summary>
    private sealed record ClassTypeBody(Guid Id, string Name);

    /// <summary>Mirrors BookingFailure.</summary>
    private sealed record FailureBody(string Reason);

    private static string BookingsOf(Guid classId) => $"/api/admin/classes/{classId}/bookings";

    private static int _slot;

    private static DateTimeOffset NextSlot() =>
        new DateTimeOffset(2042, 1, 1, 10, 0, 0, TimeSpan.Zero)
            .AddDays(3 * Interlocked.Increment(ref _slot));

    private static int _pastSlot;

    /// <summary>A distinct instant in 2018, so two started classes never share a start.</summary>
    private static DateTimeOffset NextPastSlot() =>
        new DateTimeOffset(2018, 1, 1, 10, 0, 0, TimeSpan.Zero)
            .AddHours(3 * Interlocked.Increment(ref _pastSlot));

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(fixture.ConnectionString).Options);

    private Task<HttpClient> AdminAsync() =>
        fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    // --- arrangement -----------------------------------------------------------

    private async Task<Guid> CreateTrainerAsync(HttpClient admin)
    {
        var email = $"att-trainer-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.Trainer);

        return await fixture.FindMemberIdAsync(admin, email);
    }

    /// <summary>A future class, with a fresh type and — unless one is named — a fresh trainer.</summary>
    private async Task<ClassBody> ClassAsync(HttpClient admin, Guid? instructorMemberId = null)
    {
        var typeResponse = await admin.PostAsJsonAsync("/api/admin/class-types", new
        {
            name = $"Obecność-{Guid.NewGuid():N}",
            description = (string?)"Opis",
            defaultDurationMinutes = 60,
            defaultCapacity = 12,
        });
        Assert.Equal(HttpStatusCode.OK, typeResponse.StatusCode);
        var type = (await typeResponse.Content.ReadFromJsonAsync<ClassTypeBody>())!;

        var response = await admin.PostAsJsonAsync("/api/admin/classes", new
        {
            classTypeId = type.Id,
            startsAt = NextSlot(),
            instructorMemberId = instructorMemberId ?? await CreateTrainerAsync(admin),
            durationMinutes = 60,
            capacity = 12,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ClassBody>())!;
    }

    /// <summary>A member with an account (so <c>/api/passes/mine</c> can be read) and their client.</summary>
    private async Task<(HttpClient Client, Guid MemberId)> MemberWithAccountAsync(HttpClient admin)
    {
        var email = $"att-member-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        var memberId = await fixture.FindMemberIdAsync(admin, email);
        return (await fixture.CreateAuthenticatedClientAsync(email), memberId);
    }

    private static async Task BookAsync(HttpClient admin, Guid classId, Guid memberId)
    {
        var response = await admin.PostAsJsonAsync(BookingsOf(classId), new { memberId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Booking> BookingOfAsync(Guid classId, Guid memberId)
    {
        await using var db = NewContext();

        return await db.Bookings.AsNoTracking()
            .SingleAsync(b => b.ClassId == classId && b.MemberId == memberId && b.Status == BookingStatus.Active);
    }

    /// <summary>Moves a class's start into the past — the one state the API cannot produce.</summary>
    private async Task StartAsync(Guid classId)
    {
        await using var db = NewContext();

        var past = NextPastSlot();
        await db.Classes.Where(c => c.Id == classId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.StartsAt, past));
    }

    /// <summary>Records attendance behind the API — the marking endpoint is Phase 2's.</summary>
    private async Task MarkAsync(Guid bookingId, BookingAttendance attendance)
    {
        await using var db = NewContext();

        await db.Bookings.Where(b => b.Id == bookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Attendance, attendance));
    }

    private static async Task<int> EntriesLeftForAdminAsync(HttpClient admin, Guid memberId, Guid passId)
    {
        var passes = await admin.GetFromJsonAsync<List<MembershipPassView>>(
            $"/api/admin/members/{memberId}/passes");

        return passes!.Single(p => p.Id == passId).EntriesLeft;
    }

    private static async Task<int> EntriesLeftForMemberAsync(HttpClient member) =>
        (await member.GetFromJsonAsync<MembershipPassView>("/api/passes/mine"))!.EntriesLeft;

    private async Task<string> PassStampAsync(Guid passId)
    {
        await using var db = NewContext();

        return await db.MembershipPasses.AsNoTracking()
            .Where(p => p.Id == passId).Select(p => p.ConcurrencyStamp).SingleAsync();
    }

    private static async Task<string> ReasonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason;

    // --- the entry rule (Phase 1) ----------------------------------------------

    /// <summary>
    /// UNRECORDED COUNTS AS SPENT. A class nobody marked keeps its entry forever — the default that
    /// neither silently refunds entries nor needs a background job, and today's meaning.
    /// </summary>
    [Fact]
    public async Task An_unrecorded_past_booking_still_consumes_its_entry()
    {
        var admin = await AdminAsync();
        var (member, memberId) = await MemberWithAccountAsync(admin);
        var passId = await fixture.IssuePassAsync(memberId, entryCount: 1);

        var past = await ClassAsync(admin);
        await BookAsync(admin, past.Id, memberId);
        await StartAsync(past.Id);

        Assert.Equal(0, await EntriesLeftForAdminAsync(admin, memberId, passId));
        Assert.Equal(0, await EntriesLeftForMemberAsync(member));

        // And the gate agrees with both read paths.
        var next = await ClassAsync(admin);
        var refused = await admin.PostAsJsonAsync(BookingsOf(next.Id), new { memberId });
        Assert.Equal("no_entries_left", await ReasonAsync(refused));
    }

    /// <summary>
    /// ABSENT RETURNS THE ENTRY, on all three sites at once: the admin's list, the member's card, and
    /// the gate that refused a moment ago.
    /// </summary>
    [Fact]
    public async Task A_booking_marked_absent_returns_its_entry()
    {
        var admin = await AdminAsync();
        var (member, memberId) = await MemberWithAccountAsync(admin);
        var passId = await fixture.IssuePassAsync(memberId, entryCount: 1);

        var past = await ClassAsync(admin);
        await BookAsync(admin, past.Id, memberId);
        await StartAsync(past.Id);

        var booking = await BookingOfAsync(past.Id, memberId);
        await MarkAsync(booking.Id, BookingAttendance.Absent);

        Assert.Equal(1, await EntriesLeftForAdminAsync(admin, memberId, passId));
        Assert.Equal(1, await EntriesLeftForMemberAsync(member));

        var next = await ClassAsync(admin);
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsJsonAsync(BookingsOf(next.Id), new { memberId })).StatusCode);
    }

    /// <summary>Present keeps the entry spent — the mark that changes nothing about the count.</summary>
    [Fact]
    public async Task A_booking_marked_present_keeps_its_entry_spent()
    {
        var admin = await AdminAsync();
        var (member, memberId) = await MemberWithAccountAsync(admin);
        await fixture.IssuePassAsync(memberId, entryCount: 2);

        var past = await ClassAsync(admin);
        await BookAsync(admin, past.Id, memberId);
        await StartAsync(past.Id);
        await MarkAsync((await BookingOfAsync(past.Id, memberId)).Id, BookingAttendance.Present);

        Assert.Equal(1, await EntriesLeftForMemberAsync(member));
    }

    /// <summary>
    /// OPEN ROADMAP QUESTION 7, ANSWERED. Nobody attends a cancelled class, so its bookings stop
    /// consuming. The rows stay Active — cancellation is the class's state, not the member's act.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_class_returns_the_entries_its_bookings_held()
    {
        var admin = await AdminAsync();
        var (firstClient, first) = await MemberWithAccountAsync(admin);
        var (secondClient, second) = await MemberWithAccountAsync(admin);
        await fixture.IssuePassAsync(first, entryCount: 3);
        await fixture.IssuePassAsync(second, entryCount: 3);

        var scheduled = await ClassAsync(admin);
        await BookAsync(admin, scheduled.Id, first);
        await BookAsync(admin, scheduled.Id, second);

        Assert.Equal(2, await EntriesLeftForMemberAsync(firstClient));

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsync($"/api/admin/classes/{scheduled.Id}/cancel", content: null)).StatusCode);

        Assert.Equal(3, await EntriesLeftForMemberAsync(firstClient));
        Assert.Equal(3, await EntriesLeftForMemberAsync(secondClient));

        Assert.Equal(BookingStatus.Active, (await BookingOfAsync(scheduled.Id, first)).Status);
    }

    /// <summary>
    /// The cancel returns entries, so it must rotate each booked pass's stamp — otherwise a booker
    /// whose entry check straddles the cancel decides on a pool that is mid-change. Deterministic, like
    /// <c>Lowering_the_entry_count_rotates_the_pass_stamp</c>: REMOVE THE ROTATION IN CancelClass AND
    /// THIS TEST FAILS.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_class_rotates_each_booked_pass_stamp()
    {
        var admin = await AdminAsync();
        var (_, first) = await MemberWithAccountAsync(admin);
        var (_, second) = await MemberWithAccountAsync(admin);
        var firstPass = await fixture.IssuePassAsync(first, entryCount: 3);
        var secondPass = await fixture.IssuePassAsync(second, entryCount: 3);

        var scheduled = await ClassAsync(admin);
        await BookAsync(admin, scheduled.Id, first);
        await BookAsync(admin, scheduled.Id, second);

        var firstBefore = await PassStampAsync(firstPass);
        var secondBefore = await PassStampAsync(secondPass);

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsync($"/api/admin/classes/{scheduled.Id}/cancel", content: null)).StatusCode);

        Assert.NotEqual(firstBefore, await PassStampAsync(firstPass));
        Assert.NotEqual(secondBefore, await PassStampAsync(secondPass));
    }

    /// <summary>
    /// Once a class has begun, a no-show is recorded as absence. A release would erase the booking
    /// from the member's history, so it is refused with the booking path's own reason.
    /// </summary>
    [Fact]
    public async Task Releasing_a_booking_after_the_start_is_refused()
    {
        var admin = await AdminAsync();
        var (_, memberId) = await MemberWithAccountAsync(admin);
        await fixture.IssuePassAsync(memberId);

        var past = await ClassAsync(admin);
        await BookAsync(admin, past.Id, memberId);
        await StartAsync(past.Id);

        var booking = await BookingOfAsync(past.Id, memberId);
        var response = await admin.DeleteAsync($"{BookingsOf(past.Id)}/{booking.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("class_started", await ReasonAsync(response));
        Assert.Equal(BookingStatus.Active, (await BookingOfAsync(past.Id, memberId)).Status);
    }

    /// <summary>
    /// REVOKE KEEPS ITS LITERAL MEANING. An absent booking returns its entry but still records which
    /// karnet paid, and the restrict FK would refuse the delete anyway.
    /// </summary>
    [Fact]
    public async Task A_pass_whose_only_booking_was_marked_absent_still_cannot_be_revoked()
    {
        var admin = await AdminAsync();
        var (_, memberId) = await MemberWithAccountAsync(admin);
        var passId = await fixture.IssuePassAsync(memberId, entryCount: 2);

        var past = await ClassAsync(admin);
        await BookAsync(admin, past.Id, memberId);
        await StartAsync(past.Id);
        await MarkAsync((await BookingOfAsync(past.Id, memberId)).Id, BookingAttendance.Absent);

        Assert.Equal(2, await EntriesLeftForAdminAsync(admin, memberId, passId));

        var refused = await admin.DeleteAsync($"/api/admin/members/{memberId}/passes/{passId}");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            "has_active_bookings",
            (await refused.Content.ReadFromJsonAsync<MembershipPassFailure>())!.Reason);
    }

    /// <summary>
    /// Lowering the entry count is refused only below the entries actually consumed, and an absence
    /// consumed none: three bookings, one absent, so the pass may drop to two.
    /// </summary>
    [Fact]
    public async Task Lowering_entry_count_ignores_absent_bookings()
    {
        var admin = await AdminAsync();
        var (_, memberId) = await MemberWithAccountAsync(admin);
        var passId = await fixture.IssuePassAsync(memberId, entryCount: 4);

        var absent = await ClassAsync(admin);
        await BookAsync(admin, absent.Id, memberId);
        await BookAsync(admin, (await ClassAsync(admin)).Id, memberId);
        await BookAsync(admin, (await ClassAsync(admin)).Id, memberId);

        await StartAsync(absent.Id);
        await MarkAsync((await BookingOfAsync(absent.Id, memberId)).Id, BookingAttendance.Absent);

        var response = await admin.PutAsJsonAsync(
            $"/api/admin/members/{memberId}/passes/{passId}",
            new
            {
                typeName = "Test Karnet",
                validFrom = new DateOnly(2042, 1, 1),
                validTo = new DateOnly(2042, 12, 31),
                entryCount = 2,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await EntriesLeftForAdminAsync(admin, memberId, passId));
    }
}
