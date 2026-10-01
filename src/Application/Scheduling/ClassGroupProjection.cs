using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The single construction of <see cref="ClassGroupSummary"/> from an entity.
/// </summary>
internal static class ClassGroupProjection
{
    public static ClassGroupSummary ToDto(ClassGroup entity) =>
        new(entity.Id,
            entity.Name,
            entity.Description,
            entity.DefaultDurationMinutes,
            entity.DefaultCapacity,
            entity.IsActive,
            entity.CreatedAt);
}
