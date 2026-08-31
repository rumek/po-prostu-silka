namespace po_prostu_silka.Domain;

/// <summary>
/// The flat two-role model locked by the PRD (Access Control). No Trainer role in MVP.
///
/// Constants rather than magic strings: these names are persisted in AspNetRoles and referenced by
/// authorization policies, so a typo would silently produce an authorization rule that never matches.
/// </summary>
public static class ApplicationRoles
{
    /// <summary>Uses the app: schedule, bookings, own training plan.</summary>
    public const string User = "User";

    /// <summary>Manages users, schedule, training plans, exercises. Seeded at setup, never self-registered.</summary>
    public const string Admin = "Admin";

    /// <summary>Every role that must exist in the database. The seeder creates any that are missing.</summary>
    public static readonly string[] All = [User, Admin];
}
