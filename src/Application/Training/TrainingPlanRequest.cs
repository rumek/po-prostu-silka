using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Create/edit payload. Same shape for both - an edit replaces the name and the ENTIRE item list.
///
/// <para>
/// <see cref="MemberId"/> is carried on edit too, and is validated to match the plan being
/// edited rather than ignored. Silently ignoring it would let a stale browser tab move a plan between
/// members and see the write succeed; refusing tells the client its state is old.
/// </para>
/// </summary>
public record TrainingPlanRequest(
    string Name,
    Guid MemberId,
    IReadOnlyList<TrainingPlanItemRequest> Items);
