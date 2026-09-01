namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// The email channel. Implemented against Azure Communication Services; the interface exists so the
/// worker stays provider-agnostic, tests substitute a fake with no network, and the SMTP fallback
/// documented in infrastructure.md is a one-class swap rather than a rewrite.
/// </summary>
public interface IEmailSender
{
    Task<DeliveryResult> SendAsync(
        string to, string subject, string body, CancellationToken cancellationToken);
}
