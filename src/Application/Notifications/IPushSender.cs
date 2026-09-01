using po_prostu_silka.Domain.Notifications;

namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// The Web Push channel. Takes the whole subscription because VAPID encryption needs the endpoint
/// and both client keys.
/// </summary>
public interface IPushSender
{
    Task<DeliveryResult> SendAsync(
        PushSubscription subscription, string title, string body, CancellationToken cancellationToken);
}
