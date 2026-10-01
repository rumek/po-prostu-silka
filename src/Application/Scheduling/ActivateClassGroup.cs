using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Brings a retired group back (FR-007).
/// </summary>
public static class ActivateClassGroup
{
    /// <summary>
    /// Puts a retired group back into circulation.
    ///
    /// <para>
    /// THE UNIQUENESS CHECK HERE IS NOT DECORATION. Deactivating releases a name, so another group
    /// may have claimed it in the meantime. Reactivating blindly would violate
    /// IX_ClassGroups_Name_Active and surface as an unhandled DbUpdateException — a 500 for what is
    /// really a conflict the admin can resolve. The request carries no name, which is exactly why
    /// this is easy to miss.
    /// </para>
    ///
    /// <para>
    /// Checked unconditionally rather than only when currently inactive: excluding this group's own
    /// id makes the check a no-op for an already-active group (the filtered index guarantees no other
    /// active group holds the name), so idempotency costs nothing and there is one path to reason
    /// about instead of two.
    /// </para>
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

        if (await store.IsNameTakenAsync(existing.Name, id, cancellationToken))
        {
            return Results.Json(new ClassGroupFailure("name_taken"), statusCode: 409);
        }

        existing.IsActive = true;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ClassGroupProjection.ToDto(existing));
    }
}
