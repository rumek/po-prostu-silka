using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// Every write path populates BOTH the account key and the member key (S-14, the parallel-write
/// phase).
///
/// <para>
/// WHY THIS IS WORTH ITS OWN FILE. The parallel write is invisible: nothing reads the new columns
/// yet, so a path that forgot one would pass every other test in the suite and every manual check —
/// right up until the reads move over and that row turns out to belong to nobody. These assertions
/// are the only thing standing between "we wrote it everywhere" and "we thought we did".
/// </para>
///
/// <para>
/// DELETE THIS FILE WHEN THE OLD COLUMNS GO. Once the member key is the only one, it is covered by
/// the ordinary endpoint tests and this becomes a test of nothing.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class DualWriteTests(IntegrationTestFixture fixture)
{
    private sealed record ClassTypeBody(Guid Id, string Name);

    private sealed record ClassBody(Guid Id, DateTimeOffset StartsAt);

    private sealed record PlanBody(Guid Id);

    private sealed record ExerciseBody(Guid Id);

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    private async Task<string> UserIdOfAsync(string email)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return (await userManager.FindByEmailAsync(email))!.Id;
    }

    private async Task<string> NewAccountAsync(string role, AccountStatus status = AccountStatus.Active)
    {
        var email = $"dual-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, status, role);

        return email;
    }

    /// <summary>
    /// A slot no other class can overlap.
    ///
    /// <para>
    /// Two classes may not overlap in time ANYWHERE in the club (prd-v2), so a random slot collides
    /// with itself as soon as two tests run together — which is what a 409 here means, not a bug in
    /// the dual write. Far in the future to clear the fixtures other test classes create, and stepped
    /// two hours at a time so consecutive 60-minute classes cannot touch.
    /// </para>
    /// </summary>
    private static int _slot;

    private static DateTimeOffset NextSlot() =>
        new DateTimeOffset(DateTime.UtcNow.Date.AddDays(400), TimeSpan.Zero)
            .AddHours(2 * Interlocked.Increment(ref _slot));

    /// <summary>An active type and an active trainer — what creating a class needs.</summary>
    private async Task<(HttpClient Admin, Guid TypeId, string TrainerUserId)> ArrangeAsync()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

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
        var type = (await typeResponse.Content.ReadFromJsonAsync<ClassTypeBody>())!;

        var trainerEmail = await NewAccountAsync(ApplicationRoles.Trainer);
        var trainerUserId = await UserIdOfAsync(trainerEmail);

        var granted = await admin.PostAsync(
            $"/api/admin/members/{await fixture.MemberIdOfAsync(trainerUserId)}/roles/trainer",
            content: null);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        return (admin, type.Id, trainerUserId);
    }

    private async Task<Guid> CreateClassAsync(HttpClient admin, Guid typeId, string trainerUserId)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/admin/classes",
            new
            {
                classTypeId = typeId,
                startsAt = NextSlot(),
                durationMinutes = 60,
                capacity = 10,
                instructorMemberId = await fixture.MemberIdOfAsync(trainerUserId),
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ClassBody>())!.Id;
    }

    [Fact]
    public async Task Creating_a_class_writes_both_instructor_keys()
    {
        var (admin, typeId, trainerUserId) = await ArrangeAsync();

        var classId = await CreateClassAsync(admin, typeId, trainerUserId);

        await using var db = NewContext();
        var entity = await db.Classes.AsNoTracking().SingleAsync(c => c.Id == classId);

        Assert.Equal(trainerUserId, entity.InstructorUserId);
        Assert.Equal(await fixture.MemberIdOfAsync(trainerUserId), entity.InstructorMemberId);
    }

    [Fact]
    public async Task Editing_a_class_writes_both_instructor_keys()
    {
        var (admin, typeId, trainerUserId) = await ArrangeAsync();
        var classId = await CreateClassAsync(admin, typeId, trainerUserId);

        // A second trainer, so the reassignment is a real change rather than a no-op.
        var replacementEmail = await NewAccountAsync(ApplicationRoles.Trainer);
        var replacementUserId = await UserIdOfAsync(replacementEmail);
        await admin.PostAsync(
            $"/api/admin/members/{await fixture.MemberIdOfAsync(replacementUserId)}/roles/trainer",
            content: null);

        var response = await admin.PutAsJsonAsync(
            $"/api/admin/classes/{classId}",
            new
            {
                classTypeId = typeId,
                startsAt = NextSlot(),
                durationMinutes = 60,
                capacity = 10,
                instructorMemberId = await fixture.MemberIdOfAsync(replacementUserId),
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = NewContext();
        var entity = await db.Classes.AsNoTracking().SingleAsync(c => c.Id == classId);

        Assert.Equal(replacementUserId, entity.InstructorUserId);
        Assert.Equal(await fixture.MemberIdOfAsync(replacementUserId), entity.InstructorMemberId);
    }

    [Fact]
    public async Task Booking_a_class_writes_both_member_keys()
    {
        var (admin, typeId, trainerUserId) = await ArrangeAsync();
        var classId = await CreateClassAsync(admin, typeId, trainerUserId);

        var bookerEmail = await NewAccountAsync(ApplicationRoles.User);
        var bookerUserId = await UserIdOfAsync(bookerEmail);
        var booker = await fixture.CreateAuthenticatedClientAsync(bookerEmail);

        var booked = await booker.PostAsync($"/api/classes/{classId}/bookings", content: null);
        Assert.Equal(HttpStatusCode.OK, booked.StatusCode);

        await using var db = NewContext();
        var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.ClassId == classId);

        Assert.Equal(bookerUserId, booking.MemberUserId);
        Assert.Equal(await fixture.MemberIdOfAsync(bookerUserId), booking.MemberId);
    }

    /// <summary>
    /// The plan carries TWO member keys — the assignee and the author — and they come from different
    /// places: the assignee from the request, the author from the cookie. A dual-write that populated
    /// one and not the other is exactly the kind of half-done that stays invisible.
    /// </summary>
    [Fact]
    public async Task Assigning_a_plan_writes_both_member_keys_and_both_author_keys()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        var adminUserId = await UserIdOfAsync(TestUsers.ActiveAdminEmail);

        var exercise = await admin.PostAsJsonAsync(
            "/api/admin/exercises",
            new { name = $"Ćwiczenie {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.OK, exercise.StatusCode);
        var exerciseId = (await exercise.Content.ReadFromJsonAsync<ExerciseBody>())!.Id;

        var memberEmail = await NewAccountAsync(ApplicationRoles.User);
        var memberUserId = await UserIdOfAsync(memberEmail);

        var response = await admin.PostAsJsonAsync(
            "/api/trainer/plans",
            new
            {
                name = "Masa",
                memberId = await fixture.MemberIdOfAsync(memberUserId),
                items = new[] { new { exerciseId, order = 1 } },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = (await response.Content.ReadFromJsonAsync<PlanBody>())!;

        await using var db = NewContext();
        var entity = await db.TrainingPlans.AsNoTracking().SingleAsync(p => p.Id == plan.Id);

        Assert.Equal(memberUserId, entity.MemberUserId);
        Assert.Equal(await fixture.MemberIdOfAsync(memberUserId), entity.MemberId);

        Assert.Equal(adminUserId, entity.AssignedByUserId);
        Assert.Equal(await fixture.MemberIdOfAsync(adminUserId), entity.AssignedByMemberId);
    }

    /// <summary>
    /// The AddMemberForeignKeys backfill. Every row that predates the parallel write has to carry the
    /// new key too, or the reads move onto a column that is null for the club's entire history.
    /// </summary>
    [Fact]
    public async Task No_row_anywhere_is_left_without_its_member_key()
    {
        await using var db = NewContext();

        Assert.Equal(0, await db.Bookings.CountAsync(b => b.MemberId == null));
        Assert.Equal(0, await db.TrainingPlans.CountAsync(p => p.MemberId == null));
        Assert.Equal(0, await db.TrainingPlans.CountAsync(p => p.AssignedByMemberId == null));
        Assert.Equal(0, await db.Classes.CountAsync(c => c.InstructorMemberId == null));
    }
}
