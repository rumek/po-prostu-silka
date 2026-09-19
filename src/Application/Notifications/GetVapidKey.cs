namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// The application server's public key. Public by design — the browser needs it to subscribe.
/// Authenticated anyway, because nothing anonymous needs it and a uniform surface is simpler.
/// </summary>
public static class GetVapidKey
{
    public static IResult Handle(IVapidPublicKey key) =>
        string.IsNullOrWhiteSpace(key.PublicKey)
            ? Results.Problem("Push is not configured on this server.", statusCode: 503)
            : Results.Ok(new VapidKeyResponse(key.PublicKey));
}
