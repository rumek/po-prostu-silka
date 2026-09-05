namespace po_prostu_silka.Domain.Training;

/// <summary>
/// One exercise inside a <see cref="TrainingPlan"/>, at a known position, with its prescription
/// (prd.md FR-015).
///
/// <para>
/// EVERY PRESCRIPTION FIELD IS OPTIONAL, following <see cref="Exercise"/>'s own posture. A trainer who
/// wants to write only "rozgrzewka, luźno" against an exercise should not have to invent a set count
/// to save the plan. Absent is <c>null</c>, never an empty string or a zero.
/// </para>
///
/// <para>
/// ROWS ARE REPLACED, NOT EDITED. TrainingPlanEndpoints clears a plan's items and re-inserts them from
/// the request on every write, assigning <see cref="Position"/> from the array order. That is what
/// makes reordering safe without a unique index on (plan, position) - a row-by-row reorder would pass
/// through states where two rows share a position, and SQL Server checks a unique index per statement,
/// not per transaction.
/// </para>
/// </summary>
public class TrainingPlanItem
{
    /// <summary>
    /// NOT STABLE ACROSS AN EDIT. Read this before keying anything on it.
    ///
    /// <para>
    /// Editing a plan replaces its item rows wholesale - TrainingPlanStore.ReplaceItems deletes every
    /// row and inserts the new list with fresh ids - so the same prescribed exercise comes back with a
    /// different <see cref="Id"/> after every save. That is the direct price of the replace-wholesale
    /// write model, and the model is what makes reordering trivially correct: no row carries a
    /// position to renumber, so a drag cannot produce a gap or a duplicate.
    /// </para>
    ///
    /// <para>
    /// Nothing keys on this id across requests today - it travels to the client only as a rendering
    /// key. A future feature that DOES (a workout log, per-exercise progress) cannot simply store it:
    /// its rows would be orphaned the first time the trainer edits the plan. Such a feature needs
    /// either a stable natural key (the plan plus the exercise) or a write path that reconciles rows
    /// instead of replacing them - and reconciling is what produced the EF collection-fixup bug
    /// documented on ReplaceItems, so it is a decision to make deliberately, not by accident.
    /// </para>
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>The plan this belongs to. Items have no life outside their plan.</summary>
    public Guid TrainingPlanId { get; set; }

    /// <summary>The exercise being prescribed.</summary>
    public Guid ExerciseId { get; set; }

    /// <summary>
    /// The exercise. READ SIDE ONLY - it exists so the plan projection can pull the name in one
    /// statement.
    ///
    /// <para>
    /// The foreign key behind it is RESTRICT, which is the whole reason S-10 chose deactivation over
    /// deletion for the library. A plan keeps showing an exercise that was deactivated after the plan
    /// was assigned: a member's plan does not rearrange itself because of library housekeeping, and
    /// the read path deliberately does not filter on IsActive.
    /// </para>
    /// </summary>
    public Exercise Exercise { get; set; } = null!;

    /// <summary>
    /// Where it sits in the plan, 0-based and dense. Assigned from the request array's order on every
    /// write - the API has no position field, because the order of the array IS the order.
    /// </summary>
    public int Position { get; set; }

    /// <summary>How many working sets, e.g. 4. OPTIONAL.</summary>
    public int? Sets { get; set; }

    /// <summary>
    /// How many repetitions, e.g. "8-12" or "do upadku". OPTIONAL.
    ///
    /// <para>
    /// A STRING, AND THE ONE FIELD HERE THAT BREAKS THE NUMERIC SYMMETRY. A range and a stop condition
    /// are how prescriptions are actually written on a gym floor, and an int would force both into
    /// <see cref="Note"/>, where nothing validates or displays them as a prescription. The cost is
    /// accepted knowingly: nothing can compute training volume from this column.
    /// </para>
    /// </summary>
    public string? Reps { get; set; }

    /// <summary>
    /// The working weight in kilograms, e.g. 62.50. OPTIONAL - bodyweight exercises have none.
    ///
    /// <para>
    /// THE FIRST DECIMAL COLUMN IN THIS SCHEMA, stored as decimal(5,2): up to 999.99 kg in 0.01 steps,
    /// which covers plate math (2.5 kg jumps), microplates, and any load a person will move. The
    /// precision is set explicitly in TrainingPlanItemConfiguration rather than left to EF's
    /// decimal(18,2) default, because whatever this column does becomes the convention every later
    /// decimal is read against.
    /// </para>
    /// </summary>
    public decimal? WeightKg { get; set; }

    /// <summary>Rest between sets, in seconds, e.g. 90. OPTIONAL. Seconds rather than a
    /// TimeSpan because the form collects a number and nothing does arithmetic on it.</summary>
    public int? RestSeconds { get; set; }

    /// <summary>Anything the trainer wants the member to read against this exercise. OPTIONAL.</summary>
    public string? Note { get; set; }
}
