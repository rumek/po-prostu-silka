using System.Text.Json;
using po_prostu_silka.Infrastructure.Notifications;

namespace po_prostu_silka.Tests;

/// <summary>
/// The one assertion that separates "delivered and shown" from "delivered, marked Sent, and never
/// seen" (S-09 phase 3; prd.md FR-021).
///
/// <para>
/// A regression here fails NOTHING else. The push service accepts any payload, the outbox records a
/// success, and the notification silently lands on <c>SwPush.messages</c> instead of the screen — so
/// the only place the shape can be defended is right here, at the serialiser.
/// </para>
///
/// <para>
/// No fixture and no collection: this is the serialiser alone, so it needs neither a database nor a
/// container.
/// </para>
/// </summary>
public class WebPushPayloadTests
{
    private static JsonElement Payload(string title = "Odwołane zajęcia: Joga", string body = "Treść") =>
        JsonDocument.Parse(WebPushSender.BuildPayload(title, body)).RootElement;

    /// <summary>
    /// The envelope. `ngsw-worker.js` calls showNotification only for a top-level `notification`
    /// object carrying a title; a flat { title, body } is delivered and never displayed.
    /// </summary>
    [Fact]
    public void The_payload_is_a_notification_object_with_a_non_empty_title()
    {
        var notification = Payload().GetProperty("notification");

        Assert.Equal(JsonValueKind.Object, notification.ValueKind);

        var title = notification.GetProperty("title").GetString();

        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.Equal("Odwołane zajęcia: Joga", title);
    }

    [Fact]
    public void The_body_travels_inside_the_same_envelope()
    {
        Assert.Equal(
            "Zajęcia zostały odwołane.",
            Payload(body: "Zajęcia zostały odwołane.")
                .GetProperty("notification")
                .GetProperty("body")
                .GetString());
    }

    /// <summary>
    /// A tap has somewhere to land, and it lands on the screen the message is about. Both spellings
    /// carry the same destination: `onActionClick` is what the service worker itself acts on, `url`
    /// is what a `notificationClicks` subscriber in the SPA would read.
    /// </summary>
    [Fact]
    public void A_tap_opens_the_members_bookings_screen()
    {
        var data = Payload().GetProperty("notification").GetProperty("data");

        Assert.Equal("/my-classes", data.GetProperty("url").GetString());

        var onClick = data.GetProperty("onActionClick").GetProperty("default");

        // Reuses an already-open tab rather than stacking a new one per cancellation.
        Assert.Equal("navigateLastFocusedOrOpen", onClick.GetProperty("operation").GetString());
        Assert.Equal("/my-classes", onClick.GetProperty("url").GetString());
    }

    /// <summary>
    /// The old shape, named so the regression it guards against is legible: nothing that matters may
    /// sit at the top level, because the service worker never looks there.
    /// </summary>
    [Fact]
    public void Nothing_is_left_at_the_top_level_where_the_worker_never_looks()
    {
        var root = Payload();

        Assert.False(root.TryGetProperty("title", out _));
        Assert.False(root.TryGetProperty("body", out _));
        Assert.Single(root.EnumerateObject());
    }
}
