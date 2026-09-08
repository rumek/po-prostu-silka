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
    private sealed record ClassBody(Guid Id, int Capacity, int FreeSpots);

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
        var type = await CreateTypeAsync(admin);
        var trainerId = await CreateTrainerAsync(admin);

        var response = await admin.PostAsJsonAsync(ClassesEndpoint, new
        {
            classTypeId = type.Id,
            startsAt = NextSlot(),
            instructorMemberId = trainerId,
            durationMinutes = 60,
            capacity,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (admin, (await response.Content.ReadFromJsonAsync<ClassBody>())!);
    }

    /// <summary>
    /// A person the club recorded who has never registered — the case the route exists for, since
    /// they cannot book for themselves.
    /// </summary>
    private Task<Guid> AccountlessMemberAsync(MembershipStatus status = MembershipStatus.Active) =>
        fixture.CreateMemberAsync($"Bez Konta {Guid.NewGuid():N}", status);

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

        var members = await admin.GetFromJsonAsync<List<MemberBody>>("/api/admin/members");
        var memberId = members!.Single(m => m.Email == email).Id;

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(admin, scheduled.Id, memberId)).StatusCode);

        var booking = Assert.Single(await BookingsForAsync(scheduled.Id));
        Assert.Equal(memberId, booking.MemberId);

        // The legacy account column is not written any more (Phase 8) — the member key is the key.
        Assert.Null(booking.MemberUserId);
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
}
