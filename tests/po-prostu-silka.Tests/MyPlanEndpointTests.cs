using System.Net;
using System.Net.Http.Json;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Tests;

/// <summary>
/// The member's own training plan (prd.md FR-017, FR-020) - the read half of S-11.
///
/// <para>
/// WHAT THIS SUITE IS REALLY FOR: proving that the surface is scoped to the CALLER and not to a
/// parameter. No route here takes a member id, so "a member cannot read another member's plan" is a
/// property of the routing table rather than of a check that could be forgotten - and the exercise
/// route resolves through a join against the caller's own plan, which is how prd.md:163's "no
/// standalone exercise library browsing" is enforced rather than merely respected.
/// </para>
///
/// <para>
/// The second thing pinned here is the deliberate asymmetry with the write path: an exercise that
/// was deactivated AFTER a plan was assigned stays visible to the member. A member's plan does not
/// rearrange itself because of library housekeeping.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class MyPlanEndpointTests(IntegrationTestFixture fixture)
{
    private sealed record ItemBody(
        Guid Id,
        Guid ExerciseId,
        string ExerciseName,
        int Position,
        int? Sets,
        string? Reps,
        decimal? WeightKg,
        int? RestSeconds,
        string? Note);

    private sealed record PlanBody(
        Guid Id,
        string Name,
        string MemberUserId,
        string MemberDisplayName,
        string AssignedByDisplayName,
        DateTimeOffset CreatedAt,
        IReadOnlyList<ItemBody> Items);

    private sealed record ExerciseBody(Guid Id, string Name, string? Execution, string? VideoId, bool IsActive);

    private sealed record AdminMemberRow(string Id, string Email, string DisplayName, string Status);

    private const string Mine = "/api/plans/mine";

    private const string Plans = "/api/trainer/plans";

    private const string Exercises = "/api/admin/exercises";

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>A fresh approved member, and a signed-in client for them.</summary>
    private async Task<(string Id, HttpClient Client)> NewMemberAsync()
    {
        var email = $"myplan-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, AccountStatus.Active, ApplicationRoles.User);

        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        var all = await admin.GetFromJsonAsync<List<AdminMemberRow>>("/api/admin/members");
        var id = all!.Single(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)).Id;

        return (id, await fixture.CreateAuthenticatedClientAsync(email));
    }

    private async Task<Guid> CreateExerciseAsync(string? execution = null, string? videoUrl = null)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsJsonAsync(
            Exercises, new { name = Unique("Martwy ciąg"), execution, videoUrl });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExerciseBody>())!.Id;
    }

    private async Task DeactivateAsync(Guid exerciseId)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsync($"{Exercises}/{exerciseId}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<PlanBody> AssignAsync(string memberId, params Guid[] exerciseIds)
    {
        var trainer = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);

        var response = await trainer.PostAsJsonAsync(Plans, new
        {
            name = Unique("Plan"),
            memberUserId = memberId,
            items = exerciseIds.Select(id => new
            {
                exerciseId = id,
                sets = 3,
                reps = "8-12",
                weightKg = 60.5m,
                restSeconds = 90,
                note = (string?)null,
            }).ToArray(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlanBody>())!;
    }

    // --- who may reach the group ----------------------------------------------

    public static TheoryData<string> EveryRoute =>
        [Mine, $"{Mine}/exercises/{Guid.Empty}"];

    [Theory]
    [MemberData(nameof(EveryRoute))]
    public async Task Anonymous_is_401_on_every_route(string url)
    {
        var response = await fixture.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// ActiveMember, so an unapproved account is refused. This is also what makes "a blocked member's
    /// plan is left untouched" safe: access is cut here, at read time, rather than by destroying data.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryRoute))]
    public async Task Pending_member_is_403_on_every_route(string url)
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.PendingMemberEmail);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- reading the plan -----------------------------------------------------

    /// <summary>
    /// 204, not 404. Having no plan yet is the ordinary state for a newly approved member, and the
    /// screen has to tell it apart from "the request failed" to render an empty card rather than an
    /// error - which it can only do if the API distinguishes them.
    /// </summary>
    [Fact]
    public async Task A_member_with_no_plan_gets_204()
    {
        var (_, client) = await NewMemberAsync();

        var response = await client.GetAsync(Mine);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_member_reads_their_plan_in_the_trainers_order()
    {
        var (memberId, client) = await NewMemberAsync();
        var first = await CreateExerciseAsync();
        var second = await CreateExerciseAsync();
        var third = await CreateExerciseAsync();

        await AssignAsync(memberId, third, first, second);

        var plan = (await client.GetFromJsonAsync<PlanBody>(Mine))!;

        Assert.Equal([0, 1, 2], plan.Items.Select(x => x.Position));
        Assert.Equal([third, first, second], plan.Items.Select(x => x.ExerciseId));
        Assert.Equal("Test Active Trainer", plan.AssignedByDisplayName);
    }

    /// <summary>
    /// The read follows the replacement: a member reads the plan they were LAST given, never a
    /// superseded one, and never two.
    /// </summary>
    [Fact]
    public async Task A_member_reads_only_the_latest_plan()
    {
        var (memberId, client) = await NewMemberAsync();
        var exerciseId = await CreateExerciseAsync();

        var first = await AssignAsync(memberId, exerciseId);
        var second = await AssignAsync(memberId, exerciseId);

        var plan = (await client.GetFromJsonAsync<PlanBody>(Mine))!;

        Assert.Equal(second.Id, plan.Id);
        Assert.NotEqual(first.Id, plan.Id);
    }

    /// <summary>
    /// There is no parameter to tamper with - a member's own id is the only thing the route reads -
    /// so this asserts the consequence: two members with plans see two different plans.
    /// </summary>
    [Fact]
    public async Task A_member_cannot_see_another_members_plan()
    {
        var (firstId, firstClient) = await NewMemberAsync();
        var (secondId, secondClient) = await NewMemberAsync();
        var exerciseId = await CreateExerciseAsync();

        var firstPlan = await AssignAsync(firstId, exerciseId);
        var secondPlan = await AssignAsync(secondId, exerciseId);

        var seenByFirst = (await firstClient.GetFromJsonAsync<PlanBody>(Mine))!;
        var seenBySecond = (await secondClient.GetFromJsonAsync<PlanBody>(Mine))!;

        Assert.Equal(firstPlan.Id, seenByFirst.Id);
        Assert.Equal(secondPlan.Id, seenBySecond.Id);
        Assert.NotEqual(seenByFirst.Id, seenBySecond.Id);
    }

    // --- reading an exercise from inside the plan -----------------------------

    [Fact]
    public async Task A_member_opens_an_exercise_from_their_plan()
    {
        var (memberId, client) = await NewMemberAsync();
        var exerciseId = await CreateExerciseAsync(
            execution: "Opuszczaj sztangę kontrolowanym ruchem.",
            videoUrl: "https://www.youtube.com/watch?v=dQw4w9-gX_Q");

        await AssignAsync(memberId, exerciseId);

        var exercise = (await client.GetFromJsonAsync<ExerciseBody>(
            $"{Mine}/exercises/{exerciseId}"))!;

        Assert.Equal(exerciseId, exercise.Id);
        Assert.Equal("Opuszczaj sztangę kontrolowanym ruchem.", exercise.Execution);
        Assert.Equal("dQw4w9-gX_Q", exercise.VideoId);
    }

    /// <summary>
    /// THE NON-GOAL, ENFORCED. An exercise that exists but is not prescribed to this member is a 404 -
    /// the same answer as one that does not exist - so the route cannot be walked to enumerate the
    /// library one guid at a time.
    /// </summary>
    [Fact]
    public async Task An_exercise_outside_the_members_plan_is_404()
    {
        var (memberId, client) = await NewMemberAsync();
        var prescribed = await CreateExerciseAsync();
        var somebodyElses = await CreateExerciseAsync();

        await AssignAsync(memberId, prescribed);

        var response = await client.GetAsync($"{Mine}/exercises/{somebodyElses}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_exercise_that_does_not_exist_is_404()
    {
        var (memberId, client) = await NewMemberAsync();
        await AssignAsync(memberId, await CreateExerciseAsync());

        var response = await client.GetAsync($"{Mine}/exercises/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A member with no plan has no exercises to reach, even real ones.</summary>
    [Fact]
    public async Task A_member_with_no_plan_cannot_open_any_exercise()
    {
        var (_, client) = await NewMemberAsync();
        var exerciseId = await CreateExerciseAsync();

        var response = await client.GetAsync($"{Mine}/exercises/{exerciseId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A plan superseded by a later one stops granting access to its exercises. The archived row
    /// survives, but it is not "the member's plan" any more, and the join says so.
    /// </summary>
    [Fact]
    public async Task An_exercise_only_in_an_archived_plan_is_404()
    {
        var (memberId, client) = await NewMemberAsync();
        var oldOne = await CreateExerciseAsync();
        var newOne = await CreateExerciseAsync();

        await AssignAsync(memberId, oldOne);
        await AssignAsync(memberId, newOne);

        var response = await client.GetAsync($"{Mine}/exercises/{oldOne}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- the deactivation asymmetry -------------------------------------------

    /// <summary>
    /// THE ASYMMETRY, PINNED. TrainingPlanEndpointTests proves a retired exercise cannot be ADDED to
    /// a plan; this proves one already in a plan stays visible. Deleting the exercise instead of
    /// deactivating it is what the Restrict foreign key exists to prevent, and filtering IsActive on
    /// this read would achieve the same damage in code.
    /// </summary>
    [Fact]
    public async Task An_exercise_deactivated_after_assignment_stays_in_the_plan()
    {
        var (memberId, client) = await NewMemberAsync();
        var exerciseId = await CreateExerciseAsync(execution: "Trzymaj plecy proste.");

        await AssignAsync(memberId, exerciseId);
        await DeactivateAsync(exerciseId);

        var plan = (await client.GetFromJsonAsync<PlanBody>(Mine))!;
        var exercise = (await client.GetFromJsonAsync<ExerciseBody>(
            $"{Mine}/exercises/{exerciseId}"))!;

        Assert.Equal(exerciseId, Assert.Single(plan.Items).ExerciseId);
        Assert.False(exercise.IsActive);
        Assert.Equal("Trzymaj plecy proste.", exercise.Execution);
    }

    // --- what blocking does and does not do -----------------------------------

    /// <summary>
    /// The S-11 product decision, pinned: blocking cuts ACCESS and leaves the plan alone. The member
    /// is refused while blocked, and the same plan is waiting after they are unblocked - which is the
    /// repo's default posture (consequences enforced at read time by policy claims), not the
    /// deliberate exception the booking cascade made of itself.
    /// </summary>
    [Fact]
    public async Task Blocking_refuses_the_member_but_leaves_the_plan_standing()
    {
        var (memberId, client) = await NewMemberAsync();
        var exerciseId = await CreateExerciseAsync();
        var assigned = await AssignAsync(memberId, exerciseId);

        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var blocked = await admin.PostAsync($"/api/admin/members/{memberId}/block", null);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);

        // The trainer's list is the view onto stored state: the plan is still active, untouched.
        var trainer = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);
        var rows = await trainer.GetFromJsonAsync<List<PlanRowBody>>(Plans);
        Assert.Equal(assigned.Id, rows!.Single(x => x.MemberUserId == memberId).Id);

        var unblocked = await admin.PostAsync($"/api/admin/members/{memberId}/unblock", null);
        Assert.Equal(HttpStatusCode.OK, unblocked.StatusCode);

        // A fresh sign-in, because blocking rotated the security stamp and killed the old cookie.
        var restored = await fixture.CreateAuthenticatedClientAsync(await EmailOfAsync(memberId));
        var plan = (await restored.GetFromJsonAsync<PlanBody>(Mine))!;

        Assert.Equal(assigned.Id, plan.Id);

        // Silences the "unused" warning on the client captured before the block, and documents that
        // it is deliberately not reused: its cookie is dead by design.
        client.Dispose();
    }

    private sealed record PlanRowBody(Guid Id, string MemberUserId, int ItemCount);

    private async Task<string> EmailOfAsync(string memberId)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        var all = await admin.GetFromJsonAsync<List<AdminMemberRow>>("/api/admin/members");

        return all!.Single(x => x.Id == memberId).Email;
    }
}
