using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The single construction of <see cref="ClassTypeSummary"/> from an entity.
/// </summary>
internal static class ClassTypeProjection
{
    public static ClassTypeSummary ToDto(ClassType entity) =>
        new(entity.Id,
            entity.Name,
            entity.Description,
            entity.DefaultDurationMinutes,
            entity.DefaultCapacity,
            entity.IsActive,
            entity.CreatedAt);
}
