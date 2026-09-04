using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// Cancellation as a state transition, and the messages it owes (S-09; prd.md FR-013, US-02,
/// FR-021) — plus the second trigger, an EDIT to a class people are booked on.
///
/// <para>
/// WHY THIS FILE EXISTS: <see cref="A_cancel_racing_a_booking_never_leaves_a_member_untold"/>. Every
/// other test here pins a rule a careful reading of <c>CancelAsync</c> would also give you. That one
/// pins the guarantee the slice was built for, and it is the second test in this repository that
/// FAILS if a single line is removed — the <c>ConcurrencyStamp</c> rotation in <c>CancelAsync</c>.
/// Without it a cancel commits leaving the stamp untouched, so a booking already in flight still
/// matches on its stale token and lands an ACTIVE spot on a class that is already cancelled — and
/// the messages went out before that member existed on the list. Comment that line out and this test
/// goes red; nothing else does.
/// </para>
///
/// <para>
/// ITS OWN TIME BASE, 2034. ClassEndpointTests works in 2030 and BookingEndpointTests in 2032; all
/// three files write into one container, and the overlap rule is club-wide, so a shared base would
/// make one file's classes refuse another's on <c>time_conflict</c> depending on execution order.
/// </para>
///
/// <para>
/// OUTBOX ROWS ARE MATCHED BY SUBJECT, not by truncating the table. These tests run in the same
/// collection as the delivery tests, which own that table's lifecycle; every class here is created
/// from a type with a GUID in its name, and the subject carries that name, so each test sees exactly
/// its own rows however many others ran first.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class ClassCancellationTests(IntegrationTestFixture fixture)
{
    /// <summary>Mirrors ScheduledClass — only what these tests read from it.</summary>
    private sealed record ClassBody(Guid Id, string Name, int Capacity, int FreeSpots, string Status);

    /// <summary>Mirrors ClassTypeSummary — only what these tests read from it.</summary>
    private sealed record ClassTypeBody(Guid Id, string Name);

    /// <summary>Mirrors MemberSummary — only what these tests read from it.</summary>
    private sealed record MemberBody(string Id, string Email);

    /// <summary>Mirrors MyBooking - only what these tests read from it.</summary>
    private sealed record MyBookingBody(Guid BookingId, Guid ClassId, string Name, DateTimeOffset StartsAt);

    /// <summary>Mirrors ClassBooking - only what these tests read from it.</summary>
    private sealed record ClassBookingBody(Guid BookingId, string MemberUserId, string DisplayName);

    /// <summary>Mirrors ClassFailure.</summary>
    private sealed record FailureBody(string Reason);

    private const string Endpoint = "/api/admin/classes";
    private const string TypesEndpoint = "/api/admin/class-types";

    private static string CancelOf(Guid classId) => $"{Endpoint}/{classId}/cancel";

    private static int _slot;

    private static DateTimeOffset NextSlot() =>
        new DateTimeOffset(2034, 6, 1, 10, 0, 0, TimeSpan.Zero)
            .AddDays(60 * Interlocked.Increment(ref _slot));

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    // --- fixture helpers -------------------------------------------------------

    private Task<HttpClient> AdminAsync() =>
        fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    /// <summary>
    /// A class type whose name carries a GUID. That name reaches the message subject, which is how
    /// each test finds its own outbox rows in a shared table.
    /// </summary>
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

    private Task<string> CreateTrainerAsync(HttpClient admin) =>
        CreateNamedTrainerAsync(admin, displayName: null);

    /// <summary>
    /// A trainer with a chosen display name, so a test asserting that a message carries the RIGHT
    /// name means something - every trainer is otherwise called "Test Trainer".
    /// </summary>
    private async Task<string> CreateNamedTrainerAsync(HttpClient admin, string? displayName)
    {
        var email = $"trainer-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(
            email, AccountStatus.Active, ApplicationRoles.Trainer, displayName);

        var members = await admin.GetFromJsonAsync<List<MemberBody>>("/api/admin/members");

        return members!.Single(m => m.Email == email).Id;
    }

    private async Task<(HttpClient Admin, ClassTypeBody Type, string TrainerId)> ArrangeAsync()
    {
        var admin = await AdminAsync();

        return (admin, await CreateTypeAsync(admin), await CreateTrainerAsync(admin));
    }

    private static async Task<ClassBody> PostClassAsync(
        HttpClient admin, Guid typeId, string trainerId, DateTimeOffset startsAt, int capacity = 12)
    {
        var response = await admin.PostAsJsonAsync(Endpoint, new
        {
            classTypeId = typeId,
            startsAt,
            instructorUserId = trainerId,
            durationMinutes = 60,
            capacity,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClassBody>())!;
    }

    /// <summary>
    /// Submits a full edit. Every field is sent on every call — an edit REPLACES the occurrence, so
    /// omitting a field is not "leave it alone", it is "clear it".
    /// </summary>
    private static Task<HttpResponseMessage> PutClassAsync(
        HttpClient admin,
        Guid classId,
        Guid typeId,
        string trainerId,
        DateTimeOffset startsAt,
        int duration = 60,
        int capacity = 12) =>
        admin.PutAsJsonAsync($"{Endpoint}/{classId}", new
        {
            classTypeId = typeId,
            startsAt,
            instructorUserId = trainerId,
            durationMinutes = duration,
            capacity,
        });

    /// <summary>
    /// A brand-new active member, signed in, with <paramref name="deviceCount"/> push subscriptions
    /// registered. Returns the client and the member's id.
    /// </summary>
    private async Task<(HttpClient Client, string Id, string Email)> NewMemberAsync(int deviceCount = 0)
    {
        var email = $"booker-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        await using var db = NewContext();
        var id = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();

        for (var i = 0; i < deviceCount; i++)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = id,
                Endpoint = $"https://push.test/{Guid.NewGuid():N}",
                P256dh = "p256dh",
                Auth = "auth",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();

        return (await fixture.CreateAuthenticatedClientAsync(email), id, email);
    }

    private static async Task<HttpClient> BookAsync(HttpClient member, Guid classId)
    {
        var response = await member.PostAsync($"/api/classes/{classId}/bookings", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return member;
    }

    /// <summary>
    /// Writes a class straight into the database, bypassing the API — the only way to arrange a
    /// class in the PAST, which <c>starts_in_past</c> refuses at creation.
    /// </summary>
    private async Task<Guid> InsertClassAsync(
        Guid typeId, string trainerId, DateTimeOffset startsAt, ClassStatus status)
    {
        await using var db = NewContext();

        var entity = new Class
        {
            Id = Guid.NewGuid(),
            ClassTypeId = typeId,
            InstructorUserId = trainerId,
            StartsAt = startsAt,
            DurationMinutes = 60,
            Capacity = 12,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Classes.Add(entity);
        await db.SaveChangesAsync();

        return entity.Id;
    }

    /// <summary>Every outbox row whose subject names this class type — this test's own rows.</summary>
    /// <summary>
    /// The club-local hour of an instant, derived here rather than by calling
    /// <c>MessageTime.ToClubWallClock</c>. Computing an expectation with the function under test asserts
    /// nothing — a wrong zone or a wrong culture would pass. These slots move by a `_slot` multiple
    /// of 60 days and so can land on either side of a DST transition, which is why this restates the
    /// conversion instead of pinning a literal string; the literals live in <c>ClubTimeTests</c>.
    /// </summary>
    private static string ClubWallClockOf(DateTimeOffset instant) =>
        TimeZoneInfo
            .ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"))
            .ToString("HH:mm");

    private async Task<List<OutboxMessage>> MessagesAboutAsync(string typeName)
    {
        await using var db = NewContext();

        return await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Subject.Contains(typeName))
            .ToListAsync();
    }

    private async Task<ClassStatus> StatusOfAsync(Guid classId)
    {
        await using var db = NewContext();

        return await db.Classes
            .AsNoTracking()
            .Where(c => c.Id == classId)
            .Select(c => c.Status)
            .SingleAsync();
    }

    private async Task<List<Booking>> BookingsForAsync(Guid classId)
    {
        await using var db = NewContext();

        return await db.Bookings.AsNoTracking().Where(b => b.ClassId == classId).ToListAsync();
    }

    private static async Task<string> ReasonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason;
    }

    // --- who may reach it ------------------------------------------------------

    [Fact]
    public async Task Cancel_refuses_a_member()
    {
        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await member.PostAsync(CancelOf(Guid.Empty), content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancelling_a_class_that_does_not_exist_is_404()
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsync(CancelOf(Guid.NewGuid()), content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- the transition --------------------------------------------------------

    [Fact]
    public async Task Cancelling_moves_the_class_to_cancelled_and_leaves_every_booking_active()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, NextSlot());

        var (first, _, _) = await NewMemberAsync();
        var (second, _, _) = await NewMemberAsync();
        await BookAsync(first, scheduled.Id);
        await BookAsync(second, scheduled.Id);

        var response = await admin.PostAsync(CancelOf(scheduled.Id), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The response is the class as it now stands, so the calendar can replace a tile without a
        // refetch — same contract UpdateAsync answers with.
        var body = (await response.Content.ReadFromJsonAsync<ClassBody>())!;
        Assert.Equal(nameof(ClassStatus.Cancelled), body.Status);

        Assert.Equal(ClassStatus.Cancelled, await StatusOfAsync(scheduled.Id));

        // THE MODEL. Cancellation is a state of the CLASS; cascading it onto the bookings would
        // record that the MEMBER cancelled, which is false and is the club's own history rewritten.
        var bookings = await BookingsForAsync(scheduled.Id);
        Assert.Equal(2, bookings.Count);
        Assert.All(bookings, b => Assert.Equal(BookingStatus.Active, b.Status));
        Assert.All(bookings, b => Assert.Null(b.CancelledAt));
    }

    [Fact]
    public async Task A_cancelled_class_leaves_both_calendars_but_not_the_database()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var startsAt = NextSlot();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, startsAt);

        var (member, _, _) = await NewMemberAsync();
        await BookAsync(member, scheduled.Id);

        Assert.Equal(
            HttpStatusCode.OK, (await admin.PostAsync(CancelOf(scheduled.Id), content: null)).StatusCode);

        var window = $"?from={Iso(startsAt.AddDays(-1))}&to={Iso(startsAt.AddDays(1))}";

        var memberSees = await member.GetFromJsonAsync<List<ClassBody>>($"/api/classes/{window}");
        Assert.DoesNotContain(memberSees!, c => c.Id == scheduled.Id);

        // AND THE ADMIN'S. A cancelled class is done - the members were told - and its tile would
        // leave an hour that looks occupied and is not. The overlap rule already ignores it
        // (ClassStore: two classes conflict only when BOTH are Scheduled), so before this the admin
        // saw a block sitting in a slot they were free to reuse.
        var adminSees = await admin.GetFromJsonAsync<List<ClassBody>>($"{Endpoint}/{window}");
        Assert.DoesNotContain(adminSees!, c => c.Id == scheduled.Id);

        // GONE FROM THE CALENDAR, NOT FROM THE RECORD. The class is still fetchable by id and its
        // sign-up list still names who was on it - that is the difference between this and a delete,
        // and it is the whole reason the transition exists.
        var byId = await admin.GetFromJsonAsync<ClassBody>($"{Endpoint}/{scheduled.Id}");
        Assert.Equal(nameof(ClassStatus.Cancelled), byId!.Status);

        Assert.Single(await BookingsForAsync(scheduled.Id));
    }

    private static string Iso(DateTimeOffset instant) =>
        Uri.EscapeDataString(instant.ToString("O"));

    // --- the refusals ----------------------------------------------------------

    [Fact]
    public async Task Cancelling_a_class_that_has_started_is_class_started()
    {
        var (admin, type, trainerId) = await ArrangeAsync();

        // An hour ago. Telling the members who turned up that the class is cancelled is
        // disinformation, and there is no undo — so this is refused rather than merely pointless.
        var classId = await InsertClassAsync(
            type.Id, trainerId, DateTimeOffset.UtcNow.AddHours(-1), ClassStatus.Scheduled);

        Assert.Equal(
            "class_started", await ReasonAsync(await admin.PostAsync(CancelOf(classId), content: null)));

        Assert.Equal(ClassStatus.Scheduled, await StatusOfAsync(classId));
        Assert.Empty(await MessagesAboutAsync(type.Name));
    }

    [Fact]
    public async Task Cancelling_twice_is_already_cancelled_and_sends_one_round_of_messages()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, NextSlot());

        var (member, _, _) = await NewMemberAsync();
        await BookAsync(member, scheduled.Id);

        Assert.Equal(
            HttpStatusCode.OK, (await admin.PostAsync(CancelOf(scheduled.Id), content: null)).StatusCode);

        Assert.Equal(
            "already_cancelled",
            await ReasonAsync(await admin.PostAsync(CancelOf(scheduled.Id), content: null)));

        // THE POINT OF THE REFUSAL. The transition is one-way, and a second round of email to people
        // already told their class is off is the failure it exists to prevent.
        var messages = await MessagesAboutAsync(type.Name);
        Assert.Single(messages);
    }

    // --- the fan-out -----------------------------------------------------------

    [Fact]
    public async Task Cancelling_enqueues_one_email_per_member_and_one_push_per_device()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, NextSlot());

        // Three members: one with no device, one with a phone, one with a phone and a laptop.
        var (plain, _, _) = await NewMemberAsync();
        var (oneDevice, _, _) = await NewMemberAsync(deviceCount: 1);
        var (twoDevices, _, _) = await NewMemberAsync(deviceCount: 2);

        await BookAsync(plain, scheduled.Id);
        await BookAsync(oneDevice, scheduled.Id);
        await BookAsync(twoDevices, scheduled.Id);

        // Someone who released their spot is owed nothing — the recipient list is ACTIVE bookings.
        var (released, _, _) = await NewMemberAsync(deviceCount: 1);
        await BookAsync(released, scheduled.Id);
        await released.DeleteAsync($"/api/classes/{scheduled.Id}/bookings/mine");

        // And a member booked on a DIFFERENT class of the same type must not be swept in.
        var other = await PostClassAsync(admin, type.Id, trainerId, NextSlot());
        var (bystander, _, _) = await NewMemberAsync(deviceCount: 1);
        await BookAsync(bystander, other.Id);

        Assert.Equal(
            HttpStatusCode.OK, (await admin.PostAsync(CancelOf(scheduled.Id), content: null)).StatusCode);

        var messages = await MessagesAboutAsync(type.Name);

        Assert.Equal(3, messages.Count(m => m.Channel == NotificationChannel.Email));
        Assert.Equal(3, messages.Count(m => m.Channel == NotificationChannel.Push));

        // Pending, so the worker's next pass picks them up. Nothing here delivers.
        Assert.All(messages, m => Assert.Equal(OutboxStatus.Pending, m.Status));
        Assert.All(messages, m => Assert.Equal(0, m.AttemptCount));
    }

    [Fact]
    public async Task Cancelling_a_class_nobody_booked_enqueues_nothing()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, NextSlot());

        Assert.Equal(
            HttpStatusCode.OK, (await admin.PostAsync(CancelOf(scheduled.Id), content: null)).StatusCode);

        Assert.Equal(ClassStatus.Cancelled, await StatusOfAsync(scheduled.Id));
        Assert.Empty(await MessagesAboutAsync(type.Name));
    }

    [Fact]
    public async Task Push_rows_carry_the_subscription_id_so_a_dead_device_can_be_deleted()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, NextSlot());

        var (member, memberId, _) = await NewMemberAsync(deviceCount: 1);
        await BookAsync(member, scheduled.Id);

        Assert.Equal(
            HttpStatusCode.OK, (await admin.PostAsync(CancelOf(scheduled.Id), content: null)).StatusCode);

        await using var db = NewContext();
        var subscription = await db.PushSubscriptions.AsNoTracking()
            .SingleAsync(p => p.UserId == memberId);

        var messages = await MessagesAboutAsync(type.Name);
        var push = Assert.Single(messages, m => m.Channel == NotificationChannel.Push);

        // Not the endpoint: the worker looks the row up by id so it can remove it on a 410.
        Assert.Equal(subscription.Id.ToString(), push.Recipient);
    }

    [Fact]
    public async Task The_message_names_the_class_its_club_local_time_and_its_trainer()
    {
        var (admin, type, trainerId) = await ArrangeAsync();

        // 16:00 UTC in June is 18:00 in Warsaw. Asserting on the LOCAL reading is the whole point:
        // an email saying 16:00 sends the member to the gym two hours early.
        var startsAt = new DateTimeOffset(2034, 6, 20, 16, 0, 0, TimeSpan.Zero)
            .AddDays(60 * Interlocked.Increment(ref _slot));

        var scheduled = await PostClassAsync(admin, type.Id, trainerId, startsAt);

        var (member, _, _) = await NewMemberAsync();
        await BookAsync(member, scheduled.Id);

        Assert.Equal(
            HttpStatusCode.OK, (await admin.PostAsync(CancelOf(scheduled.Id), content: null)).StatusCode);

        var message = Assert.Single(await MessagesAboutAsync(type.Name));

        Assert.Contains(type.Name, message.Subject);
        Assert.Contains(type.Name, message.Body);
        Assert.Contains(ClubWallClockOf(startsAt), message.Body);
        Assert.Contains($"Test {ApplicationRoles.Trainer}", message.Body);

        // Rendered at ENQUEUE time and frozen into the row, so a retry hours later says what the
        // first attempt said.
        Assert.False(string.IsNullOrWhiteSpace(message.Body));
    }

    /// <summary>
    /// ClassType.Name is allowed the full 200 characters and OutboxMessage.Subject is nvarchar(200),
    /// so a prefixed subject overflows the column. This is a CANCELLATION test, not a formatting one:
    /// SQL Server refuses the insert, the truncation error surfaces as a DbUpdateException which
    /// TrySaveChangesAsync does not catch, and because the enqueue shares its unit of work with the
    /// status flip, the cancellation itself would never commit.
    /// </summary>
    [Fact]
    public async Task A_class_type_name_at_the_column_limit_still_lets_the_cancellation_commit()
    {
        var admin = await AdminAsync();
        var trainerId = await CreateTrainerAsync(admin);

        // Exactly the 200 the column allows, unique so it cannot collide with another test's type.
        var longName = $"{Guid.NewGuid():N}{new string('A', 168)}";

        var typeResponse = await admin.PostAsJsonAsync(TypesEndpoint, new
        {
            name = longName,
            description = (string?)"Opis zajęć",
            defaultDurationMinutes = 60,
            defaultCapacity = 12,
        });

        Assert.Equal(HttpStatusCode.OK, typeResponse.StatusCode);
        var type = (await typeResponse.Content.ReadFromJsonAsync<ClassTypeBody>())!;

        var scheduled = await PostClassAsync(admin, type.Id, trainerId, NextSlot());

        var (member, _, _) = await NewMemberAsync();
        await BookAsync(member, scheduled.Id);

        var response = await admin.PostAsync(CancelOf(scheduled.Id), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ClassStatus.Cancelled, await StatusOfAsync(scheduled.Id));

        // The name is truncated in the subject, so look the row up by the part that survives.
        var prefix = longName[..32];

        await using var db = NewContext();
        var message = await db.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Subject.Contains(prefix));

        Assert.True(
            message.Subject.Length <= 200,
            $"Subject is {message.Subject.Length} characters; the column holds 200.");

        // Truncation trims the NAME, never the prefix — that is what a member scans a mailbox for.
        Assert.StartsWith("Odwołane zajęcia: ", message.Subject);

        // The body is nvarchar(max), so it keeps the whole name.
        Assert.Contains(longName, message.Body);
    }

    // --- S-09 phase 2: the edit trigger ---------------------------------------
    //
    // A pure PRODUCT RULE, and one that lives nowhere but a three-field comparison. Nothing in the
    // type system says capacity is silent while duration is not, so these tests are the only
    // statement of it.

    /// <summary>
    /// Each of the three member-visible fields, moved on its own. All three notify, and the message
    /// names the old value beside the new one - a message saying only "something changed" would send
    /// the member back to the app to find out what.
    /// </summary>
    [Fact]
    public async Task Moving_the_start_time_notifies_every_booked_member()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var startsAt = NextSlot();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, startsAt);

        var (first, _, _) = await NewMemberAsync();
        var (second, _, _) = await NewMemberAsync(deviceCount: 1);
        await BookAsync(first, scheduled.Id);
        await BookAsync(second, scheduled.Id);

        var moved = startsAt.AddHours(2);

        Assert.Equal(
            HttpStatusCode.OK,
            (await PutClassAsync(admin, scheduled.Id, type.Id, trainerId, moved)).StatusCode);

        var messages = await MessagesAboutAsync(type.Name);

        // Two members, one of them with a phone: two emails and one push.
        Assert.Equal(2, messages.Count(m => m.Channel == NotificationChannel.Email));
        Assert.Single(messages, m => m.Channel == NotificationChannel.Push);

        // BOTH TIMES, in the club's wall clock. "18:00 -> 18:00" is what a message built from the
        // tracked entity after the mutation would say.
        Assert.All(messages, m =>
        {
            Assert.Contains(ClubWallClockOf(startsAt), m.Body);
            Assert.Contains(ClubWallClockOf(moved), m.Body);
        });
    }

    [Fact]
    public async Task Changing_the_duration_notifies_every_booked_member()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var startsAt = NextSlot();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, startsAt);

        var (member, _, _) = await NewMemberAsync();
        await BookAsync(member, scheduled.Id);

        Assert.Equal(
            HttpStatusCode.OK,
            (await PutClassAsync(admin, scheduled.Id, type.Id, trainerId, startsAt, duration: 90))
                .StatusCode);

        var message = Assert.Single(await MessagesAboutAsync(type.Name));

        Assert.Contains("60 min", message.Body);
        Assert.Contains("90 min", message.Body);
    }

    [Fact]
    public async Task Reassigning_the_trainer_notifies_and_names_both_of_them()
    {
        var admin = await AdminAsync();
        var type = await CreateTypeAsync(admin);

        // Named accounts, not two "Test Trainer"s: asserting that the message carries the RIGHT
        // display name is meaningless while both trainers are called the same thing.
        var leaving = await CreateNamedTrainerAsync(admin, "Anna Kowalska");
        var arriving = await CreateNamedTrainerAsync(admin, "Piotr Nowak");

        var startsAt = NextSlot();
        var scheduled = await PostClassAsync(admin, type.Id, leaving, startsAt);

        var (member, _, _) = await NewMemberAsync();
        await BookAsync(member, scheduled.Id);

        Assert.Equal(
            HttpStatusCode.OK,
            (await PutClassAsync(admin, scheduled.Id, type.Id, arriving, startsAt)).StatusCode);

        var message = Assert.Single(await MessagesAboutAsync(type.Name));

        // The OLD name comes from the tracked entity's navigation and the NEW one from the account
        // the edit validated. Reading both off the entity would print the leaving trainer twice.
        Assert.Contains("Anna Kowalska", message.Body);
        Assert.Contains("Piotr Nowak", message.Body);
    }

    /// <summary>
    /// The silent half of the rule, and the half that keeps the loud half worth reading. Capacity is
    /// administrative - it moves for reasons that have nothing to do with the people already in - and
    /// a PUT that changes nothing changes nothing.
    /// </summary>
    [Fact]
    public async Task Editing_only_the_capacity_or_nothing_at_all_notifies_nobody()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var startsAt = NextSlot();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, startsAt, capacity: 12);

        var (member, _, _) = await NewMemberAsync(deviceCount: 1);
        await BookAsync(member, scheduled.Id);

        // Capacity 12 -> 20, everything else identical.
        Assert.Equal(
            HttpStatusCode.OK,
            (await PutClassAsync(admin, scheduled.Id, type.Id, trainerId, startsAt, capacity: 20))
                .StatusCode);

        Assert.Empty(await MessagesAboutAsync(type.Name));

        // And a PUT that moves nothing at all - the shape a form submitted without an edit produces.
        Assert.Equal(
            HttpStatusCode.OK,
            (await PutClassAsync(admin, scheduled.Id, type.Id, trainerId, startsAt, capacity: 20))
                .StatusCode);

        Assert.Empty(await MessagesAboutAsync(type.Name));
    }

    /// <summary>
    /// Editing a CANCELLED class notifies nobody. Its members have already been told it is not
    /// happening; correcting its record afterwards is bookkeeping, and there is no live appointment
    /// left to update.
    /// </summary>
    [Fact]
    public async Task Editing_a_cancelled_class_notifies_nobody()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var startsAt = NextSlot();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, startsAt);

        var (member, _, _) = await NewMemberAsync();
        await BookAsync(member, scheduled.Id);

        Assert.Equal(
            HttpStatusCode.OK, (await admin.PostAsync(CancelOf(scheduled.Id), content: null)).StatusCode);

        // One message so far: the cancellation.
        Assert.Single(await MessagesAboutAsync(type.Name));

        Assert.Equal(
            HttpStatusCode.OK,
            (await PutClassAsync(admin, scheduled.Id, type.Id, trainerId, startsAt.AddHours(3)))
                .StatusCode);

        // Still one. The edit landed on the record and sent nothing.
        Assert.Single(await MessagesAboutAsync(type.Name));
    }

    [Fact]
    public async Task Editing_a_class_nobody_booked_notifies_nobody()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var startsAt = NextSlot();
        var scheduled = await PostClassAsync(admin, type.Id, trainerId, startsAt);

        Assert.Equal(
            HttpStatusCode.OK,
            (await PutClassAsync(admin, scheduled.Id, type.Id, trainerId, startsAt.AddHours(1)))
                .StatusCode);

        Assert.Empty(await MessagesAboutAsync(type.Name));
    }

    // --- S-09 phase 2: the read path ------------------------------------------

    /// <summary>
    /// A cancelled class leaves "Moje zajecia" while its booking row stays Active - the two halves
    /// of the model, asserted together because either alone is the wrong shape.
    /// </summary>
    [Fact]
    public async Task A_cancelled_class_leaves_my_bookings_but_the_row_stays_active()
    {
        var (admin, type, trainerId) = await ArrangeAsync();
        var cancelled = await PostClassAsync(admin, type.Id, trainerId, NextSlot());
        var surviving = await PostClassAsync(admin, type.Id, trainerId, NextSlot());

        var (member, _, _) = await NewMemberAsync();
        await BookAsync(member, cancelled.Id);
        await BookAsync(member, surviving.Id);

        Assert.Equal(
            2, (await member.GetFromJsonAsync<List<MyBookingBody>>("/api/bookings/mine"))!.Count);

        Assert.Equal(
            HttpStatusCode.OK, (await admin.PostAsync(CancelOf(cancelled.Id), content: null)).StatusCode);

        var mine = await member.GetFromJsonAsync<List<MyBookingBody>>("/api/bookings/mine");

        // ONLY the cancelled one is gone. A member with two bookings does not lose both.
        var remaining = Assert.Single(mine!);
        Assert.Equal(surviving.Id, remaining.ClassId);

        // The row itself is untouched: this is the class's state, not the member's decision, and the
        // admin's "Zapisani" list must still show who was signed up.
        var row = Assert.Single(await BookingsForAsync(cancelled.Id));
        Assert.Equal(BookingStatus.Active, row.Status);
        Assert.Null(row.CancelledAt);

        var stillListed = await admin.GetFromJsonAsync<List<ClassBookingBody>>(
            $"{Endpoint}/{cancelled.Id}/bookings");
        Assert.Single(stillListed!);
    }

    // --- atomicity and the race ------------------------------------------------

    /// <summary>
    /// The flip and its messages are ONE unit of work: neither is observable without the other.
    ///
    /// <para>
    /// Both directions are asserted here, because either alone is satisfiable by an accident. A
    /// REFUSED cancel leaves the class Scheduled and enqueues nothing; an ACCEPTED one leaves it
    /// Cancelled with exactly the rows its recipients are owed. What the single SaveChangesAsync
    /// rules out is the pair in between — a cancelled class whose members were never told.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_flip_and_the_messages_are_never_observable_apart()
    {
        var (admin, type, trainerId) = await ArrangeAsync();

        // A class in the past with somebody booked on it — the refusal path, arranged around the API
        // because starts_in_past would refuse to create it and BookAsync would refuse to join it.
        var refusedId = await InsertClassAsync(
            type.Id, trainerId, DateTimeOffset.UtcNow.AddHours(-2), ClassStatus.Scheduled);

        var (_, bookedId, _) = await NewMemberAsync();

        await using (var db = NewContext())
        {
            db.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                ClassId = refusedId,
                MemberUserId = bookedId,
                Status = BookingStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        Assert.Equal(
            "class_started", await ReasonAsync(await admin.PostAsync(CancelOf(refusedId), content: null)));

        Assert.Equal(ClassStatus.Scheduled, await StatusOfAsync(refusedId));
        Assert.Empty(await MessagesAboutAsync(type.Name));

        var accepted = await PostClassAsync(admin, type.Id, trainerId, NextSlot());
        var (member, _, _) = await NewMemberAsync();
        await BookAsync(member, accepted.Id);

        Assert.Equal(
            HttpStatusCode.OK, (await admin.PostAsync(CancelOf(accepted.Id), content: null)).StatusCode);

        Assert.Equal(ClassStatus.Cancelled, await StatusOfAsync(accepted.Id));
        Assert.Single(await MessagesAboutAsync(type.Name));
    }

    /// <summary>
    /// A cancel and a booking racing for the same class. Whichever lands first, the invariant that
    /// must hold is: a CANCELLED class never holds an active booking that was never told.
    ///
    /// <para>
    /// THIS IS THE TEST THE STAMP ROTATION EXISTS FOR. Without the rotation in <c>CancelAsync</c>, EF
    /// writes the status flip while leaving <c>ConcurrencyStamp</c> at the value the in-flight booker
    /// read, so the booker's <c>UPDATE ... WHERE stamp = S1</c> still matches and commits an ACTIVE
    /// booking onto a class whose messages have already gone out — a member holding a confirmed spot
    /// on a class that is not happening, and no email. With the rotation the booker's save matches no
    /// row, it re-reads, sees Cancelled, and is refused with <c>class_cancelled</c>.
    /// </para>
    ///
    /// <para>
    /// Raced over several independent classes rather than once: the hole is a timing window, and one
    /// attempt can miss it on a fast machine. Each class is its own arrangement, so a failure names
    /// the invariant rather than an ordering.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_cancel_racing_a_booking_never_leaves_a_member_untold()
    {
        const int Rounds = 6;

        var (admin, type, trainerId) = await ArrangeAsync();

        for (var round = 0; round < Rounds; round++)
        {
            var scheduled = await PostClassAsync(admin, type.Id, trainerId, NextSlot(), capacity: 1);
            var (challenger, _, challengerEmail) = await NewMemberAsync();

            await Task.WhenAll(
                admin.PostAsync(CancelOf(scheduled.Id), content: null),
                challenger.PostAsync($"/api/classes/{scheduled.Id}/bookings", content: null));

            var status = await StatusOfAsync(scheduled.Id);
            var active = (await BookingsForAsync(scheduled.Id))
                .Count(b => b.Status == BookingStatus.Active);

            if (status != ClassStatus.Cancelled)
            {
                // The booking committed first, so the cancel's save matched no row and wrote NOTHING
                // — not the flip, not the outbox rows. The member still has a class.
                Assert.Equal(ClassStatus.Scheduled, status);
                Assert.Equal(1, active);
                Assert.DoesNotContain(
                    await MessagesAboutAsync(type.Name), m => m.Recipient == challengerEmail);
                continue;
            }

            // The cancel committed. THE INVARIANT: every spot still held on this class was on the
            // list the cancel rendered messages for. Both outcomes below are legitimate — the
            // booking may have landed before the cancel read its recipients, or not at all — and
            // what is never legitimate is an active booking with no message.
            if (active > 0)
            {
                Assert.Equal(1, active);
                Assert.Contains(
                    await MessagesAboutAsync(type.Name), m => m.Recipient == challengerEmail);
            }
        }

        // Whatever the orderings were, no class that ended Cancelled left anybody unnotified: the
        // count of email rows is the count of members who held a spot when the flip committed.
        var messages = await MessagesAboutAsync(type.Name);
        Assert.All(messages, m => Assert.Equal(OutboxStatus.Pending, m.Status));
    }
}
