namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// Lets a browser register itself for Web Push.
///
/// Everything here uses bare RequireAuthorization(), NOT the ActiveMember policy: a Pending member's
/// device may subscribe before approval, and the account-approved notification is precisely the
/// message they are waiting for.
/// </summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/push").WithTags("Push").RequireAuthorization();

        group.MapGet("/vapid-key", GetVapidKey.Handle);
        group.MapPost("/subscribe", Subscribe.HandleAsync);
        group.MapPost("/unsubscribe", Unsubscribe.HandleAsync);

        return app;
    }
}
