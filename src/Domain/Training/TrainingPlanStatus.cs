namespace po_prostu_silka.Domain.Training;

/// <summary>
/// Whether a training plan is the one its member is currently following, or a superseded one kept
/// for the record (prd.md FR-016).
///
/// <para>
/// THE NUMERIC VALUES ARE PINNED AND LOAD-BEARING. TrainingPlanConfiguration's filtered unique index
/// names <c>Active</c> as the literal "[Status] = 0" - SQL Server has no idea this enum exists, so a
/// renumbering here would silently move the index onto archived plans and let a member hold two
/// active ones. The same dependency exists on BookingStatus and ClassStatus, for the same reason.
/// </para>
/// </summary>
public enum TrainingPlanStatus
{
    /// <summary>The plan the member sees at /my-plan. At most one per member - see the filtered index.</summary>
    Active = 0,

    /// <summary>
    /// Superseded by a later assignment. Kept rather than deleted, but nothing reads it: prd.md:164
    /// cuts plan history from the MVP, so there is no history screen. The row exists so that adding
    /// one later is a feature rather than a data-recovery exercise.
    /// </summary>
    Archived = 1,
}
