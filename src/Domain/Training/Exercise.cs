namespace po_prostu_silka.Domain.Training;

/// <summary>
/// One entry in the exercise library (prd.md FR-018, FR-019) - the first entity in the training
/// context, and what a training plan will be assembled from in S-11.
///
/// <para>
/// EVERYTHING EXCEPT THE NAME IS OPTIONAL, DELIBERATELY. The library is worth nothing until dozens
/// of exercises exist, and an admin who must fill eight fields to record "Wyciskanie sztangi leżąc"
/// records nothing at all. A name alone is a valid entry; the prose fills in as someone finds time.
/// Absent is <c>null</c>, never an empty string, so "no instructions" has one representation.
/// </para>
///
/// <para>
/// The video is stored as a bare YouTube id, not as the URL the admin pasted - see
/// <see cref="YouTubeVideoId"/> for why. The thumbnail on the list and the player on the detail
/// screen are both composed from it.
/// </para>
///
/// <para>
/// S-10 manages the library. It does NOT reference exercises from anywhere: training plans, the
/// member-facing view and the ordering of exercises within a plan are all S-11.
/// </para>
/// </summary>
public class Exercise
{
    public Guid Id { get; set; }

    /// <summary>
    /// What the exercise is called, e.g. "Wyciskanie sztangi leżąc". Unique among ACTIVE exercises
    /// only - see ExerciseConfiguration's filtered index. Deactivating releases the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What the exercise is, in a sentence or two. OPTIONAL.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// The muscle group worked, e.g. "klatka piersiowa". OPTIONAL, and deliberately FREE TEXT rather
    /// than an enum or a dictionary table: nothing reads it programmatically yet - the PRD never
    /// asks to filter or group by it - so a closed vocabulary would buy strictness nobody pays for.
    /// The form suggests values already used in the library, which is what keeps the wording
    /// consistent in practice. If a later slice needs to group by this, the migration is manual
    /// mapping over free text, which is why the suggestions matter.
    /// </summary>
    public string? MuscleGroup { get; set; }

    /// <summary>How demanding it is, e.g. "średnie". OPTIONAL, free text for the same reason as
    /// <see cref="MuscleGroup"/>, and suggested from existing values the same way.</summary>
    public string? Difficulty { get; set; }

    /// <summary>What is needed to do it, e.g. "sztanga, ławka płaska". OPTIONAL.</summary>
    public string? Equipment { get; set; }

    /// <summary>How to set up before the first rep - loading, bench height, collar checks. OPTIONAL.</summary>
    public string? Preparation { get; set; }

    /// <summary>Where the body starts - grip width, foot placement, back arch. OPTIONAL.</summary>
    public string? StartingPosition { get; set; }

    /// <summary>How the rep is performed, the longest field and the one a member reads mid-set. OPTIONAL.</summary>
    public string? Execution { get; set; }

    /// <summary>
    /// The 11-character YouTube id of the instructional video, or null when there is none. Never a
    /// URL: see <see cref="YouTubeVideoId"/>. The link rot this invites is a risk the PRD accepts
    /// explicitly (FR-019) - self-hosted video is out of scope.
    /// </summary>
    public string? VideoId { get; set; }

    /// <summary>
    /// Whether the exercise is offered. Hard deletion is ruled out for the same reason it is for
    /// class types: S-11's plans will reference exercises, and a deleted row would either orphan a
    /// plan or be blocked by the foreign key. A bool rather than an enum because there are exactly
    /// two states.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the admin added it. Not shown to members; useful for ordering and diagnostics.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
