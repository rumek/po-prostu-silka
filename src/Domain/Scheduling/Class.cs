namespace po_prostu_silka.Domain.Scheduling;

/// <summary>
/// One occurrence of a class on the schedule (prd-v2 US-01, FR-008 - FR-012).
///
/// <para>
/// AN INSTANCE, NOT A DESCRIPTION. Since S-06 this entity no longer carries its own identity: the
/// name and description resolve BY REFERENCE through <see cref="ClassType"/>, and the instructor
/// resolves through <see cref="Instructor"/>. What the occurrence owns is its moment in time and its
/// own COPIES of the numbers - see <see cref="Capacity"/>.
/// </para>
///
/// This is the aggregate S-08 books against, so its shape outlives this slice: <see cref="Capacity"/>
/// is what the no-overbooking guarantee is checked against, and <see cref="Status"/> is what S-09
/// transitions. Adding a field here later is cheap; changing the meaning of one is not.
///
/// No booking count lives here. A denormalized counter would pre-commit S-08's concurrency design -
/// the load-bearing correctness decision of the milestone - and that is not this slice's to make.
/// Free spots are projected at read time (see IClassScheduleQuery).
/// </summary>
public class Class
{
    public Guid Id { get; set; }

    /// <summary>
    /// DEAD COLUMN. Read <see cref="ClassType"/>.Name instead.
    ///
    /// <para>
    /// The occurrence stopped owning its name in S-06 (prd-v2 FR-010): a typed name is exactly what
    /// drifted between weeks, and resolving it through the type is what makes a correction apply
    /// everywhere at once. Nothing reads or writes this property any more.
    /// </para>
    ///
    /// <para>
    /// It survives as a NULLABLE column for exactly one release. AGENTS.md: rollback redeploys the
    /// previous artifact but does NOT roll back the schema, so the previous build - which still
    /// INSERTs this column - has to find it. A follow-up change drops it.
    /// </para>
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// When the class starts, in UTC like every other timestamp in this app. The SPA groups the
    /// schedule into days by the BROWSER's local date and renders with DatePipe; the server never
    /// groups and never hardcodes a timezone.
    /// </summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>
    /// How long the class runs. Stored as minutes rather than an end timestamp so the two can never
    /// contradict each other, and so the time-overlap check is plain arithmetic
    /// (StartsAt + DurationMinutes), which EF translates to DATEADD.
    ///
    /// <para>
    /// A COPY of <see cref="Scheduling.ClassType.DefaultDurationMinutes"/>, taken when the occurrence
    /// was created and overridable for this session alone. Never re-read from the type.
    /// </para>
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// DEAD COLUMN. There is no room.
    ///
    /// <para>
    /// The club has one room, so the field never carried information (prd-v2 FR-011). The overlap
    /// invariant it used to serve did not disappear with it - it WIDENED, from "one room, one class
    /// at a time" to "one club, one class at a time" (FR-012).
    /// </para>
    ///
    /// <para>
    /// Nullable for one release, for the same rollback reason as <see cref="Name"/>.
    /// </para>
    /// </summary>
    public string? Room { get; set; }

    /// <summary>
    /// DEAD COLUMN, mapped to <c>Instructor</c>. Read <see cref="Instructor"/>.DisplayName instead.
    ///
    /// <para>
    /// This is the free-text instructor the schedule used to carry (prd-v2 FR-009). It was free text
    /// only because the product shipped without a Trainer role, so there was no person to point at;
    /// S-04 added the role and S-06 points at it, leaving this string behind.
    /// </para>
    ///
    /// <para>
    /// The PROPERTY is renamed but the COLUMN is not: <see cref="Instructor"/> is now the navigation,
    /// and the two cannot share a name. ClassConfiguration keeps the column name with
    /// <c>HasColumnName("Instructor")</c>, so the older build a rollback restores still finds the
    /// column it writes. Dropped one release later, with <see cref="Name"/> and <see cref="Room"/>.
    /// </para>
    /// </summary>
    public string? InstructorName { get; set; }

    /// <summary>
    /// How many spots exist. S-08 enforces that bookings never exceed this, even under simultaneous
    /// requests - the PRD's headline guardrail.
    ///
    /// <para>
    /// A COPY of <see cref="Scheduling.ClassType.DefaultCapacity"/>, and the copy semantics matter
    /// more here than anywhere else in the model (prd-v2 FR-007). Resolving this through the type
    /// would let a type edit change the capacity of a class that already has bookings - moving the
    /// very value the no-overbooking guarantee is checked against.
    /// </para>
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>Gates visibility on the schedule. Only ever Scheduled until S-09; see <see cref="ClassStatus"/>.</summary>
    public ClassStatus Status { get; set; } = ClassStatus.Scheduled;

    /// <summary>When the admin created it. Not shown to members; useful for ordering and diagnostics.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Which <see cref="Scheduling.ClassType"/> this occurrence instantiates (prd-v2 FR-008).
    ///
    /// <para>
    /// REQUIRED since S-06. An occurrence with no definition has no name, so there is no such thing.
    /// IMMUTABLE once set: the API refuses an edit that changes it (class_type_immutable), which
    /// keeps the reference stable and makes a client bug loud rather than silent.
    /// </para>
    /// </summary>
    public Guid ClassTypeId { get; set; }

    /// <summary>
    /// The definition this occurrence instantiates. READ SIDE ONLY.
    ///
    /// <para>
    /// READ THIS BEFORE USING IT. S-05 deliberately shipped WITHOUT this navigation, because having
    /// one invites a write path to reach <see cref="Scheduling.ClassType.DefaultCapacity"/> through
    /// it - the exact inversion of FR-007 that the no-overbooking guarantee cannot survive. S-06
    /// added it so ClassScheduleQuery can project the type's name and description in one statement,
    /// and that is the ONLY thing it is for.
    /// </para>
    ///
    /// <para>
    /// NO WRITE PATH MAY READ <c>DefaultDurationMinutes</c> OR <c>DefaultCapacity</c> THROUGH THIS.
    /// Creation copies both numbers from the REQUEST (the client prefilled them from the type and the
    /// admin may have overridden them); the server loads the type only to check it exists and is
    /// active. The compile-time barrier that used to enforce this is gone, so ClassEndpointTests is
    /// what enforces it now - see the copy-semantics tests there before changing anything here.
    /// </para>
    /// </summary>
    public ClassType ClassType { get; set; } = null!;

    /// <summary>
    /// Who runs it (prd-v2 FR-009). An account, not a string.
    ///
    /// <para>
    /// It used to be free text precisely because the product shipped without a Trainer role, so
    /// there was no person to point at. S-04 added the role and S-06 points at it: only an ACTIVE
    /// account holding <c>Trainer</c> may be assigned, so the schedule names someone the system
    /// knows. The guest instructor without an account is unsupported (prd-v2 Open Question 1).
    /// </para>
    ///
    /// <para>
    /// Identity's default string key. Unlike <see cref="ClassTypeId"/> this IS mutable - reassigning
    /// a class to another trainer is ordinary admin work.
    /// </para>
    /// </summary>
    public string InstructorUserId { get; set; } = string.Empty;

    /// <summary>
    /// The instructor's account. READ SIDE ONLY, same contract as <see cref="ClassType"/>: it exists
    /// so the read queries can project <c>DisplayName</c> in one statement. Assignment goes through
    /// <see cref="InstructorUserId"/> after the endpoint has validated the role and status.
    ///
    /// <para>
    /// Note what this does NOT guarantee: an account referenced here may later be blocked, or have
    /// its Trainer role revoked, and nothing refuses either action or flags the classes left behind
    /// (an accepted risk of this slice). Validation happens on write, not on read.
    /// </para>
    /// </summary>
    public ApplicationUser Instructor { get; set; } = null!;
}
