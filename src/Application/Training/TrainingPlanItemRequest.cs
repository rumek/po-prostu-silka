using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// One exercise inside a create/edit payload.
///
/// <para>
/// THERE IS NO POSITION FIELD, and its absence is the contract: the ORDER OF THE ARRAY IS THE ORDER
/// OF THE PLAN. A client that could send positions could send duplicates or gaps, and the server
/// would have to decide what those mean; numbering them here on write makes the dense, collision-free
/// sequence a property of the write path rather than a hope about the caller.
/// </para>
/// </summary>
public record TrainingPlanItemRequest(
    Guid ExerciseId,
    int? Sets,
    string? Reps,
    decimal? WeightKg,
    int? RestSeconds,
    string? Note,
    int? DurationSeconds);
