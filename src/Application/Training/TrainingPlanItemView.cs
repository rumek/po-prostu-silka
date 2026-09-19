using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// One exercise inside a plan, as every screen reads it.
///
/// <para>
/// Carries <see cref="ExerciseName"/> denormalised from the exercise row so a plan renders in one
/// request. It does NOT carry the exercise's prose or video: the member's detail screen fetches those
/// per exercise through MyPlanEndpoints, because sending eight prose fields per item would make the
/// plan payload mostly text nobody has asked to read yet.
/// </para>
/// </summary>
public record TrainingPlanItemView(
    Guid Id,
    Guid ExerciseId,
    string ExerciseName,
    int Position,
    int? Sets,
    string? Reps,
    decimal? WeightKg,
    int? RestSeconds,
    string? Note,
    int? DurationSeconds,
    /// <summary>
    /// The exercise's muscle group, for the member's plan card. READ-ONLY AND DELIBERATELY ABSENT
    /// FROM <see cref="TrainingPlanItemRequest"/>: it is a fact about the exercise, not part of the
    /// prescription, so a write path that accepted it would be offering to edit the library through
    /// a plan. Nullable because Exercise.MuscleGroup is.
    /// </summary>
    string? MuscleGroup);
