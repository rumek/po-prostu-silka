using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Why an exercise write was refused. All 400 except <c>name_taken</c>, which is a 409 — it is a
/// conflict with existing state rather than bad input.
///
/// <para>
/// Reasons: <c>missing_field</c>, <c>name_too_long</c>, <c>description_too_long</c>,
/// <c>muscle_group_too_long</c>, <c>difficulty_too_long</c>, <c>equipment_too_long</c>,
/// <c>preparation_too_long</c>, <c>starting_position_too_long</c>, <c>execution_too_long</c>,
/// <c>invalid_video_url</c>, <c>name_taken</c>. Adding one here means adding it to the SPA's
/// ExerciseFailure union too — that type mirrors this one field for field, and the form maps each
/// reason onto the control that owns it.
/// </para>
/// </summary>
public record ExerciseFailure(string Reason);
