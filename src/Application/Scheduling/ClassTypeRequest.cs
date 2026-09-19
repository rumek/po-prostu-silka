using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Create/edit payload. Same shape for both — an edit replaces every field, like
/// <see cref="ClassRequest"/>.
///
/// <para>
/// <c>IsActive</c> is deliberately ABSENT. Activation has its own two endpoints, so a careless edit
/// cannot silently resurrect a type the admin retired — the same reasoning that keeps block/unblock
/// off the member edit surface.
/// </para>
/// </summary>
public record ClassTypeRequest(
    string Name,
    string? Description,
    int DefaultDurationMinutes,
    int DefaultCapacity);
