using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Notifications;

namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// FR-021's "account approved" notification — the transport's first real consumer, and the shape
/// S-05 will copy for cancellations.
///
/// Rendering happens HERE, not in the worker. The worker delivers already-rendered bytes, so a
/// retry hours later says exactly what the first attempt said.
///
/// INTEGRATION POINT FOR S-01: S-01 owns the admin's approve action and does not exist yet. When it
/// lands, it calls NotifyAsync after flipping the member to Active, in the same unit of work — and
/// if it wraps that in an explicit transaction it must go through
/// Database.CreateExecutionStrategy().ExecuteAsync(...), because EnableRetryOnFailure is on.
/// </summary>
public interface IAccountApprovedNotification
{
    Task NotifyAsync(ApplicationUser member, CancellationToken cancellationToken);
}

public class AccountApprovedNotification(
    IOutboxEnqueuer enqueuer,
    IPushSubscriptionStore subscriptions) : IAccountApprovedNotification
{
    private const string Subject = "Twoje konto zostało zatwierdzone";

    public async Task NotifyAsync(ApplicationUser member, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(member.DisplayName) ? "Cześć" : member.DisplayName;

        // Plain text, no template engine. One message does not justify one, and S-05 can introduce
        // templating when it has several to render.
        var body =
            $"{name}, Twoje konto w Po Prostu Siłka zostało zatwierdzone.\n\n" +
            "Możesz się teraz zalogować i zapisać na zajęcia.";

        if (!string.IsNullOrWhiteSpace(member.Email))
        {
            enqueuer.Enqueue(NotificationChannel.Email, member.Email, Subject, body);
        }

        // One row per subscription: a member with a phone and a laptop gets both, and a failure on
        // one device does not affect the other.
        var devices = await subscriptions.GetForUserAsync(member.Id, cancellationToken);
        foreach (var device in devices)
        {
            // Recipient carries the subscription id, not the endpoint — the worker looks the row up
            // so it can delete it if the push service reports the subscription is gone.
            enqueuer.Enqueue(NotificationChannel.Push, device.Id.ToString(), Subject, body);
        }
    }
}
