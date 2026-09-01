using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Persistence;

namespace po_prostu_silka.Infrastructure.Persistence;

/// <summary>
/// Infrastructure side of <see cref="IUnitOfWork"/>. The scoped <see cref="AppDbContext"/> is the
/// same instance Identity's stores use, so an entity loaded through UserManager and a row added
/// through <see cref="Notifications.OutboxWriter"/> are both tracked here and commit together.
/// </summary>
public class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The caller's concurrency token no longer matches: another request committed first, and
            // SaveChangesAsync is atomic, so nothing of ours was written. Catching this HERE is what
            // keeps Microsoft.EntityFrameworkCore out of Application (AGENTS.md's one hard rule).
            return false;
        }
    }
}
