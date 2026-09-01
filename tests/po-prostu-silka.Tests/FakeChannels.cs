using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Domain.Notifications;

namespace po_prostu_silka.Tests;

/// <summary>
/// Scripted channels. Tests set <see cref="NextResult"/> and assert on what the worker did with it.
///
/// No network, no credentials, no cost per CI run — and the behaviour actually under test is the
/// outbox state machine, not whether the ACS SDK works.
/// </summary>
public class FakeEmailSender : IEmailSender
{
    public DeliveryResult NextResult { get; set; } = DeliveryResult.Success();

    public List<(string To, string Subject, string Body)> Sent { get; } = [];

    public Task<DeliveryResult> SendAsync(
        string to, string subject, string body, CancellationToken cancellationToken)
    {
        Sent.Add((to, subject, body));
        return Task.FromResult(NextResult);
    }
}

public class FakePushSender : IPushSender
{
    public DeliveryResult NextResult { get; set; } = DeliveryResult.Success();

    public List<(string Endpoint, string Title, string Body)> Sent { get; } = [];

    public Task<DeliveryResult> SendAsync(
        PushSubscription subscription, string title, string body, CancellationToken cancellationToken)
    {
        Sent.Add((subscription.Endpoint, title, body));
        return Task.FromResult(NextResult);
    }
}

/// <summary>
/// Controllable clock. The worker's whole contract is about time — backoff windows, lease expiry,
/// retention — so tests advance this instead of sleeping.
/// </summary>
public class TestTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
