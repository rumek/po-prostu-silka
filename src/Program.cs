using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

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

app.MapHealthChecks("/health");

// Must stay last: the SPA fallback claims every route no earlier endpoint matched.
app.MapFallbackToFile("index.html");

app.Run();
