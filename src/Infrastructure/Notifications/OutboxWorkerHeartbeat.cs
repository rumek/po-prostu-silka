namespace po_prostu_silka.Infrastructure.Notifications;

/// <summary>
/// When the delivery worker last finished a pass, so /health can tell a live worker from a stalled
/// one.
///
/// <para>
/// IN MEMORY, NOT IN THE DATABASE. The question /health answers is "is THIS instance's worker
/// running?", and the probe hits the instance whose worker it is. A row in a table would say that
/// some instance passed recently, which is exactly the answer that hid a stalled pass for hours.
/// </para>
///
/// <para>
/// Before the first pass it reports the moment it was created — the app's start — so a worker that
/// never completes a single pass is flagged too, rather than looking fresh forever.
/// </para>
/// </summary>
public sealed class OutboxWorkerHeartbeat(TimeProvider timeProvider)
{
    private long _lastPassTicks = timeProvider.GetUtcNow().UtcTicks;

    public DateTimeOffset LastPassAt =>
        new(Interlocked.Read(ref _lastPassTicks), TimeSpan.Zero);

    public void Beat(DateTimeOffset at) => Interlocked.Exchange(ref _lastPassTicks, at.UtcTicks);
}
