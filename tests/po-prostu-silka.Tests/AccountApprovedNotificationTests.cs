using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Infrastructure.Notifications;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Tests;

/// <summary>
/// The transport's first real consumer. Asserts the fan-out: one email row plus one push row per
/// registered device — the shape S-05 will copy for cancellations.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class AccountApprovedNotificationTests(IntegrationTestFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.OutboxMessages.ExecuteDeleteAsync();
        await db.PushSubscriptions.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<List<OutboxMessage>> NotifyAsync(ApplicationUser member, int deviceCount)
    {
        await using var db = NewContext();

        for (var i = 0; i < deviceCount; i++)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = member.Id,
                Endpoint = $"https://push.test/{Guid.NewGuid()}",
                P256dh = "p256dh",
                Auth = "auth",
                CreatedAt = Now,
            });
        }

        await db.SaveChangesAsync();

        var notification = new AccountApprovedNotification(
            new OutboxEnqueuer(new OutboxWriter(db), new TestTimeProvider(Now)),
            new PushSubscriptionStore(db));

        await notification.NotifyAsync(member, CancellationToken.None);

        // Enqueue deliberately does not save — the caller owns the unit of work so the enqueue can
        // be atomic with the domain change that triggered it.
        await db.SaveChangesAsync();

        return await db.OutboxMessages.AsNoTracking().ToListAsync();
    }

    private async Task<ApplicationUser> GetMemberAsync()
    {
        await using var db = NewContext();
        return await db.Users.FirstAsync(u => u.Email == TestUsers.ActiveMemberEmail);
    }

    [Fact]
    public async Task Member_with_no_devices_gets_exactly_one_email_row()
    {
        var member = await GetMemberAsync();

        var rows = await NotifyAsync(member, deviceCount: 0);

        var row = Assert.Single(rows);
        Assert.Equal(NotificationChannel.Email, row.Channel);
        Assert.Equal(member.Email, row.Recipient);
        Assert.Equal(OutboxStatus.Pending, row.Status);
    }

    [Fact]
    public async Task Member_with_two_devices_gets_one_email_and_two_push_rows()
    {
        var member = await GetMemberAsync();

        var rows = await NotifyAsync(member, deviceCount: 2);

        Assert.Equal(1, rows.Count(r => r.Channel == NotificationChannel.Email));
        Assert.Equal(2, rows.Count(r => r.Channel == NotificationChannel.Push));
    }

    [Fact]
    public async Task Push_rows_carry_the_subscription_id_so_a_dead_device_can_be_deleted()
    {
        var member = await GetMemberAsync();

        var rows = await NotifyAsync(member, deviceCount: 1);
        var push = Assert.Single(rows, r => r.Channel == NotificationChannel.Push);

        await using var db = NewContext();
        var subscription = await db.PushSubscriptions.SingleAsync();

        // Not the endpoint: the worker looks the row up by id so it can remove it on a 410.
        Assert.Equal(subscription.Id.ToString(), push.Recipient);
    }

    [Fact]
    public async Task Rendered_message_is_non_empty_and_names_the_member()
    {
        var member = await GetMemberAsync();

        var rows = await NotifyAsync(member, deviceCount: 1);

        Assert.All(rows, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Subject));
            Assert.False(string.IsNullOrWhiteSpace(r.Body));
            Assert.Contains(member.DisplayName, r.Body);
        });
    }
}
