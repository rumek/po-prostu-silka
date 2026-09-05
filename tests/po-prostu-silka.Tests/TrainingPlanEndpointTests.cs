using System.Net;
using System.Net.Http.Json;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Tests;

/// <summary>
/// Training-plan authoring (prd.md FR-015, FR-016) - the write half of S-11.
///
/// <para>
/// THE REASON THIS RUNS AGAINST A REAL ENGINE: the slice's central rule - a member has at most ONE
/// active plan - is enforced by a FILTERED unique index (<c>IX_TrainingPlans_Member_Active</c>,
/// <c>HasFilter("[Status] = 0")</c>) backing a rotated concurrency token. No in-memory provider
/// implements filtered indexes or optimistic concurrency the way SQL Server does, so
/// <see cref="Concurrent_assignments_leave_exactly_one_active_plan"/> is the only test that actually
/// pins the design - everything else would pass against a mock that had no rule at all.
/// </para>
///
/// <para>
/// The other half is the bounds. Every length and range the endpoint guards is pinned below at the
/// bound and one past it, because an unguarded bound reaches SQL Server as an unhandled 500 for what
/// is ordinary bad input - the single most repeated finding in this repo's review history.
/// </para>
///
/// <para>
/// Each test assigns to its OWN freshly created member. The one-active-plan rule is per member and
/// global to the table, so sharing a member between tests would make them collide with each other
/// rather than with the rule under test.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class TrainingPlanEndpointTests(IntegrationTestFixture fixture)
{
    /// <summary>Mirrors TrainingPlanItemView.</summary>
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

    /// <summary>Mirrors TrainingPlanDetail.</summary>
    private sealed record PlanBody(
        Guid Id,
        string Name,
        string MemberUserId,
        string MemberDisplayName,
        string AssignedByDisplayName,
        DateTimeOffset CreatedAt,
        IReadOnlyList<ItemBody> Items);

    /// <summary>Mirrors TrainingPlanSummary.</summary>
    private sealed record PlanRow(
        Guid Id,
        string Name,
        string MemberUserId,
        string MemberDisplayName,
        string AssignedByDisplayName,
        DateTimeOffset CreatedAt,
        int ItemCount);

    /// <summary>Mirrors AssignableMember.</summary>
    private sealed record MemberRow(string Id, string DisplayName);

    /// <summary>Mirrors ExerciseSummary, for the exercises these plans are built from.</summary>
    private sealed record ExerciseBody(Guid Id, string Name, bool IsActive);

    private sealed record FailureBody(string Reason);

    private const string Endpoint = "/api/trainer/plans";

    private const string Exercises = "/api/admin/exercises";

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static object Item(
        Guid exerciseId,
        int? sets = 3,
        string? reps = "8-12",
        decimal? weightKg = 60.5m,
        int? restSeconds = 90,
        string? note = null) =>
        new { exerciseId, sets, reps, weightKg, restSeconds, note };

    private static object Request(string name, string memberUserId, params object[] items) =>
        new { name, memberUserId, items };

    private async Task<Guid> CreateExerciseAsync(bool active = true)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsJsonAsync(Exercises, new { name = Unique("Przysiad") });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = (await response.Content.ReadFromJsonAsync<ExerciseBody>())!;

        if (!active)
        {
            var off = await admin.PostAsync($"{Exercises}/{created.Id}/deactivate", null);
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        }

        return created.Id;
    }

    private async Task<(HttpClient Trainer, string MemberId, Guid ExerciseId)> ArrangeAsync()
    {
        var trainer = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);
        return (trainer, await NewMemberIdAsync(), await CreateExerciseAsync());
    }

    /// <summary>Creates an approved member and returns their id, via the admin member list.</summary>
    private async Task<string> NewMemberIdAsync(AccountStatus status = AccountStatus.Active)
    {
        var email = $"plan-member-{Guid.NewGuid():N}@test.local";
        await fixture.CreateUserAsync(email, status, ApplicationRoles.User);

        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        var all = await admin.GetFromJsonAsync<List<AdminMemberRow>>("/api/admin/members");

        return all!.Single(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)).Id;
    }

    private sealed record AdminMemberRow(string Id, string Email, string DisplayName, string Status);

    private async Task<PlanBody> AssignAsync(
        HttpClient trainer, string memberId, params object[] items)
    {
        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request(Unique("Masa"), memberId, items));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlanBody>())!;
    }

    // --- who may reach the group ----------------------------------------------

    public static TheoryData<string, string> EveryRoute => new()
    {
        { "GET", Endpoint },
        { "GET", $"{Endpoint}/members" },
        { "GET", $"{Endpoint}/{Guid.Empty}" },
        { "POST", Endpoint },
        { "PUT", $"{Endpoint}/{Guid.Empty}" },
    };

    /// <summary>
    /// The policy is applied at the GROUP, so this asserts the property that makes that worth doing:
    /// no route on the surface is reachable unauthenticated, including ones added later.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryRoute))]
    public async Task Anonymous_is_401_on_every_route(string method, string url)
    {
        var client = fixture.CreateClient();

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = JsonContent.Create(Request("x", "y")),
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// An approved member who is neither trainer nor admin cannot author plans - they only ever read
    /// their own, through MyPlanEndpoints.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryRoute))]
    public async Task Plain_member_is_403_on_every_route(string method, string url)
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = JsonContent.Create(Request("x", "y")),
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The union in the policy is real in both directions: FR-015 gave authoring to the admin and
    /// S-11 widened it to trainers rather than moving it, so an admin who does not teach keeps it.
    /// </summary>
    [Fact]
    public async Task Admin_may_author_plans_too()
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);
        var memberId = await NewMemberIdAsync();
        var exerciseId = await CreateExerciseAsync();

        var plan = await AssignAsync(admin, memberId, Item(exerciseId));

        Assert.Equal($"Test {ApplicationRoles.Admin}", plan.AssignedByDisplayName);
    }

    // --- assigning ------------------------------------------------------------

    [Fact]
    public async Task Trainer_assigns_a_plan_and_gets_every_field_back()
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var plan = await AssignAsync(
            trainer, memberId, Item(exerciseId, 4, "6-8", 82.5m, 120, "tempo 3-1-1"));

        var item = Assert.Single(plan.Items);
        Assert.Equal(memberId, plan.MemberUserId);
        Assert.Equal("Test Active Trainer", plan.AssignedByDisplayName);
        Assert.Equal(0, item.Position);
        Assert.Equal(4, item.Sets);
        Assert.Equal("6-8", item.Reps);
        Assert.Equal(82.5m, item.WeightKg);
        Assert.Equal(120, item.RestSeconds);
        Assert.Equal("tempo 3-1-1", item.Note);
    }

    /// <summary>
    /// The array's order IS the plan's order. There is no position field on the wire, so this is the
    /// only thing that decides what the member sees first.
    /// </summary>
    [Fact]
    public async Task Item_order_follows_the_request_array()
    {
        var (trainer, memberId, first) = await ArrangeAsync();
        var second = await CreateExerciseAsync();
        var third = await CreateExerciseAsync();

        var plan = await AssignAsync(
            trainer, memberId, Item(third), Item(first), Item(second));

        Assert.Equal([0, 1, 2], plan.Items.Select(x => x.Position));
        Assert.Equal([third, first, second], plan.Items.Select(x => x.ExerciseId));
    }

    /// <summary>Every prescription field is optional - a note alone is a valid prescription.</summary>
    [Fact]
    public async Task An_item_may_carry_nothing_but_an_exercise()
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var plan = await AssignAsync(
            trainer, memberId, Item(exerciseId, null, null, null, null, null));

        var item = Assert.Single(plan.Items);
        Assert.Null(item.Sets);
        Assert.Null(item.Reps);
        Assert.Null(item.WeightKg);
        Assert.Null(item.RestSeconds);
        Assert.Null(item.Note);
    }

    /// <summary>Whitespace and absent must not become two representations of the same thing.</summary>
    [Fact]
    public async Task Whitespace_only_text_is_stored_as_null()
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var plan = await AssignAsync(
            trainer, memberId, Item(exerciseId, 3, "   ", 60m, 60, "  "));

        var item = Assert.Single(plan.Items);
        Assert.Null(item.Reps);
        Assert.Null(item.Note);
    }

    // --- the one-active-plan rule ---------------------------------------------

    /// <summary>
    /// FR-016 in one test: a new assignment REPLACES the old, and the old is archived rather than
    /// deleted - it is simply no longer the plan anything reads.
    /// </summary>
    [Fact]
    public async Task Assigning_again_replaces_the_previous_plan()
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var first = await AssignAsync(trainer, memberId, Item(exerciseId));
        var second = await AssignAsync(trainer, memberId, Item(exerciseId));

        var active = await trainer.GetFromJsonAsync<List<PlanRow>>(Endpoint);
        var mine = active!.Where(x => x.MemberUserId == memberId).ToList();

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(second.Id, Assert.Single(mine).Id);

        // The archived plan is still addressable by id - the row survives, which is what makes a
        // history screen a later feature rather than a data-recovery exercise.
        var archived = await trainer.GetAsync($"{Endpoint}/{first.Id}");
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
    }

    /// <summary>
    /// THE TEST THIS WHOLE SUITE EXISTS FOR. N trainers assign to the same member at the same
    /// instant; exactly one plan may survive as active.
    ///
    /// <para>
    /// Each racer gets its OWN HttpClient, hence its own cookie, its own request scope and its own
    /// DbContext - the same construction BookingEndpointTests uses. Sharing a client would serialise
    /// the requests through one connection and the test would pass against no protection at all.
    /// </para>
    ///
    /// <para>
    /// It exercises both guards at once. Racers that find no active plan collide on
    /// IX_TrainingPlans_Member_Active (the stamp cannot help - there is no row to rotate); racers
    /// that find one collide on the stamp. Comment out the rotation in CreateAsync and this test is
    /// what fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Concurrent_assignments_leave_exactly_one_active_plan()
    {
        const int racers = 6;

        var memberId = await NewMemberIdAsync();
        var exerciseId = await CreateExerciseAsync();

        var clients = new List<HttpClient>();
        for (var i = 0; i < racers; i++)
        {
            clients.Add(await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail));
        }

        var responses = await Task.WhenAll(clients.Select(client =>
            client.PostAsJsonAsync(Endpoint, Request(Unique("Wyścig"), memberId, Item(exerciseId)))));

        // Every racer either wins or is told to try again. A 500 here means an unhandled
        // DbUpdateException escaped, which is the exact failure TrySaveAsync exists to prevent.
        Assert.All(responses, response => Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"unexpected {(int)response.StatusCode} from a racing assignment"));

        var reader = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);
        var active = await reader.GetFromJsonAsync<List<PlanRow>>(Endpoint);

        Assert.Single(active!, x => x.MemberUserId == memberId);
    }

    // --- editing --------------------------------------------------------------

    [Fact]
    public async Task Editing_replaces_the_name_and_the_whole_item_list()
    {
        var (trainer, memberId, first) = await ArrangeAsync();
        var second = await CreateExerciseAsync();

        var plan = await AssignAsync(trainer, memberId, Item(first));

        var response = await trainer.PutAsJsonAsync(
            $"{Endpoint}/{plan.Id}", Request("Siła - zima", memberId, Item(second, 5, "5", 100m, 180)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<PlanBody>())!;
        var item = Assert.Single(updated.Items);

        Assert.Equal("Siła - zima", updated.Name);
        Assert.Equal(second, item.ExerciseId);
        Assert.Equal(plan.Id, updated.Id);
    }

    /// <summary>
    /// A stale tab must not be able to move a plan to a different member. Ignoring the field would
    /// let the write succeed and tell the client nothing.
    /// </summary>
    [Fact]
    public async Task Editing_with_a_different_member_is_409()
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();
        var someoneElse = await NewMemberIdAsync();

        var plan = await AssignAsync(trainer, memberId, Item(exerciseId));

        var response = await trainer.PutAsJsonAsync(
            $"{Endpoint}/{plan.Id}", Request("x", someoneElse, Item(exerciseId)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("member_changed", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>An archived plan is not editable - nothing addresses it, so this is a stale request.</summary>
    [Fact]
    public async Task Editing_an_archived_plan_is_404()
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var first = await AssignAsync(trainer, memberId, Item(exerciseId));
        await AssignAsync(trainer, memberId, Item(exerciseId));

        var response = await trainer.PutAsJsonAsync(
            $"{Endpoint}/{first.Id}", Request("x", memberId, Item(exerciseId)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Editing_a_plan_that_does_not_exist_is_404()
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var response = await trainer.PutAsJsonAsync(
            $"{Endpoint}/{Guid.NewGuid()}", Request("x", memberId, Item(exerciseId)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- the member being assigned to -----------------------------------------

    [Fact]
    public async Task Assigning_to_an_account_that_does_not_exist_is_409()
    {
        var (trainer, _, exerciseId) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request("x", Guid.NewGuid().ToString(), Item(exerciseId)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("member_not_found", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    [Theory]
    [InlineData(AccountStatus.Pending)]
    [InlineData(AccountStatus.Blocked)]
    public async Task Assigning_to_an_unapproved_account_is_409(AccountStatus status)
    {
        var (trainer, _, exerciseId) = await ArrangeAsync();
        var memberId = await NewMemberIdAsync(status);

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request("x", memberId, Item(exerciseId)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("member_not_active", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>The picker offers approved accounts only - a plan cannot be assigned to the others.</summary>
    [Fact]
    public async Task The_member_picker_offers_active_accounts_only()
    {
        var trainer = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);
        var pendingId = await NewMemberIdAsync(AccountStatus.Pending);
        var activeId = await NewMemberIdAsync();

        var members = await trainer.GetFromJsonAsync<List<MemberRow>>($"{Endpoint}/members");

        Assert.Contains(members!, x => x.Id == activeId);
        Assert.DoesNotContain(members!, x => x.Id == pendingId);
    }

    /// <summary>
    /// The literal segment is registered before the {id:guid} route. The guid constraint would save
    /// it anyway, but the ordering is the contract and this is what notices if it is reversed.
    /// </summary>
    [Fact]
    public async Task The_members_route_is_not_swallowed_by_the_id_route()
    {
        var trainer = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);

        var response = await trainer.GetAsync($"{Endpoint}/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<MemberRow>>());
    }

    // --- the exercises a plan references --------------------------------------

    [Fact]
    public async Task Referencing_an_exercise_that_does_not_exist_is_400()
    {
        var (trainer, memberId, _) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request("x", memberId, Item(Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unknown_exercise", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>
    /// A retired exercise may not be ADDED to a plan. Note the deliberate asymmetry with the read
    /// path, pinned in MyPlanEndpointTests: one already in a plan stays visible to the member.
    /// </summary>
    [Fact]
    public async Task Referencing_a_deactivated_exercise_is_400()
    {
        var (trainer, memberId, _) = await ArrangeAsync();
        var retired = await CreateExerciseAsync(active: false);

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request("x", memberId, Item(retired)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("inactive_exercise", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>A double-click in the picker must not put the same exercise in twice.</summary>
    [Fact]
    public async Task The_same_exercise_twice_in_one_plan_is_400()
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request("x", memberId, Item(exerciseId), Item(exerciseId)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("duplicate_exercise", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    // --- bounds ---------------------------------------------------------------

    [Fact]
    public async Task A_plan_with_no_items_is_400()
    {
        var (trainer, memberId, _) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(Endpoint, Request("x", memberId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no_items", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_plan_with_no_name_is_400(string name)
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request(name, memberId, Item(exerciseId)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("missing_field", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>
    /// At the bound and one past it. The 121st character is the one that would otherwise reach a
    /// nvarchar(120) column and come back as a 500.
    /// </summary>
    [Theory]
    [InlineData(120, HttpStatusCode.OK)]
    [InlineData(121, HttpStatusCode.BadRequest)]
    public async Task The_name_bound_is_enforced(int length, HttpStatusCode expected)
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request(new string('a', length), memberId, Item(exerciseId)));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(50, HttpStatusCode.OK)]
    [InlineData(51, HttpStatusCode.BadRequest)]
    public async Task The_reps_bound_is_enforced(int length, HttpStatusCode expected)
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request("x", memberId, Item(exerciseId, reps: new string('a', length))));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(500, HttpStatusCode.OK)]
    [InlineData(501, HttpStatusCode.BadRequest)]
    public async Task The_note_bound_is_enforced(int length, HttpStatusCode expected)
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request("x", memberId, Item(exerciseId, note: new string('a', length))));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(0, "invalid_sets")]
    [InlineData(21, "invalid_sets")]
    public async Task The_sets_range_is_enforced(int sets, string reason)
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request("x", memberId, Item(exerciseId, sets: sets)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(reason, (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    [Theory]
    [InlineData(-1, "invalid_rest")]
    [InlineData(3601, "invalid_rest")]
    public async Task The_rest_range_is_enforced(int rest, string reason)
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request("x", memberId, Item(exerciseId, restSeconds: rest)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(reason, (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>
    /// 999.99 is what decimal(5,2) holds. One step past it is a truncation error at the database if
    /// the endpoint does not refuse it first - the whole reason this bound exists in three places.
    /// </summary>
    [Theory]
    [InlineData(999.99, HttpStatusCode.OK)]
    [InlineData(1000.00, HttpStatusCode.BadRequest)]
    [InlineData(-0.01, HttpStatusCode.BadRequest)]
    public async Task The_weight_range_is_enforced(decimal weight, HttpStatusCode expected)
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();

        var response = await trainer.PostAsJsonAsync(
            Endpoint, Request("x", memberId, Item(exerciseId, weightKg: weight)));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_plan_with_more_than_fifty_items_is_400()
    {
        var (trainer, memberId, _) = await ArrangeAsync();

        // Distinct ids, so this fails on the count rather than on the duplicate rule. They do not
        // need to exist: the count is checked before the database is consulted.
        var items = Enumerable.Range(0, 51).Select(_ => Item(Guid.NewGuid())).ToArray();

        var response = await trainer.PostAsJsonAsync(Endpoint, Request("x", memberId, items));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("too_many_items", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    // --- reading --------------------------------------------------------------

    [Fact]
    public async Task The_list_counts_items_without_returning_them()
    {
        var (trainer, memberId, first) = await ArrangeAsync();
        var second = await CreateExerciseAsync();

        await AssignAsync(trainer, memberId, Item(first), Item(second));

        var rows = await trainer.GetFromJsonAsync<List<PlanRow>>(Endpoint);

        Assert.Equal(2, rows!.Single(x => x.MemberUserId == memberId).ItemCount);
    }

    [Fact]
    public async Task Reading_a_plan_that_does_not_exist_is_404()
    {
        var trainer = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveTrainerEmail);

        var response = await trainer.GetAsync($"{Endpoint}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task There_is_no_delete_endpoint()
    {
        var (trainer, memberId, exerciseId) = await ArrangeAsync();
        var plan = await AssignAsync(trainer, memberId, Item(exerciseId));

        var response = await trainer.DeleteAsync($"{Endpoint}/{plan.Id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
