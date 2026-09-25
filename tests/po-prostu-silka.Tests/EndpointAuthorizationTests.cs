using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Tests;

/// <summary>
/// The whole API's authorization, read from endpoint metadata rather than probed route by route.
///
/// <para>
/// WHY THIS FILE EXISTS. There is no authorization fallback policy (Program.cs), so an endpoint is
/// ANONYMOUS unless its MapGroup says otherwise, and the per-suite "every route" theories only see the
/// routes someone remembered to list. Roadmap S-18 re-creates every route group in new files; a group
/// that loses its RequireAuthorization, or a route that lands in the wrong one of the two groups
/// sharing /api/admin/classes, fails every test here and no other test anywhere.
/// </para>
///
/// <para>
/// THE EXPECTATIONS ARE WRITTEN BY HAND FROM PRODUCT SOURCES, never read from the endpoints. A test
/// that derived its expected policies from the data source would bless whatever the code does today.
/// Every allowlist and exception entry is also asserted to EXIST, so a renamed route cannot quietly
/// turn a rule into a no-op.
/// </para>
///
/// <para>
/// What this cannot see: whether a policy's DEFINITION is right (a redefined Admin policy that
/// admits trainers passes here), and the inline instructor check on staff booking. Both are covered
/// over HTTP in the owning suites.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class EndpointAuthorizationTests(IntegrationTestFixture fixture)
{
    /// <summary>One method on one route pattern, with the authorization metadata it carries.</summary>
    private sealed record Route(
        string Method,
        string Pattern,
        IReadOnlyList<string?> Policies,
        bool HasAuthorize,
        bool AllowsAnonymous)
    {
        public override string ToString() => $"{Method} {Pattern}";
    }

    /// <summary>
    /// THE ONLY ANONYMOUS API ROUTES. prd.md Access Control: "Unauthenticated access: login/registration
    /// only" — plus the pre-login password recovery pair FR-026 adds (a member who cannot sign in is
    /// exactly who it is for).
    /// </summary>
    private static readonly (string Method, string Pattern)[] AnonymousApiRoutes =
    [
        ("POST", "/api/auth/login"),
        ("POST", "/api/auth/register"),
        ("POST", "/api/auth/forgot-password"),
        ("POST", "/api/auth/reset-password"),
    ];

    /// <summary>
    /// The only endpoints outside /api that carry no authorization: the health probe (a probe that
    /// needs credentials cannot answer "is the app reachable") and the SPA fallback, which serves the
    /// shell and never data. Plus S-28's <c>/test/throw</c>, which is mapped only under
    /// <c>Testing</c> (Program.cs) and exists so SecurityHeadersTests can pin the generic 500: it
    /// carries no data, and a probe for an ANONYMOUS failure has to be reachable anonymously.
    /// </summary>
    private static readonly string[] AnonymousNonApiPatterns = ["/health", "/{*path:nonfile}", "/test/throw"];

    /// <summary>
    /// /api/admin routes that a TRAINER may reach — every other /api/admin route is Admin only.
    /// Staff booking: S-16 MP-02, a trainer books into and releases spots on the classes they instruct,
    /// and since S-27 marks attendance on them.
    /// Library read: S-11, a trainer reads the exercise library to build plans (writes stay Admin).
    /// </summary>
    private static readonly (string Method, string Pattern)[] TrainerAdminRoutes =
    [
        ("GET", "/api/admin/classes/{classId:guid}/bookings"),
        ("POST", "/api/admin/classes/{classId:guid}/bookings"),
        ("DELETE", "/api/admin/classes/{classId:guid}/bookings/{bookingId:guid}"),
        ("PUT", "/api/admin/classes/{classId:guid}/bookings/{bookingId:guid}/attendance"),
        ("GET", "/api/admin/exercises"),
        ("GET", "/api/admin/exercises/{id:guid}"),
    ];

    /// <summary>
    /// The member persona's own-data routes (S-25, PRD v2 "Amendment: role-based visibility"): the
    /// karnet, the plan and the bookings belong to members, and staff hold none of them. MemberOnly —
    /// not ActiveMember, which admits staff.
    /// </summary>
    private static readonly (string Method, string Pattern)[] MemberOnlyRoutes =
    [
        ("GET", "/api/passes/mine"),
        ("GET", "/api/plans/mine"),
        ("GET", "/api/plans/mine/exercises/{exerciseId:guid}"),
        ("GET", "/api/bookings/mine"),
        ("GET", "/api/bookings/history"),
    ];

    /// <summary>
    /// The schedule (S-25): a staff read. A member is refused; a trainer is narrowed to the classes
    /// they instruct inside the handler, which ClassEndpointTests covers over HTTP.
    /// </summary>
    private static readonly (string Method, string Pattern) ScheduleRoute = ("GET", "/api/classes");

    private IReadOnlyList<Route> Routes()
    {
        var source = fixture.Factory.Services.GetRequiredService<EndpointDataSource>();

        return source.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
                var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                              ?? ["*"];

                return methods.Select(method => new Route(
                    method,
                    Normalize(endpoint.RoutePattern.RawText),
                    authorize.Select(a => a.Policy).ToList(),
                    authorize.Count > 0,
                    anonymous));
            })
            .ToList();
    }

    /// <summary>
    /// A leading slash, no trailing one: a group route mapped as MapGet("/") surfaces as
    /// "/api/admin/exercises/", and the lists above are written the way a person reads a route.
    /// </summary>
    private static string Normalize(string? raw) => "/" + (raw ?? string.Empty).Trim('/');

    private static bool IsApi(Route route) => route.Pattern.StartsWith("/api/", StringComparison.Ordinal);

    private static bool Matches(Route route, (string Method, string Pattern) entry) =>
        route.Method == entry.Method && route.Pattern == entry.Pattern;

    [Fact]
    public void Every_listed_route_exists()
    {
        var routes = Routes();

        // Without this, renaming /api/auth/login would leave the allowlist pointing at nothing and every
        // rule below would still pass. The same trap MemberAdminEndpointTests documents for a policy
        // test left pointing at a deleted route.
        foreach (var entry in AnonymousApiRoutes.Concat(TrainerAdminRoutes).Concat(MemberOnlyRoutes).Append(ScheduleRoute))
        {
            Assert.True(
                routes.Any(r => Matches(r, entry)),
                $"{entry.Method} {entry.Pattern} is listed here but is not mapped");
        }
    }

    [Fact]
    public void Only_the_login_registration_and_reset_routes_are_anonymous()
    {
        var offenders = Routes()
            .Where(IsApi)
            .Where(r => AnonymousApiRoutes.Any(entry => Matches(r, entry))
                ? !r.AllowsAnonymous
                : r.AllowsAnonymous || !r.HasAuthorize)
            .Select(r => r.ToString())
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "API routes whose anonymous access disagrees with prd.md Access Control:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void Outside_the_api_only_the_health_probe_and_the_spa_shell_are_anonymous()
    {
        var offenders = Routes()
            .Where(r => !IsApi(r))
            .Where(r => r.AllowsAnonymous || !r.HasAuthorize)
            .Where(r => !AnonymousNonApiPatterns.Contains(r.Pattern))
            .Select(r => r.ToString())
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Non-API endpoints reachable without authorization:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Admin_routes_require_the_admin_policy_except_the_trainer_routes()
    {
        var admin = Routes().Where(r => r.Pattern.StartsWith("/api/admin/", StringComparison.Ordinal)).ToList();

        // A vacuous pass would be the worst outcome here: an empty set satisfies every rule.
        Assert.Contains(admin, r => !TrainerAdminRoutes.Any(entry => Matches(r, entry)));

        var offenders = new List<string>();

        foreach (var route in admin)
        {
            if (TrainerAdminRoutes.Any(entry => Matches(route, entry)))
            {
                // TrainerOrAdmin and NOT Admin: combined with Admin, the trainer would be locked out of
                // the very thing S-16 and S-11 gave them.
                if (!route.Policies.Contains(AuthorizationPolicyNames.TrainerOrAdmin)
                    || route.Policies.Contains(AuthorizationPolicyNames.Admin))
                {
                    offenders.Add($"{route} must be {AuthorizationPolicyNames.TrainerOrAdmin} only");
                }
            }
            else if (!route.Policies.Contains(AuthorizationPolicyNames.Admin))
            {
                // The S-18 failure this exists for: a class-management route landing in the
                // staff-booking group would let every trainer cancel and delete classes.
                offenders.Add($"{route} must require {AuthorizationPolicyNames.Admin}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    [Fact]
    public void The_members_own_data_routes_require_the_member_only_policy()
    {
        var routes = Routes();

        var offenders = MemberOnlyRoutes
            .SelectMany(entry => routes.Where(r => Matches(r, entry)))
            .Where(r => !r.Policies.Contains(AuthorizationPolicyNames.MemberOnly)
                        || r.Policies.Contains(AuthorizationPolicyNames.ActiveMember))
            .Select(r => r.ToString())
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Own-data routes not under {AuthorizationPolicyNames.MemberOnly} (S-25):\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void The_schedule_requires_the_trainer_or_admin_policy()
    {
        var schedule = Routes().Where(r => Matches(r, ScheduleRoute)).ToList();

        Assert.NotEmpty(schedule);
        Assert.All(schedule, r =>
        {
            Assert.Contains(AuthorizationPolicyNames.TrainerOrAdmin, r.Policies);
            Assert.DoesNotContain(AuthorizationPolicyNames.ActiveMember, r.Policies);
        });
    }

    [Fact]
    public void No_product_route_uses_the_active_member_policy()
    {
        // S-25 moved every product route off ActiveMember; it stays only for the Testing probe,
        // /test/active-member, which lives outside /api and is mapped only under Testing.
        var offenders = Routes()
            .Where(IsApi)
            .Where(r => r.Policies.Contains(AuthorizationPolicyNames.ActiveMember))
            .Select(r => r.ToString())
            .ToList();

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    [Fact]
    public void Trainer_routes_require_the_trainer_or_admin_policy()
    {
        var trainer = Routes()
            .Where(r => r.Pattern.StartsWith("/api/trainer/", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(trainer);

        // S-11: plan authoring belongs to trainers AND admins. Admin alone would lock trainers out.
        var offenders = trainer
            .Where(r => !r.Policies.Contains(AuthorizationPolicyNames.TrainerOrAdmin)
                        || r.Policies.Contains(AuthorizationPolicyNames.Admin))
            .Select(r => r.ToString())
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Trainer routes not under {AuthorizationPolicyNames.TrainerOrAdmin}:\n"
            + string.Join("\n", offenders));
    }
}
