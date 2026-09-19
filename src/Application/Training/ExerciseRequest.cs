using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Create/edit payload. Same shape for both — an edit replaces every field.
///
/// <para>
/// It carries <c>VideoUrl</c>, not a video id: the client sends whatever the admin pasted and the
/// SERVER owns the parsing (<see cref="YouTubeVideoId"/>). Accepting an id here instead would move
/// that parse into the browser, where a second implementation would eventually disagree with this
/// one.
/// </para>
///
/// <para>
/// <c>IsActive</c> is deliberately ABSENT, exactly as in <see cref="Scheduling.ClassTypeRequest"/>:
/// activation has its own two endpoints, so a careless edit cannot resurrect a retired exercise.
/// </para>
/// </summary>
public record ExerciseRequest(
    string Name,
    string? Description,
    string? MuscleGroup,
    string? Difficulty,
    string? Equipment,
    string? Preparation,
    string? StartingPosition,
    string? Execution,
    string? VideoUrl);
