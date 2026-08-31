using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace po_prostu_silka.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <see cref="AppDbContext"/>, used by the `dotnet ef` CLI.
///
/// WHY THIS EXISTS - do not "fix" the placeholder below.
///
/// Without this factory, EF tooling builds Program.cs's host to resolve the DbContext.
/// On a CI runner ASPNETCORE_ENVIRONMENT is unset, so the environment is Production,
/// appsettings.Development.json never loads, and GetConnectionString("Default") returns
/// the empty placeholder from appsettings.json - which UseSqlServer rejects. That would
/// break `dotnet ef migrations script` in the deploy pipeline.
///
/// EF prefers this factory over the application host whenever it is present, so design-time
/// commands never touch runtime configuration. NO CONNECTION IS EVER OPENED with the string
/// below: migrations scripting and scaffolding only need the model, not a live database.
/// Commands that DO connect (e.g. `database update`) are given a real connection string
/// explicitly via --connection. The value here is deliberately not a credential.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DesignTimePlaceholderConnection =
        "Server=(localdb)\\design-time;Database=po-prostu-silka-design-time;Trusted_Connection=True;";

    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(DesignTimePlaceholderConnection)
            .Options;

        return new AppDbContext(options);
    }
}
