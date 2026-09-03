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
/// This is the aggregate bookings are made against: <see cref="Capacity"/> is what the no-overbooking
/// guarantee is checked against, and <see cref="Status"/> is what S-09 transitions. Adding a field
/// here later is cheap; changing the meaning of one is not.
///
/// STILL NO BOOKING COUNT HERE, and now by a settled decision rather than a deferred one. S-08 chose
/// a live count of Active Booking rows over a denormalized counter, so free spots stay projected at
/// read time (see IClassScheduleQuery) and there is no second source of truth to drift. What S-08 did
/// add is <see cref="ConcurrencyStamp"/> - the mechanism that makes counting-then-inserting atomic.
/// </summary>
public class Class
{
    public Guid Id { get; set; }

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
    /// THE NO-OVERBOOKING GUARANTEE LIVES ON THIS LINE. Read it before touching any code that changes
    /// how many spots on this class are taken.
    ///
    /// <para>
    /// Booking is a read-then-write sequence - count the Active bookings, compare against
    /// <see cref="Capacity"/>, insert a row - and two members doing it at the same instant would both
    /// pass the count and both insert. This column is configured as a concurrency token, so EF puts it
    /// in the WHERE clause of every UPDATE against Classes: whoever commits second matches zero rows
    /// and is told to try again, having written nothing.
    /// </para>
    ///
    /// <para>
    /// THAT ONLY WORKS IF THE WRITE ROTATES IT. A booking inserts into Bookings and touches nothing
    /// here, so unless the handler explicitly assigns a new value to this property, EF issues no
    /// UPDATE against Classes at all, no WHERE clause carries the token, and both writes commit. The
    /// rotation is not bookkeeping - it IS the mechanism. Cancellation must rotate it too: a cancel
    /// and a booking racing for the same last spot must not both believe they won.
    /// </para>
    ///
    /// <para>
    /// A string rather than a SQL rowversion, following the only concurrency-token precedent this
    /// codebase has - Identity's ConcurrencyStamp, rotated by hand in MemberAdminEndpoints for exactly
    /// the same reason. A rowversion would not help here anyway: the database only bumps it when the
    /// row is updated, and the whole hazard is a write path that never updates the row.
    /// </para>
    /// </summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

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
