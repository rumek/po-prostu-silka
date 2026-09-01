using Azure.Communication.Email;
using Lib.Net.Http.WebPush;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Infrastructure.Notifications;
using po_prostu_silka.Application.Auth;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Members;
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
    .AddDbContextCheck<AppDbContext>()
    // Degraded (not Unhealthy) when dead-lettered messages pile up - see OutboxHealthCheck.
    .AddCheck<OutboxHealthCheck>("outbox");

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

// ---------------------------------------------------------------------------
// Notification delivery (F-03).
//
// Everything goes through the outbox: infrastructure.md:79 records that App Service recycles
// without warning, so a fire-and-forget send silently drops whatever was in flight - a direct hit
// on the "no missed cancellations" guardrail. The worker below is what makes delivery survive that.
// ---------------------------------------------------------------------------
builder.Services.Configure<AcsOptions>(builder.Configuration.GetSection(AcsOptions.SectionName));
builder.Services.Configure<VapidOptions>(builder.Configuration.GetSection(VapidOptions.SectionName));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));

builder.Services.TryAddSingleton(TimeProvider.System);

// Null when unconfigured rather than throwing: a developer with no ACS credentials must still be
// able to run the app, and AcsEmailSender degrades to a logged no-op.
builder.Services.AddSingleton(sp =>
{
    var acs = sp.GetRequiredService<IOptions<AcsOptions>>().Value;
    return new AcsEmailClientHolder(acs.IsConfigured ? new EmailClient(acs.ConnectionString) : null);
});

// PushServiceClient wraps an HttpClient, so it goes through IHttpClientFactory for pooling.
builder.Services.AddHttpClient<PushServiceClient>();

builder.Services.AddScoped<IEmailSender, AcsEmailSender>();
builder.Services.AddScoped<IPushSender, WebPushSender>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
builder.Services.AddScoped<IOutboxEnqueuer, OutboxEnqueuer>();
builder.Services.AddScoped<IPushSubscriptionStore, PushSubscriptionStore>();
builder.Services.AddScoped<IVapidPublicKey, VapidPublicKeyProvider>();
builder.Services.AddScoped<IAccountApprovedNotification, AccountApprovedNotification>();

// S-01's approve action needs to commit a status flip and the outbox rows it triggers together, and
// to read the pending queue - both without Application referencing EF Core (AGENTS.md layering).
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IPendingMemberQuery, PendingMemberQuery>();

builder.Services.AddHostedService<OutboxDeliveryWorker>();

var app = builder.Build();

// Idempotent by construction - runs on every cold start, and App Service recycles without warning.
var seedLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSeeder");
using (var scope = app.Services.CreateScope())
{
    try
    {
        await AdminSeeder.SeedAsync(scope.ServiceProvider, app.Configuration, seedLogger);
    }
    catch (Exception ex)
    {
        // Never take the site down over seeding. EnableRetryOnFailure covers transient SQL faults,
        // but not an unmigrated schema ("invalid object name" is not transient, so it is never
        // retried) nor a retry-exhausted outage - and an exception escaping here kills the process
        // before /health is even mapped, so App Service sees a crash-loop instead of a running app
        // that reports itself unhealthy. Log loudly and carry on.
        seedLogger.LogError(ex, "Admin seeding failed; continuing startup so /health can report.");
    }
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
app.MapPushEndpoints();
app.MapMemberAdminEndpoints();

// Probe endpoints for the ActiveMember and Admin policies, in the "Testing" environment only.
//
// S-01 gave the Admin policy its first production consumer (/api/admin/members), but ActiveMember
// still has none: /me and /api/push are deliberately bare RequireAuthorization() so a Pending member
// can read their own status and register a device, and Home is a placeholder until S-03. These
// probes are therefore the ONLY surface where the ActiveMember policy - and the claim staleness that
// POST /api/auth/refresh exists to fix - can be observed. Keep them until S-03 ships a real one.
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
