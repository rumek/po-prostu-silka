using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Why a class-type write was refused. All 400 except <c>name_taken</c>, which is a 409 — it is a
/// conflict with existing state rather than bad input, exactly like <c>room_conflict</c>.
///
/// <para>
/// Reasons: <c>missing_field</c>, <c>name_too_long</c>, <c>description_too_long</c>,
/// <c>invalid_duration</c>, <c>invalid_capacity</c>, <c>name_taken</c>. Adding one here means adding
/// it to the SPA's ClassTypeFailure union too — that type mirrors this one field for field.
/// </para>
/// </summary>
public record ClassTypeFailure(string Reason);
