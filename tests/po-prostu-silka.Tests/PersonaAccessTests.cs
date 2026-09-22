using System.Net;

namespace po_prostu_silka.Tests;

/// <summary>
/// The member persona's own-data routes refuse staff (S-25, PRD v2 "Amendment: role-based
/// visibility").
///
/// <para>
/// EndpointAuthorizationTests pins that these routes carry MemberOnly; this pins what the policy
/// DOES over HTTP, which that file cannot see. The case that matters most is the account holding
/// User AND Trainer: a role list alone admits it, and only the policy's negative assertion refuses
/// it. The Admin-only account matters too — the seeded admin holds no User, so the refusal must not
/// depend on it.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class PersonaAccessTests(IntegrationTestFixture fixture)
{
    private static readonly string[] OwnDataRoutes =
    [
        "/api/passes/mine",
        "/api/plans/mine",
        $"/api/plans/mine/exercises/{Guid.Empty}",
        "/api/bookings/mine",
    ];

    private static readonly string[] StaffEmails =
    [
        TestUsers.ActiveTrainerEmail,
        TestUsers.ActiveAdminEmail,
        TestUsers.ActiveAdminTrainerEmail,
    ];

    public static TheoryData<string, string> StaffOnOwnDataRoutes()
    {
        var data = new TheoryData<string, string>();
        foreach (var email in StaffEmails)
        {
            foreach (var route in OwnDataRoutes)
            {
                data.Add(email, route);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(StaffOnOwnDataRoutes))]
    public async Task Staff_are_refused_their_own_pass_plan_and_bookings(string email, string route)
    {
        var staff = await fixture.CreateAuthenticatedClientAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(route)).StatusCode);
    }

    /// <summary>
    /// The member still gets through — a policy that refused everybody would pass the theory above.
    /// The exercise route answers 404 for an exercise not on the member's plan, which is still "the
    /// policy admitted them".
    /// </summary>
    [Theory]
    [InlineData("/api/passes/mine")]
    [InlineData("/api/plans/mine")]
    [InlineData("/api/bookings/mine")]
    public async Task A_member_is_admitted_to_their_own_data(string route)
    {
        var member = await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail);

        var status = (await member.GetAsync(route)).StatusCode;

        Assert.NotEqual(HttpStatusCode.Forbidden, status);
        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
    }
}
