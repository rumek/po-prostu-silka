using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Infrastructure.Notifications;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// Proves the outbox state machine — the part that actually breaks. The channels are faked, because
/// what matters here is what the worker does with a result, not whether ACS works.
///
/// The clock is controlled rather than real: backoff windows, lease expiry and retention are all
/// time-based, so tests advance a TestTimeProvider instead of sleeping.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class OutboxDeliveryTests(IntegrationTestFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeEmailSender _email = new();
    private readonly FakePushSender _push = new();
    private readonly TestTimeProvider _clock = new(Start);
    private ServiceProvider _services = null!;
    private OutboxDeliveryWorker _worker = null!;

    public async Task InitializeAsync()
    {
        var options = Options.Create(new OutboxOptions
        {
            BatchSize = 20,
            MaxAttempts = 5,
            LeaseTimeout = TimeSpan.FromMinutes(5),
            SentRetention = TimeSpan.FromDays(30),
            PruneInterval = TimeSpan.Zero,
            BackoffSchedule =
            [
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15),
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(4),
            ],
        });

        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddDbContext<AppDbContext>(o => o.UseSqlServer(fixture.ConnectionString));
        collection.AddSingleton<IEmailSender>(_email);
        collection.AddSingleton<IPushSender>(_push);
        collection.AddSingleton(options);
        collection.AddSingleton<TimeProvider>(_clock);
        _services = collection.BuildServiceProvider();

        _worker = new OutboxDeliveryWorker(
            _services.GetRequiredService<IServiceScopeFactory>(),
            options,
            _clock,
            _services.GetRequiredService<ILogger<OutboxDeliveryWorker>>());

        // These tests share a database with the auth tests, so start from a known-empty outbox.
        await using var db = NewContext();
        await db.OutboxMessages.ExecuteDeleteAsync();
        await db.PushSubscriptions.ExecuteDeleteAsync();
    }

    public Task DisposeAsync()
    {
        _services.Dispose();
        return Task.CompletedTask;
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    private async Task<Guid> EnqueueEmailAsync()
    {
        await using var db = NewContext();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Channel = NotificationChannel.Email,
            Recipient = "member@test.local",
            Subject = "Subject",
            Body = "Body",
            Status = OutboxStatus.Pending,
            CreatedAt = _clock.GetUtcNow(),
            NextAttemptAt = _clock.GetUtcNow(),
        };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private async Task<OutboxMessage> ReloadAsync(Guid id)
    {
        await using var db = NewContext();
        return await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    // --- happy path ----------------------------------------------------------

    [Fact]
    public async Task Pending_message_is_sent_and_marked_Sent()
    {
        var id = await EnqueueEmailAsync();

        await _worker.RunPassAsync(CancellationToken.None);

        var message = await ReloadAsync(id);
        Assert.Equal(OutboxStatus.Sent, message.Status);
        Assert.NotNull(message.SentAt);
        Assert.Null(message.ClaimedAt);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task Message_scheduled_in_the_future_is_not_claimed()
    {
        await using (var db = NewContext())
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Channel = NotificationChannel.Email,
                Recipient = "later@test.local",
                Subject = "S",
                Body = "B",
                Status = OutboxStatus.Pending,
                CreatedAt = _clock.GetUtcNow(),
                NextAttemptAt = _clock.GetUtcNow().AddMinutes(30),
            });
            await db.SaveChangesAsync();
        }

        await _worker.RunPassAsync(CancellationToken.None);

        Assert.Empty(_email.Sent);
    }

    // --- retry and dead-lettering -------------------------------------------

    [Fact]
    public async Task Transient_failure_increments_attempts_and_backs_off()
    {
        var id = await EnqueueEmailAsync();
        _email.NextResult = DeliveryResult.Transient("acs_503");

        await _worker.RunPassAsync(CancellationToken.None);

        var message = await ReloadAsync(id);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("acs_503", message.LastError);

        // First backoff step is one minute, so the row is no longer eligible right now.
        Assert.Equal(Start.AddMinutes(1), message.NextAttemptAt);
        Assert.Null(message.ClaimedAt);
    }

    [Fact]
    public async Task Backoff_grows_between_attempts()
    {
        var id = await EnqueueEmailAsync();
        _email.NextResult = DeliveryResult.Transient("acs_503");

        await _worker.RunPassAsync(CancellationToken.None);
        var afterFirst = await ReloadAsync(id);

        _clock.Advance(TimeSpan.FromMinutes(2));
        await _worker.RunPassAsync(CancellationToken.None);
        var afterSecond = await ReloadAsync(id);

        var firstGap = afterFirst.NextAttemptAt - Start;
        var secondGap = afterSecond.NextAttemptAt - Start.AddMinutes(2);

        Assert.Equal(2, afterSecond.AttemptCount);
        Assert.True(secondGap > firstGap, $"expected growing backoff, got {firstGap} then {secondGap}");
    }

    [Fact]
    public async Task Transient_failures_dead_letter_at_the_attempt_cap()
    {
        var id = await EnqueueEmailAsync();
        _email.NextResult = DeliveryResult.Transient("acs_503");

        // Five attempts, advancing past each backoff window so the row is eligible again.
        for (var i = 0; i < 5; i++)
        {
            await _worker.RunPassAsync(CancellationToken.None);
            _clock.Advance(TimeSpan.FromHours(6));
        }

        var message = await ReloadAsync(id);
        Assert.Equal(OutboxStatus.Failed, message.Status);
        Assert.Equal(5, message.AttemptCount);
    }

    [Fact]
    public async Task Permanent_failure_dead_letters_on_the_first_attempt()
    {
        var id = await EnqueueEmailAsync();
        _email.NextResult = DeliveryResult.Permanent("acs_400");

        await _worker.RunPassAsync(CancellationToken.None);

        var message = await ReloadAsync(id);
        Assert.Equal(OutboxStatus.Failed, message.Status);
        Assert.Equal(1, message.AttemptCount);

        // The point of Permanent: no further attempts, no quota burned on a rejected address.
        _clock.Advance(TimeSpan.FromDays(1));
        await _worker.RunPassAsync(CancellationToken.None);
        Assert.Single(_email.Sent);
    }

    // --- recycle survival ----------------------------------------------------

    [Fact]
    public async Task Stale_lease_is_reclaimed_and_delivered()
    {
        // A row Claimed by a worker that then died - exactly what an App Service recycle mid-send
        // leaves behind. Without lease reclaim this row is stranded forever.
        var id = Guid.NewGuid();
        await using (var db = NewContext())
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = id,
                Channel = NotificationChannel.Email,
                Recipient = "stranded@test.local",
                Subject = "S",
                Body = "B",
                Status = OutboxStatus.Claimed,
                ClaimedAt = Start.AddMinutes(-10),
                CreatedAt = Start.AddMinutes(-10),
                NextAttemptAt = Start.AddMinutes(-10),
            });
            await db.SaveChangesAsync();
        }

        await _worker.RunPassAsync(CancellationToken.None);

        var message = await ReloadAsync(id);
        Assert.Equal(OutboxStatus.Sent, message.Status);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task Fresh_lease_is_not_stolen()
    {
        var id = Guid.NewGuid();
        await using (var db = NewContext())
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = id,
                Channel = NotificationChannel.Email,
                Recipient = "inflight@test.local",
                Subject = "S",
                Body = "B",
                Status = OutboxStatus.Claimed,
                // Claimed a moment ago: another instance is plausibly still sending it.
                ClaimedAt = Start.AddSeconds(-30),
                CreatedAt = Start,
                NextAttemptAt = Start,
            });
            await db.SaveChangesAsync();
        }

        await _worker.RunPassAsync(CancellationToken.None);

        Assert.Empty(_email.Sent);
        Assert.Equal(OutboxStatus.Claimed, (await ReloadAsync(id)).Status);
    }

    // --- push ----------------------------------------------------------------

    [Fact]
    public async Task Dead_push_subscription_is_deleted_and_the_message_still_marked_Sent()
    {
        var (messageId, subscriptionId) = await EnqueuePushAsync();
        _push.NextResult = DeliveryResult.SubscriptionGone("push_410");

        await _worker.RunPassAsync(CancellationToken.None);

        // Push is best-effort: a dead subscription is not a delivery failure, so this must NOT
        // land in the Failed count that /health reports.
        var message = await ReloadAsync(messageId);
        Assert.Equal(OutboxStatus.Sent, message.Status);

        await using var db = NewContext();
        Assert.False(await db.PushSubscriptions.AnyAsync(s => s.Id == subscriptionId));
    }

    [Fact]
    public async Task Push_is_delivered_to_the_stored_subscription()
    {
        var (messageId, _) = await EnqueuePushAsync();

        await _worker.RunPassAsync(CancellationToken.None);

        Assert.Equal(OutboxStatus.Sent, (await ReloadAsync(messageId)).Status);
        Assert.Single(_push.Sent);
        Assert.Empty(_email.Sent);
    }

    private async Task<(Guid MessageId, Guid SubscriptionId)> EnqueuePushAsync()
    {
        await using var db = NewContext();

        var user = await db.Users.FirstAsync();
        var subscription = new PushSubscription
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Endpoint = $"https://push.test/{Guid.NewGuid()}",
            P256dh = "p256dh-value",
            Auth = "auth-value",
            CreatedAt = _clock.GetUtcNow(),
        };
        db.PushSubscriptions.Add(subscription);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Channel = NotificationChannel.Push,
            Recipient = subscription.Id.ToString(),
            Subject = "Title",
            Body = "Body",
            Status = OutboxStatus.Pending,
            CreatedAt = _clock.GetUtcNow(),
            NextAttemptAt = _clock.GetUtcNow(),
        };
        db.OutboxMessages.Add(message);

        await db.SaveChangesAsync();
        return (message.Id, subscription.Id);
    }

    // --- retention -----------------------------------------------------------

    [Fact]
    public async Task Prune_removes_old_Sent_rows_but_keeps_Failed()
    {
        var oldSent = Guid.NewGuid();
        var oldFailed = Guid.NewGuid();
        var recentSent = Guid.NewGuid();

        await using (var db = NewContext())
        {
            db.OutboxMessages.AddRange(
                Row(oldSent, OutboxStatus.Sent, Start.AddDays(-40)),
                Row(oldFailed, OutboxStatus.Failed, Start.AddDays(-40)),
                Row(recentSent, OutboxStatus.Sent, Start.AddDays(-5)));
            await db.SaveChangesAsync();
        }

        await _worker.RunPassAsync(CancellationToken.None);

        await using var check = NewContext();
        Assert.False(await check.OutboxMessages.AnyAsync(m => m.Id == oldSent));

        // Failed rows are the diagnostic record - pruning them would destroy the evidence for
        // exactly the delivery complaint someone is most likely to raise.
        Assert.True(await check.OutboxMessages.AnyAsync(m => m.Id == oldFailed));
        Assert.True(await check.OutboxMessages.AnyAsync(m => m.Id == recentSent));

        OutboxMessage Row(Guid id, OutboxStatus status, DateTimeOffset sentAt) => new()
        {
            Id = id,
            Channel = NotificationChannel.Email,
            Recipient = "old@test.local",
            Subject = "S",
            Body = "B",
            Status = status,
            CreatedAt = sentAt,
            NextAttemptAt = sentAt,
            SentAt = sentAt,
        };
    }
}
