using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// Booking and cancelling a spot (prd.md US-01, FR-008, FR-009, FR-010).
///
/// <para>
/// WHY THIS FILE EXISTS: <see cref="Concurrent_bookings_never_exceed_capacity"/>. Every other test
/// here pins a product rule that a careful reading of BookingEndpoints would also give you. That one
/// pins the guarantee the whole slice was built for, and it is the only test in this repository that
/// FAILS if a single line is removed — the <c>ConcurrencyStamp</c> rotation in <c>TryBookAsync</c>.
/// Without the rotation EF issues no UPDATE against Classes, no WHERE clause carries the token, and
/// every racer's capacity check passes against the same stale count. Comment that line out and this
/// test goes red; nothing else does.
/// </para>
///
/// <para>
/// EVERY TEST TAKES ITS OWN TIME SLOT, for the reason ClassEndpointTests documents: the overlap rule
/// is club-wide, so any two classes anywhere in the table collide. <see cref="NextSlot"/> starts in
/// 2032 to stay clear of ClassEndpointTests' 2030 slots — the two files share one database.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class BookingEndpointTests(IntegrationTestFixture fixture)
{
    /// <summary>Mirrors ScheduledClass.</summary>
    private sealed record ClassBody(
        Guid Id,
        Guid ClassTypeId,
        string Name,
        string? Description,
        DateTimeOffset StartsAt,
        int DurationMinutes,
        Guid InstructorMemberId,
        string Instructor,
        int Capacity,
        int FreeSpots,
        string Status);

    /// <summary>Mirrors ClassTypeSummary — only what these tests read from it.</summary>
    private sealed record ClassTypeBody(Guid Id, string Name);

    /// <summary>Mirrors MemberSummary — only what these tests read from it.</summary>
    /// <summary>
    /// Mirrors MemberSummary — only what these tests read from it. Since S-14 that includes
    /// <c>UserId</c>: <c>Id</c> is now the MEMBER, and these tests want the account behind them.
    /// </summary>
    private sealed record MemberBody(Guid Id, string? UserId, string Email);

    /// <summary>Mirrors MyBooking.</summary>
    private sealed record MyBookingBody(
        Guid BookingId,
        Guid ClassId,
        string Name,
        string? Description,
        DateTimeOffset StartsAt,
        int DurationMinutes,
        string Instructor,
        DateTimeOffset BookedAt);

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
    private const string MineEndpoint = "/api/bookings/mine";

    // BookingsOf and MyBookingOn are GONE with the routes they addressed (S-16, MP-01). The one
    // test that still names those paths spells them out inline, because its whole subject is that
    // they no longer resolve.

    private static string MyBookingOn(Guid classId) => $"/api/classes/{classId}/bookings/mine";

    private static string AdminBookingsOf(Guid classId) =>
        $"/api/admin/classes/{classId}/bookings";

    /// <summary>
    /// Slot allocator, 2032 rather than ClassEndpointTests' 2030. Both files write into the same
    /// container, and a shared base would make one file's classes refuse the other's on time_conflict
    /// depending on execution order — the least reproducible failure available.
    /// </summary>
    private static int _slot;

    private static DateTimeOffset NextSlot() =>
        new DateTimeOffset(2032, 6, 1, 10, 0, 0, TimeSpan.Zero)
            .AddDays(60 * Interlocked.Increment(ref _slot));

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

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

    /// <summary>
    /// Creates an account and returns its Identity id, looked up through the admin member list —
    /// the id is generated by Identity and there is no other way for a test to learn it.
    /// </summary>
    private async Task<Guid> CreateAccountAsync(HttpClient admin, AccountStatus status, string role)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, status, role);

        var members = await admin.GetFromJsonAsync<List<MemberBody>>("/api/admin/members");

        return members!.Single(m => m.Email == email).Id;
    }

    /// <summary>
    /// A brand-new active member, signed in and holding a karnet. Every booking test needs one nobody
    /// else holds.
    ///
    /// <para>
    /// THE PASS IS PART OF THE ARRANGEMENT SINCE S-16, and deliberately a wide one — these tests are
    /// about capacity, duplicates and races, not about the karnet gate, which the entry-pool tests
    /// below cover on their own terms with real bounds.
    /// </para>
    /// </summary>
    private async Task<HttpClient> NewMemberAsync()
    {
        var email = $"booker-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);
        await fixture.IssuePassForAccountAsync(email);

        return await fixture.CreateAuthenticatedClientAsync(email);
    }

    /// <summary>An admin, a type and a trainer — the arrangement every class needs.</summary>
    private async Task<(HttpClient Admin, Guid TypeId, Guid TrainerId)> ArrangeAsync()
    {
        var admin = await AdminAsync();
        var type = await CreateTypeAsync(admin);
        var trainerId = await CreateAccountAsync(admin, AccountStatus.Active, ApplicationRoles.Trainer);

        return (admin, type.Id, trainerId);
    }

    private static async Task<ClassBody> PostClassAsync(
        HttpClient admin, Guid typeId, Guid trainerId, int capacity = 12)
    {
        var response = await admin.PostAsJsonAsync(ClassesEndpoint, new
        {
            classTypeId = typeId,
            startsAt = NextSlot(),
            instructorMemberId = trainerId,
            durationMinutes = 60,
            capacity,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClassBody>())!;
    }

    /// <summary>
    /// A bookable class, plus a member signed in and ready to book it.
    /// </summary>
    private async Task<(ClassBody Class, HttpClient Member)> BookableAsync(int capacity = 12)
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var created = await PostClassAsync(admin, typeId, trainerId, capacity);

        return (created, await NewMemberAsync());
    }

    /// <summary>
    /// Writes a class straight into the database, bypassing the API.
    ///
    /// <para>
    /// Needed because two of the refusals below describe states the API cannot produce: a class in
    /// the PAST is refused at creation by <c>starts_in_past</c>, and <c>ClassStatus.Cancelled</c> has
    /// no endpoint that sets it until S-09. Going around the API is the only way to arrange them, and
    /// arranging them is the only way to prove the refusals work.
    /// </para>
    /// </summary>
    private async Task<Guid> InsertClassAsync(
        Guid typeId, Guid trainerId, DateTimeOffset startsAt, ClassStatus status, int capacity = 12)
    {
        await using var db = NewContext();

        var entity = new Class
        {
            Id = Guid.NewGuid(),
            ClassTypeId = typeId,
            InstructorMemberId = trainerId,
            StartsAt = startsAt,
            DurationMinutes = 60,
            Capacity = capacity,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Classes.Add(entity);
        await db.SaveChangesAsync();

        return entity.Id;
    }

    private async Task<List<Booking>> BookingsForAsync(Guid classId)
    {
        await using var db = NewContext();

        return await db.Bookings
            .AsNoTracking()
            .Where(b => b.ClassId == classId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();
    }

    /// <summary>The class's concurrency stamp, read straight from the database — the mechanism itself.</summary>
    private async Task<string> StampOfAsync(Guid classId)
    {
        await using var db = NewContext();

        return await db.Classes
            .AsNoTracking()
            .Where(c => c.Id == classId)
            .Select(c => c.ConcurrencyStamp)
            .SingleAsync();
    }

    private static async Task<string> ReasonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason;
    }

    /// <summary>
    /// Books <paramref name="client"/>'s member into a class — THROUGH THE STAFF ROUTE since S-16.
    ///
    /// <para>
    /// The signature is unchanged on purpose. MP-01 removed <c>POST /api/classes/{id}/bookings</c>,
    /// but almost every assertion in this file is about the write path's BEHAVIOUR — capacity,
    /// duplicates, the two races, what a cancelled class does — and that behaviour did not move, it
    /// only changed who may invoke it. Keeping the helper's shape is what lets those tests stay
    /// exactly as they were rather than being rewritten around a new arrangement, which is where the
    /// coverage would have quietly thinned.
    /// </para>
    ///
    /// <para>
    /// The member id comes from the member's own session, so the tests keep expressing "book THIS
    /// member" without each one growing an id parameter.
    /// </para>
    /// </summary>
    private async Task<HttpResponseMessage> BookAsync(HttpClient client, Guid classId)
    {
        var me = await client.GetFromJsonAsync<MeBody>("/api/auth/me");
        var admin = await AdminAsync();

        return await admin.PostAsJsonAsync(AdminBookingsOf(classId), new { memberId = me!.MemberId });
    }

    /// <summary>
    /// Releases <paramref name="client"/>'s member from a class, through the staff route — the
    /// replacement for the member's own <c>DELETE .../bookings/mine</c>, which MP-01 removed.
    ///
    /// <para>
    /// Addressed by BOOKING ID rather than by class, which is the one shape difference between the
    /// two routes: the member's cancel could name the class because a member holds at most one active
    /// booking on it, while the staff route releases a specific person's specific spot. The roster
    /// read is how a test learns that id.
    /// </para>
    /// </summary>
    private async Task<HttpResponseMessage> ReleaseAsync(HttpClient client, Guid classId)
    {
        var me = await client.GetFromJsonAsync<MeBody>("/api/auth/me");
        var admin = await AdminAsync();

        var roster = await admin.GetFromJsonAsync<List<ClassBookingBody>>(AdminBookingsOf(classId));
        var row = roster!.Single(b => b.MemberId == me!.MemberId);

        return await admin.DeleteAsync($"{AdminBookingsOf(classId)}/{row.BookingId}");
    }

    /// <summary>Mirrors CurrentUser — only the member id, which is what the helpers above need.</summary>
    private sealed record MeBody(Guid MemberId);

    // --- who may reach the group ----------------------------------------------

    /// <summary>
    /// ONE ROUTE SINCE S-16. The two write routes that used to be here — POST
    /// /api/classes/{id}/bookings and DELETE /api/classes/{id}/bookings/mine — were removed with
    /// MP-01; <see cref="The_removed_member_booking_routes_are_gone"/> is what pins their absence.
    /// </summary>
    public static TheoryData<string, string> EveryMemberRoute => new()
    {
        { "GET", MineEndpoint },
    };

    /// <summary>
    /// The ActiveMember policy is applied at the GROUP, so this covers every route the file adds —
    /// including any added later without a test of its own.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryMemberRoute))]
    public async Task Booking_routes_refuse_a_pending_member(string method, string route)
    {
        var pending = await fixture.CreateAuthenticatedClientAsync(TestUsers.PendingMemberEmail);

        var response = await pending.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A blocked member never reaches these routes at all, because they never get a cookie: login
    /// itself refuses Blocked (AuthEndpoints — handing a 30-day cookie to someone whose access was
    /// revoked inverts what Blocked is for). The ActiveMember policy is still the second line of
    /// defence for a session issued BEFORE the block, which the theory above covers with Pending.
    /// </summary>
    [Fact]
    public async Task A_blocked_member_cannot_even_sign_in()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = TestUsers.BlockedMemberEmail, password = TestUsers.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// MP-01, asserted as an absence. A member may no longer book or cancel for themselves, and the
    /// routes that let them are not merely hidden by the SPA — they do not exist.
    ///
    /// <para>
    /// 405 RATHER THAN 404, and that is the app's routing rather than anything this test chose:
    /// <c>MapFallbackToFile</c> claims every unmatched path for GET and HEAD only, so a POST or
    /// DELETE to a path nothing else maps ends up matching the fallback's PATTERN but not its method.
    /// The distinction that matters is not which of the two it is — it is that the answer is neither
    /// a 200 nor a 409, both of which would mean the handler ran.
    /// </para>
    ///
    /// <para>
    /// The client used is an ACTIVE member — the one caller who WOULD have been allowed before — so
    /// the refusal cannot be explained away as authorization.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task The_removed_member_booking_routes_are_gone(string method)
    {
        var (scheduled, member) = await BookableAsync();

        var route = method == "POST"
            ? $"/api/classes/{scheduled.Id}/bookings"
            : $"/api/classes/{scheduled.Id}/bookings/mine";

        var response = await member.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), route));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // --- the happy path --------------------------------------------------------

    [Fact]
    public async Task Booking_creates_exactly_one_active_row()
    {
        var (scheduled, member) = await BookableAsync();

        var response = await BookAsync(member, scheduled.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The class comes back, not the booking: the client's next act is to redraw the tile.
        var body = (await response.Content.ReadFromJsonAsync<ClassBody>())!;
        Assert.Equal(scheduled.Id, body.Id);

        var booking = Assert.Single(await BookingsForAsync(scheduled.Id));
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Null(booking.CancelledAt);
    }

    [Fact]
    public async Task Booking_an_unknown_class_is_404()
    {
        var member = await NewMemberAsync();

        var response = await BookAsync(member, Guid.NewGuid());

        // 404 and not a BookingFailure reason: an id nobody ever issued is not a disagreement about
        // state, it is a wrong address.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- the refusals ----------------------------------------------------------

    [Fact]
    public async Task Booking_the_same_class_twice_is_already_booked()
    {
        var (scheduled, member) = await BookableAsync();

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(member, scheduled.Id)).StatusCode);

        Assert.Equal("already_booked", await ReasonAsync(await BookAsync(member, scheduled.Id)));
        Assert.Single(await BookingsForAsync(scheduled.Id));
    }

    [Fact]
    public async Task Booking_a_full_class_is_class_full()
    {
        var (scheduled, first) = await BookableAsync(capacity: 1);
        var second = await NewMemberAsync();

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(first, scheduled.Id)).StatusCode);

        Assert.Equal("class_full", await ReasonAsync(await BookAsync(second, scheduled.Id)));
        Assert.Single(await BookingsForAsync(scheduled.Id));
    }

    [Fact]
    public async Task Booking_a_class_that_has_started_is_class_started()
    {
        var (_, typeId, trainerId) = await ArrangeAsync();

        // An hour ago: past the start, and the class is still running. Booking is refused from the
        // START, not from the end - a class you cannot join from the beginning is not one you may
        // join halfway through.
        var classId = await InsertClassAsync(
            typeId, trainerId, DateTimeOffset.UtcNow.AddHours(-1), ClassStatus.Scheduled);

        var member = await NewMemberAsync();

        Assert.Equal("class_started", await ReasonAsync(await BookAsync(member, classId)));
        Assert.Empty(await BookingsForAsync(classId));
    }

    [Fact]
    public async Task Booking_a_cancelled_class_is_class_cancelled()
    {
        var (_, typeId, trainerId) = await ArrangeAsync();
        var classId = await InsertClassAsync(typeId, trainerId, NextSlot(), ClassStatus.Cancelled);

        var member = await NewMemberAsync();

        // Nothing sets this status until S-09. The refusal is written now so S-09 adds a transition
        // and not a hole.
        Assert.Equal("class_cancelled", await ReasonAsync(await BookAsync(member, classId)));
        Assert.Empty(await BookingsForAsync(classId));
    }

    // --- FR-009: cancelling keeps history, and does not lock the member out ----

    [Fact]
    public async Task Cancelling_keeps_the_row_and_stamps_it()
    {
        var (scheduled, member) = await BookableAsync();
        await BookAsync(member, scheduled.Id);

        var response = await ReleaseAsync(member, scheduled.Id);

        // 204: the staff release answers with no body, unlike the booking that answers with a class.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var booking = Assert.Single(await BookingsForAsync(scheduled.Id));
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.NotNull(booking.CancelledAt);
    }

    [Fact]
    public async Task Cancelling_then_booking_again_leaves_two_rows()
    {
        var (scheduled, member) = await BookableAsync();

        await BookAsync(member, scheduled.Id);
        await ReleaseAsync(member, scheduled.Id);

        // THE FILTERED INDEX IS WHY THIS PASSES. A plain unique index on (ClassId, MemberUserId)
        // would reject this second booking forever, because FR-009 keeps the cancelled row.
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(member, scheduled.Id)).StatusCode);

        var rows = await BookingsForAsync(scheduled.Id);
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, b => b.Status == BookingStatus.Cancelled);
        Assert.Single(rows, b => b.Status == BookingStatus.Active);
    }

    // --- FR-010: the member's own list ----------------------------------------

    [Fact]
    public async Task Mine_lists_the_members_upcoming_bookings_and_nobody_elses()
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var first = await PostClassAsync(admin, typeId, trainerId);
        var second = await PostClassAsync(admin, typeId, trainerId);

        var member = await NewMemberAsync();
        var stranger = await NewMemberAsync();

        // Booked in reverse chronological order, so an accidental order-by-CreatedAt would show up.
        await BookAsync(member, second.Id);
        await BookAsync(member, first.Id);
        await BookAsync(stranger, second.Id);

        var mine = await member.GetFromJsonAsync<List<MyBookingBody>>(MineEndpoint);

        Assert.Equal(new[] { first.Id, second.Id }, mine!.Select(b => b.ClassId).ToArray());

        // Resolved through the class's type and instructor - a booking stores none of the three.
        Assert.All(mine!, b => Assert.Equal(first.Name, b.Name));
        Assert.All(mine!, b => Assert.Equal("Opis zajęć", b.Description));
        Assert.All(mine!, b => Assert.False(string.IsNullOrWhiteSpace(b.Instructor)));
    }

    [Fact]
    public async Task Mine_drops_a_cancelled_booking()
    {
        var (scheduled, member) = await BookableAsync();
        await BookAsync(member, scheduled.Id);
        await ReleaseAsync(member, scheduled.Id);

        var mine = await member.GetFromJsonAsync<List<MyBookingBody>>(MineEndpoint);

        Assert.Empty(mine!);
    }

    [Fact]
    public async Task Mine_hides_a_class_that_has_already_started()
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var upcoming = await PostClassAsync(admin, typeId, trainerId);
        var past = await InsertClassAsync(
            typeId, trainerId, DateTimeOffset.UtcNow.AddHours(-3), ClassStatus.Scheduled);

        var member = await NewMemberAsync();
        await BookAsync(member, upcoming.Id);

        // The past booking cannot be made through the API - class_started refuses it - so it is
        // written directly, which is also what a booking made yesterday looks like today.
        await using (var db = NewContext())
        {
            db.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                ClassId = past,
                MemberId = (await db.Bookings
                    .AsNoTracking()
                    .Where(b => b.ClassId == upcoming.Id)
                    .Select(b => b.MemberId)
                    .SingleAsync()),
                Status = BookingStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });

            await db.SaveChangesAsync();
        }

        var mine = await member.GetFromJsonAsync<List<MyBookingBody>>(MineEndpoint);

        // Upcoming only. The past booking still EXISTS - history is kept, it is just not this list's
        // job to show it.
        Assert.Equal(new[] { upcoming.Id }, mine!.Select(b => b.ClassId).ToArray());
    }

    // --- the guarantee ---------------------------------------------------------

    /// <summary>
    /// The headline test: N+1 members race for N spots and exactly N win.
    ///
    /// <para>
    /// Each member gets their OWN HttpClient, which is what makes the race real — separate cookies
    /// mean separate DI scopes and separate DbContexts, so nothing is shared through the change
    /// tracker. All four requests read the booked count before any of them writes, so a capacity
    /// check alone lets every one of them through. What stops the fourth is that the three winners
    /// each rotated <c>Class.ConcurrencyStamp</c>: the losers' UPDATEs match no row, TrySaveAsync
    /// reports a conflict, and the retry re-reads a count that has moved.
    /// </para>
    ///
    /// <para>
    /// REMOVE THE ROTATION AND THIS TEST FAILS — with four Active rows against a capacity of three.
    /// That is the whole point of it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Concurrent_bookings_never_exceed_capacity()
    {
        const int Capacity = 3;

        var (admin, typeId, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, typeId, trainerId, Capacity);

        var racers = await Task.WhenAll(
            Enumerable.Range(0, Capacity + 1).Select(_ => NewMemberAsync()));

        var responses = await Task.WhenAll(racers.Select(r => BookAsync(r, scheduled.Id)));

        Assert.Equal(Capacity, responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        var refused = responses.Where(r => r.StatusCode != HttpStatusCode.OK).ToList();
        Assert.Single(refused);
        Assert.Equal("class_full", await ReasonAsync(refused[0]));

        // The assertion that matters. Everything above could be satisfied by a coincidence of
        // scheduling; the row count cannot.
        var rows = await BookingsForAsync(scheduled.Id);
        Assert.Equal(Capacity, rows.Count(b => b.Status == BookingStatus.Active));
    }

    /// <summary>
    /// A cancel and a book racing for the same last spot. Both must terminate, and the class must not
    /// end up over capacity whichever order they land in.
    ///
    /// <para>
    /// This is why cancellation rotates the stamp too. It cannot overbook on its own — freeing a spot
    /// is always conservative — but a booker whose capacity check straddles the cancel would be
    /// deciding on a count that is mid-change.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_cancel_racing_a_booking_never_overbooks()
    {
        const int Capacity = 1;

        var (admin, typeId, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, typeId, trainerId, Capacity);

        var holder = await NewMemberAsync();
        var challenger = await NewMemberAsync();

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(holder, scheduled.Id)).StatusCode);

        var responses = await Task.WhenAll(
            ReleaseAsync(holder, scheduled.Id),
            BookAsync(challenger, scheduled.Id));

        // The release always wins its own right - the holder does hold a booking. 204, because the
        // staff route answers with no body; see the response-shape test for why that asymmetry is
        // deliberate.
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);

        // The booking may go either way depending on which committed first, and BOTH answers are
        // correct. What is never correct is two active rows against a capacity of one.
        var rows = await BookingsForAsync(scheduled.Id);
        Assert.True(
            rows.Count(b => b.Status == BookingStatus.Active) <= Capacity,
            "the class holds more active bookings than it has spots");
    }

    /// <summary>
    /// The guarantee from the OTHER side: an edit that lowers capacity must rotate the stamp too.
    ///
    /// <para>
    /// Deterministic on purpose, because the hazard it guards is not. IsConcurrencyToken only puts
    /// the column in the WHERE clause — it does not generate a new value — so an edit that forgets
    /// to assign one leaves the stamp in the database untouched. A member who read the class before
    /// the shrink would then still hold a matching token and commit a booking against a capacity
    /// that no longer exists. Asserting the rotation directly catches that; racing for it would not,
    /// since the interleaving that exposes it cannot be forced from the API.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Lowering_capacity_rotates_the_class_stamp()
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, typeId, trainerId, capacity: 4);

        var before = await StampOfAsync(scheduled.Id);

        var response = await admin.PutAsJsonAsync($"{ClassesEndpoint}/{scheduled.Id}", new
        {
            classTypeId = typeId,
            startsAt = scheduled.StartsAt,
            instructorMemberId = trainerId,
            durationMinutes = scheduled.DurationMinutes,
            capacity = 2,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // REMOVE THE ROTATION IN UpdateAsync AND THIS LINE FAILS. That is the whole point of it.
        Assert.NotEqual(before, await StampOfAsync(scheduled.Id));
    }

    /// <summary>
    /// A capacity shrink racing a booking for the last spot. Whichever lands first, the class must
    /// not end up holding more active bookings than the capacity it finally has.
    /// </summary>
    [Fact]
    public async Task A_capacity_shrink_racing_a_booking_never_overbooks()
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, typeId, trainerId, capacity: 2);

        // One spot already taken, one left — the spot both writers are reaching for.
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(await NewMemberAsync(), scheduled.Id)).StatusCode);

        var challenger = await NewMemberAsync();

        await Task.WhenAll(
            admin.PutAsJsonAsync($"{ClassesEndpoint}/{scheduled.Id}", new
            {
                classTypeId = typeId,
                startsAt = scheduled.StartsAt,
                instructorMemberId = trainerId,
                durationMinutes = scheduled.DurationMinutes,
                capacity = 1,
            }),
            BookAsync(challenger, scheduled.Id));

        // Either outcome is correct — the shrink may win and refuse the booking as class_full, or
        // the booking may win and the shrink be refused. What is never correct is a class holding
        // more people than the capacity it ended up with.
        var final = await admin.GetFromJsonAsync<ClassBody>($"{ClassesEndpoint}/{scheduled.Id}");
        var rows = await BookingsForAsync(scheduled.Id);

        Assert.True(
            rows.Count(b => b.Status == BookingStatus.Active) <= final!.Capacity,
            "the class holds more active bookings than it has spots");
    }

    // --- phase 2: bookings become visible ------------------------------------

    /// <summary>
    /// The read path's free-spot count, which was a placeholder equal to capacity until this phase.
    /// Checked on BOTH the member schedule and the admin list because they share one projection —
    /// and a regression that split them would be invisible from either side alone.
    /// </summary>
    [Fact]
    public async Task Free_spots_fall_on_a_booking_and_recover_on_a_cancellation()
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, typeId, trainerId, capacity: 5);
        var member = await NewMemberAsync();

        var window = $"?from={Uri.EscapeDataString(scheduled.StartsAt.AddHours(-1).ToString("o"))}"
                     + $"&to={Uri.EscapeDataString(scheduled.StartsAt.AddHours(1).ToString("o"))}";

        async Task<int> FreeSpotsAsync(HttpClient client, string route)
        {
            var rows = await client.GetFromJsonAsync<List<ClassBody>>(route + window);
            return rows!.Single(r => r.Id == scheduled.Id).FreeSpots;
        }

        Assert.Equal(5, await FreeSpotsAsync(member, "/api/classes"));

        await BookAsync(member, scheduled.Id);

        Assert.Equal(4, await FreeSpotsAsync(member, "/api/classes"));
        Assert.Equal(4, await FreeSpotsAsync(admin, ClassesEndpoint));

        // The single-class read the edit form uses has its own construction of the same number.
        var one = await admin.GetFromJsonAsync<ClassBody>($"{ClassesEndpoint}/{scheduled.Id}");
        Assert.Equal(4, one!.FreeSpots);

        await ReleaseAsync(member, scheduled.Id);

        Assert.Equal(5, await FreeSpotsAsync(member, "/api/classes"));
    }

    /// <summary>
    /// The booking response carries the class as it now stands, so the calendar tile can be redrawn
    /// without a refetch — the whole reason that endpoint answers with a class rather than a booking.
    ///
    /// <para>
    /// THE RELEASE DOES NOT, and that asymmetry is deliberate rather than an oversight: it answers
    /// 204, because the screen that releases a spot is a list of PEOPLE and reloads that list rather
    /// than a tile. So the freed spot is verified by re-reading the class.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Booking_answers_with_the_updated_free_spots_and_releasing_frees_one()
    {
        var (scheduled, member) = await BookableAsync(capacity: 4);

        var booked = await (await BookAsync(member, scheduled.Id))
            .Content.ReadFromJsonAsync<ClassBody>();
        Assert.Equal(3, booked!.FreeSpots);

        Assert.Equal(
            HttpStatusCode.NoContent, (await ReleaseAsync(member, scheduled.Id)).StatusCode);

        var admin = await AdminAsync();
        var window = $"?from={Uri.EscapeDataString(scheduled.StartsAt.AddHours(-1).ToString("o"))}"
                     + $"&to={Uri.EscapeDataString(scheduled.StartsAt.AddHours(1).ToString("o"))}";

        var reread = (await admin.GetFromJsonAsync<List<ClassBody>>(ClassesEndpoint + window))!
            .Single(c => c.Id == scheduled.Id);

        Assert.Equal(4, reread.FreeSpots);
    }

    // --- FR-014: the admin's list ---------------------------------------------

    [Fact]
    public async Task Admin_sees_who_signed_up_in_booking_order()
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, typeId, trainerId);

        Assert.Empty((await admin.GetFromJsonAsync<List<ClassBookingBody>>(
            AdminBookingsOf(scheduled.Id)))!);

        var first = await NewMemberAsync();
        var second = await NewMemberAsync();
        await BookAsync(first, scheduled.Id);
        await BookAsync(second, scheduled.Id);

        // Cancelled rows are history, not a sign-up list.
        var third = await NewMemberAsync();
        await BookAsync(third, scheduled.Id);
        await ReleaseAsync(third, scheduled.Id);

        var rows = await admin.GetFromJsonAsync<List<ClassBookingBody>>(
            AdminBookingsOf(scheduled.Id));

        Assert.Equal(2, rows!.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Email)));
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.DisplayName)));
        Assert.True(rows[0].BookedAt <= rows[1].BookedAt);
    }

    [Fact]
    public async Task Admin_releasing_a_spot_frees_it()
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, typeId, trainerId, capacity: 2);
        var member = await NewMemberAsync();
        await BookAsync(member, scheduled.Id);

        var row = Assert.Single(
            (await admin.GetFromJsonAsync<List<ClassBookingBody>>(AdminBookingsOf(scheduled.Id)))!);

        var response = await admin.DeleteAsync($"{AdminBookingsOf(scheduled.Id)}/{row.BookingId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var booking = Assert.Single(await BookingsForAsync(scheduled.Id));
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.NotNull(booking.CancelledAt);

        // The member is free to book again - a release is a cancellation, not a ban.
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(member, scheduled.Id)).StatusCode);
    }

    [Fact]
    public async Task Admin_releasing_a_booking_from_another_class_is_404()
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var holding = await PostClassAsync(admin, typeId, trainerId);
        var other = await PostClassAsync(admin, typeId, trainerId);

        var member = await NewMemberAsync();
        await BookAsync(member, holding.Id);

        var row = Assert.Single(
            (await admin.GetFromJsonAsync<List<ClassBookingBody>>(AdminBookingsOf(holding.Id)))!);

        var response = await admin.DeleteAsync($"{AdminBookingsOf(other.Id)}/{row.BookingId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(BookingStatus.Active, Assert.Single(await BookingsForAsync(holding.Id)).Status);
    }

    [Fact]
    public async Task Admin_booking_routes_refuse_a_member()
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, typeId, trainerId);
        var member = await NewMemberAsync();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.GetAsync(AdminBookingsOf(scheduled.Id))).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.DeleteAsync($"{AdminBookingsOf(scheduled.Id)}/{Guid.NewGuid()}")).StatusCode);
    }

    // --- the block cascade -----------------------------------------------------

    /// <summary>
    /// Blocking a member frees the seats they were holding — the answer to the PRD open question
    /// that S-01 and S-02 both deferred.
    ///
    /// <para>
    /// FUTURE ONLY. The past booking in this test is what stops the cascade being written as "cancel
    /// everything they hold", which would rewrite attendance history.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Blocking_a_member_cancels_their_future_bookings_and_keeps_the_past()
    {
        var (admin, typeId, trainerId) = await ArrangeAsync();
        var upcoming = await PostClassAsync(admin, typeId, trainerId, capacity: 3);
        var past = await InsertClassAsync(
            typeId, trainerId, DateTimeOffset.UtcNow.AddDays(-2), ClassStatus.Scheduled);

        var email = $"cascade-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        // The karnet the gate requires (S-16). This test seeds its own member rather than going
        // through NewMemberAsync, because it needs the address to look the member id up afterwards.
        await fixture.IssuePassForAccountAsync(email);

        var member = await fixture.CreateAuthenticatedClientAsync(email);

        await BookAsync(member, upcoming.Id);

        var memberId = (await admin.GetFromJsonAsync<List<MemberBody>>("/api/admin/members"))!
            .Single(m => m.Email == email).Id;

        await using (var db = NewContext())
        {
            db.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                ClassId = past,
                MemberId = memberId,
                Status = BookingStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            });

            await db.SaveChangesAsync();
        }

        // Addressed by MEMBER since S-14, which is also the only key the booking carries now.
        var blocked = await admin.PostAsync(
            $"/api/admin/members/{memberId}/block", content: null);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);

        Assert.Equal(
            BookingStatus.Cancelled,
            Assert.Single(await BookingsForAsync(upcoming.Id)).Status);

        // Untouched: this class already happened.
        Assert.Equal(BookingStatus.Active, Assert.Single(await BookingsForAsync(past)).Status);

        // And the seat is genuinely back on the schedule, not merely marked cancelled.
        var one = await admin.GetFromJsonAsync<ClassBody>($"{ClassesEndpoint}/{upcoming.Id}");
        Assert.Equal(3, one!.FreeSpots);
    }
}
