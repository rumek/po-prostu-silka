namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// Where this application lives, as an absolute origin. Exists for exactly one reason: the
/// password-reset email carries a link, and there is nowhere else to get the host from.
///
/// <para>
/// NOT DERIVED FROM THE REQUEST, AND THAT IS THE POINT. Notification bodies are rendered at enqueue
/// time and delivered later by a background worker — a retry four hours after the fact has no HTTP
/// request to read a host off. Even at enqueue time the incoming <c>Host</c> header is attacker
/// controlled, and a reset link built from it is a password-reset poisoning primitive. A setting is
/// the only source that is both available on a retry and not supplied by the person asking.
/// </para>
///
/// <para>
/// Unset, the forgot-password handler logs an error and still answers exactly as it would have. A
/// misconfiguration must degrade into a broken link, never into a response that tells an anonymous
/// caller whether an address is registered.
/// </para>
/// </summary>
public class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// Absolute origin with no trailing slash, e.g. <c>https://po-prostu-silka.azurewebsites.net</c>.
    /// Production supplies it as the App Service setting <c>App__BaseUrl</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
