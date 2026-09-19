using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One class type in full.
/// </summary>
public static class GetClassType
{
    /// <summary>
    /// One type, for the edit form — so opening /admin/class-types/:id directly costs one row
    /// instead of the whole list. Same reasoning as ClassEndpoints.GetByIdAsync.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid id,
        IClassTypeStore store,
        CancellationToken cancellationToken)
    {
        var found = await store.FindAsync(id, cancellationToken);

        return found is null ? Results.NotFound() : Results.Ok(ClassTypeProjection.ToDto(found));
    }
}
