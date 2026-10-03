using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The single construction of <see cref="ClassGroupSummary"/> from an entity.
/// </summary>
internal static class ClassGroupProjection
{
    /// <param name="rosterCount">S-37: how many members the group's fixed roster holds - zero for a group
    /// created this instant.</param>
    public static ClassGroupSummary ToDto(ClassGroup entity, int rosterCount = 0) =>
        new(entity.Id,
            entity.Name,
            entity.Description,
            entity.DefaultDurationMinutes,
            entity.DefaultCapacity,
            entity.IsActive,
            entity.CreatedAt,
            rosterCount);
}
