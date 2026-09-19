using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Create/edit payload. Same shape for both — an edit replaces every field it is allowed to change.
///
/// <para>
/// A FORM OF SELECTIONS, NOT OF TEXT (prd-v2 US-01). There is no name and no room to type;
/// <see cref="ClassTypeId"/> and <see cref="InstructorMemberId"/> are references the client picked from
/// two lists. What remains typed are the two numbers — and they arrive here PREFILLED from the type's
/// defaults, which the admin may have overridden for this session.
/// </para>
///
/// <para>
/// <see cref="ClassTypeId"/> is required on an edit too, but only so the server can refuse a change
/// to it: the type is immutable once an occurrence exists (<c>class_type_immutable</c>).
/// </para>
/// </summary>
public record ClassRequest(
    Guid ClassTypeId,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    Guid InstructorMemberId,
    int Capacity);
