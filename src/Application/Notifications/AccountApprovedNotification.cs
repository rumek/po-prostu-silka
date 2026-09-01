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
/// The caller is MemberAdminEndpoints.ApproveAsync (S-01): it flips the member to Active, calls
/// NotifyAsync, then commits both with a single IUnitOfWork.SaveChangesAsync, so the approval and
/// the queued email land together or not at all. It deliberately opens no explicit transaction - one
/// would have to go through Database.CreateExecutionStrategy().ExecuteAsync(...), because
/// EnableRetryOnFailure is on.
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
        // "Możesz się teraz zalogować" would be stale: since S-01 a pending member is already
        // signed in while they wait, so this arrives in a session they still have. Send them back
        // to the app instead of telling them to do something they have already done.
        var body =
            $"{name}, Twoje konto w Po Prostu Siłka zostało zatwierdzone.\n\n" +
            "Wróć do aplikacji, aby zapisać się na zajęcia.";

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
