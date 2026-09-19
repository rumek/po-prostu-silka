using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Why a plan write was refused.
///
/// <para>
/// 400 (bad input): <c>missing_field</c>, <c>name_too_long</c>, <c>no_items</c>,
/// <c>too_many_items</c>, <c>invalid_sets</c>, <c>reps_too_long</c>, <c>invalid_weight</c>,
/// <c>invalid_rest</c>, <c>invalid_duration</c>, <c>note_too_long</c>, <c>unknown_exercise</c>,
/// <c>inactive_exercise</c>,
/// <c>duplicate_exercise</c>.
/// </para>
///
/// <para>
/// 409 (conflict with existing state): <c>member_not_found</c>, <c>member_not_active</c>,
/// <c>member_changed</c>, <c>conflict</c>.
/// </para>
///
/// <para>
/// Adding one here means adding it to the SPA's TrainingPlanFailure union too - that type mirrors
/// this one, and the builder maps each reason onto the control that owns it.
/// </para>
/// </summary>
public record TrainingPlanFailure(string Reason);
