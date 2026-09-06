using po_prostu_silka.Application.Notifications;

namespace po_prostu_silka.Infrastructure.Notifications;

/// <summary>
/// Writes the whole email to the log and reports success. Development only.
///
/// <para>
/// WHY THIS EXISTS: <see cref="AcsEmailSender"/> answers <c>Permanent("acs_not_configured")</c> when
/// it has no credentials, so on a developer machine every message fails terminally and the body is
/// never seen. That was tolerable while the only email was a notification. It is not tolerable for
/// the password reset, where the body carries the ONLY route back into an account — without this,
/// the flow cannot be exercised or manually verified locally at all.
/// </para>
///
/// <para>
/// Registered only when the environment is Development AND ACS is unconfigured (see Program.cs).
/// Production resolution is untouched: configure ACS, even locally, and this is not used.
/// </para>
///
/// <para>
/// It logs a password-reset link in plain text, which is precisely why the registration is fenced.
/// Never widen that condition.
/// </para>
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task<DeliveryResult> SendAsync(
        string to, string subject, string body, CancellationToken cancellationToken)
    {
        // One entry with the whole body, not three: a reset link wrapped across log lines is not
        // clickable, and this exists to be read by a human at a terminal.
        logger.LogInformation(
            "DEV EMAIL (not sent anywhere)\n  To: {To}\n  Subject: {Subject}\n  Body:\n{Body}",
            to,
            subject,
            body);

        return Task.FromResult(DeliveryResult.Success());
    }
}
