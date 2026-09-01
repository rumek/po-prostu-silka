namespace po_prostu_silka.Application.Persistence;

/// <summary>
/// Commits whatever the current request has changed, as one transaction.
///
/// Exists because a handler sometimes has to make a domain change and enqueue an outbox message in
/// the SAME unit of work — S-01's approve is the first such case: the status flip and the approval
/// email must land together or not at all. <see cref="Notifications.IOutboxEnqueuer"/> deliberately
/// does not save, so somebody has to, and Application cannot reference EF Core (AGENTS.md layering).
///
/// IMPORTANT: this is one SaveChangesAsync, which is atomic on its own and covered by the retry
/// strategy. It does NOT open an explicit transaction. If a caller ever needs several saves to be
/// atomic, it must go through Database.CreateExecutionStrategy().ExecuteAsync(...) in Infrastructure,
/// because EnableRetryOnFailure is on (Program.cs) and BeginTransaction otherwise throws at runtime.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
