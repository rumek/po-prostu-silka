using System.Net;
using System.Net.Http.Json;

namespace po_prostu_silka.Tests;

/// <summary>
/// The admin's class-type definitions (prd-v2 FR-004..FR-007).
///
/// <para>
/// THE REASON THIS RUNS AGAINST A REAL ENGINE: the slice's central rule — a name is unique among
/// ACTIVE types only — is enforced by a FILTERED unique index
/// (<c>IX_ClassTypes_Name_Active</c>, <c>HasFilter("[IsActive] = 1")</c>). No in-memory provider
/// implements filtered indexes, and no mocked frontend spec can reach the behaviour at all. The
/// deactivate-then-reuse-then-reactivate cycle below is the one test that actually pins the design.
/// </para>
///
/// <para>
/// Every test creates its own types with a GUID-suffixed name. The uniqueness rule is global to the
/// table, so fixed names would make these tests collide with each other rather than with the rule
/// under test.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class ClassTypeEndpointTests(IntegrationTestFixture fixture)
{
    /// <summary>Mirrors ClassTypeSummary.</summary>
    private sealed record ClassTypeBody(
        Guid Id,
        string Name,
        string? Description,
        int DefaultDurationMinutes,
        int DefaultCapacity,
        bool IsActive,
        DateTimeOffset CreatedAt);

    private sealed record FailureBody(string Reason);

    private const string Endpoint = "/api/admin/class-types";

    private static string UniqueName(string prefix = "Joga") => $"{prefix}-{Guid.NewGuid():N}";

    private static object Request(
        string name,
        string? description = null,
        int duration = 60,
        int capacity = 12) =>
        new
        {
            name,
            description,
            defaultDurationMinutes = duration,
            defaultCapacity = capacity,
        };

    private async Task<(HttpClient Admin, ClassTypeBody Created)> CreateAsync(
        string? name = null,
        string? description = null,
        int duration = 60,
        int capacity = 12)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsJsonAsync(
            Endpoint, Request(name ?? UniqueName(), description, duration, capacity));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (admin, (await response.Content.ReadFromJsonAsync<ClassTypeBody>())!);
    }

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
    public async Task Admin_creates_a_type_and_gets_it_back()
    {
        var name = UniqueName();

        var (_, created) = await CreateAsync(name, "Spokojne zajęcia.", 45, 8);

        Assert.Equal(name, created.Name);
        Assert.Equal("Spokojne zajęcia.", created.Description);
        Assert.Equal(45, created.DefaultDurationMinutes);
        Assert.Equal(8, created.DefaultCapacity);
        Assert.True(created.IsActive);
    }

    /// <summary>
    /// A whitespace-only description and a missing one mean the same thing to a reader, so they must
    /// not become two different values in the database.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_description_is_stored_as_null(string? description)
    {
        var (_, created) = await CreateAsync(description: description);

        Assert.Null(created.Description);
    }

    [Fact]
    public async Task Name_and_description_are_trimmed()
    {
        var name = UniqueName();

        var (_, created) = await CreateAsync($"  {name}  ", "  Opis.  ");

        Assert.Equal(name, created.Name);
        Assert.Equal("Opis.", created.Description);
    }

    // --- validation bounds ----------------------------------------------------

    public static TheoryData<object, string> InvalidRequests()
    {
        var data = new TheoryData<object, string>
        {
            { Request("   "), "missing_field" },
            { Request(UniqueName(), duration: 0), "invalid_duration" },
            { Request(UniqueName(), duration: 481), "invalid_duration" },
            { Request(UniqueName(), capacity: 0), "invalid_capacity" },
            { Request(UniqueName(), capacity: 201), "invalid_capacity" },
            { Request(UniqueName(), new string('x', 1001)), "description_too_long" },
        };

        // F2 from the implementation review: Name is nvarchar(200) but was never length-checked, so
        // a 201-character name reached SQL Server and came back as an unhandled 500.
        data.Add(Request(new string('n', 201)), "name_too_long");

        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_request_is_refused_with_its_reason(object request, string reason)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.PostAsJsonAsync(Endpoint, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(reason, (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>The boundary values themselves must pass, or the bounds are off by one.</summary>
    [Fact]
    public async Task Boundary_values_are_accepted()
    {
        var (_, created) = await CreateAsync(
            new string('b', 200), new string('d', 1000), duration: 480, capacity: 200);

        Assert.Equal(480, created.DefaultDurationMinutes);
        Assert.Equal(200, created.DefaultCapacity);
    }

    // --- uniqueness among ACTIVE types ---------------------------------------

    [Fact]
    public async Task Second_active_type_with_the_same_name_is_409()
    {
        var (admin, created) = await CreateAsync();

        var response = await admin.PostAsJsonAsync(Endpoint, Request(created.Name));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("name_taken", (await response.Content.ReadFromJsonAsync<FailureBody>())!.Reason);
    }

    /// <summary>
    /// SQL Server's default collation is case-insensitive, which is why IsNameTakenAsync compares
    /// with a plain == rather than ToLower(). This pins that assumption: if the database is ever
    /// deployed with a case-sensitive collation, this test fails rather than the rule silently
    /// weakening.
    /// </summary>
    [Fact]
    public async Task Name_comparison_is_case_insensitive()
    {
        var (admin, created) = await CreateAsync("Joga" + Guid.NewGuid().ToString("N"));

        var response = await admin.PostAsJsonAsync(Endpoint, Request(created.Name.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Editing a type while keeping its own name must not collide with itself.</summary>
    [Fact]
    public async Task Editing_a_type_keeping_its_own_name_succeeds()
    {
        var (admin, created) = await CreateAsync(duration: 60);

        var response = await admin.PutAsJsonAsync(
            $"{Endpoint}/{created.Id}", Request(created.Name, "Nowy opis.", duration: 90));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<ClassTypeBody>())!;
        Assert.Equal(90, updated.DefaultDurationMinutes);
        Assert.Equal("Nowy opis.", updated.Description);
    }

    [Fact]
    public async Task Editing_a_type_onto_another_active_name_is_409()
    {
        var (admin, first) = await CreateAsync();

        var secondResponse = await admin.PostAsJsonAsync(Endpoint, Request(UniqueName()));
        var second = (await secondResponse.Content.ReadFromJsonAsync<ClassTypeBody>())!;

        var response = await admin.PutAsJsonAsync($"{Endpoint}/{second.Id}", Request(first.Name));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- deactivation, name release, and the reactivation collision ----------

    /// <summary>
    /// THE TEST THIS CLASS EXISTS FOR. The whole design rests on the filtered index: deactivating
    /// RELEASES a name, so a retired type never holds one hostage — but reactivating can then
    /// collide, even though the activate request carries no name at all. That last step is the
    /// slice's sharpest edge, and the one an unhandled DbUpdateException would turn into a 500.
    /// </summary>
    [Fact]
    public async Task Deactivating_releases_the_name_and_reactivating_into_a_taken_one_is_409()
    {
        var (admin, original) = await CreateAsync();

        // 1. Retire it. The name is now free.
        var deactivated = await admin.PostAsync($"{Endpoint}/{original.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.False((await deactivated.Content.ReadFromJsonAsync<ClassTypeBody>())!.IsActive);

        // 2. A brand-new ACTIVE type may take the released name — this is what the filter buys.
        var replacement = await admin.PostAsJsonAsync(Endpoint, Request(original.Name));
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var replacementBody = (await replacement.Content.ReadFromJsonAsync<ClassTypeBody>())!;

        // 3. Reactivating the original now collides — a clean 409, never a 500.
        var refused = await admin.PostAsync($"{Endpoint}/{original.Id}/activate", null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("name_taken", (await refused.Content.ReadFromJsonAsync<FailureBody>())!.Reason);

        // 4. Free the name again and the original comes back.
        await admin.PostAsync($"{Endpoint}/{replacementBody.Id}/deactivate", null);

        var restored = await admin.PostAsync($"{Endpoint}/{original.Id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.True((await restored.Content.ReadFromJsonAsync<ClassTypeBody>())!.IsActive);
    }

    /// <summary>
    /// Both verbs are idempotent: repeating one is a 200, not a refusal. Failing would force the
    /// screen to explain an error that means "already done".
    /// </summary>
    [Fact]
    public async Task Deactivate_and_activate_are_idempotent()
    {
        var (admin, created) = await CreateAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Endpoint}/{created.Id}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Endpoint}/{created.Id}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Endpoint}/{created.Id}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Endpoint}/{created.Id}/activate", null)).StatusCode);
    }

    // --- reading --------------------------------------------------------------

    /// <summary>
    /// The list is deliberately UNFILTERED — the screen's "show inactive" toggle filters rows it
    /// already holds, so a server-side flag would cost a round trip per flick.
    /// </summary>
    [Fact]
    public async Task List_returns_inactive_types_too_active_first()
    {
        var (admin, retired) = await CreateAsync("Zzz-retired-" + Guid.NewGuid().ToString("N"));
        await admin.PostAsync($"{Endpoint}/{retired.Id}/deactivate", null);
        await CreateAsync("Aaa-active-" + Guid.NewGuid().ToString("N"));

        var all = (await admin.GetFromJsonAsync<ClassTypeBody[]>(Endpoint))!;

        Assert.Contains(all, t => t.Id == retired.Id && !t.IsActive);

        // Active first, then by name: no inactive row may precede an active one.
        var firstInactive = Array.FindIndex(all, t => !t.IsActive);
        var lastActive = Array.FindLastIndex(all, t => t.IsActive);
        Assert.True(firstInactive == -1 || lastActive < firstInactive);
    }

    [Fact]
    public async Task Get_by_id_returns_the_type()
    {
        var (admin, created) = await CreateAsync();

        var fetched = await admin.GetFromJsonAsync<ClassTypeBody>($"{Endpoint}/{created.Id}");

        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(created.Name, fetched.Name);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "")]
    [InlineData("POST", "/deactivate")]
    [InlineData("POST", "/activate")]
    public async Task Unknown_id_is_404(string method, string suffix)
    {
        var admin = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail);

        var response = await admin.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), $"{Endpoint}/{Guid.NewGuid()}{suffix}")
            {
                Content = JsonContent.Create(Request(UniqueName())),
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- there is no hard delete ----------------------------------------------

    /// <summary>
    /// FR-006 replaces deletion with deactivation, so an orphaned occurrence is impossible. This
    /// asserts the absence rather than trusting that nobody adds MapDelete later.
    /// </summary>
    [Fact]
    public async Task There_is_no_delete_endpoint()
    {
        var (admin, created) = await CreateAsync();

        var response = await admin.DeleteAsync($"{Endpoint}/{created.Id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
