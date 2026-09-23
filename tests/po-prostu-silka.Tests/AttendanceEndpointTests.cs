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

    // --- the marking API (Phase 2) ---------------------------------------------

    /// <summary>Mirrors ClassBooking — only what these tests read from it.</summary>
    private sealed record RosterRow(Guid BookingId, Guid MemberId, string? Attendance);

    private static string AttendanceOf(Guid classId, Guid bookingId) =>
        $"{BookingsOf(classId)}/{bookingId}/attendance";

    private static Task<HttpResponseMessage> PutMarkAsync(
        HttpClient client, Guid classId, Guid bookingId, string attendance) =>
        client.PutAsJsonAsync(AttendanceOf(classId, bookingId), new { attendance });

    /// <summary>A trainer's signed-in client and their member id, which a class names as instructor.</summary>
    private async Task<(HttpClient Client, Guid MemberId)> TrainerAsync(HttpClient admin)
    {
        var email = $"att-marker-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.Trainer);

        var memberId = await fixture.FindMemberIdAsync(admin, email);
        return (await fixture.CreateAuthenticatedClientAsync(email), memberId);
    }

    /// <summary>
    /// A started class with one booked member holding a wide pass — the arrangement nearly every
    /// marking test starts from.
    /// </summary>
    private async Task<(Guid ClassId, Guid BookingId, Guid MemberId)> StartedBookingAsync(
        HttpClient admin, Guid? instructorMemberId = null, int entryCount = 10)
    {
        var (_, memberId) = await MemberWithAccountAsync(admin);
        await fixture.IssuePassAsync(memberId, entryCount: entryCount);

        var scheduled = await ClassAsync(admin, instructorMemberId);
        await BookAsync(admin, scheduled.Id, memberId);
        await StartAsync(scheduled.Id);

        return (scheduled.Id, (await BookingOfAsync(scheduled.Id, memberId)).Id, memberId);
    }

    [Fact]
    public async Task A_trainer_marks_attendance_on_a_class_they_instruct()
    {
        var admin = await AdminAsync();
        var (trainer, trainerId) = await TrainerAsync(admin);
        var (classId, bookingId, memberId) = await StartedBookingAsync(admin, trainerId);

        var response = await PutMarkAsync(trainer, classId, bookingId, "present");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = (await response.Content.ReadFromJsonAsync<RosterRow>())!;
        Assert.Equal(bookingId, row.BookingId);
        Assert.Equal(memberId, row.MemberId);
        Assert.Equal("present", row.Attendance);
    }

    /// <summary>AT-01: the instructor check, not the policy, is what stops this one.</summary>
    [Fact]
    public async Task A_trainer_cannot_mark_attendance_on_another_trainers_class()
    {
        var admin = await AdminAsync();
        var (trainer, _) = await TrainerAsync(admin);
        var (classId, bookingId, _) = await StartedBookingAsync(admin);

        var response = await PutMarkAsync(trainer, classId, bookingId, "present");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null((await BookingByIdAsync(bookingId)).Attendance);
    }

    [Fact]
    public async Task An_admin_marks_attendance_on_any_class()
    {
        var admin = await AdminAsync();
        var (classId, bookingId, _) = await StartedBookingAsync(admin);

        var response = await PutMarkAsync(admin, classId, bookingId, "absent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingAttendance.Absent, (await BookingByIdAsync(bookingId)).Attendance);
    }

    [Fact]
    public async Task A_member_cannot_mark_attendance()
    {
        var admin = await AdminAsync();
        var (classId, bookingId, _) = await StartedBookingAsync(admin);
        var (member, _) = await MemberWithAccountAsync(admin);

        var response = await PutMarkAsync(member, classId, bookingId, "present");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Marking_before_the_start_is_refused()
    {
        var admin = await AdminAsync();
        var (_, memberId) = await MemberWithAccountAsync(admin);
        await fixture.IssuePassAsync(memberId);

        var future = await ClassAsync(admin);
        await BookAsync(admin, future.Id, memberId);
        var booking = await BookingOfAsync(future.Id, memberId);

        var response = await PutMarkAsync(admin, future.Id, booking.Id, "present");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("class_not_started", await ReasonAsync(response));
    }

    [Fact]
    public async Task Marking_on_a_cancelled_class_is_refused()
    {
        var admin = await AdminAsync();
        var (_, memberId) = await MemberWithAccountAsync(admin);
        await fixture.IssuePassAsync(memberId);

        var scheduled = await ClassAsync(admin);
        await BookAsync(admin, scheduled.Id, memberId);
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsync($"/api/admin/classes/{scheduled.Id}/cancel", content: null)).StatusCode);
        await StartAsync(scheduled.Id);

        var booking = await BookingOfAsync(scheduled.Id, memberId);
        var response = await PutMarkAsync(admin, scheduled.Id, booking.Id, "present");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("class_cancelled", await ReasonAsync(response));
    }

    /// <summary>AT-02: only a standing booking can be marked; a released one holds nothing.</summary>
    [Fact]
    public async Task Marking_a_released_booking_is_not_found()
    {
        var admin = await AdminAsync();
        var (_, memberId) = await MemberWithAccountAsync(admin);
        await fixture.IssuePassAsync(memberId);

        var scheduled = await ClassAsync(admin);
        await BookAsync(admin, scheduled.Id, memberId);
        var booking = await BookingOfAsync(scheduled.Id, memberId);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"{BookingsOf(scheduled.Id)}/{booking.Id}")).StatusCode);
        await StartAsync(scheduled.Id);

        var response = await PutMarkAsync(admin, scheduled.Id, booking.Id, "present");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_attendance_value_is_a_bad_request()
    {
        var admin = await AdminAsync();
        var (classId, bookingId, _) = await StartedBookingAsync(admin);

        var response = await PutMarkAsync(admin, classId, bookingId, "late");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>The same mark twice is a 200 with no write — a double tap rotates nothing.</summary>
    [Fact]
    public async Task Marking_the_same_state_twice_is_idempotent()
    {
        var admin = await AdminAsync();
        var (classId, bookingId, _) = await StartedBookingAsync(admin);

        Assert.Equal(HttpStatusCode.OK, (await PutMarkAsync(admin, classId, bookingId, "absent")).StatusCode);

        var passId = (await BookingByIdAsync(bookingId)).MembershipPassId!.Value;
        var stampBefore = await PassStampAsync(passId);
        var recordedBefore = (await BookingByIdAsync(bookingId)).AttendanceRecordedAt;

        var again = await PutMarkAsync(admin, classId, bookingId, "absent");

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("absent", (await again.Content.ReadFromJsonAsync<RosterRow>())!.Attendance);
        Assert.Equal(stampBefore, await PassStampAsync(passId));
        Assert.Equal(recordedBefore, (await BookingByIdAsync(bookingId)).AttendanceRecordedAt);
    }

    /// <summary>
    /// THE GATE ON A CORRECTION. Eight entries; one class marked absent frees an entry; a new booking
    /// takes it; correcting the absence back to present would now overdraw, so it is refused.
    /// </summary>
    [Fact]
    public async Task Correcting_absent_to_present_is_refused_when_the_pass_is_full()
    {
        const int Entries = 8;

        var admin = await AdminAsync();
        var (_, memberId) = await MemberWithAccountAsync(admin);
        var passId = await fixture.IssuePassAsync(memberId, entryCount: Entries);

        var classes = new List<ClassBody>();
        for (var i = 0; i < Entries; i++)
        {
            var c = await ClassAsync(admin);
            await BookAsync(admin, c.Id, memberId);
            classes.Add(c);
        }

        var missed = classes[0];
        await StartAsync(missed.Id);
        var booking = await BookingOfAsync(missed.Id, memberId);
        Assert.Equal(HttpStatusCode.OK, (await PutMarkAsync(admin, missed.Id, booking.Id, "absent")).StatusCode);

        // The freed entry is spent elsewhere.
        await BookAsync(admin, (await ClassAsync(admin)).Id, memberId);
        Assert.Equal(0, await EntriesLeftForAdminAsync(admin, memberId, passId));

        var refused = await PutMarkAsync(admin, missed.Id, booking.Id, "present");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("no_entries_left", await ReasonAsync(refused));
        Assert.Equal(BookingAttendance.Absent, (await BookingByIdAsync(booking.Id)).Attendance);
    }

    [Fact]
    public async Task Correcting_absent_to_present_spends_the_entry_again()
    {
        var admin = await AdminAsync();
        var (classId, bookingId, memberId) = await StartedBookingAsync(admin, entryCount: 3);
        var passId = (await BookingByIdAsync(bookingId)).MembershipPassId!.Value;

        Assert.Equal(HttpStatusCode.OK, (await PutMarkAsync(admin, classId, bookingId, "absent")).StatusCode);
        Assert.Equal(3, await EntriesLeftForAdminAsync(admin, memberId, passId));

        Assert.Equal(HttpStatusCode.OK, (await PutMarkAsync(admin, classId, bookingId, "present")).StatusCode);
        Assert.Equal(2, await EntriesLeftForAdminAsync(admin, memberId, passId));
    }

    /// <summary>
    /// The race the pass stamp exists for, on the new write: one free entry, and at the same instant a
    /// booking wants it and a correction wants it back. Asserted against the DATABASE, like every race
    /// in this repository. Run several rounds, since one round can pass by a coincidence of scheduling.
    /// </summary>
    [Fact]
    public async Task Concurrent_rebooking_and_correction_never_overdraw_the_pass()
    {
        for (var round = 0; round < 5; round++)
        {
            var admin = await AdminAsync();
            var (classId, bookingId, memberId) = await StartedBookingAsync(admin, entryCount: 1);
            var passId = (await BookingByIdAsync(bookingId)).MembershipPassId!.Value;

            Assert.Equal(HttpStatusCode.OK, (await PutMarkAsync(admin, classId, bookingId, "absent")).StatusCode);

            var next = await ClassAsync(admin);
            var booker = await AdminAsync();
            var corrector = await AdminAsync();

            await Task.WhenAll(
                booker.PostAsJsonAsync(BookingsOf(next.Id), new { memberId }),
                PutMarkAsync(corrector, classId, bookingId, "present"));

            Assert.True(await ConsumingForPassAsync(passId) <= 1);
        }
    }

    [Fact]
    public async Task The_roster_reports_each_rows_attendance()
    {
        var admin = await AdminAsync();
        var (_, present) = await MemberWithAccountAsync(admin);
        var (_, absent) = await MemberWithAccountAsync(admin);
        var (_, unrecorded) = await MemberWithAccountAsync(admin);
        foreach (var id in new[] { present, absent, unrecorded })
        {
            await fixture.IssuePassAsync(id);
        }

        var scheduled = await ClassAsync(admin);
        foreach (var id in new[] { present, absent, unrecorded })
        {
            await BookAsync(admin, scheduled.Id, id);
        }

        await StartAsync(scheduled.Id);
        await PutMarkAsync(admin, scheduled.Id, (await BookingOfAsync(scheduled.Id, present)).Id, "present");
        await PutMarkAsync(admin, scheduled.Id, (await BookingOfAsync(scheduled.Id, absent)).Id, "absent");

        var roster = (await admin.GetFromJsonAsync<List<RosterRow>>(BookingsOf(scheduled.Id)))!;

        Assert.Equal("present", roster.Single(r => r.MemberId == present).Attendance);
        Assert.Equal("absent", roster.Single(r => r.MemberId == absent).Attendance);
        Assert.Null(roster.Single(r => r.MemberId == unrecorded).Attendance);
    }

    [Fact]
    public async Task Marking_attendance_records_who_and_when()
    {
        var admin = await AdminAsync();
        var (trainer, trainerId) = await TrainerAsync(admin);
        var (classId, bookingId, _) = await StartedBookingAsync(admin, trainerId);

        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.Equal(HttpStatusCode.OK, (await PutMarkAsync(trainer, classId, bookingId, "present")).StatusCode);

        var booking = await BookingByIdAsync(bookingId);
        Assert.NotNull(booking.AttendanceRecordedAt);
        Assert.True(booking.AttendanceRecordedAt > before);

        await using var db = NewContext();
        var trainerUserId = await db.Members.AsNoTracking()
            .Where(m => m.Id == trainerId).Select(m => m.UserId).SingleAsync();
        Assert.Equal(trainerUserId, booking.AttendanceRecordedBy);
    }

    private async Task<Booking> BookingByIdAsync(Guid bookingId)
    {
        await using var db = NewContext();

        return await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
    }

    /// <summary>The entry rule spelled out by hand, so the race is judged by the rule, not by the code under test.</summary>
    private async Task<int> ConsumingForPassAsync(Guid passId)
    {
        await using var db = NewContext();

        return await db.Bookings.AsNoTracking().CountAsync(b =>
            b.MembershipPassId == passId
            && b.Status == BookingStatus.Active
            && b.Class.Status != ClassStatus.Cancelled
            && (b.Attendance == null || b.Attendance != BookingAttendance.Absent));
    }
}
