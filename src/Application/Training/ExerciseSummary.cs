using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// One exercise as the admin's list, detail screen and form see it. This is a CONTRACT the SPA's
/// exercise service mirrors — renaming a field breaks three screens silently.
///
/// <para>
/// ONE SHAPE SERVES BOTH THE LIST AND THE DETAIL SCREEN. A trimmed list DTO would save a few
/// kilobytes on a library of dozens of rows and cost a second contract to keep in step with this
/// one; the list simply ignores the fields it does not render.
/// </para>
///
/// <para>
/// <see cref="VideoId"/> crosses the wire as the bare 11-character id, never a URL. The client
/// composes the thumbnail (img.youtube.com) and the player (youtube-nocookie.com) from it, so the
/// API carries no derived URLs that could drift from each other.
/// </para>
/// </summary>
public record ExerciseSummary(
    Guid Id,
    string Name,
    string? Description,
    string? MuscleGroup,
    string? Difficulty,
    string? Equipment,
    string? Preparation,
    string? StartingPosition,
    string? Execution,
    string? VideoId,
    bool IsActive,
    DateTimeOffset CreatedAt);
