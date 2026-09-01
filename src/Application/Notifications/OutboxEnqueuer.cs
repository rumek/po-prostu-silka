using po_prostu_silka.Domain.Notifications;

namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// The single way anything gets into the outbox. S-01 (account approved) and S-05 (class cancelled
/// or changed) both go through here.
///
/// Messages are enqueued ALREADY RENDERED. The worker delivers bytes and never re-renders, so a
/// retry three hours later says exactly what the first attempt said, even if the underlying data
/// moved on.
/// </summary>
public interface IOutboxEnqueuer
{
    /// <summary>
    /// Queue one message for one recipient on one channel. Does NOT save — the caller controls the
    /// unit of work, so the enqueue can be atomic with whatever domain change triggered it.
    ///
    /// IMPORTANT: if you wrap that in an explicit transaction, it must go through
    /// <c>Database.CreateExecutionStrategy().ExecuteAsync(...)</c>. EnableRetryOnFailure is on
    /// (Program.cs), and its execution strategy refuses a user-initiated transaction that could span
    /// retries. Calling BeginTransaction directly throws at RUNTIME, not compile time.
    /// </summary>
    void Enqueue(NotificationChannel channel, string recipient, string subject, string body);
}

public class OutboxEnqueuer(IOutboxWriter writer, TimeProvider timeProvider) : IOutboxEnqueuer
{
    public void Enqueue(NotificationChannel channel, string recipient, string subject, string body)
    {
        var now = timeProvider.GetUtcNow();

        writer.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Channel = channel,
            Recipient = recipient,
            Subject = subject,
            Body = body,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            CreatedAt = now,

            // Eligible immediately; the worker picks it up on its next pass.
            NextAttemptAt = now,
        });
    }
}

/// <summary>
/// Narrow seam over the DbSet so Application does not reference EF Core (AGENTS.md layering).
/// Implemented in Infrastructure.
/// </summary>
public interface IOutboxWriter
{
    void Add(OutboxMessage message);
}
