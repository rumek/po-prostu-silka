using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// One plan as the trainer's list sees it. A CONTRACT the SPA's training-plan service mirrors.
///
/// <para>
/// Trimmed rather than sharing <see cref="TrainingPlanDetail"/>'s shape, which is the opposite of
/// what <see cref="ExerciseSummary"/> does - and deliberately so. The exercise list carries a dozen
/// rows of prose because the same screen also renders the detail; this list renders none of a plan's
/// items, and shipping every item of every plan in the club to draw a table of names would be paying
/// for something no pixel uses.
/// </para>
/// </summary>
public record TrainingPlanSummary(
    Guid Id,
    string Name,
    Guid MemberId,
    string MemberDisplayName,
    string AssignedByDisplayName,
    DateTimeOffset CreatedAt,
    int ItemCount);
