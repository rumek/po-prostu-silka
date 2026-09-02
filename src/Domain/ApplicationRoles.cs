namespace po_prostu_silka.Domain;

/// <summary>
/// The application's roles. An account holds a SET of these, not one of them: registration grants
/// <see cref="User"/>, the seeder grants <see cref="Admin"/> and nothing else, and
/// <see cref="Trainer"/> is added on top of whatever an account already has.
///
/// Constants rather than magic strings: these names are persisted in AspNetRoles and referenced by
/// authorization policies, so a typo would silently produce an authorization rule that never matches.
///
/// <para>
/// TWO ARRAYS, TWO JOBS - and a new role must be placed in each of them deliberately. They were one
/// array until Trainer arrived, which made the difference visible: <see cref="All"/> is what the
/// seeder creates, <see cref="MemberFacing"/> is what satisfies the ActiveMember policy. While the
/// only roles were User and Admin the two sets happened to coincide, so adding a role to the single
/// array silently granted it access to every member-facing endpoint. Trainer is the first role for
/// which that is the wrong default.
/// </para>
/// </summary>
public static class ApplicationRoles
{
    /// <summary>Uses the app: schedule, bookings, own training plan.</summary>
    public const string User = "User";

    /// <summary>Manages users, schedule, training plans, exercises. Seeded at setup, never self-registered.</summary>
    public const string Admin = "Admin";

    /// <summary>
    /// Runs classes. Granted by the admin to an already-approved account and additive: it takes
    /// nothing away, and on its own it confers nothing either. It exists so a class can name a
    /// person the system knows instead of a typed string.
    /// </summary>
    public const string Trainer = "Trainer";

    /// <summary>
    /// Every role that must exist in the database. The seeder creates any that are missing.
    /// A new role belongs here ALWAYS - omitting it means the role can never be granted.
    /// </summary>
    public static readonly string[] All = [User, Admin, Trainer];

    /// <summary>
    /// The roles that satisfy the ActiveMember policy - the set consumed by
    /// <c>Infrastructure/Authorization/AuthorizationPolicies.cs</c>.
    ///
    /// A new role belongs here only if holding it ALONE should grant access to member-facing
    /// endpoints. Trainer deliberately does not: a real trainer is additive and already holds
    /// <see cref="User"/>, so it passes on that; an account holding only Trainer is a state nothing
    /// creates today and must not be authorised by accident if something ever does.
    /// </summary>
    public static readonly string[] MemberFacing = [User, Admin];
}
