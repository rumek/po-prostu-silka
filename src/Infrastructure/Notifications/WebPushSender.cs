using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;
using po_prostu_silka.Application.Notifications;
using DomainPushSubscription = po_prostu_silka.Domain.Notifications.PushSubscription;
using LibPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace po_prostu_silka.Infrastructure.Notifications;

public class VapidOptions
{
    public const string SectionName = "VapidKeys";

    /// <summary>Served to the browser by design — this is what the SPA subscribes with.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Secret. Lives only in App Service settings.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>VAPID requires a contact for the application server, as a mailto: or https: URI.</summary>
    public string Subject { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey)
        && !string.IsNullOrWhiteSpace(PrivateKey)
        && !string.IsNullOrWhiteSpace(Subject);
}

/// <summary>
/// Web Push with VAPID signing.
///
/// Unconfigured, this logs and reports a permanent failure rather than throwing, so a developer
/// without VAPID keys can still run the app.
/// </summary>
public class WebPushSender(
    PushServiceClient client,
    IOptions<VapidOptions> options,
    ILogger<WebPushSender> logger) : IPushSender
{
    private readonly VapidOptions _options = options.Value;

    public async Task<DeliveryResult> SendAsync(
        DomainPushSubscription subscription,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            logger.LogWarning(
                "Push not sent: VAPID is not configured ({Section}:PublicKey / PrivateKey / Subject).",
                VapidOptions.SectionName);
            return DeliveryResult.Permanent("vapid_not_configured");
        }

        var target = new LibPushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);

        // The service worker reads these fields to build the notification.
        var payload = JsonSerializer.Serialize(new { title, body });

        try
        {
            await client.RequestPushMessageDeliveryAsync(
                target,
                new PushMessage(payload) { Urgency = PushMessageUrgency.Normal },
                new VapidAuthentication(_options.PublicKey, _options.PrivateKey)
                {
                    Subject = _options.Subject,
                },
                cancellationToken);

            return DeliveryResult.Success();
        }
        catch (PushServiceClientException ex)
            when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // The browser revoked permission, cleared its data, or the push service expired the
            // endpoint. The subscription will never work again - the worker deletes it rather than
            // retrying forever and permanently polluting the failure count.
            logger.LogInformation(
                "Push subscription is gone ({Status}); it will be deleted.", ex.StatusCode);
            return DeliveryResult.SubscriptionGone($"push_{(int)ex.StatusCode}");
        }
        catch (PushServiceClientException ex) when (IsPermanent(ex.StatusCode))
        {
            logger.LogWarning(ex, "Push permanently rejected with {Status}.", ex.StatusCode);
            return DeliveryResult.Permanent($"push_{(int)ex.StatusCode}");
        }
        catch (PushServiceClientException ex)
        {
            logger.LogWarning(ex, "Push transiently failed with {Status}.", ex.StatusCode);
            return DeliveryResult.Transient($"push_{(int)ex.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Push failed with an unexpected error.");
            return DeliveryResult.Transient("push_unexpected");
        }
    }

    private static bool IsPermanent(HttpStatusCode status) =>
        (int)status is >= 400 and < 500
        && status is not HttpStatusCode.RequestTimeout and not HttpStatusCode.TooManyRequests;
}
