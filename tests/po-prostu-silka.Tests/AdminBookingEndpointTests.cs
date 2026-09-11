using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// The admin booking a class on somebody else's behalf (S-14, AM-007).
///
/// <para>
/// WHY THIS FILE EXISTS: <see cref="Concurrent_admin_bookings_never_exceed_capacity"/>. The route was
/// built by extracting <c>BookAsync</c>'s loop into <c>TryBookAsync</c> and calling it from two
/// places, and the entire argument for doing it that way is that the no-overbooking protocol stays
/// literally the same one. That argument is worth exactly as much as a test that runs the race
/// through the ADMIN route — everything else here would pass against a second, subtly wrong copy of
/// the loop.
/// </para>
///
/// <para>
/// Slots start in 2036, clear of BookingEndpointTests' 2032 and ClassEndpointTests' 2030: the overlap
/// rule is club-wide and all three files share one database.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class AdminBookingEndpointTests(IntegrationTestFixture fixture)
{
    /// <summary>Mirrors ScheduledClass — only what these tests read from it.</summary>
    private sealed record ClassBody(Guid Id, int Capacity, int FreeSpots, DateTimeOffset StartsAt);

    /// <summary>Mirrors ClassTypeSummary — only what these tests read from it.</summary>
    private sealed record ClassTypeBody(Guid Id, string Name);

    /// <summary>Mirrors MemberSummary — only what these tests read from it.</summary>
    private sealed record MemberBody(Guid Id, string? UserId, string Email);

    /// <summary>Mirrors ClassBooking.</summary>
    private sealed record ClassBookingBody(
        Guid BookingId,
        Guid MemberId,
        string? UserId,
        string DisplayName,
        string Email,
        DateTimeOffset BookedAt);

    /// <summary>Mirrors BookingFailure.</summary>
    private sealed record FailureBody(string Reason);

    private const string ClassesEndpoint = "/api/admin/classes";
    private const string TypesEndpoint = "/api/admin/class-types";

    private static string AdminBookingsOf(Guid classId) => $"/api/admin/classes/{classId}/bookings";

    private static int _slot;

    private static DateTimeOffset NextSlot() =>
        new DateTimeOffset(2036, 6, 1, 10, 0, 0, TimeSpan.Zero)
            .AddDays(60 * Interlocked.Increment(ref _slot));

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(fixture.ConnectionString).Options);

    // --- fixture helpers -------------------------------------------------------

    private Task<HttpClient> AdminAsync() =>
        fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    private static async Task<ClassTypeBody> CreateTypeAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync(TypesEndpoint, new
        {
            name = $"Joga-{Guid.NewGuid():N}",
            description = (string?)"Opis zajęć",
            defaultDurationMinutes = 60,
            defaultCapacity = 12,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClassTypeBody>())!;
    }

    /// <summary>An active trainer's MEMBER id, which is what a class names as its instructor.</summary>
    private async Task<Guid> CreateTrainerAsync(HttpClient admin)
    {
        var email = $"trainer-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.Trainer);

        var members = await admin.GetFromJsonAsync<List<MemberBody>>("/api/admin/members");

        return members!.Single(m => m.Email == email).Id;
    }

    /// <summary>A class with the given capacity, and the admin who made it.</summary>
    private async Task<(HttpClient Admin, ClassBody Class)> ArrangeAsync(int capacity = 12)
    {
        var admin = await AdminAsync();
        var (scheduled, _) = await ClassWithInstructorAsync(admin, capacity: capacity);

        return (admin, scheduled);
    }

    /// <summary>
    /// A class and the MEMBER id of the trainer instructing it — which the ownership tests need, since
    /// the whole question they ask is whether the caller is that person.
    /// </summary>
    /// <param name="instructorMemberId">
    /// Whom to put in front of the class. Null mints a fresh trainer, which is what every test that
    /// does not care about the instructor wants.
    /// </param>
    private async Task<(ClassBody Class, Guid InstructorMemberId)> ClassWithInstructorAsync(
        HttpClient admin,
        Guid? instructorMemberId = null,
        int capacity = 12)
    {
        var type = await CreateTypeAsync(admin);
        var trainerId = instructorMemberId ?? await CreateTrainerAsync(admin);

        var response = await admin.PostAsJsonAsync(ClassesEndpoint, new
        {
            classTypeId = type.Id,
            startsAt = NextSlot(),
            instructorMemberId = trainerId,
            durationMinutes = 60,
            capacity,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return ((await response.Content.ReadFromJsonAsync<ClassBody>())!, trainerId);
    }

    /// <summary>
    /// A staff account holding BOTH User and Trainer — what promoting a member actually produces, and
    /// therefore the only shape worth testing the narrowing against. Returns the signed-in client and
    /// their member id.
    /// </summary>
    private async Task<(HttpClient Client, Guid MemberId)> NewTrainerAsync(HttpClient admin)
    {
        var email = $"scoped-trainer-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(
            email, AccountStatus.Active, ApplicationRoles.User, additionalRole: ApplicationRoles.Trainer);

        var members = await admin.GetFromJsonAsync<List<MemberBody>>("/api/admin/members");
        var memberId = members!.Single(m => m.Email == email).Id;

        return (await fixture.CreateAuthenticatedClientAsync(email), memberId);
    }

    /// <summary>
    /// A person the club recorded who has never registered — the case the route exists for, since
    /// they cannot book for themselves.
    /// </summary>
    private async Task<Guid> AccountlessMemberAsync(MembershipStatus status = MembershipStatus.Active)
    {
        var memberId = await fixture.CreateMemberAsync($"Bez Konta {Guid.NewGuid():N}", status);

        // The karnet is part of the arrangement since S-16 — see IntegrationTestFixture.IssuePassAsync
        // for why it is deliberately wide and why it is not folded into member creation. A BLOCKED
        // member gets one too, on purpose: the membership refusal must be what stops them, or
        // "member_blocked" would be passing for the wrong reason.
        await fixture.IssuePassAsync(memberId);

        return memberId;
    }

    /// <summary>A member with no karnet at all — the case the gate exists for.</summary>
    private Task<Guid> PasslessMemberAsync() =>
        fixture.CreateMemberAsync($"Bez Karnetu {Guid.NewGuid():N}");

    private static Task<HttpResponseMessage> BookAsync(
        HttpClient admin, Guid classId, Guid memberId) =>
        admin.PostAsJsonAsync(AdminBookingsOf(classId), new { memberId });

    private static async Task<string> ReasonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason;

    private async Task<List<Booking>> BookingsForAsync(Guid classId)
    {
        await using var db = NewContext();

        return await db.Bookings.AsNoTracking().Where(b => b.ClassId == classId).ToListAsync();
    }

    /// <summary>
    /// The bounds of a karnet that is never the thing under test. Wide enough to cover this file's
    /// fabricated 2036 slots however far they slide — see IntegrationTestFixture.IssuePassAsync.
    /// </summary>
    private static readonly DateOnly WideFrom = new(2000, 1, 1);

    private static readonly DateOnly WideTo = new(2099, 12, 31);

    /// <summary>
    /// A second (third, fourth) class in its own slot, for the entry-pool tests — which need one
    /// member booked into SEVERAL classes, the one shape a class's own capacity stamp cannot guard.
    /// </summary>
    private async Task<ClassBody> AnotherClassAsync(HttpClient admin)
    {
        var type = await CreateTypeAsync(admin);
        var trainerId = await CreateTrainerAsync(admin);

        var response = await admin.PostAsJsonAsync(ClassesEndpoint, new
        {
            classTypeId = type.Id,
            startsAt = NextSlot(),
            instructorMemberId = trainerId,
            durationMinutes = 60,
            capacity = 12,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClassBody>())!;
    }

    /// <summary>
    /// A class at an EXACT instant rather than the next sliding slot — for the karnet range tests,
    /// which need to know the class's date without computing it (see the note above those tests).
    /// </summary>
    private async Task<ClassBody> ClassAtAsync(HttpClient admin, DateTimeOffset startsAt)
    {
        var type = await CreateTypeAsync(admin);
        var trainerId = await CreateTrainerAsync(admin);

        var response = await admin.PostAsJsonAsync(ClassesEndpoint, new
        {
            classTypeId = type.Id,
            startsAt,
            instructorMemberId = trainerId,
            durationMinutes = 60,
            capacity = 12,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClassBody>())!;
    }

    /// <summary>A fresh admin client — the racers each need their own, so their scopes are separate.</summary>
    private Task<HttpClient> AdminClientAsync() => AdminAsync();

    /// <summary>
    /// How many entries of this karnet are actually spent, read straight from the database rather
    /// than from any response. This is the assertion the entry-pool race turns on.
    /// </summary>
    private async Task<int> ActiveBookingsForPassAsync(Guid passId)
    {
        await using var db = NewContext();

        return await db.Bookings
            .AsNoTracking()
            .CountAsync(b => b.MembershipPassId == passId && b.Status == BookingStatus.Active);
    }

    // --- the happy path --------------------------------------------------------

    /// <summary>
    /// THE POINT OF THE SLICE: a member with no login gets a spot. The response is the class as it
    /// now stands, exactly as the member's own route answers, so the admin's schedule tile updates
    /// without a refetch.
    /// </summary>
    [Fact]
    public async Task An_admin_books_a_member_who_has_no_account()
    {
        var (admin, scheduled) = await ArrangeAsync(capacity: 5);
        var memberId = await AccountlessMemberAsync();

        var response = await BookAsync(admin, scheduled.Id, memberId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<ClassBody>())!;
        Assert.Equal(4, updated.FreeSpots);

        var booking = Assert.Single(await BookingsForAsync(scheduled.Id));
        Assert.Equal(memberId, booking.MemberId);
        Assert.Equal(BookingStatus.Active, booking.Status);
    }

    /// <summary>
    /// The spot is a real one: it appears on the class's "Zapisani" panel like any other, with the
    /// member's name and no account behind them.
    /// </summary>
    [Fact]
    public async Task The_booking_appears_on_the_class_roster()
    {
        var (admin, scheduled) = await ArrangeAsync();
        var memberId = await AccountlessMemberAsync();

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, memberId)).StatusCode);

        var roster = await admin.GetFromJsonAsync<List<ClassBookingBody>>(
            AdminBookingsOf(scheduled.Id));

        var entry = Assert.Single(roster!);
        Assert.Equal(memberId, entry.MemberId);
        Assert.Null(entry.UserId);
    }

    /// <summary>
    /// It works for members WITH accounts too — a phone call to the desk is an ordinary way to sign
    /// up, and restricting the route to accountless people would be a rule nobody asked for.
    /// </summary>
    [Fact]
    public async Task An_admin_books_a_member_who_does_have_an_account()
    {
        var (admin, scheduled) = await ArrangeAsync();

        var email = $"booked-for-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);
        await fixture.IssuePassForAccountAsync(email);

        var members = await admin.GetFromJsonAsync<List<MemberBody>>("/api/admin/members");
        var memberId = members!.Single(m => m.Email == email).Id;

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, memberId)).StatusCode);

        var booking = Assert.Single(await BookingsForAsync(scheduled.Id));
        Assert.Equal(memberId, booking.MemberId);
    }

    // --- trainer scoping (S-16, MP-02) -----------------------------------------

    /// <summary>
    /// The half of MP-02 that is new capability: a trainer books somebody into a class they run,
    /// without an admin having to do it for them.
    /// </summary>
    [Fact]
    public async Task A_trainer_books_into_a_class_they_instruct()
    {
        var admin = await AdminAsync();
        var (trainer, trainerMemberId) = await NewTrainerAsync(admin);
        var (scheduled, _) = await ClassWithInstructorAsync(admin, trainerMemberId);

        var memberId = await AccountlessMemberAsync();

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(trainer, scheduled.Id, memberId)).StatusCode);
    }

    /// <summary>
    /// The half that is a restriction, and the reason the group policy alone is not enough: the same
    /// trainer, the same member, a class somebody else instructs.
    /// </summary>
    [Fact]
    public async Task A_trainer_is_refused_on_a_class_someone_else_instructs()
    {
        var admin = await AdminAsync();
        var (trainer, _) = await NewTrainerAsync(admin);
        var (scheduled, _) = await ClassWithInstructorAsync(admin);

        var memberId = await AccountlessMemberAsync();
        var response = await BookAsync(trainer, scheduled.Id, memberId);

        // 403, not 404 — the class is on every member's schedule, so its existence is not a secret.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await BookingsForAsync(scheduled.Id));
    }

    [Fact]
    public async Task A_trainer_releases_a_spot_on_their_own_class_and_is_refused_on_another()
    {
        var admin = await AdminAsync();
        var (trainer, trainerMemberId) = await NewTrainerAsync(admin);

        var (own, _) = await ClassWithInstructorAsync(admin, trainerMemberId);
        var (other, _) = await ClassWithInstructorAsync(admin);

        var memberId = await AccountlessMemberAsync();
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, own.Id, memberId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, other.Id, memberId)).StatusCode);

        var onOther = Assert.Single(await BookingsForAsync(other.Id));
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await trainer.DeleteAsync($"{AdminBookingsOf(other.Id)}/{onOther.Id}")).StatusCode);

        var onOwn = Assert.Single(await BookingsForAsync(own.Id));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await trainer.DeleteAsync($"{AdminBookingsOf(own.Id)}/{onOwn.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_trainer_reads_the_roster_of_their_own_class_and_is_refused_on_another()
    {
        var admin = await AdminAsync();
        var (trainer, trainerMemberId) = await NewTrainerAsync(admin);

        var (own, _) = await ClassWithInstructorAsync(admin, trainerMemberId);
        var (other, _) = await ClassWithInstructorAsync(admin);

        Assert.Equal(HttpStatusCode.OK, (await trainer.GetAsync(AdminBookingsOf(own.Id))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden, (await trainer.GetAsync(AdminBookingsOf(other.Id))).StatusCode);
    }

    /// <summary>
    /// AN ADMIN SKIPS THE COMPARISON ENTIRELY. Roles are additive here, so the realistic owner-who-
    /// teaches holds Trainer as well — and must not be narrowed to their own classes by holding it.
    /// </summary>
    [Fact]
    public async Task An_admin_acts_on_a_class_they_do_not_instruct()
    {
        var admin = await AdminAsync();
        var (scheduled, _) = await ClassWithInstructorAsync(admin);
        var memberId = await AccountlessMemberAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(AdminBookingsOf(scheduled.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, memberId)).StatusCode);

        var booking = Assert.Single(await BookingsForAsync(scheduled.Id));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"{AdminBookingsOf(scheduled.Id)}/{booking.Id}")).StatusCode);
    }

    /// <summary>
    /// An ordinary member holds neither role and never reaches the ownership check — the group policy
    /// is still doing its half of the job.
    /// </summary>
    [Fact]
    public async Task A_plain_member_is_still_refused_by_the_group_policy()
    {
        var admin = await AdminAsync();
        var (scheduled, _) = await ClassWithInstructorAsync(admin);

        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        Assert.Equal(
            HttpStatusCode.Forbidden, (await member.GetAsync(AdminBookingsOf(scheduled.Id))).StatusCode);
    }

    // --- the karnet gate (S-16) ------------------------------------------------

    // THE RANGE TESTS BELOW WRITE THEIR DATES BY HAND, on purpose. The gate compares a pass's range
    // against the class's CLUB-LOCAL date, and a test that computed its expectation through ClubTime
    // would share the conversion under test - it could never catch a bug in it. So every class here
    // starts at a fixed instant, and the Warsaw reading of that instant is written in a comment
    // beside it, checkable against a wall clock without running anything.
    //
    // 2038 AND ODD HOURS: the other suites slide their classes through these years at 10:00Z and
    // 16:00Z, and the overlap rule is club-wide over one shared database. Nothing else uses 20:00Z,
    // 22:30Z or 23:30Z, and every test below takes a day of its own.

    [Fact]
    public async Task A_member_with_no_karnet_cannot_be_booked()
    {
        var (admin, scheduled) = await ArrangeAsync();
        var memberId = await PasslessMemberAsync();

        var response = await BookAsync(admin, scheduled.Id, memberId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_valid_pass", await ReasonAsync(response));

        // Nothing was written. A refusal that left a row would be worse than one that did not refuse.
        Assert.Empty(await BookingsForAsync(scheduled.Id));
    }

    /// <summary>
    /// A karnet that ran out the day before the class does not cover it. One day is the whole test —
    /// an inclusive comparison written as exclusive fails exactly here and nowhere else.
    /// </summary>
    [Fact]
    public async Task A_karnet_that_ends_the_day_before_the_class_does_not_cover_it()
    {
        var admin = await AdminAsync();

        // 20:00Z on 10 March 2038 is 21:00 CET the same day in Warsaw - class date 2038-03-10.
        var scheduled = await ClassAtAsync(
            admin, new DateTimeOffset(2038, 3, 10, 20, 0, 0, TimeSpan.Zero));
        var memberId = await PasslessMemberAsync();

        await fixture.IssuePassAsync(
            memberId, entryCount: 10, validFrom: new DateOnly(2038, 2, 8), validTo: new DateOnly(2038, 3, 9));

        var response = await BookAsync(admin, scheduled.Id, memberId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_valid_pass", await ReasonAsync(response));
    }

    /// <summary>
    /// A karnet that starts the day after the class does not cover it either — the mirror image of
    /// the test above, and the other half of the inclusive range.
    /// </summary>
    [Fact]
    public async Task A_karnet_that_starts_the_day_after_the_class_does_not_cover_it()
    {
        var admin = await AdminAsync();

        // 20:00Z on 17 March 2038 is 21:00 CET the same day in Warsaw - class date 2038-03-17.
        var scheduled = await ClassAtAsync(
            admin, new DateTimeOffset(2038, 3, 17, 20, 0, 0, TimeSpan.Zero));
        var memberId = await PasslessMemberAsync();

        await fixture.IssuePassAsync(
            memberId, entryCount: 10, validFrom: new DateOnly(2038, 3, 18), validTo: new DateOnly(2038, 4, 16));

        var response = await BookAsync(admin, scheduled.Id, memberId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_valid_pass", await ReasonAsync(response));
    }

    /// <summary>
    /// BOTH ENDS ARE INCLUSIVE. A one-day karnet covering exactly the class's date is enough — this
    /// is the boundary a &lt; written for a &lt;= would silently move.
    /// </summary>
    [Fact]
    public async Task A_karnet_covering_exactly_the_class_date_is_enough()
    {
        var admin = await AdminAsync();

        // 20:00Z on 24 March 2038 is 21:00 CET the same day in Warsaw (DST starts on the 28th) -
        // class date 2038-03-24.
        var scheduled = await ClassAtAsync(
            admin, new DateTimeOffset(2038, 3, 24, 20, 0, 0, TimeSpan.Zero));
        var memberId = await PasslessMemberAsync();

        var classDate = new DateOnly(2038, 3, 24);
        await fixture.IssuePassAsync(memberId, entryCount: 1, validFrom: classDate, validTo: classDate);

        Assert.Equal(
            HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, memberId)).StatusCode);
    }

    // --- the club-local date, across midnight ------------------------------------
    //
    // WHY THESE FOUR EXIST. Every other class in this file starts at a daytime hour, where the UTC
    // date and the Warsaw date agree - so a gate that read the UTC date would pass all of them. These
    // put the class just after midnight in Warsaw, which is still the previous day in UTC, and test
    // both directions: the Warsaw date admits, the UTC date refuses. Summer and winter both, because
    // the gap is two hours in one and one hour in the other.

    /// <summary>
    /// Summer (CEST, UTC+2): a class at 00:30 Warsaw on 15 July is covered by a karnet valid on the
    /// 15th, even though the instant is still the 14th in UTC.
    /// </summary>
    [Fact]
    public async Task A_summer_class_just_after_midnight_is_covered_by_a_karnet_for_the_warsaw_date()
    {
        var admin = await AdminAsync();

        // 22:30Z on 14 July 2038 = 00:30 CEST on 15 July 2038 in Warsaw.
        var scheduled = await ClassAtAsync(
            admin, new DateTimeOffset(2038, 7, 14, 22, 30, 0, TimeSpan.Zero));
        var memberId = await PasslessMemberAsync();

        var warsawDate = new DateOnly(2038, 7, 15);
        await fixture.IssuePassAsync(memberId, entryCount: 1, validFrom: warsawDate, validTo: warsawDate);

        Assert.Equal(
            HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, memberId)).StatusCode);
    }

    /// <summary>
    /// The mirror of the test above: a karnet valid only on the UTC date of that instant does NOT
    /// cover a class that is already the next day in Warsaw.
    /// </summary>
    [Fact]
    public async Task A_summer_class_just_after_midnight_is_not_covered_by_a_karnet_for_the_utc_date()
    {
        var admin = await AdminAsync();

        // 22:30Z on 21 July 2038 = 00:30 CEST on 22 July 2038 in Warsaw.
        var scheduled = await ClassAtAsync(
            admin, new DateTimeOffset(2038, 7, 21, 22, 30, 0, TimeSpan.Zero));
        var memberId = await PasslessMemberAsync();

        var utcDate = new DateOnly(2038, 7, 21);
        await fixture.IssuePassAsync(memberId, entryCount: 1, validFrom: utcDate, validTo: utcDate);

        var response = await BookAsync(admin, scheduled.Id, memberId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_valid_pass", await ReasonAsync(response));
        Assert.Empty(await BookingsForAsync(scheduled.Id));
    }

    /// <summary>
    /// Winter (CET, UTC+1): the same boundary with a one-hour gap.
    /// </summary>
    [Fact]
    public async Task A_winter_class_just_after_midnight_is_covered_by_a_karnet_for_the_warsaw_date()
    {
        var admin = await AdminAsync();

        // 23:30Z on 20 January 2038 = 00:30 CET on 21 January 2038 in Warsaw.
        var scheduled = await ClassAtAsync(
            admin, new DateTimeOffset(2038, 1, 20, 23, 30, 0, TimeSpan.Zero));
        var memberId = await PasslessMemberAsync();

        var warsawDate = new DateOnly(2038, 1, 21);
        await fixture.IssuePassAsync(memberId, entryCount: 1, validFrom: warsawDate, validTo: warsawDate);

        Assert.Equal(
            HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, memberId)).StatusCode);
    }

    /// <summary>The winter mirror: the UTC date does not cover the Warsaw date.</summary>
    [Fact]
    public async Task A_winter_class_just_after_midnight_is_not_covered_by_a_karnet_for_the_utc_date()
    {
        var admin = await AdminAsync();

        // 23:30Z on 27 January 2038 = 00:30 CET on 28 January 2038 in Warsaw.
        var scheduled = await ClassAtAsync(
            admin, new DateTimeOffset(2038, 1, 27, 23, 30, 0, TimeSpan.Zero));
        var memberId = await PasslessMemberAsync();

        var utcDate = new DateOnly(2038, 1, 27);
        await fixture.IssuePassAsync(memberId, entryCount: 1, validFrom: utcDate, validTo: utcDate);

        var response = await BookAsync(admin, scheduled.Id, memberId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_valid_pass", await ReasonAsync(response));
        Assert.Empty(await BookingsForAsync(scheduled.Id));
    }

    /// <summary>
    /// The entry pool. A two-entry karnet pays for two classes and refuses the third — and the
    /// refusal names the pool, not the validity, because they are different conversations at the desk.
    /// </summary>
    [Fact]
    public async Task Exhausting_the_entries_refuses_the_next_booking()
    {
        var (admin, first) = await ArrangeAsync();
        var second = await AnotherClassAsync(admin);
        var third = await AnotherClassAsync(admin);

        var memberId = await PasslessMemberAsync();
        await fixture.IssuePassAsync(memberId, entryCount: 2, validFrom: WideFrom, validTo: WideTo);

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, first.Id, memberId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, second.Id, memberId)).StatusCode);

        var refused = await BookAsync(admin, third.Id, memberId);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("no_entries_left", await ReasonAsync(refused));
    }

    /// <summary>
    /// RELEASING RETURNS THE ENTRY. Derived, not decremented — the entry comes back because the row
    /// stopped being Active, and nothing had to remember to give it back.
    /// </summary>
    [Fact]
    public async Task Releasing_a_booking_returns_the_entry()
    {
        var (admin, first) = await ArrangeAsync();
        var second = await AnotherClassAsync(admin);

        var memberId = await PasslessMemberAsync();
        await fixture.IssuePassAsync(memberId, entryCount: 1, validFrom: WideFrom, validTo: WideTo);

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, first.Id, memberId)).StatusCode);

        var refused = await BookAsync(admin, second.Id, memberId);
        Assert.Equal("no_entries_left", await ReasonAsync(refused));

        var booking = Assert.Single(await BookingsForAsync(first.Id));
        var released = await admin.DeleteAsync($"{AdminBookingsOf(first.Id)}/{booking.Id}");
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);

        // The entry is back, so the booking that was refused a moment ago now succeeds.
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, second.Id, memberId)).StatusCode);
    }

    /// <summary>
    /// The booking records WHICH karnet paid — that attribution is what keeps entries-left stable
    /// when a pass's range is later edited.
    /// </summary>
    [Fact]
    public async Task A_booking_records_the_karnet_that_paid_for_it()
    {
        var (admin, scheduled) = await ArrangeAsync();
        var memberId = await PasslessMemberAsync();

        var passId = await fixture.IssuePassAsync(
            memberId, entryCount: 5, validFrom: WideFrom, validTo: WideTo);

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, memberId)).StatusCode);

        Assert.Equal(passId, Assert.Single(await BookingsForAsync(scheduled.Id)).MembershipPassId);
    }

    /// <summary>
    /// THE TEST THE SECOND STAMP EXISTS FOR, and it is a race the CLASS stamp cannot catch: one
    /// member, one entry left, N+1 DIFFERENT classes. The bookings touch N+1 different Class rows, so
    /// Class.ConcurrencyStamp serializes none of them against each other — only
    /// MembershipPass.ConcurrencyStamp does.
    ///
    /// <para>
    /// Asserted against the DATABASE, like every other race in this repository: all N+1 requests read
    /// an entry count of zero before any of them writes, so counting HTTP successes could be satisfied
    /// by a coincidence of scheduling. Remove the <c>pass.ConcurrencyStamp</c> rotation in
    /// TryBookAsync and this test finds N+1 active rows against an N-entry karnet.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Concurrent_bookings_never_exceed_the_pass_entry_count()
    {
        const int Entries = 2;

        var (admin, first) = await ArrangeAsync();

        var classes = new List<ClassBody> { first };
        for (var i = 0; i < Entries; i++)
        {
            classes.Add(await AnotherClassAsync(admin));
        }

        var memberId = await PasslessMemberAsync();
        var passId = await fixture.IssuePassAsync(
            memberId, entryCount: Entries, validFrom: WideFrom, validTo: WideTo);

        // Separate clients, so the requests run in separate DI scopes with separate DbContexts and
        // nothing is shared through the change tracker.
        var racers = await Task.WhenAll(classes.Select(_ => AdminClientAsync()));

        var responses = await Task.WhenAll(
            classes.Select((c, i) => BookAsync(racers[i], c.Id, memberId)));

        Assert.Equal(Entries, responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        var refused = Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK);
        Assert.Equal("no_entries_left", await ReasonAsync(refused));

        // The assertion that matters.
        Assert.Equal(Entries, await ActiveBookingsForPassAsync(passId));
    }

    // --- the refusals ----------------------------------------------------------

    /// <summary>
    /// A member id nobody issued is a wrong address, not a state disagreement — the same reasoning
    /// that makes an unknown class a 404 rather than a reason.
    /// </summary>
    [Fact]
    public async Task An_unknown_member_is_not_found()
    {
        var (admin, scheduled) = await ArrangeAsync();

        var response = await BookAsync(admin, scheduled.Id, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await BookingsForAsync(scheduled.Id));
    }

    /// <summary>
    /// A blocked member may not attend. Writing the spot anyway would have the club promising a seat
    /// it has already decided not to honour — and the next block cascade would cancel it regardless.
    /// </summary>
    [Fact]
    public async Task A_blocked_member_is_refused()
    {
        var (admin, scheduled) = await ArrangeAsync();
        var memberId = await AccountlessMemberAsync(MembershipStatus.Blocked);

        var response = await BookAsync(admin, scheduled.Id, memberId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("member_blocked", await ReasonAsync(response));
        Assert.Empty(await BookingsForAsync(scheduled.Id));
    }

    /// <summary>Booking the same person twice is the same refusal the member's own route gives.</summary>
    [Fact]
    public async Task A_second_booking_for_the_same_member_is_refused()
    {
        var (admin, scheduled) = await ArrangeAsync();
        var memberId = await AccountlessMemberAsync();

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, memberId)).StatusCode);

        var response = await BookAsync(admin, scheduled.Id, memberId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("already_booked", await ReasonAsync(response));
        Assert.Single(await BookingsForAsync(scheduled.Id));
    }

    /// <summary>
    /// NO ADMIN EXEMPTION. The capacity is a fact about the room, not a permission the admin can
    /// override, and a club that overfills a class has to turn somebody away at the door.
    /// </summary>
    [Fact]
    public async Task A_full_class_is_refused_even_for_an_admin()
    {
        var (admin, scheduled) = await ArrangeAsync(capacity: 1);

        Assert.Equal(
            HttpStatusCode.OK,
            (await BookAsync(admin, scheduled.Id, await AccountlessMemberAsync())).StatusCode);

        var response = await BookAsync(admin, scheduled.Id, await AccountlessMemberAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("class_full", await ReasonAsync(response));
        Assert.Single(await BookingsForAsync(scheduled.Id));
    }

    [Fact]
    public async Task An_unknown_class_is_not_found()
    {
        var admin = await AdminAsync();
        var memberId = await AccountlessMemberAsync();

        var response = await BookAsync(admin, Guid.NewGuid(), memberId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The route names its member in the BODY, which every other booking route refuses to allow. The
    /// only thing standing between that and a member booking somebody else in is the Admin policy on
    /// the group, so it is worth a test of its own.
    /// </summary>
    [Fact]
    public async Task An_ordinary_member_may_not_book_for_somebody_else()
    {
        var (_, scheduled) = await ArrangeAsync();
        var memberId = await AccountlessMemberAsync();

        var email = $"nosy-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);
        var member = await fixture.CreateAuthenticatedClientAsync(email);

        var response = await BookAsync(member, scheduled.Id, memberId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await BookingsForAsync(scheduled.Id));
    }

    // --- the guarantee ---------------------------------------------------------

    /// <summary>
    /// The race, run through the ADMIN route: N+1 members for N spots, exactly N win.
    ///
    /// <para>
    /// THIS IS THE TEST THAT JUSTIFIES THE EXTRACTION. One HttpClient issues every request, but each
    /// one is a separate HTTP request and therefore a separate DI scope and DbContext — nothing is
    /// shared through the change tracker, so the race is as real as the member-side one. If a future
    /// change gives the admin route its own copy of the loop and forgets the
    /// <c>Class.ConcurrencyStamp</c> rotation, this goes red with N+1 active rows and nothing else
    /// does.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Concurrent_admin_bookings_never_exceed_capacity()
    {
        const int Capacity = 3;

        var (admin, scheduled) = await ArrangeAsync(Capacity);

        var members = await Task.WhenAll(
            Enumerable.Range(0, Capacity + 1).Select(_ => AccountlessMemberAsync()));

        var responses = await Task.WhenAll(
            members.Select(id => BookAsync(admin, scheduled.Id, id)));

        Assert.Equal(Capacity, responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        var refused = Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK);
        Assert.Equal("class_full", await ReasonAsync(refused));

        // The assertion that matters — everything above could be a coincidence of scheduling, the
        // row count cannot.
        var rows = await BookingsForAsync(scheduled.Id);
        Assert.Equal(Capacity, rows.Count(b => b.Status == BookingStatus.Active));
    }

    // --- the block cascade, on someone with no account -------------------------

    /// <summary>
    /// THE CASCADE REACHES A MEMBER WITH NO LOGIN, which is the whole reason it was re-keyed onto the
    /// member. Before S-14 it ran off <c>Bookings.MemberUserId</c>, so an accountless member had no
    /// key to cascade on and their seats would have stayed held after the club blocked them —
    /// the schedule promising spots to somebody who cannot attend while others are turned away full.
    ///
    /// <para>
    /// It lives HERE rather than in MemberEndpointTests, where the plan named it, for the arrange:
    /// this file already owns a class, a trainer and an accountless booker, and that file owns no
    /// scheduling scaffolding at all. The behaviour under test is the same one either way.
    /// </para>
    ///
    /// <para>
    /// AND IT TOUCHES NO ACCOUNT — asserted by there being none to touch. The block path reaches for
    /// an <c>ApplicationUser</c> in three places (the is_admin refusal, the status flip, the stamp
    /// rotation) and every one of them has to be reachable with a null <c>UserId</c>; a regression
    /// there is a 500, not a wrong answer.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Blocking_an_accountless_member_cancels_their_future_bookings()
    {
        var (admin, scheduled) = await ArrangeAsync();

        var memberId = await AccountlessMemberAsync();
        var other = await AccountlessMemberAsync();

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, memberId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, other)).StatusCode);

        var blocked = await admin.PostAsync($"/api/admin/members/{memberId}/block", content: null);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);

        var rows = await BookingsForAsync(scheduled.Id);

        var theirs = Assert.Single(rows, b => b.MemberId == memberId);
        Assert.Equal(BookingStatus.Cancelled, theirs.Status);
        Assert.NotNull(theirs.CancelledAt);

        // FUTURE ONLY, and only THEIRS: the cascade is keyed on the member, so a second member's seat
        // in the same class is untouched. A cascade keyed on the class would pass every assertion
        // above and fail this one.
        Assert.Equal(BookingStatus.Active, Assert.Single(rows, b => b.MemberId == other).Status);
    }

    /// <summary>
    /// THE CASCADE GIVES THE ENTRY BACK. Blocking cancels the member's future bookings, and because
    /// entries left is derived from active bookings, the entry those bookings held returns to the
    /// karnet. Asserted twice, for two different failures:
    ///
    /// <para>
    /// The PASS STAMP must rotate. Returning an entry makes a booking possible that was refused a
    /// moment ago, so a booker whose entry check straddles the cascade must lose its save. REMOVE THE
    /// ROTATION LOOP IN BookingStore.CancelActiveFutureForMemberAsync AND THE STAMP ASSERTION FAILS.
    /// </para>
    ///
    /// <para>
    /// And the entry must be USABLE again: after an unblock, the same one-entry karnet pays for a
    /// different class. That is only possible if the cancelled booking stopped counting.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Blocking_a_member_returns_the_entries_their_future_bookings_held()
    {
        var admin = await AdminAsync();
        var first = await AnotherClassAsync(admin);
        var second = await AnotherClassAsync(admin);

        var memberId = await PasslessMemberAsync();
        var passId = await fixture.IssuePassAsync(
            memberId, entryCount: 1, validFrom: WideFrom, validTo: WideTo);

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, first.Id, memberId)).StatusCode);

        var stampBefore = await PassStampAsync(passId);

        var blocked = await admin.PostAsync($"/api/admin/members/{memberId}/block", content: null);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);

        Assert.Equal(0, await ActiveBookingsForPassAsync(passId));
        Assert.NotEqual(stampBefore, await PassStampAsync(passId));

        var unblocked = await admin.PostAsync($"/api/admin/members/{memberId}/unblock", content: null);
        Assert.Equal(HttpStatusCode.OK, unblocked.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, second.Id, memberId)).StatusCode);
    }

    private async Task<string> PassStampAsync(Guid passId)
    {
        await using var db = NewContext();

        return await db.MembershipPasses
            .AsNoTracking()
            .Where(p => p.Id == passId)
            .Select(p => p.ConcurrencyStamp)
            .SingleAsync();
    }

    /// <summary>
    /// A BLOCK REVOKES A LIVE CODE. Registration already refuses a blocked member's code, but that is
    /// a read-time check — the code itself would come back the moment the member is unblocked, which
    /// makes an unrelated act quietly reissue a credential the club had reason to withdraw.
    /// </summary>
    [Fact]
    public async Task Blocking_revokes_a_live_access_code_and_a_blocked_member_cannot_be_issued_one()
    {
        var admin = await AdminAsync();
        var memberId = await AccountlessMemberAsync();

        var issued = await admin.PostAsync($"/api/admin/members/{memberId}/access-code", content: null);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsync($"/api/admin/members/{memberId}/block", content: null)).StatusCode);

        await using (var db = NewContext())
        {
            var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId);
            Assert.Null(member.AccessCode);
            Assert.Null(member.AccessCodeExpiresAt);
        }

        // The screen hides this action on a blocked row; the endpoint refuses it regardless, which is
        // the boundary the members surface states its own rules at.
        var refused = await admin.PostAsync($"/api/admin/members/{memberId}/access-code", content: null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("member_blocked", await ReasonAsync(refused));
    }
}
