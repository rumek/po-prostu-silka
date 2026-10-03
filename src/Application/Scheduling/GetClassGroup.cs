using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One group in full.
/// </summary>
public static class GetClassGroup
{
    /// <summary>
    /// One group, for the edit form — so opening /admin/class-groups/:id directly costs one row
    /// instead of the whole list. Same reasoning as ClassEndpoints.GetByIdAsync.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid id,
        IClassGroupStore store,
        IGroupRosterStore roster,
        CancellationToken cancellationToken)
    {
        var found = await store.FindAsync(id, cancellationToken);

        return found is null ? Results.NotFound() : Results.Ok(ClassGroupProjection.ToDto(
            found, await roster.CountAsync(found.Id, cancellationToken)));
    }
}
