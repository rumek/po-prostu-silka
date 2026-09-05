namespace po_prostu_silka.Domain.Training;

/// <summary>
/// A named, ordered training plan assigned to one member (prd.md FR-015, FR-016, FR-017).
///
/// <para>
/// AT MOST ONE ACTIVE PLAN PER MEMBER. Assigning a new plan archives the old one rather than editing
/// it, which is what FR-016 asks for and what makes "the member's plan" a question with one answer.
/// The rule is carried by TrainingPlanConfiguration's filtered unique index on (MemberUserId) WHERE
/// Status = 0, which makes two active rows for one member unrepresentable. TrainingPlanEndpoints
/// archives-and-inserts inside a retry loop over <see cref="ConcurrencyStamp"/>, but that loop is a
/// second line rather than the first - see the remarks on <see cref="ConcurrencyStamp"/>.
/// </para>
///
/// <para>
/// FLAT BY DESIGN, FOR NOW. FR-015 specifies an ordered exercise list, not a week of training days,
/// and this ships flat. <see cref="TrainingPlanItem"/> is nevertheless a table of its own carrying an
/// explicit position rather than an owned collection, precisely so that introducing a day between the
/// plan and its items later is a migration that re-parents rows instead of a rewrite.
/// </para>
///
/// Anemic on purpose, like <see cref="Exercise"/> and Class: the invariants live in
/// TrainingPlanEndpoints, which is where the archive, the insert and the stamp rotation have to sit
/// together to be atomic.
/// </summary>
public class TrainingPlan
{
    public Guid Id { get; set; }

    /// <summary>
    /// What the trainer calls it, e.g. "Masa - jesień". Required: two plans for one member differ
    /// otherwise only by a date, which is nothing to refer to in a conversation on the gym floor.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whose plan it is. Identity's default string key, like Booking.MemberUserId.</summary>
    public string MemberUserId { get; set; } = string.Empty;

    /// <summary>
    /// The member's account. READ SIDE ONLY - it exists so the trainer's plan list can project
    /// <c>DisplayName</c> in one statement.
    /// </summary>
    public ApplicationUser Member { get; set; } = null!;

    /// <summary>
    /// The trainer or admin who assigned it. Taken from the authenticated principal at write time,
    /// never from the request body.
    ///
    /// <para>
    /// DISPLAY ONLY, NOT AN AUTHORIZATION BOUNDARY. Any account holding Trainer or Admin may edit any
    /// plan - there is no trainer-to-member relationship in this product, so "whose plan is this to
    /// edit" has no answer to enforce. The column exists because the member's screen says "Plan od:
    /// ...", and a field nothing reads would not have earned its migration.
    /// </para>
    /// </summary>
    public string AssignedByUserId { get; set; } = string.Empty;

    /// <summary>The author's account. READ SIDE ONLY, same contract as <see cref="Member"/>.</summary>
    public ApplicationUser AssignedBy { get; set; } = null!;

    /// <summary>
    /// Whether this is the plan the member is following. Only <see cref="TrainingPlanStatus.Active"/>
    /// is ever read by a screen.
    /// </summary>
    public TrainingPlanStatus Status { get; set; } = TrainingPlanStatus.Active;

    /// <summary>When the plan was assigned. Shown to the member; orders the trainer's list.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When a later assignment superseded it, or null while the plan is active. Nullable is the
    /// point: it distinguishes "still current" from "archived at an unknown time", the same way
    /// Booking.CancelledAt does.
    /// </summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>
    /// SECOND LINE OF DEFENCE ON ASSIGNMENT, FIRST LINE ON EDIT. Read it before touching any code
    /// that changes a plan's <see cref="Status"/>.
    ///
    /// <para>
    /// Assignment is a read-then-write sequence - find the member's active plan, archive it, insert
    /// the new one - and two trainers doing it at the same instant would both find the same old plan
    /// and both insert. This column is configured as a concurrency token, so EF puts it in the WHERE
    /// clause of the UPDATE that archives the old plan: whoever commits second matches zero rows and
    /// is told to try again, having written nothing.
    /// </para>
    ///
    /// <para>
    /// IT IS NOT WHAT MAKES THE RACE SAFE, THOUGH - MEASURED, NOT ASSUMED. Commenting the rotation
    /// out of the assignment handler leaves the race test green; weakening the filtered unique index
    /// instead leaves six racers with six active plans. Every assignment INSERTS a new active row,
    /// and it is the index that rejects the second one, so the invariant holds on the database. This
    /// is the reverse of Class.ConcurrencyStamp, where the stamp is the whole mechanism because the
    /// booking count has no index to express it. Here the rotation earns its place by failing a
    /// loser earlier and more cheaply than a unique violation would, and by guarding the EDIT path,
    /// where nothing is inserted and the index therefore says nothing: two trainers editing one plan
    /// cannot overwrite each other silently only because this rotates.
    /// </para>
    ///
    /// <para>
    /// A string rather than a SQL rowversion, following Class.ConcurrencyStamp and Identity's own.
    /// </para>
    /// </summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The exercises, in the trainer's order. Read by every screen and REPLACED WHOLESALE on write -
    /// the endpoints clear this list and re-add from the request rather than reconciling row by row,
    /// which is what keeps <see cref="TrainingPlanItem.Position"/> dense and collision-free without a
    /// unique index that a partial reorder would trip over.
    ///
    /// <para>
    /// This collection is deliberately unlike the Class-to-Bookings navigation that Booking.Class
    /// warns about. That one was refused because a write path could count through it to check
    /// capacity; here the items ARE the plan's content and no invariant is derived from counting
    /// them, so the hazard does not transfer.
    /// </para>
    /// </summary>
    public List<TrainingPlanItem> Items { get; set; } = [];
}
