using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// A full plan with its items in order. ONE SHAPE SERVES BOTH the trainer's edit load and the
/// member's read - the two want exactly the same fields, and a second contract would be two things to
/// keep in step for no gain.
/// </summary>
public record TrainingPlanDetail(
    Guid Id,
    string Name,
    Guid MemberId,
    string MemberDisplayName,
    string AssignedByDisplayName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TrainingPlanItemView> Items);
