using Microsoft.Data.SqlClient;
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
    /// <summary>
    /// SQL Server's two "you broke a uniqueness rule" errors: 2601 is a unique INDEX violation (what
    /// a filtered unique index raises) and 2627 is a unique CONSTRAINT or primary key violation.
    /// Everything else is a genuine failure and must keep throwing.
    /// </summary>
    private const int UniqueIndexViolation = 2601;

    private const int UniqueConstraintViolation = 2627;

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

    public async Task<SaveOutcome> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return SaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return SaveOutcome.ConcurrencyConflict;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A unique index rejected the row. Reported rather than thrown so the caller can answer
            // with its own domain refusal instead of a 500 - the gap three implementation reviews
            // found and deferred, closed here because the mapping needs EF Core and SqlClient types
            // that Application is not allowed to see.
            return SaveOutcome.UniqueViolation;
        }
    }

    public void DiscardChanges() => db.ChangeTracker.Clear();

    /// <summary>
    /// Whether this update failed because a unique index or constraint rejected it.
    ///
    /// EF Core wraps the provider's exception, so the SQL Server error number is on the INNER
    /// exception. Matching on the number rather than the message keeps this independent of the
    /// server's language, which is not something this application controls.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql
        && sql.Number is UniqueIndexViolation or UniqueConstraintViolation;
}
