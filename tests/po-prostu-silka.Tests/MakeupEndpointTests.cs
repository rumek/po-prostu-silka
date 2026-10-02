using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// Makeups (S-36, class-makeups): the "Odrabianie" list, its status rule, and the free makeup booking.
///
/// <para>
/// TIME IS ARRANGED IN THE DATABASE. The API creates classes only in the future and refuses overlaps
/// club-wide, while these tests need classes at exact instants around today (a deadline is today's
/// club-local date plus thirty days). So each class is created at a far slot of its own - 2044, used by
/// no other suite - and then MOVED to the instant the case needs, which no overlap rule sees. Moved
/// instants carry a per-test random minute offset, so two classes never share a start.
/// </para>
///
/// <para>
/// MOVED CLASSES ARE MOVED BACK AFTER EACH TEST. They sit near today, where other suites create
/// classes through the API, and the club-wide overlap rule would refuse those. The collection runs its
/// tests one at a time, so returning every class this test moved to a far slot in
/// <see cref="DisposeAsync"/> leaves near-today free for whichever test runs next.
/// </para>
///
/// <para>
/// THE LIST IS THE WHOLE CLUB'S, shared by every test in the collection, so assertions look up their
/// own item by id and never count rows.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class MakeupEndpointTests(IntegrationTestFixture fixture) : IAsyncLifetime
{
    private readonly List<Guid> _moved = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        foreach (var classId in _moved.Distinct())
        {
            var away = NextFarSlot();
            await db.Classes.Where(c => c.Id == classId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.StartsAt, away));
        }
    }

    private sealed record ClassBody(Guid Id, DateTimeOffset StartsAt, int FreeSpots);

    private sealed record ClassGroupBody(Guid Id, string Name);

    private sealed record FailureBody(string Reason);

    private sealed record MakeupClassBody(Guid ClassId, Guid BookingId, string Name, DateTimeOffset StartsAt);

    private sealed record ItemBody(
        Guid AbsenceBookingId,
        Guid MemberId,
        DateOnly Deadline,
        string Status,
        bool ClosedByHand,
        MakeupClassBody? Makeup);

    private sealed record MineBody(int Count, DateOnly? NearestDeadline);

    private static int _slot;

    private static DateTimeOffset NextFarSlot() =>
        new DateTimeOffset(2044, 1, 1, 10, 0, 0, TimeSpan.Zero)
            .AddDays(2 * Interlocked.Increment(ref _slot));

    private static int _minute;

    /// <summary>A distinct minute offset, so two moved classes never share an instant.</summary>
    private static TimeSpan NextMinute() => TimeSpan.FromMinutes(Interlocked.Increment(ref _minute) % 50);

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(fixture.ConnectionString).Options);

    private Task<HttpClient> AdminAsync() =>
        fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    private static DateOnly ClubToday() =>
        DateOnly.FromDateTime(ClubTime.ToClubLocal(DateTimeOffset.UtcNow).DateTime);

    /// <summary>A club-local wall-clock time on a club-local date, as a UTC instant.</summary>
    private static DateTimeOffset AtClubTime(DateOnly date, int hour, int minute = 0) =>
        ClubTime.StartOfLocalDay(date).AddHours(hour).AddMinutes(minute);

    // --- arrangement -----------------------------------------------------------

    private async Task<(HttpClient Client, Guid MemberId)> TrainerAsync(HttpClient admin)
    {
        var email = $"mk-trainer-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.Trainer);

        return (await fixture.CreateAuthenticatedClientAsync(email), await fixture.FindMemberIdAsync(admin, email));
    }

    private async Task<(HttpClient Client, Guid MemberId)> MemberAsync(HttpClient admin)
    {
        var email = $"mk-member-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        return (await fixture.CreateAuthenticatedClientAsync(email), await fixture.FindMemberIdAsync(admin, email));
    }

    /// <summary>A class at <paramref name="startsAt"/>: created far away, then moved there.</summary>
    private async Task<Guid> ClassAtAsync(
        HttpClient admin, DateTimeOffset startsAt, int capacity = 12, Guid? instructorMemberId = null)
    {
        var groupResponse = await admin.PostAsJsonAsync("/api/admin/class-groups", new
        {
            name = $"Odrabianie-{Guid.NewGuid():N}",
            description = (string?)"Opis",
            defaultDurationMinutes = 60,
            defaultCapacity = capacity,
        });
        Assert.Equal(HttpStatusCode.OK, groupResponse.StatusCode);
        var group = (await groupResponse.Content.ReadFromJsonAsync<ClassGroupBody>())!;

        var response = await admin.PostAsJsonAsync("/api/admin/classes", new
        {
            classGroupId = group.Id,
            startsAt = NextFarSlot(),
            instructorMemberId = instructorMemberId ?? (await TrainerAsync(admin)).MemberId,
            durationMinutes = 60,
            capacity,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ClassBody>())!;

        await MoveAsync(created.Id, startsAt);
        return created.Id;
    }

    private async Task MoveAsync(Guid classId, DateTimeOffset startsAt)
    {
        await using var db = NewContext();
        var at = startsAt + NextMinute();
        await db.Classes.Where(c => c.Id == classId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.StartsAt, at));
        _moved.Add(classId);
    }

    /// <summary>Books through the staff route while the class is still far in the future, then moves it.</summary>
    private async Task<Guid> BookedClassAtAsync(HttpClient admin, Guid memberId, DateTimeOffset startsAt)
    {
        var classId = await ClassAtAsync(admin, NextFarSlot());
        var response = await admin.PostAsJsonAsync($"/api/admin/classes/{classId}/bookings", new { memberId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await MoveAsync(classId, startsAt);
        return classId;
    }

    /// <summary>
    /// A karnet covering only 2044, for tests whose real karnets are narrow: the staff route books at
    /// the far slot before the class is moved, and must find a karnet there. It covers no day near
    /// today, so it changes nothing those tests assert.
    /// </summary>
    private Task FarSlotPassAsync(Guid memberId) =>
        fixture.IssuePassAsync(memberId, validFrom: new DateOnly(2044, 1, 1), validTo: new DateOnly(2044, 12, 31));

    private async Task<Guid> BookingIdAsync(Guid classId, Guid memberId)
    {
        await using var db = NewContext();
        return await db.Bookings.AsNoTracking()
            .Where(b => b.ClassId == classId && b.MemberId == memberId && b.Status == BookingStatus.Active)
            .Select(b => b.Id)
            .SingleAsync();
    }

    /// <summary>
    /// A member with a wide karnet and one absence marked "odrobi" through the API, on a class that took
    /// place <paramref name="daysAgo"/> club-local days ago at 10:00.
    /// </summary>
    private async Task<(HttpClient Member, Guid MemberId, Guid AbsenceId, Guid PassId)> AbsenceAsync(
        HttpClient admin, int daysAgo = 2, int entryCount = 10)
    {
        var (member, memberId) = await MemberAsync(admin);
        var passId = await fixture.IssuePassAsync(memberId, entryCount: entryCount);

        var classId = await BookedClassAtAsync(admin, memberId, AtClubTime(ClubToday().AddDays(-daysAgo), 10));
        var absenceId = await BookingIdAsync(classId, memberId);

        var marked = await admin.PutAsJsonAsync(
            $"/api/admin/classes/{classId}/bookings/{absenceId}/attendance", new { attendance = "makeup" });
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);

        return (member, memberId, absenceId, passId);
    }

    private static async Task<ItemBody> ItemAsync(HttpClient staff, Guid absenceId, bool closed = true)
    {
        for (var page = 1; ; page++)
        {
            var body = (await staff.GetFromJsonAsync<MemberPageBody<ItemBody>>(
                $"/api/makeups?closed={closed.ToString().ToLowerInvariant()}&page={page}"))!;

            var found = body.Items.FirstOrDefault(i => i.AbsenceBookingId == absenceId);
            if (found is not null)
            {
                return found;
            }

            Assert.True(page * body.PageSize < body.Total, $"Item {absenceId} is not on the list");
        }
    }

    private static async Task<bool> ListedAsync(HttpClient staff, Guid absenceId, bool closed)
    {
        for (var page = 1; ; page++)
        {
            var body = (await staff.GetFromJsonAsync<MemberPageBody<ItemBody>>(
                $"/api/makeups?closed={closed.ToString().ToLowerInvariant()}&page={page}"))!;

            if (body.Items.Any(i => i.AbsenceBookingId == absenceId))
            {
                return true;
            }

            if (page * body.PageSize >= body.Total)
            {
                return false;
            }
        }
    }

    private static Task<HttpResponseMessage> BookMakeupAsync(HttpClient staff, Guid absenceId, Guid classId) =>
        staff.PostAsJsonAsync($"/api/makeups/{absenceId}/booking", new { classId });

    private static async Task<string> ReasonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason;

    private static async Task<int> EntriesLeftAsync(HttpClient admin, Guid memberId, Guid passId) =>
        (await admin.GetFromJsonAsync<List<MembershipPassView>>($"/api/admin/members/{memberId}/passes"))!
        .Single(p => p.Id == passId).EntriesLeft;

    private async Task MarkAsync(Guid bookingId, BookingAttendance attendance)
    {
        await using var db = NewContext();
        await db.Bookings.Where(b => b.Id == bookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Attendance, attendance));
    }

    // --- status ----------------------------------------------------------------

    [Fact]
    public async Task A_fresh_absence_is_open_with_a_thirty_day_deadline()
    {
        var admin = await AdminAsync();
        var (_, memberId, absenceId, _) = await AbsenceAsync(admin, daysAgo: 2);

        var item = await ItemAsync(admin, absenceId, closed: false);

        Assert.Equal("open", item.Status);
        Assert.Equal(memberId, item.MemberId);
        Assert.Equal(ClubToday().AddDays(-2).AddDays(MakeupRules.DeadlineDays), item.Deadline);
        Assert.Null(item.Makeup);
    }

    /// <summary>
    /// Past the deadline with nothing booked, the item is "nie odrobił": off the working list, on the
    /// closed one.
    /// </summary>
    [Fact]
    public async Task An_absence_past_its_deadline_is_not_made_up()
    {
        var admin = await AdminAsync();
        var (_, _, absenceId, _) = await AbsenceAsync(admin, daysAgo: MakeupRules.DeadlineDays + 1);

        Assert.False(await ListedAsync(admin, absenceId, closed: false));
        Assert.Equal("not_made_up", (await ItemAsync(admin, absenceId)).Status);
    }

    /// <summary>The deadline day itself is still open - the boundary is inclusive.</summary>
    [Fact]
    public async Task An_absence_whose_deadline_is_today_is_still_open()
    {
        var admin = await AdminAsync();
        var (_, _, absenceId, _) = await AbsenceAsync(admin, daysAgo: MakeupRules.DeadlineDays);

        Assert.Equal("open", (await ItemAsync(admin, absenceId, closed: false)).Status);
    }

    /// <summary>
    /// A PLANNED MAKEUP OUTLIVES THE DEADLINE. Booked in time, it waits for its mark however long the
    /// absence has been past thirty days.
    /// </summary>
    [Fact]
    public async Task A_planned_makeup_stays_planned_past_the_deadline()
    {
        var admin = await AdminAsync();
        var (_, memberId, absenceId, _) = await AbsenceAsync(admin, daysAgo: MakeupRules.DeadlineDays + 5);

        var makeupClass = await BookedClassAtAsync(admin, memberId, AtClubTime(ClubToday().AddDays(-6), 18));
        var makeupId = await BookingIdAsync(makeupClass, memberId);
        await using (var db = NewContext())
        {
            await db.Bookings.Where(b => b.Id == makeupId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.MakeupForBookingId, absenceId));
        }

        var item = await ItemAsync(admin, absenceId, closed: false);
        Assert.Equal("planned", item.Status);
        Assert.Equal(makeupClass, item.Makeup!.ClassId);
    }

    // --- booking ---------------------------------------------------------------

    /// <summary>
    /// The heart of it: a trainer books a member's makeup into ANOTHER trainer's class, the makeup costs
    /// no entry, and attending it makes the item "odrobił".
    /// </summary>
    [Fact]
    public async Task A_trainer_books_a_free_makeup_into_another_trainers_class()
    {
        var admin = await AdminAsync();
        var (trainer, _) = await TrainerAsync(admin);
        var (_, memberId, absenceId, passId) = await AbsenceAsync(admin);
        var entriesBefore = await EntriesLeftAsync(admin, memberId, passId);

        var target = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(3), 17));

        var booked = await BookMakeupAsync(trainer, absenceId, target);
        Assert.Equal(HttpStatusCode.OK, booked.StatusCode);

        var item = (await booked.Content.ReadFromJsonAsync<ItemBody>())!;
        Assert.Equal("planned", item.Status);
        Assert.Equal(target, item.Makeup!.ClassId);
        Assert.Equal(entriesBefore, await EntriesLeftAsync(admin, memberId, passId));

        // The makeup happens: moved into the past and marked present by the admin.
        await MoveAsync(target, DateTimeOffset.UtcNow.AddHours(-2));
        var marked = await admin.PutAsJsonAsync(
            $"/api/admin/classes/{target}/bookings/{item.Makeup.BookingId}/attendance",
            new { attendance = "present" });
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);

        Assert.Equal("made_up", (await ItemAsync(admin, absenceId)).Status);
        Assert.Equal(entriesBefore, await EntriesLeftAsync(admin, memberId, passId));
    }

    [Fact]
    public async Task A_makeup_marked_forfeited_is_not_made_up()
    {
        var admin = await AdminAsync();
        var (_, _, absenceId, _) = await AbsenceAsync(admin);
        var target = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(2), 9));
        var item = (await (await BookMakeupAsync(admin, absenceId, target)).Content.ReadFromJsonAsync<ItemBody>())!;

        await MoveAsync(target, DateTimeOffset.UtcNow.AddHours(-1));
        await admin.PutAsJsonAsync(
            $"/api/admin/classes/{target}/bookings/{item.Makeup!.BookingId}/attendance",
            new { attendance = "forfeited" });

        Assert.Equal("not_made_up", (await ItemAsync(admin, absenceId)).Status);
    }

    [Fact]
    public async Task A_second_makeup_for_the_same_absence_is_refused()
    {
        var admin = await AdminAsync();
        var (_, _, absenceId, _) = await AbsenceAsync(admin);
        var first = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(4), 8));
        var second = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(5), 8));

        Assert.Equal(HttpStatusCode.OK, (await BookMakeupAsync(admin, absenceId, first)).StatusCode);

        var refused = await BookMakeupAsync(admin, absenceId, second);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("makeup_not_open", await ReasonAsync(refused));
    }

    /// <summary>
    /// Two staff book the same absence at once, into two different classes - two different class
    /// stamps, so only the filtered unique index stands between them. Exactly one makeup survives.
    /// </summary>
    [Fact]
    public async Task Concurrent_makeups_for_one_absence_leave_exactly_one()
    {
        for (var round = 0; round < 3; round++)
        {
            var admin = await AdminAsync();
            var (_, _, absenceId, _) = await AbsenceAsync(admin);
            var first = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(6), 7));
            var second = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(7), 7));

            await Task.WhenAll(
                BookMakeupAsync(await AdminAsync(), absenceId, first),
                BookMakeupAsync(await AdminAsync(), absenceId, second));

            await using var db = NewContext();
            Assert.Equal(1, await db.Bookings.CountAsync(
                b => b.MakeupForBookingId == absenceId && b.Status == BookingStatus.Active));
        }
    }

    /// <summary>The deadline governs the makeup CLASS's club-local date, inclusive, around Warsaw midnight.</summary>
    [Fact]
    public async Task The_deadline_bounds_the_makeup_class_date_at_club_midnight()
    {
        var admin = await AdminAsync();
        var (_, _, absenceId, _) = await AbsenceAsync(admin, daysAgo: 10);
        var deadline = ClubToday().AddDays(-10).AddDays(MakeupRules.DeadlineDays);

        var lateOnTheLastDay = await ClassAtAsync(admin, AtClubTime(deadline, 23));
        var earlyTheDayAfter = await ClassAtAsync(admin, AtClubTime(deadline.AddDays(1), 0));

        var eligible = (await admin.GetFromJsonAsync<List<ClassBody>>($"/api/makeups/{absenceId}/classes"))!;
        Assert.Contains(eligible, c => c.Id == lateOnTheLastDay);
        Assert.DoesNotContain(eligible, c => c.Id == earlyTheDayAfter);

        var refused = await BookMakeupAsync(admin, absenceId, earlyTheDayAfter);
        Assert.Equal("makeup_deadline_passed", await ReasonAsync(refused));

        Assert.Equal(HttpStatusCode.OK, (await BookMakeupAsync(admin, absenceId, lateOnTheLastDay)).StatusCode);
    }

    /// <summary>
    /// The picker offers only what the booking would accept: not a full class, not one the member is
    /// already in, not a day no karnet of theirs covers.
    /// </summary>
    [Fact]
    public async Task The_eligible_classes_exclude_full_taken_and_uncovered_days()
    {
        var admin = await AdminAsync();
        var (_, memberId) = await MemberAsync(admin);
        var today = ClubToday();
        await fixture.IssuePassAsync(memberId, validFrom: today.AddDays(-5), validTo: today.AddDays(8));
        await FarSlotPassAsync(memberId);

        var absenceClass = await BookedClassAtAsync(admin, memberId, AtClubTime(today.AddDays(-2), 10));
        var absenceId = await BookingIdAsync(absenceClass, memberId);
        await admin.PutAsJsonAsync(
            $"/api/admin/classes/{absenceClass}/bookings/{absenceId}/attendance", new { attendance = "makeup" });

        var open = await ClassAtAsync(admin, AtClubTime(today.AddDays(2), 12));

        var (_, other) = await MemberAsync(admin);
        await fixture.IssuePassAsync(other);
        var full = await ClassAtAsync(admin, NextFarSlot(), capacity: 1);
        await admin.PostAsJsonAsync($"/api/admin/classes/{full}/bookings", new { memberId = other });
        await MoveAsync(full, AtClubTime(today.AddDays(3), 12));

        var taken = await BookedClassAtAsync(admin, memberId, AtClubTime(today.AddDays(4), 12));
        var uncovered = await ClassAtAsync(admin, AtClubTime(today.AddDays(10), 12));

        var eligible = (await admin.GetFromJsonAsync<List<ClassBody>>($"/api/makeups/{absenceId}/classes"))!
            .Select(c => c.Id).ToList();

        Assert.Contains(open, eligible);
        Assert.DoesNotContain(full, eligible);
        Assert.DoesNotContain(taken, eligible);
        Assert.DoesNotContain(uncovered, eligible);

        Assert.Equal("no_valid_pass", await ReasonAsync(await BookMakeupAsync(admin, absenceId, uncovered)));
    }

    /// <summary>A renewal does not cancel the right to make up: any karnet covering the makeup day will do.</summary>
    [Fact]
    public async Task A_renewed_karnet_covers_a_makeup_after_the_original_one_ended()
    {
        var admin = await AdminAsync();
        var (_, memberId) = await MemberAsync(admin);
        var today = ClubToday();
        await fixture.IssuePassAsync(memberId, validFrom: today.AddDays(-20), validTo: today.AddDays(1));
        await fixture.IssuePassAsync(memberId, validFrom: today.AddDays(2), validTo: today.AddDays(40));
        await FarSlotPassAsync(memberId);

        var absenceClass = await BookedClassAtAsync(admin, memberId, AtClubTime(today.AddDays(-3), 10));
        var absenceId = await BookingIdAsync(absenceClass, memberId);
        await admin.PutAsJsonAsync(
            $"/api/admin/classes/{absenceClass}/bookings/{absenceId}/attendance", new { attendance = "makeup" });

        var afterRenewal = await ClassAtAsync(admin, AtClubTime(today.AddDays(5), 11));

        Assert.Equal(HttpStatusCode.OK, (await BookMakeupAsync(admin, absenceId, afterRenewal)).StatusCode);
    }

    // --- release, cancellation, re-marking --------------------------------------

    [Fact]
    public async Task Releasing_a_makeup_opens_the_item_again()
    {
        var admin = await AdminAsync();
        var (trainer, _) = await TrainerAsync(admin);
        var (_, _, absenceId, _) = await AbsenceAsync(admin);
        var target = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(3), 6));
        Assert.Equal(HttpStatusCode.OK, (await BookMakeupAsync(admin, absenceId, target)).StatusCode);

        var released = await trainer.DeleteAsync($"/api/makeups/{absenceId}/booking");

        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal("open", (await released.Content.ReadFromJsonAsync<ItemBody>())!.Status);
        Assert.Equal(HttpStatusCode.OK, (await BookMakeupAsync(admin, absenceId, target)).StatusCode);
    }

    /// <summary>
    /// Cancelling the makeup's class opens the item again, and a new makeup can be booked beside the
    /// still-active row the cancelled class left behind.
    /// </summary>
    [Fact]
    public async Task A_cancelled_makeup_class_opens_the_item_and_it_can_be_rebooked()
    {
        var admin = await AdminAsync();
        var (_, memberId, absenceId, _) = await AbsenceAsync(admin);
        var first = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(3), 20));
        Assert.Equal(HttpStatusCode.OK, (await BookMakeupAsync(admin, absenceId, first)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsync($"/api/admin/classes/{first}/cancel", content: null)).StatusCode);
        Assert.Equal("open", (await ItemAsync(admin, absenceId, closed: false)).Status);

        var second = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(4), 20));
        Assert.Equal(HttpStatusCode.OK, (await BookMakeupAsync(admin, absenceId, second)).StatusCode);
        Assert.Equal(second, (await ItemAsync(admin, absenceId, closed: false)).Makeup!.ClassId);
    }

    [Fact]
    public async Task An_absence_with_a_planned_makeup_cannot_be_re_marked()
    {
        var admin = await AdminAsync();
        var (_, memberId, absenceId, _) = await AbsenceAsync(admin);
        var target = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(2), 21));
        Assert.Equal(HttpStatusCode.OK, (await BookMakeupAsync(admin, absenceId, target)).StatusCode);

        await using var db = NewContext();
        var absenceClass = await db.Bookings.Where(b => b.Id == absenceId).Select(b => b.ClassId).SingleAsync();

        var refused = await admin.PutAsJsonAsync(
            $"/api/admin/classes/{absenceClass}/bookings/{absenceId}/attendance", new { attendance = "present" });
        Assert.Equal("makeup_booked", await ReasonAsync(refused));
    }

    // --- close and reopen ------------------------------------------------------

    [Fact]
    public async Task Closing_and_reopening_within_the_deadline()
    {
        var admin = await AdminAsync();
        var (trainer, _) = await TrainerAsync(admin);
        var (_, _, absenceId, _) = await AbsenceAsync(admin);

        var closed = await trainer.PutAsync($"/api/makeups/{absenceId}/closed", content: null);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var item = (await closed.Content.ReadFromJsonAsync<ItemBody>())!;
        Assert.Equal("not_made_up", item.Status);
        Assert.True(item.ClosedByHand);
        Assert.False(await ListedAsync(admin, absenceId, closed: false));

        var target = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(2), 22));
        Assert.Equal("makeup_not_open", await ReasonAsync(await BookMakeupAsync(admin, absenceId, target)));

        var reopened = await trainer.DeleteAsync($"/api/makeups/{absenceId}/closed");
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal("open", (await reopened.Content.ReadFromJsonAsync<ItemBody>())!.Status);
    }

    [Fact]
    public async Task A_planned_item_cannot_be_closed()
    {
        var admin = await AdminAsync();
        var (_, _, absenceId, _) = await AbsenceAsync(admin);
        var target = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(3), 5));
        Assert.Equal(HttpStatusCode.OK, (await BookMakeupAsync(admin, absenceId, target)).StatusCode);

        var refused = await admin.PutAsync($"/api/makeups/{absenceId}/closed", content: null);
        Assert.Equal("makeup_not_open", await ReasonAsync(refused));
    }

    [Fact]
    public async Task Reopening_after_the_deadline_is_refused()
    {
        var admin = await AdminAsync();
        var (_, _, absenceId, _) = await AbsenceAsync(admin, daysAgo: MakeupRules.DeadlineDays + 2);
        await using (var db = NewContext())
        {
            await db.Bookings.Where(b => b.Id == absenceId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.MakeupClosedAt, DateTimeOffset.UtcNow.AddDays(-20)));
        }

        var refused = await admin.DeleteAsync($"/api/makeups/{absenceId}/closed");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("makeup_not_reopenable", await ReasonAsync(refused));
    }

    // --- the member, and access ------------------------------------------------

    [Fact]
    public async Task The_member_sees_only_their_open_items_and_the_nearest_deadline()
    {
        var admin = await AdminAsync();
        var (member, memberId, nearer, _) = await AbsenceAsync(admin, daysAgo: 6);

        var laterClass = await BookedClassAtAsync(admin, memberId, AtClubTime(ClubToday().AddDays(-1), 9));
        var later = await BookingIdAsync(laterClass, memberId);
        await MarkAsync(later, BookingAttendance.Makeup);

        var plannedClass = await BookedClassAtAsync(admin, memberId, AtClubTime(ClubToday().AddDays(-3), 9));
        var planned = await BookingIdAsync(plannedClass, memberId);
        await MarkAsync(planned, BookingAttendance.Makeup);
        var target = await ClassAtAsync(admin, AtClubTime(ClubToday().AddDays(2), 23));
        Assert.Equal(HttpStatusCode.OK, (await BookMakeupAsync(admin, planned, target)).StatusCode);

        var mine = (await member.GetFromJsonAsync<MineBody>("/api/makeups/mine"))!;

        Assert.Equal(2, mine.Count);
        Assert.Equal(ClubToday().AddDays(-6).AddDays(MakeupRules.DeadlineDays), mine.NearestDeadline);
        _ = nearer;
    }

    [Fact]
    public async Task A_member_is_refused_the_staff_routes_and_staff_the_members_route()
    {
        var admin = await AdminAsync();
        var (member, _, absenceId, _) = await AbsenceAsync(admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/makeups")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.PutAsync($"/api/makeups/{absenceId}/closed", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/makeups/mine")).StatusCode);
    }

    [Fact]
    public async Task An_ordinary_booking_is_not_an_item()
    {
        var admin = await AdminAsync();
        var (_, memberId) = await MemberAsync(admin);
        await fixture.IssuePassAsync(memberId);
        var classId = await BookedClassAtAsync(admin, memberId, AtClubTime(ClubToday().AddDays(-1), 13));
        var bookingId = await BookingIdAsync(classId, memberId);
        await MarkAsync(bookingId, BookingAttendance.Forfeited);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/makeups/{bookingId}/classes")).StatusCode);
        Assert.False(await ListedAsync(admin, bookingId, closed: true));
    }
}
