using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// The member code end to end (S-14, AM-004 and AM-005): the admin issues it, the person registers
/// with it, and they land on the record the club has been keeping — with the history already on it.
///
/// <para>
/// THE HISTORY ASSERTION IS THE SLICE. Everything else here is a guard around it: if a claim did not
/// inherit the bookings and the plan, the code would be an elaborate way to make an ordinary account.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class MemberClaimTests(IntegrationTestFixture fixture)
{
    private const string Members = "/api/admin/members";

    private sealed record CreatedBody(Guid Id);

    private sealed record AccessCodeBody(string Code, DateTimeOffset ExpiresAt);

    private sealed record FailureBody(string Reason);

    private sealed record CurrentUserBody(
        string Id,
        string Email,
        string DisplayName,
        string Status,
        string[] Roles,
        Guid? MemberId,
        string? MembershipStatus);

    private sealed record MyBookingBody(Guid BookingId, Guid ClassId);

    private sealed record PlanBody(Guid Id, string Name);

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    private Task<HttpClient> AdminAsync() =>
        fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

    private static string NewEmail() => $"claim-{Guid.NewGuid():N}@test.local";

    private static object Registration(string email, string? memberCode = null) =>
        new
        {
            email,
            password = TestUsers.Password,
            displayName = "Imię Z Formularza",
            phoneNumber = "601202303",
            street = "Polna",
            houseNumber = "7/2",
            postalCode = "00-002",
            city = "Kraków",
            memberCode,
        };

    /// <summary>An accountless member the club recorded, with a live code in the admin's hand.</summary>
    private async Task<(Guid MemberId, string Code, HttpClient Admin)> RecordedMemberAsync(
        string? displayName = null)
    {
        var admin = await AdminAsync();

        var created = await admin.PostAsJsonAsync(
            Members,
            new
            {
                displayName = displayName ?? $"Klubowicz {Guid.NewGuid():N}",
                email = (string?)null,
                phoneNumber = (string?)null,
                street = (string?)null,
                houseNumber = (string?)null,
                postalCode = (string?)null,
                city = (string?)null,
            });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var memberId = (await created.Content.ReadFromJsonAsync<CreatedBody>())!.Id;

        var issued = await admin.PostAsync($"{Members}/{memberId}/access-code", content: null);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);

        var code = (await issued.Content.ReadFromJsonAsync<AccessCodeBody>())!.Code;

        return (memberId, code, admin);
    }

    // --- issuing --------------------------------------------------------------

    [Fact]
    public async Task A_code_is_issued_readable_and_revocable()
    {
        var (memberId, code, admin) = await RecordedMemberAsync();

        // Formatted for reading aloud: the admin says this down the phone.
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", code);

        var read = await admin.GetFromJsonAsync<AccessCodeBody>($"{Members}/{memberId}/access-code");
        Assert.Equal(code, read!.Code);

        var revoked = await admin.DeleteAsync($"{Members}/{memberId}/access-code");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        var afterRevoke = await admin.GetAsync($"{Members}/{memberId}/access-code");
        Assert.Equal(HttpStatusCode.NoContent, afterRevoke.StatusCode);
    }

    /// <summary>
    /// Re-issuing replaces rather than refuses: the admin pressing this again is someone who lost the
    /// note they wrote it on. What must NOT survive is the old code.
    /// </summary>
    [Fact]
    public async Task Re_issuing_replaces_the_previous_code()
    {
        var (memberId, first, admin) = await RecordedMemberAsync();

        var second = (await (await admin.PostAsync($"{Members}/{memberId}/access-code", content: null))
            .Content.ReadFromJsonAsync<AccessCodeBody>())!.Code;

        Assert.NotEqual(first, second);

        var client = fixture.CreateClient();
        var withOld = await client.PostAsJsonAsync(
            "/api/auth/register", Registration(NewEmail(), first));

        Assert.Equal(HttpStatusCode.Conflict, withOld.StatusCode);
        Assert.Equal(
            "unknown_member_code",
            (await withOld.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    [Fact]
    public async Task Issuing_a_code_for_a_member_who_already_has_an_account_is_409()
    {
        var admin = await AdminAsync();
        var email = NewEmail();
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userId = (await userManager.FindByEmailAsync(email))!.Id;
        var memberId = await fixture.MemberIdOfAsync(userId);

        var response = await admin.PostAsync($"{Members}/{memberId}/access-code", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("has_account", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    [Fact]
    public async Task Reading_a_code_requires_an_admin()
    {
        var (memberId, _, _) = await RecordedMemberAsync();
        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await member.GetAsync($"{Members}/{memberId}/access-code");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- claiming -------------------------------------------------------------

    /// <summary>
    /// THE POINT OF THE SLICE. A person the club recorded, booked into a class and given a plan,
    /// registers with their code and finds all of it already there.
    /// </summary>
    [Fact]
    public async Task Claiming_a_record_inherits_its_bookings_and_its_active_plan()
    {
        var (memberId, code, admin) = await RecordedMemberAsync("Karol Klubowicz");

        // A booking and a plan, written straight onto the accountless member — which is only
        // expressible at all because both foreign keys point at Members now.
        var classId = await SeedFutureClassAsync(admin);
        var planId = await SeedPlanAsync(admin, memberId);

        await using (var db = NewContext())
        {
            db.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                ClassId = classId,
                MemberId = memberId,
                Status = BookingStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        var email = NewEmail();
        var client = fixture.CreateClient();

        var registered = await client.PostAsJsonAsync("/api/auth/register", Registration(email, code));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        var session = (await registered.Content.ReadFromJsonAsync<CurrentUserBody>())!;

        // The SAME member, not a new one.
        Assert.Equal(memberId, session.MemberId);

        // THE CLUB KEEPS THE NAME. The form said "Imię Z Formularza"; the record says otherwise, and
        // the record wins — a claim must not become the one way to rename yourself.
        Assert.Equal("Karol Klubowicz", session.DisplayName);

        // Active from the first request (S-16). Claiming a record and registering fresh produce the
        // same account status now — the code proves the club knows this person, and it never had
        // anything to do with approval.
        Assert.Equal(nameof(AccountStatus.Active), session.Status);

        await using (var db = NewContext())
        {
            var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId);

            Assert.NotNull(member.UserId);
            Assert.NotNull(member.ClaimedAt);

            // Consumed. Nulling the code IS the single-use mechanism.
            Assert.Null(member.AccessCode);
            Assert.Null(member.AccessCodeExpiresAt);

            // The submitted contact details overwrote the club's blanks.
            Assert.Equal("601202303", member.PhoneNumber);
            Assert.Equal("Kraków", member.City);

            // EXACTLY ONE member for this account — the claim linked, it did not also create.
            Assert.Equal(1, await db.Members.CountAsync(m => m.UserId == member.UserId));
        }

        // The history is reachable through the new session once the account is approved.
        var approve = await admin.PostAsync($"{Members}/{memberId}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var member2 = await fixture.CreateAuthenticatedClientAsync(email);

        var bookings = await member2.GetFromJsonAsync<List<MyBookingBody>>("/api/bookings/mine");
        Assert.Contains(bookings!, b => b.ClassId == classId);

        var plan = await member2.GetFromJsonAsync<PlanBody>("/api/plans/mine");
        Assert.Equal(planId, plan!.Id);
    }

    [Fact]
    public async Task Registering_without_a_code_still_creates_a_fresh_record()
    {
        var email = NewEmail();
        var client = fixture.CreateClient();

        var registered = await client.PostAsJsonAsync("/api/auth/register", Registration(email));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        var session = (await registered.Content.ReadFromJsonAsync<CurrentUserBody>())!;

        Assert.NotNull(session.MemberId);

        // No record was claimed, so the form's name stands — the rule only inverts on a claim.
        Assert.Equal("Imię Z Formularza", session.DisplayName);
    }

    [Fact]
    public async Task A_code_is_single_use()
    {
        var (_, code, _) = await RecordedMemberAsync();

        var first = await fixture.CreateClient()
            .PostAsJsonAsync("/api/auth/register", Registration(NewEmail(), code));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await fixture.CreateClient()
            .PostAsJsonAsync("/api/auth/register", Registration(NewEmail(), code));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(
            "unknown_member_code",
            (await second.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    [Fact]
    public async Task A_revoked_code_is_refused()
    {
        var (memberId, code, admin) = await RecordedMemberAsync();
        await admin.DeleteAsync($"{Members}/{memberId}/access-code");

        var response = await fixture.CreateClient()
            .PostAsJsonAsync("/api/auth/register", Registration(NewEmail(), code));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// An expired code is refused, and the expiry is honoured from the stored column rather than
    /// recomputed — which is what lets the validity default change without re-dating live codes.
    /// </summary>
    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var (memberId, code, _) = await RecordedMemberAsync();

        await using (var db = NewContext())
        {
            var member = await db.Members.SingleAsync(m => m.Id == memberId);
            member.AccessCodeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var response = await fixture.CreateClient()
            .PostAsJsonAsync("/api/auth/register", Registration(NewEmail(), code));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "unknown_member_code",
            (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>
    /// A blocked member's outstanding code must not be a way back in. The block is the club's
    /// decision; claiming around it would quietly undo it.
    /// </summary>
    [Fact]
    public async Task A_blocked_members_code_is_refused()
    {
        var (memberId, code, admin) = await RecordedMemberAsync();
        await admin.PostAsync($"{Members}/{memberId}/block", content: null);

        var response = await fixture.CreateClient()
            .PostAsJsonAsync("/api/auth/register", Registration(NewEmail(), code));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_code_is_400_and_creates_nothing()
    {
        var email = NewEmail();

        var response = await fixture.CreateClient()
            .PostAsJsonAsync("/api/auth/register", Registration(email, "nie-jest-kodem"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_member_code",
            (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);

        // NOTHING WAS WRITTEN. The code is resolved before the account is created precisely so that a
        // failed attempt does not burn the address the person wanted to use.
        await using var db = NewContext();
        Assert.Equal(0, await db.Users.CountAsync(u => u.Email == email));
    }

    /// <summary>
    /// The code is normalised the way a person types it — lowercase, and with the dash the admin read
    /// out. Refusing our own display form would be perverse.
    /// </summary>
    [Fact]
    public async Task A_code_typed_in_lower_case_still_claims()
    {
        var (memberId, code, _) = await RecordedMemberAsync();

        var response = await fixture.CreateClient()
            .PostAsJsonAsync("/api/auth/register", Registration(NewEmail(), code.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = (await response.Content.ReadFromJsonAsync<CurrentUserBody>())!;
        Assert.Equal(memberId, session.MemberId);
    }

    /// <summary>
    /// TWO PEOPLE, ONE CODE. Exactly one may win, and the loser must be left able to register
    /// normally rather than with a half-made account. The unique index on Members.UserId is what
    /// settles it; this proves the compensation around it works.
    /// </summary>
    [Fact]
    public async Task Two_people_racing_one_code_produce_exactly_one_claim()
    {
        var (memberId, code, _) = await RecordedMemberAsync();

        var firstEmail = NewEmail();
        var secondEmail = NewEmail();

        var responses = await Task.WhenAll(
            fixture.CreateClient().PostAsJsonAsync("/api/auth/register", Registration(firstEmail, code)),
            fixture.CreateClient().PostAsJsonAsync("/api/auth/register", Registration(secondEmail, code)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        await using var db = NewContext();

        var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId);
        Assert.NotNull(member.UserId);
        Assert.Null(member.AccessCode);

        // The loser holds no account: the compensating delete removed it, so their address is free
        // and they can simply register again.
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == firstEmail || u.Email == secondEmail));
    }

    // --- seeding helpers ------------------------------------------------------

    private static int _slot;

    private async Task<Guid> SeedFutureClassAsync(HttpClient admin)
    {
        var typeResponse = await admin.PostAsJsonAsync(
            "/api/admin/class-types",
            new
            {
                name = $"Typ {Guid.NewGuid():N}",
                description = (string?)null,
                defaultDurationMinutes = 60,
                defaultCapacity = 10,
            });

        Assert.Equal(HttpStatusCode.OK, typeResponse.StatusCode);
        var typeId = (await typeResponse.Content.ReadFromJsonAsync<CreatedBody>())!.Id;

        var trainerId = await fixture.MemberIdOfAsync(
            await UserIdOfAsync(TestUsers.ActiveTrainerEmail));

        // Far in the future and stepped two hours apart, so this never overlaps another test's class
        // — two classes may not overlap anywhere in the club.
        var startsAt = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(500), TimeSpan.Zero)
            .AddHours(2 * Interlocked.Increment(ref _slot));

        var response = await admin.PostAsJsonAsync(
            "/api/admin/classes",
            new
            {
                classTypeId = typeId,
                startsAt,
                durationMinutes = 60,
                capacity = 10,
                instructorMemberId = trainerId,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreatedBody>())!.Id;
    }

    private async Task<Guid> SeedPlanAsync(HttpClient admin, Guid memberId)
    {
        var exercise = await admin.PostAsJsonAsync(
            "/api/admin/exercises", new { name = $"Ćwiczenie {Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.OK, exercise.StatusCode);
        var exerciseId = (await exercise.Content.ReadFromJsonAsync<CreatedBody>())!.Id;

        var response = await admin.PostAsJsonAsync(
            "/api/trainer/plans",
            new
            {
                name = "Plan sprzed konta",
                memberId,
                items = new[] { new { exerciseId, order = 1 } },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreatedBody>())!.Id;
    }

    private async Task<string> UserIdOfAsync(string email)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return (await userManager.FindByEmailAsync(email))!.Id;
    }
}
