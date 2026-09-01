using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;
using po_prostu_silka.Application.Notifications;

namespace po_prostu_silka.Infrastructure.Notifications;

public class AcsOptions
{
    public const string SectionName = "Acs";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Full From address on the verified domain, e.g. DoNotReply@&lt;guid&gt;.azurecomm.net.
    /// Configuration rather than a constant, so the eventual custom-domain migration is a setting
    /// change and not a code change.
    /// </summary>
    public string SenderAddress { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(SenderAddress);
}

/// <summary>
/// Holds the ACS client, or nothing when ACS is unconfigured.
///
/// A wrapper rather than a nullable service registration: DI's <c>TService : class</c> constraint
/// rejects a nullable type argument, and the alternative — throwing at resolve time — would take
/// the worker down on every pass on a developer machine with no credentials.
/// </summary>
public sealed class AcsEmailClientHolder(EmailClient? client)
{
    public EmailClient? Client { get; } = client;
}

/// <summary>
/// Email over Azure Communication Services.
///
/// Unconfigured, this logs and reports a permanent failure rather than throwing — a developer with
/// no ACS credentials must still be able to run the app. Throwing here would take down the worker
/// on every pass.
/// </summary>
public class AcsEmailSender(
    AcsEmailClientHolder holder,
    IOptions<AcsOptions> options,
    ILogger<AcsEmailSender> logger) : IEmailSender
{
    private readonly AcsOptions _options = options.Value;
    private readonly EmailClient? client = holder.Client;

    public async Task<DeliveryResult> SendAsync(
        string to, string subject, string body, CancellationToken cancellationToken)
    {
        if (client is null || !_options.IsConfigured)
        {
            logger.LogWarning(
                "Email not sent: ACS is not configured ({Section}:ConnectionString / SenderAddress).",
                AcsOptions.SectionName);
            return DeliveryResult.Permanent("acs_not_configured");
        }

        try
        {
            // WaitUntil.Started, not Completed: we only need ACS to accept the message. Blocking a
            // worker pass on provider-side delivery would stall the whole batch.
            await client.SendAsync(
                WaitUntil.Started,
                _options.SenderAddress,
                to,
                subject,
                htmlContent: null,
                plainTextContent: body,
                cancellationToken: cancellationToken);

            return DeliveryResult.Success();
        }
        catch (RequestFailedException ex) when (IsPermanent(ex.Status))
        {
            // A rejected or malformed recipient will be rejected identically forever; retrying only
            // burns the managed domain's send quota.
            logger.LogWarning(ex, "Email permanently rejected for status {Status}.", ex.Status);
            return DeliveryResult.Permanent($"acs_{ex.Status}");
        }
        catch (RequestFailedException ex)
        {
            // 408, 429, 5xx and anything unrecognised: assume the provider might succeed later.
            logger.LogWarning(ex, "Email transiently failed with status {Status}.", ex.Status);
            return DeliveryResult.Transient($"acs_{ex.Status}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Email failed with an unexpected error.");
            return DeliveryResult.Transient("acs_unexpected");
        }
    }

    /// <summary>4xx other than throttling and timeout will not change on retry.</summary>
    private static bool IsPermanent(int status) =>
        status is >= 400 and < 500 && status is not 408 and not 429;
}
