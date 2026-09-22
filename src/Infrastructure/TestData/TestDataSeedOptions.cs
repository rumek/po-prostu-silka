namespace po_prostu_silka.Infrastructure.TestData;

/// <summary>
/// The <c>TestDataSeed</c> section (S-24). Documented without values in appsettings.json; Azure
/// supplies <c>TestDataSeed__Enabled</c> / <c>__Reset</c> / <c>__Password</c> as app settings.
/// </summary>
public sealed class TestDataSeedOptions
{
    public const string SectionName = "TestDataSeed";

    /// <summary>Off by default. On, the seeder inserts the data set if its sentinel account is absent.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Wipes every piece of domain data except the AdminSeed account first. Turn it off again by hand:
    /// left on, every App Service recycle wipes the environment.
    /// </summary>
    public bool Reset { get; set; }

    /// <summary>The one password every seeded account shares. Never logged.</summary>
    public string? Password { get; set; }
}
