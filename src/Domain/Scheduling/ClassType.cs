namespace po_prostu_silka.Domain.Scheduling;

/// <summary>
/// The definition a class occurrence is built from (prd-v2 FR-004) - the second entity in the
/// scheduling context, and the one that gives a class an identity that outlives any single week.
///
/// <para>
/// THE BINDING IS ASYMMETRIC, AND THAT ASYMMETRY IS LOAD-BEARING (prd-v2 FR-007). <see cref="Name"/>
/// and <see cref="Description"/> are IDENTITY: an occurrence resolves them BY REFERENCE, so
/// correcting a typo here corrects it everywhere, past occurrences included. The two
/// <c>Default*</c> numbers are TEMPLATE VALUES: they are COPIED onto an occurrence when it is
/// created and never re-read afterwards.
/// </para>
///
/// <para>
/// Getting that backwards is the one mistake this type cannot survive. Capacity resolved through
/// the definition would let an edit here change the capacity of a class that already has bookings -
/// the value the no-overbooking guarantee is checked against - which is exactly the PRD's headline
/// guardrail. The <c>Default</c> prefix is the guardrail: at the S-06 call site,
/// <see cref="DefaultCapacity"/> cannot be mistaken for the occurrence's own capacity.
/// </para>
///
/// <para>
/// S-05 defines and manages types. It does NOT yet build occurrences from them - no selector, no
/// prefill, no name resolution. That is S-06.
/// </para>
/// </summary>
public class ClassType
{
    public Guid Id { get; set; }

    /// <summary>
    /// What the member sees, e.g. "Joga dla początkujących". Unique among ACTIVE types only - see
    /// ClassTypeConfiguration's filtered index. Deactivating a type releases its name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What the class actually is, for a member deciding whether to book. OPTIONAL: forcing prose on
    /// a definition being created in a hurry produces the word "opis", which is worse than nothing.
    /// Null rather than empty string when absent, so "no description" has one representation.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// How long a class of this type usually runs. A DEFAULT - copied onto the occurrence at
    /// creation and overridable there, because a single session legitimately varies.
    /// </summary>
    public int DefaultDurationMinutes { get; set; }

    /// <summary>
    /// How many spots a class of this type usually has. A DEFAULT, and the copy semantics matter
    /// more here than anywhere else in the model - see the type-level remarks.
    /// </summary>
    public int DefaultCapacity { get; set; }

    /// <summary>
    /// Whether the type is offered. FR-006 rules out hard deletion: an inactive type disappears from
    /// every selection while the occurrences that reference it stay intact. A bool rather than an
    /// enum because there are exactly two states and an enum would imply others.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the admin defined it. Not shown to members; useful for ordering and diagnostics.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
