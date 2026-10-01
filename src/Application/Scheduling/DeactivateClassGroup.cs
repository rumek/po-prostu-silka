using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Retires a group without deleting it (FR-007).
/// </summary>
public static class DeactivateClassGroup
{
    /// <summary>
    /// Retires a group: it leaves every selection, and the occurrences referencing it are untouched
    /// (FR-006). No uniqueness check is needed — deactivating only ever RELEASES a name.
    ///
    /// Idempotent. Deactivating an already-inactive group is a 200, not a refusal: nothing is gained
    /// by failing, and the screen would have to explain an error that means "already done".
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid id,
        IClassGroupStore store,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        existing.IsActive = false;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ClassGroupProjection.ToDto(existing));
    }
}
