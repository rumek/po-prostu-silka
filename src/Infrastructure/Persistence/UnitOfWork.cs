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
}
