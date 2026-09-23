using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// The member's attendance history (S-27, AT-04): their own past classes, three club-local months a
/// page, with a summary of the current karnet.
///
/// <para>
/// The window is relative to the real clock, so every past class here is placed relative to "now":
/// the arrangement books a future class through the API (slots in 2043, used by no other suite) and
/// then moves its start in the database — the one state the API cannot produce.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class AttendanceHistoryTests(IntegrationTestFixture fixture)
{
    private sealed record ClassBody(Guid Id);

    private sealed record ClassTypeBody(Guid Id, string Name);

    private sealed record Entry(Guid BookingId, Guid ClassId, string Name, DateTimeOffset StartsAt, string Outcome);

    private sealed record Summary(
        string TypeName, DateOnly ValidFrom, DateOnly ValidTo, int EntryCount, int Present, int Absent, int Unrecorded);

    private sealed record History(Summary? Summary, List<Entry> Items, DateOnly? EarlierBefore);

    private const string HistoryEndpoint = "/api/bookings/history";

    private static int _slot;

    private static DateTimeOffset NextSlot() =>
        new DateTimeOffset(2043, 1, 1, 10, 0, 0, TimeSpan.Zero)
            .AddDays(2 * Interlocked.Increment(ref _slot));

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(fixture.ConnectionString).Options);

    private Task<HttpClient> AdminAsync() =>
        fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    // --- arrangement -----------------------------------------------------------

    private async Task<ClassBody> ClassAsync(HttpClient admin)
    {
        var typeResponse = await admin.PostAsJsonAsync("/api/admin/class-types", new
        {
            name = $"Historia-{Guid.NewGuid():N}",
            description = (string?)"Opis",
            defaultDurationMinutes = 60,
            defaultCapacity = 12,
        });
        Assert.Equal(HttpStatusCode.OK, typeResponse.StatusCode);
        var type = (await typeResponse.Content.ReadFromJsonAsync<ClassTypeBody>())!;

        var trainerEmail = $"hist-trainer-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(trainerEmail, AccountStatus.Active, ApplicationRoles.Trainer);

        var response = await admin.PostAsJsonAsync("/api/admin/classes", new
        {
            classTypeId = type.Id,
            startsAt = NextSlot(),
            instructorMemberId = await fixture.FindMemberIdAsync(admin, trainerEmail),
            durationMinutes = 60,
            capacity = 12,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ClassBody>())!;
    }

    private async Task<(HttpClient Client, Guid MemberId)> MemberAsync(HttpClient admin, bool withPass = true)
    {
        var email = $"hist-member-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        var memberId = await fixture.FindMemberIdAsync(admin, email);
        if (withPass)
        {
            await fixture.IssuePassAsync(memberId);
        }

        return (await fixture.CreateAuthenticatedClientAsync(email), memberId);
    }

    private static async Task BookAsync(HttpClient admin, Guid classId, Guid memberId) =>
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/admin/classes/{classId}/bookings", new { memberId })).StatusCode);

    /// <summary>A class this member booked, whose start is then moved to <paramref name="startsAt"/>.</summary>
    private async Task<(Guid ClassId, Guid BookingId)> PastClassAsync(
        HttpClient admin, Guid memberId, DateTimeOffset startsAt)
    {
        var scheduled = await ClassAsync(admin);
        await BookAsync(admin, scheduled.Id, memberId);
        await MoveAsync(scheduled.Id, startsAt);

        return (scheduled.Id, await BookingIdAsync(scheduled.Id, memberId));
    }

    private async Task MoveAsync(Guid classId, DateTimeOffset startsAt)
    {
        await using var db = NewContext();
        await db.Classes.Where(c => c.Id == classId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.StartsAt, startsAt));
    }

    private async Task<Guid> BookingIdAsync(Guid classId, Guid memberId)
    {
        await using var db = NewContext();
        return await db.Bookings.AsNoTracking()
            .Where(b => b.ClassId == classId && b.MemberId == memberId)
            .Select(b => b.Id)
            .SingleAsync();
    }

    private async Task MarkAsync(Guid bookingId, BookingAttendance attendance)
    {
        await using var db = NewContext();
        await db.Bookings.Where(b => b.Id == bookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Attendance, attendance));
    }

    private static async Task<History> HistoryAsync(HttpClient member, string? before = null)
    {
        var response = await member.GetAsync(before is null ? HistoryEndpoint : $"{HistoryEndpoint}?before={before}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<History>())!;
    }

    private static DateOnly ClubToday() =>
        DateOnly.FromDateTime(ClubTime.ToClubLocal(DateTimeOffset.UtcNow).DateTime);

    private static DateOnly FirstOfThisMonth()
    {
        var today = ClubToday();
        return new DateOnly(today.Year, today.Month, 1);
    }

    // --- scope and outcomes ----------------------------------------------------

    [Fact]
    public async Task History_lists_past_classes_with_their_outcome()
    {
        var admin = await AdminAsync();
        var (member, memberId) = await MemberAsync(admin);
        var now = DateTimeOffset.UtcNow;

        var (presentClass, presentBooking) = await PastClassAsync(admin, memberId, now.AddHours(-1));
        var (absentClass, absentBooking) = await PastClassAsync(admin, memberId, now.AddHours(-2));
        var (unrecordedClass, _) = await PastClassAsync(admin, memberId, now.AddHours(-3));

        // A cancelled class must be cancelled while it is still in the future, then moved.
        var cancelled = await ClassAsync(admin);
        await BookAsync(admin, cancelled.Id, memberId);
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsync($"/api/admin/classes/{cancelled.Id}/cancel", content: null)).StatusCode);
        await MoveAsync(cancelled.Id, now.AddHours(-4));

        // A mark on a cancelled class loses to "cancelled".
        await MarkAsync(await BookingIdAsync(cancelled.Id, memberId), BookingAttendance.Present);

        await MarkAsync(presentBooking, BookingAttendance.Present);
        await MarkAsync(absentBooking, BookingAttendance.Absent);

        var history = await HistoryAsync(member);

        Assert.Equal(
            [presentClass, absentClass, unrecordedClass, cancelled.Id],
            history.Items.Select(i => i.ClassId).ToArray());
        Assert.Equal(
            ["present", "absent", "unrecorded", "cancelled"],
            history.Items.Select(i => i.Outcome).ToArray());
    }

    [Fact]
    public async Task History_omits_released_bookings_and_future_classes()
    {
        var admin = await AdminAsync();
        var (member, memberId) = await MemberAsync(admin);

        var released = await ClassAsync(admin);
        await BookAsync(admin, released.Id, memberId);
        var releasedBooking = await BookingIdAsync(released.Id, memberId);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/admin/classes/{released.Id}/bookings/{releasedBooking}")).StatusCode);
        await MoveAsync(released.Id, DateTimeOffset.UtcNow.AddHours(-1));

        var future = await ClassAsync(admin);
        await BookAsync(admin, future.Id, memberId);

        var (kept, _) = await PastClassAsync(admin, memberId, DateTimeOffset.UtcNow.AddHours(-2));

        var history = await HistoryAsync(member);

        Assert.Equal(kept, Assert.Single(history.Items).ClassId);
    }

    [Fact]
    public async Task History_never_includes_another_members_bookings()
    {
        var admin = await AdminAsync();
        var (mine, mineId) = await MemberAsync(admin);
        var (_, otherId) = await MemberAsync(admin);

        var shared = await ClassAsync(admin);
        await BookAsync(admin, shared.Id, mineId);
        await BookAsync(admin, shared.Id, otherId);
        await MoveAsync(shared.Id, DateTimeOffset.UtcNow.AddHours(-1));

        await PastClassAsync(admin, otherId, DateTimeOffset.UtcNow.AddHours(-2));

        var history = await HistoryAsync(mine);

        var entry = Assert.Single(history.Items);
        Assert.Equal(await BookingIdAsync(shared.Id, mineId), entry.BookingId);
    }

    [Fact]
    public async Task A_class_started_but_unmarked_reads_unrecorded()
    {
        var admin = await AdminAsync();
        var (member, memberId) = await MemberAsync(admin);

        await PastClassAsync(admin, memberId, DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.Equal("unrecorded", Assert.Single((await HistoryAsync(member)).Items).Outcome);
    }

    // --- the window ------------------------------------------------------------

    /// <summary>
    /// Three CLUB-LOCAL months. 23:30 UTC on the last day of a month is already the next month at the
    /// gym, so it lands on the first page; half an hour before the window's club-local midnight does not.
    /// </summary>
    [Fact]
    public async Task The_window_is_three_club_local_months_and_pages_backwards()
    {
        var admin = await AdminAsync();
        var (member, memberId) = await MemberAsync(admin);

        var firstMonth = FirstOfThisMonth().AddMonths(-2);
        var windowStart = ClubTime.StartOfLocalDay(firstMonth);

        var lastDayBefore = firstMonth.AddDays(-1);
        var utcLateEvening = new DateTimeOffset(
            lastDayBefore.Year, lastDayBefore.Month, lastDayBefore.Day, 23, 30, 0, TimeSpan.Zero);
        var (insideByClubTime, _) = await PastClassAsync(admin, memberId, utcLateEvening);

        var (outside, _) = await PastClassAsync(admin, memberId, windowStart.AddMinutes(-30));

        var first = await HistoryAsync(member);

        Assert.Contains(first.Items, i => i.ClassId == insideByClubTime);
        Assert.DoesNotContain(first.Items, i => i.ClassId == outside);
        Assert.Equal(firstMonth, first.EarlierBefore);

        var second = await HistoryAsync(member, firstMonth.ToString("yyyy-MM-dd"));

        Assert.Equal(outside, Assert.Single(second.Items).ClassId);
        Assert.Null(second.EarlierBefore);
        Assert.Null(second.Summary);
    }

    [Fact]
    public async Task EarlierBefore_is_null_when_nothing_older_exists()
    {
        var admin = await AdminAsync();
        var (member, memberId) = await MemberAsync(admin);

        await PastClassAsync(admin, memberId, DateTimeOffset.UtcNow.AddHours(-1));

        Assert.Null((await HistoryAsync(member)).EarlierBefore);
    }

    [Fact]
    public async Task A_before_that_is_not_the_first_of_a_month_is_a_bad_request()
    {
        var admin = await AdminAsync();
        var (member, _) = await MemberAsync(admin);

        var response = await member.GetAsync($"{HistoryEndpoint}?before=2026-09-15");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- the summary -----------------------------------------------------------

    [Fact]
    public async Task The_summary_counts_the_current_pass_only_and_skips_cancelled_classes()
    {
        var admin = await AdminAsync();
        var (member, memberId) = await MemberAsync(admin, withPass: false);

        // The current karnet covers today; an older one covers 2043, where the API books. Bookings
        // are re-attributed to the current pass in the database to model "paid by this karnet".
        var today = ClubToday();
        var current = await fixture.IssuePassAsync(
            memberId, entryCount: 8, validFrom: today.AddDays(-20), validTo: today.AddDays(20));
        await fixture.IssuePassAsync(
            memberId, entryCount: 8, validFrom: new DateOnly(2043, 1, 1), validTo: new DateOnly(2043, 12, 31));

        var now = DateTimeOffset.UtcNow;
        var (_, present) = await PastClassAsync(admin, memberId, now.AddHours(-1));
        var (_, absent) = await PastClassAsync(admin, memberId, now.AddHours(-2));
        var (_, unrecorded) = await PastClassAsync(admin, memberId, now.AddHours(-3));
        var (_, otherPass) = await PastClassAsync(admin, memberId, now.AddHours(-4));

        var cancelled = await ClassAsync(admin);
        await BookAsync(admin, cancelled.Id, memberId);
        await admin.PostAsync($"/api/admin/classes/{cancelled.Id}/cancel", content: null);
        await MoveAsync(cancelled.Id, now.AddHours(-5));
        var cancelledBooking = await BookingIdAsync(cancelled.Id, memberId);

        await using (var db = NewContext())
        {
            var ids = new[] { present, absent, unrecorded, cancelledBooking };
            await db.Bookings.Where(b => ids.Contains(b.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.MembershipPassId, current));
        }

        await MarkAsync(present, BookingAttendance.Present);
        await MarkAsync(absent, BookingAttendance.Absent);
        await MarkAsync(otherPass, BookingAttendance.Present);

        var summary = (await HistoryAsync(member)).Summary;

        Assert.NotNull(summary);
        Assert.Equal(8, summary.EntryCount);
        Assert.Equal(1, summary.Present);
        Assert.Equal(1, summary.Absent);
        Assert.Equal(1, summary.Unrecorded);
    }

    [Fact]
    public async Task There_is_no_summary_without_a_covering_pass()
    {
        var admin = await AdminAsync();
        var (member, memberId) = await MemberAsync(admin, withPass: false);
        await fixture.IssuePassAsync(
            memberId, validFrom: new DateOnly(2043, 1, 1), validTo: new DateOnly(2043, 12, 31));

        await PastClassAsync(admin, memberId, DateTimeOffset.UtcNow.AddHours(-1));

        var history = await HistoryAsync(member);

        Assert.Null(history.Summary);
        Assert.Single(history.Items);
    }
}
