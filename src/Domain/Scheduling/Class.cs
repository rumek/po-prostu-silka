namespace po_prostu_silka.Domain.Scheduling;

/// <summary>
/// One group class on the schedule (FR-007, FR-011) - the first entity in the scheduling context.
///
/// This is the aggregate S-04 books against, so its shape outlives this slice: <see cref="Capacity"/>
/// is what the no-overbooking guarantee is checked against, and <see cref="Status"/> is what S-05
/// transitions. Adding a field here later is cheap; changing the meaning of one is not.
///
/// No booking count lives here. A denormalized counter would pre-commit S-04's concurrency design -
/// the load-bearing correctness decision of the milestone - and that is not this slice's to make.
/// Free spots are projected at read time (see IClassScheduleQuery).
/// </summary>
public class Class
{
    public Guid Id { get; set; }

    /// <summary>What the member sees first in the schedule, e.g. "Joga dla początkujących".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// When the class starts, in UTC like every other timestamp in this app. The SPA groups the
    /// schedule into days by the BROWSER's local date and renders with DatePipe; the server never
    /// groups and never hardcodes a timezone.
    /// </summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>
    /// How long the class runs. Stored as minutes rather than an end timestamp so the two can never
    /// contradict each other, and so the room-overlap check is plain arithmetic
    /// (StartsAt + DurationMinutes), which EF translates to DATEADD.
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>Where in the club. Half of the overlap invariant: one room, one class at a time.</summary>
    public string Room { get; set; } = string.Empty;

    /// <summary>
    /// Who runs it. FREE TEXT, deliberately - the PRD's Non-Goals rule out a Trainer role ("User and
    /// Admin only"), so there is no account to link to, and a small club's instructors may not have
    /// one at all.
    /// </summary>
    public string Instructor { get; set; } = string.Empty;

    /// <summary>
    /// How many spots exist. S-04 enforces that bookings never exceed this, even under simultaneous
    /// requests - the PRD's headline guardrail.
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>Gates visibility on the schedule. Only ever Scheduled in S-03; see <see cref="ClassStatus"/>.</summary>
    public ClassStatus Status { get; set; } = ClassStatus.Scheduled;

    /// <summary>When the admin created it. Not shown to members; useful for ordering and diagnostics.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Which <see cref="ClassType"/> this occurrence instantiates (prd-v2 FR-008).
    ///
    /// <para>
    /// NULLABLE ON PURPOSE, AND ONLY FOR NOW. S-05 lands the column so the definition layer has
    /// somewhere to attach, but nothing writes it yet: the create form cannot supply a type until
    /// S-06 adds the selector, and a NOT NULL column would break every existing class write today.
    /// S-06 populates it and tightens it to required in its own migration.
    /// </para>
    ///
    /// <para>
    /// No navigation property, deliberately. Nothing traverses this in S-05, and offering one would
    /// invite S-06 to read <see cref="ClassType.DefaultCapacity"/> through it - the exact inversion
    /// of FR-007 that the no-overbooking guarantee cannot survive. Capacity is COPIED.
    /// </para>
    /// </summary>
    public Guid? ClassTypeId { get; set; }
}
