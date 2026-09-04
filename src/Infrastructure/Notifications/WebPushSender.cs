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

        var payload = BuildPayload(title, body);

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

    /// <summary>
    /// The payload shape the Angular service worker requires in order to DISPLAY a notification.
    ///
    /// <para>
    /// THE ENVELOPE IS LOAD-BEARING. `ngsw-worker.js` calls `showNotification` only for a payload
    /// whose top level is a `notification` object; anything else is pushed onto the
    /// `SwPush.messages` stream, where nothing in this SPA is listening — so it is delivered,
    /// marked Sent, and never seen. Only `title` is required by that contract (Angular's SwPush
    /// docs); `body` and the click target are what make the notification worth receiving.
    /// </para>
    ///
    /// <para>
    /// This edits code F-03 shipped and reviewed, and the payload was not wrong on its own terms —
    /// it simply predated any client that had to render it. S-09 is where a member first sees one.
    /// </para>
    ///
    /// <para>
    /// `data.onActionClick.default` is the service worker's OWN click handler:
    /// `navigateLastFocusedOrOpen` reuses an already-open tab rather than stacking a new one on
    /// every cancellation. The flat `data.url` beside it is the same destination in the shape a
    /// `notificationClicks` subscriber would read, so a future in-app handler needs no second
    /// server change. Both point at the member's bookings screen: every message this slice sends is
    /// about a class they hold a spot on.
    /// </para>
    /// </summary>
    public static string BuildPayload(string title, string body) =>
        JsonSerializer.Serialize(new
        {
            notification = new
            {
                title,
                body,
                data = new
                {
                    url = ClickTarget,
                    onActionClick = new
                    {
                        @default = new
                        {
                            operation = "navigateLastFocusedOrOpen",
                            url = ClickTarget,
                        },
                    },
                },
            },
        });

    /// <summary>The member's bookings screen — the SPA route, matching app.routes.ts.</summary>
    private const string ClickTarget = "/my-classes";

    private static bool IsPermanent(HttpStatusCode status) =>
        (int)status is >= 400 and < 500
        && status is not HttpStatusCode.RequestTimeout and not HttpStatusCode.TooManyRequests;
}
