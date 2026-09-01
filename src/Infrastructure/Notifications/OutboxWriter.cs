using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Notifications;

/// <summary>
/// Infrastructure side of <see cref="IOutboxWriter"/>. Exists so Application can enqueue without
/// referencing EF Core, which AGENTS.md reserves for Infrastructure.
///
/// Deliberately does not save: the caller owns the unit of work, so an enqueue can be atomic with
/// whatever domain change triggered it.
/// </summary>
public class OutboxWriter(AppDbContext db) : IOutboxWriter
{
    public void Add(OutboxMessage message) => db.OutboxMessages.Add(message);
}
