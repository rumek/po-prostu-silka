namespace po_prostu_silka.Domain.Members;

/// <summary>
/// A person in the club. THE ACCOUNT IS OPTIONAL — that is the entire point of this type.
///
/// <para>
/// Until S-14 the club's records hung off <see cref="ApplicationUser"/>, which made
/// "someone who trains here but has never registered" impossible to write down: bookings, plans and
/// the class instructor all pointed at an Identity row, and an Identity row is a login. This type
/// takes over that role. <see cref="ApplicationUser"/> goes back to being credentials, roles and
/// devices; everything club-shaped points here.
/// </para>
///
/// <para>
/// THE LINK IS <see cref="UserId"/>, AND IT IS NULLABLE. Null means the person has no login: the admin
/// keeps their record, books them into classes and assigns them plans, and they simply never sign in.
/// When they later want an account they register with an <see cref="AccessCode"/>, which attaches the
/// new account to THIS row rather than creating a second one — so their bookings and their active plan
/// are already there. A member has at most one account and an account has at most one member, enforced
/// by the filtered unique index in MemberConfiguration, not by convention.
/// </para>
///
/// <para>
/// Anemic, like <see cref="Scheduling.Class"/> and <see cref="Scheduling.Booking"/>: the rules that
/// need to be atomic (claiming a code, blocking and releasing spots in one save) live in the endpoints
/// where they can share a unit of work, and putting half of them here would be worse than putting none.
/// </para>
/// </summary>
public class Member
{
    public Guid Id { get; set; }

    /// <summary>
    /// The linked account, or null when this person has no login.
    ///
    /// <para>
    /// 450 characters because that is Identity's key length — the same reasoning
    /// <see cref="Scheduling.Booking"/> used for the column this one replaces. Unique WHERE NOT NULL:
    /// many members may have no account, but no two may share one.
    /// </para>
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// The account, when there is one. READ SIDE ONLY, the same contract every other navigation in this
    /// codebase carries: it exists so a projection can reach the account's status and roles in one
    /// statement, never so a write path can mutate Identity through it.
    /// </summary>
    public ApplicationUser? User { get; set; }

    /// <summary>
    /// How the club refers to this person. Required, and OWNED BY THE CLUB — a member cannot edit it
    /// (S-13, FR-006), and claiming a record with a code does not let the newcomer rename themselves.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Contact address. NULLABLE, unlike the account's, because a person recorded at the desk may not
    /// have given one — and without an email there is nothing to notify them at, which is a stated
    /// consequence of the accountless case rather than a bug.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>Contact number. Bounded in MemberConfiguration from ContactDetails' own constants.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Street name, without the house number.</summary>
    public string? Street { get; set; }

    /// <summary>House number, optionally with a flat number ("12A/3") — one field, as a Polish address is written.</summary>
    public string? HouseNumber { get; set; }

    /// <summary>Polish postal code in NN-NNN form.</summary>
    public string? PostalCode { get; set; }

    /// <summary>Town or city.</summary>
    public string? City { get; set; }

    /// <summary>
    /// Whether this person may use the club. See <see cref="MembershipStatus"/> for why this is not the
    /// same question as <see cref="ApplicationUser.Status"/>, and why both have to exist.
    /// </summary>
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    /// <summary>When the club started keeping this record — not when an account was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The single-use code that lets someone attach a new account to this record, or null when none is
    /// outstanding.
    ///
    /// <para>
    /// Stored in PLAINTEXT, which is a deliberate and narrow exception. The admin has to be able to read
    /// it back in order to hand it over — the club gives it out at the desk or over the phone, not by an
    /// email this row may not even have an address for. What that costs is precise: a database read
    /// discloses live codes. What it does not cost is access, because the code grants exactly one thing —
    /// "attach the account I am creating to this member" — and never a session. It expires, it is
    /// consumed on use, and it is refused outright once <see cref="UserId"/> is set.
    /// </para>
    /// </summary>
    public string? AccessCode { get; set; }

    /// <summary>
    /// When <see cref="AccessCode"/> stops being accepted.
    ///
    /// A STORED COLUMN RATHER THAN A DERIVATION from an issued-at timestamp, on purpose: deriving it
    /// would mean that changing the default validity silently re-dates every code already in someone's
    /// hand, in both directions. Null exactly when <see cref="AccessCode"/> is null.
    /// </summary>
    public DateTimeOffset? AccessCodeExpiresAt { get; set; }

    /// <summary>
    /// When an account was attached, or null if none ever was. Distinct from
    /// <see cref="ApplicationUser.CreatedAt"/>, which is when the ACCOUNT was made: a member recorded in
    /// January who claims in June has both dates, and they mean different things.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token, the same mechanism <see cref="Training.TrainingPlan"/> uses.
    ///
    /// <para>
    /// It guards the two writes here that are read-then-write and must not interleave: claiming a code
    /// (two people must not attach two accounts to one record) and blocking (which reads the status,
    /// then releases bookings on the strength of it). Rotate it by hand on every such write — nothing
    /// rotates it for you, because these paths deliberately share one SaveChangesAsync rather than going
    /// through a manager that would issue its own.
    /// </para>
    /// </summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
}
