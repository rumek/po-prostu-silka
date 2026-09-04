using System.Net;
using System.Net.Http.Json;

namespace po_prostu_silka.Tests;

/// <summary>
/// The admin's exercise library (prd.md FR-018, FR-019).
///
/// <para>
/// THE REASON THIS RUNS AGAINST A REAL ENGINE: the slice's central rule — a name is unique among
/// ACTIVE exercises only — is enforced by a FILTERED unique index (<c>IX_Exercises_Name_Active</c>,
/// <c>HasFilter("[IsActive] = 1")</c>). No in-memory provider implements filtered indexes, and no
/// mocked frontend spec can reach the behaviour at all. The deactivate-then-reuse-then-reactivate
/// cycle below is the one test that actually pins the design.
/// </para>
///
/// <para>
/// The other half of what these tests exist for is the length bounds. F2 of the class-type
/// implementation review was a 201-character name reaching SQL Server as an unhandled 500 because
/// the column had a limit and the endpoint did not. This entity has EIGHT such columns, so every one
/// of them is pinned below, at the bound and one past it.
/// </para>
///
/// <para>
/// Every test creates its own exercises with a GUID-suffixed name. The uniqueness rule is global to
/// the table, so fixed names would make these tests collide with each other rather than with the
/// rule under test.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class ExerciseEndpointTests(IntegrationTestFixture fixture)
{
    /// <summary>Mirrors ExerciseSummary.</summary>
    private sealed record ExerciseBody(
        Guid Id,
        string Name,
        string? Description,
        string? MuscleGroup,
        string? Difficulty,
        string? Equipment,
        string? Preparation,
        string? StartingPosition,
        string? Execution,
        string? VideoId,
        bool IsActive,
        DateTimeOffset CreatedAt);

    private sealed record FailureBody(string Reason);

    private const string Endpoint = "/api/admin/exercises";

    private const string VideoId = "dQw4w9-gX_Q";

    private static string UniqueName(string prefix = "Przysiad") => $"{prefix}-{Guid.NewGuid():N}";

    private static object Request(
        string name,
        string? description = null,
        string? muscleGroup = null,
        string? difficulty = null,
        string? equipment = null,
        string? preparation = null,
        string? startingPosition = null,
        string? execution = null,
        string? videoUrl = null) =>
        new
        {
            name,
            description,
            muscleGroup,
            difficulty,
            equipment,
            preparation,
            startingPosition,
            execution,
            videoUrl,
        };

    private async Task<(HttpClient Admin, ExerciseBody Created)> CreateAsync(object request)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsJsonAsync(Endpoint, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (admin, (await response.Content.ReadFromJsonAsync<ExerciseBody>())!);
    }

    private Task<(HttpClient Admin, ExerciseBody Created)> CreateAsync(string? name = null) =>
        CreateAsync(Request(name ?? UniqueName()));

    // --- who may reach the group ----------------------------------------------

    public static TheoryData<string, string> EveryRoute => new()
    {
        { "GET", Endpoint },
        { "GET", $"{Endpoint}/{Guid.Empty}" },
        { "POST", Endpoint },
        { "PUT", $"{Endpoint}/{Guid.Empty}" },
        { "POST", $"{Endpoint}/{Guid.Empty}/deactivate" },
        { "POST", $"{Endpoint}/{Guid.Empty}/activate" },
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
                Content = JsonContent.Create(Request("x")),
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The library is admin-only: prd.md cuts standalone browsing for members, and FR-020 reaches an
    /// exercise through an assigned plan instead (S-11). An approved member has no way in here.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryRoute))]
    public async Task Active_non_admin_is_403_on_every_route(string method, string url)
    {
        var client = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = JsonContent.Create(Request("x")),
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- creating -------------------------------------------------------------

    [Fact]
    public async Task Admin_creates_an_exercise_and_gets_every_field_back()
    {
        var name = UniqueName();

        var (_, created) = await CreateAsync(Request(
            name,
            description: "Podstawowe ćwiczenie na nogi.",
            muscleGroup: "nogi",
            difficulty: "średnie",
            equipment: "sztanga, stojaki",
            preparation: "Ustaw gryf na wysokości barków.",
            startingPosition: "Stopy na szerokość bioder.",
            execution: "Schodź kontrolowanie do kąta prostego w kolanach.",
            videoUrl: $"https://www.youtube.com/watch?v={VideoId}"));

        Assert.Equal(name, created.Name);
        Assert.Equal("Podstawowe ćwiczenie na nogi.", created.Description);
        Assert.Equal("nogi", created.MuscleGroup);
        Assert.Equal("średnie", created.Difficulty);
        Assert.Equal("sztanga, stojaki", created.Equipment);
        Assert.Equal("Ustaw gryf na wysokości barków.", created.Preparation);
        Assert.Equal("Stopy na szerokość bioder.", created.StartingPosition);
        Assert.Equal("Schodź kontrolowanie do kąta prostego w kolanach.", created.Execution);
        Assert.Equal(VideoId, created.VideoId);
        Assert.True(created.IsActive);
    }

    /// <summary>
    /// FR-018 makes every descriptive field optional, and this is the entry the library will
    /// actually be filled with: a name typed between sets, with the prose promised for later.
    /// </summary>
    [Fact]
    public async Task A_name_alone_is_a_valid_exercise()
    {
        var (_, created) = await CreateAsync();

        Assert.Null(created.Description);
        Assert.Null(created.MuscleGroup);
        Assert.Null(created.Difficulty);
        Assert.Null(created.Equipment);
        Assert.Null(created.Preparation);
        Assert.Null(created.StartingPosition);
        Assert.Null(created.Execution);
        Assert.Null(created.VideoId);
    }

    /// <summary>
    /// A whitespace-only value and a missing one mean the same thing to a reader, so they must not
    /// become two different values in the database — otherwise every screen has to test for both.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_optional_fields_are_stored_as_null(string? blank)
    {
        var (_, created) = await CreateAsync(Request(
            UniqueName(),
            description: blank,
            muscleGroup: blank,
            difficulty: blank,
            equipment: blank,
            preparation: blank,
            startingPosition: blank,
            execution: blank,
            videoUrl: blank));

        Assert.Null(created.Description);
        Assert.Null(created.MuscleGroup);
        Assert.Null(created.Execution);
        Assert.Null(created.VideoId);
    }

    [Fact]
    public async Task Values_are_trimmed()
    {
        var name = UniqueName();

        var (_, created) = await CreateAsync(Request(
            $"  {name}  ", description: "  Opis.  ", muscleGroup: "  klatka piersiowa  "));

        Assert.Equal(name, created.Name);
        Assert.Equal("Opis.", created.Description);
        Assert.Equal("klatka piersiowa", created.MuscleGroup);
    }

    // --- the video link -------------------------------------------------------

    /// <summary>
    /// The parser's contract as seen through the API: whatever shape the admin pasted, the library
    /// stores one canonical id. This is what lets the list's thumbnail and the detail screen's
    /// player both be composed from a single trustworthy value.
    /// </summary>
    [Theory]
    [InlineData("dQw4w9-gX_Q")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9-gX_Q")]
    [InlineData("https://youtu.be/dQw4w9-gX_Q?t=42")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9-gX_Q")]
    [InlineData("https://www.youtube.com/watch?list=PLabc123&v=dQw4w9-gX_Q")]
    [InlineData("youtube.com/embed/dQw4w9-gX_Q")]
    public async Task Every_accepted_link_shape_stores_the_same_video_id(string videoUrl)
    {
        var (_, created) = await CreateAsync(Request(UniqueName(), videoUrl: videoUrl));

        Assert.Equal(VideoId, created.VideoId);
    }

    /// <summary>
    /// A link we cannot use is refused at the boundary rather than stored and rendered as a broken
    /// image days later. The reason is what puts the message on the video field instead of a banner.
    /// </summary>
    [Theory]
    [InlineData("https://vimeo.com/123456789")]
    [InlineData("nie-jest-linkiem")]
    [InlineData("https://www.youtube.com/playlist?list=PLabc123")]
    [InlineData("https://www.youtube.com/watch?v=tooshort")]
    // Past the 2048-character ceiling: refused on length before the parser ever sees it. VideoUrl is
    // the one input whose bound mirrors no column, because only the parsed id is ever stored.
    [InlineData(
        "https://www.youtube.com/watch?v=dQw4w9-gX_Q&pad="
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
            + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task An_unusable_video_link_is_refused(string videoUrl)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsJsonAsync(
            Endpoint, Request(UniqueName(), videoUrl: videoUrl));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_video_url",
            (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>
    /// The edit form shows a canonical watch URL rebuilt from the stored id. Saving that back
    /// unchanged must keep the video — otherwise editing an exercise twice would lose it.
    /// </summary>
    [Fact]
    public async Task The_canonical_watch_url_survives_a_round_trip_through_the_form()
    {
        var (admin, created) = await CreateAsync(Request(UniqueName(), videoUrl: VideoId));

        var response = await admin.PutAsJsonAsync(
            $"{Endpoint}/{created.Id}",
            Request(created.Name, videoUrl: $"https://www.youtube.com/watch?v={created.VideoId}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(VideoId, (await response.Content.ReadFromJsonAsync<ExerciseBody>())!.VideoId);
    }

    /// <summary>Clearing the field removes the video rather than leaving the old one behind.</summary>
    [Fact]
    public async Task Clearing_the_video_url_removes_the_video()
    {
        var (admin, created) = await CreateAsync(Request(UniqueName(), videoUrl: VideoId));

        var response = await admin.PutAsJsonAsync(
            $"{Endpoint}/{created.Id}", Request(created.Name, videoUrl: null));

        Assert.Null((await response.Content.ReadFromJsonAsync<ExerciseBody>())!.VideoId);
    }

    // --- validation bounds ----------------------------------------------------

    public static TheoryData<object, string> InvalidRequests() =>
        new()
        {
            { Request("   "), "missing_field" },
            { Request(new string('n', 201)), "name_too_long" },
            { Request(UniqueName(), description: new string('x', 1001)), "description_too_long" },
            { Request(UniqueName(), muscleGroup: new string('x', 101)), "muscle_group_too_long" },
            { Request(UniqueName(), difficulty: new string('x', 51)), "difficulty_too_long" },
            { Request(UniqueName(), equipment: new string('x', 201)), "equipment_too_long" },
            { Request(UniqueName(), preparation: new string('x', 2001)), "preparation_too_long" },
            {
                Request(UniqueName(), startingPosition: new string('x', 2001)),
                "starting_position_too_long"
            },
            { Request(UniqueName(), execution: new string('x', 4001)), "execution_too_long" },
        };

    /// <summary>
    /// Every column with a HasMaxLength has a guard here. Without one, the value reaches SQL Server
    /// and comes back as "String or binary data would be truncated" — an unhandled 500 for what is
    /// ordinary bad input.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_request_is_refused_with_its_reason(object request, string reason)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsJsonAsync(Endpoint, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(reason, (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>The boundary values themselves must pass, or every bound is off by one.</summary>
    [Fact]
    public async Task Boundary_lengths_are_accepted()
    {
        var (_, created) = await CreateAsync(Request(
            new string('b', 200),
            description: new string('d', 1000),
            muscleGroup: new string('m', 100),
            difficulty: new string('f', 50),
            equipment: new string('e', 200),
            preparation: new string('p', 2000),
            startingPosition: new string('s', 2000),
            execution: new string('x', 4000)));

        Assert.Equal(200, created.Name.Length);
        Assert.Equal(4000, created.Execution!.Length);
    }

    // --- uniqueness among ACTIVE exercises ------------------------------------

    [Fact]
    public async Task Second_active_exercise_with_the_same_name_is_409()
    {
        var (admin, created) = await CreateAsync();

        var response = await admin.PostAsJsonAsync(Endpoint, Request(created.Name));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("name_taken", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>
    /// SQL Server's default collation is case-insensitive, which is why IsNameTakenAsync compares
    /// with a plain == rather than ToLower(). This pins that assumption: a case-sensitive collation
    /// fails this test rather than silently weakening the rule.
    /// </summary>
    [Fact]
    public async Task Name_comparison_is_case_insensitive()
    {
        var (admin, created) = await CreateAsync();

        var response = await admin.PostAsJsonAsync(
            Endpoint, Request(created.Name.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Editing_an_exercise_keeping_its_own_name_succeeds()
    {
        var (admin, created) = await CreateAsync();

        var response = await admin.PutAsJsonAsync(
            $"{Endpoint}/{created.Id}", Request(created.Name, description: "Nowy opis."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Nowy opis.",
            (await response.Content.ReadFromJsonAsync<ExerciseBody>())!.Description);
    }

    // --- deactivation, name release, and the reactivation collision -----------

    /// <summary>
    /// The whole design in one test: retiring an exercise frees its name, the name can be reused,
    /// and reactivating the original then collides — which the activate handler must turn into a
    /// clean 409 rather than letting the filtered index throw a 500. The request carries no name,
    /// which is exactly why this check is easy to forget.
    /// </summary>
    [Fact]
    public async Task Deactivating_releases_the_name_and_reactivating_into_a_taken_one_is_409()
    {
        var (admin, original) = await CreateAsync();

        var deactivated = await admin.PostAsync($"{Endpoint}/{original.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.False((await deactivated.Content.ReadFromJsonAsync<ExerciseBody>())!.IsActive);

        var reused = await admin.PostAsJsonAsync(Endpoint, Request(original.Name));
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);

        var reactivated = await admin.PostAsync($"{Endpoint}/{original.Id}/activate", null);
        Assert.Equal(HttpStatusCode.Conflict, reactivated.StatusCode);
        Assert.Equal(
            "name_taken",
            (await reactivated.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>Nothing is gained by refusing an action that is already done.</summary>
    [Fact]
    public async Task Deactivating_twice_is_idempotent()
    {
        var (admin, created) = await CreateAsync();

        await admin.PostAsync($"{Endpoint}/{created.Id}/deactivate", null);
        var second = await admin.PostAsync($"{Endpoint}/{created.Id}/deactivate", null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False((await second.Content.ReadFromJsonAsync<ExerciseBody>())!.IsActive);
    }

    [Fact]
    public async Task An_uncontested_name_reactivates()
    {
        var (admin, created) = await CreateAsync();

        await admin.PostAsync($"{Endpoint}/{created.Id}/deactivate", null);
        var reactivated = await admin.PostAsync($"{Endpoint}/{created.Id}/activate", null);

        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.True((await reactivated.Content.ReadFromJsonAsync<ExerciseBody>())!.IsActive);
    }

    /// <summary>
    /// An edit must not be able to resurrect a retired exercise, which is why IsActive is absent
    /// from the request DTO in the first place.
    /// </summary>
    [Fact]
    public async Task Editing_a_deactivated_exercise_leaves_it_deactivated()
    {
        var (admin, created) = await CreateAsync();
        await admin.PostAsync($"{Endpoint}/{created.Id}/deactivate", null);

        var response = await admin.PutAsJsonAsync(
            $"{Endpoint}/{created.Id}", Request(created.Name, description: "Poprawka."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<ExerciseBody>())!.IsActive);
    }

    // --- reading --------------------------------------------------------------

    [Fact]
    public async Task The_list_carries_active_and_inactive_rows()
    {
        var (admin, retired) = await CreateAsync();
        await admin.PostAsync($"{Endpoint}/{retired.Id}/deactivate", null);

        var rows = await admin.GetFromJsonAsync<List<ExerciseBody>>(Endpoint);

        Assert.Contains(rows!, e => e.Id == retired.Id && !e.IsActive);
    }

    [Fact]
    public async Task One_exercise_can_be_read_by_id()
    {
        var (admin, created) = await CreateAsync(Request(UniqueName(), muscleGroup: "plecy"));

        var fetched = await admin.GetFromJsonAsync<ExerciseBody>($"{Endpoint}/{created.Id}");

        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("plecy", fetched.MuscleGroup);
    }

    /// <summary>
    /// Every route that takes an id must answer 404 for one that does not exist — the detail screen
    /// renders a "nie znaleziono" state from exactly this, rather than spinning forever.
    /// </summary>
    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "")]
    [InlineData("POST", "/deactivate")]
    [InlineData("POST", "/activate")]
    public async Task An_unknown_id_is_404(string method, string suffix)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), $"{Endpoint}/{Guid.NewGuid()}{suffix}")
            {
                Content = JsonContent.Create(Request(UniqueName())),
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- there is no hard delete ---------------------------------------------

    /// <summary>
    /// Deactivation replaces deletion here for a reason that gets stronger with S-11: a training
    /// plan will reference exercises, so a deleted row would either orphan a plan or be blocked by a
    /// foreign key. This pins the absence of the route rather than trusting the reviewer to notice
    /// one appearing.
    /// </summary>
    [Fact]
    public async Task There_is_no_delete_endpoint()
    {
        var (admin, created) = await CreateAsync();

        var response = await admin.DeleteAsync($"{Endpoint}/{created.Id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
