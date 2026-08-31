using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Auth;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Authorization;
using po_prostu_silka.Infrastructure.Identity;
using po_prostu_silka.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// In production the connection string comes from the App Service connection string named
// "Default" of type SQLAzure, which the platform exposes as SQLAZURECONNSTR_Default and the
// default configuration provider maps back onto ConnectionStrings:Default. Locally it comes
// from appsettings.Development.json, pointing at the docker-compose SQL Server.
//
// EnableRetryOnFailure: F-01 deliberately deferred connection resiliency until "the first real
// query surface lands in F-02" — login is that surface, and Azure SQL Basic (5 DTU) throttles
// under load, so a transient failure would otherwise surface as a failed login rather than a
// retried one.
//
// Consequence for later slices: the resulting execution strategy does not allow a user-initiated
// transaction to span retries. S-04's booking transaction (the no-overbooking guarantee) must wrap
// its work in db.Database.CreateExecutionStrategy().Execute(...) rather than calling
// BeginTransaction directly, or it throws at runtime.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sqlOptions => sqlOptions.EnableRetryOnFailure()));

// Opens a real connection to the database, so /health answers "can the running app reach
// its data?" rather than merely "did the DI container resolve?".
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

// ---------------------------------------------------------------------------
// Authentication: Identity cookies.
//
// The Angular SPA is served from this app's own wwwroot (angular.json sets outputMode "static"),
// so it is same-origin with the API. That is what makes cookies the cheap choice here: no CORS, no
// cross-site cookie flags, and no token sitting in JS-reachable storage where XSS could read it.
//
// AddIdentityCookies() registers the cookie handlers; AddIdentityCore() does NOT do this on its
// own. If login appears to succeed but no cookie is ever set, this pairing is the first thing to
// check.
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        // Length over composition: members type these on phones, and character-class rules push
        // people towards "Password1!" and towards abandoning registration altogether.
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;

        // No confirmation flow in this milestone - the admin-approval gate is the vetting
        // mechanism, so requiring a confirmed email would lock out every pending member.
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;

    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

    // Lax, not Strict. Strict looks safer, but F-03/S-05 send class-cancellation emails whose links
    // navigate back into the app - under Strict the cookie is withheld on that cross-site
    // navigation and the member lands logged out on the exact link the product exists to deliver.
    // Lax still withholds the cookie on cross-site POST, which is the CSRF vector that matters.
    options.Cookie.SameSite = SameSiteMode.Lax;

    // This app answers with status codes, not redirects. Identity's default is a 302 to
    // /Account/Login; here that redirect would be followed to MapFallbackToFile and the caller
    // would receive 200 text/html where it expected 401 - a failure that looks like success.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// How long a status change takes to bite. Without this the default is 30 minutes anyway, but it is
// stated explicitly because it is the bound on how long a just-blocked member keeps access - the
// number matters to S-02, not just to Identity.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(30));

builder.Services.AddAuthorizationBuilder().AddApplicationPolicies();

var app = builder.Build();

// Idempotent by construction - runs on every cold start, and App Service recycles without warning.
using (var scope = app.Services.CreateScope())
{
    await AdminSeeder.SeedAsync(
        scope.ServiceProvider,
        app.Configuration,
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSeeder"));
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}
// In production, Azure App Service terminates TLS at the edge and forwards plain HTTP
// internally — HTTPS is enforced there via the "HTTPS Only" site setting instead, so a
// redirect here would fight the reverse proxy.

app.UseDefaultFiles();
app.UseStaticFiles();

// Order is load-bearing: authentication must run before authorization, and both must run before
// the endpoints below - MapFallbackToFile in particular, which would otherwise claim /api routes
// before the auth middleware ever sees them.
app.UseAuthentication();
app.UseAuthorization();

// Anonymous by design: a health probe that needs credentials cannot answer "is the app reachable".
app.MapHealthChecks("/health");

app.MapAuthEndpoints();

// Probe endpoints for the ActiveMember and Admin policies, in the "Testing" environment only.
//
// No production endpoint uses those policies yet: /me is deliberately bare RequireAuthorization()
// so a Pending member can read their own status for S-01's awaiting-approval screen. Without these
// the policies would ship entirely unexercised.
//
// They live here rather than in the test fixture because WebApplicationFactory has no supported
// hook for adding endpoints to a minimal-API app - anything bolted on via a startup filter matches
// BEFORE UseAuthentication/UseAuthorization, so the policy under test would be bypassed and the
// assertion would pass while proving nothing. Guarded by environment, they cannot exist in
// Development or Production.
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/test/active-member", () => Results.Ok("active-member"))
        .RequireAuthorization(AuthorizationPolicies.ActiveMember);

    app.MapGet("/test/admin-only", () => Results.Ok("admin-only"))
        .RequireAuthorization(AuthorizationPolicies.Admin);
}

// Must stay last: the SPA fallback claims every route no earlier endpoint matched.
app.MapFallbackToFile("index.html");

app.Run();

// Exposed so WebApplicationFactory<Program> can boot this app in the Phase 3 integration tests.
// Top-level statements compile to an internal Program class; this makes it public without
// changing any behaviour.
public partial class Program;
