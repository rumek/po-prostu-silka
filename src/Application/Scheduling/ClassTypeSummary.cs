using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One class type as the admin's list and form see it. This is a CONTRACT the SPA's class-type
/// service mirrors — renaming a field breaks both screens silently.
///
/// <para>
/// <see cref="IsActive"/> crosses the wire as a bool rather than a status name, unlike
/// <see cref="ScheduledClass.Status"/>: there are exactly two states and no enum behind it, so
/// there is no numbering for a name to protect.
/// </para>
///
/// <para>
/// The two <c>Default*</c> fields keep their prefix all the way out to the client. S-06 copies them
/// onto an occurrence at creation; nothing ever resolves an occurrence's capacity through them
/// (prd-v2 FR-007), and the naming is what keeps that obvious at the call site.
/// </para>
/// </summary>
public record ClassTypeSummary(
    Guid Id,
    string Name,
    string? Description,
    int DefaultDurationMinutes,
    int DefaultCapacity,
    bool IsActive,
    DateTimeOffset CreatedAt);
