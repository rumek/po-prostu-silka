using System.Globalization;
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
    IOptions<AppOptions> appOptions,
    ILogger<AcsEmailSender> logger) : IEmailSender
{
    /// <summary>
    /// The <c>LastError</c> a throttled email carries until it is sent. <see cref="OutboxHealthCheck"/>
    /// reads it as "the email lane is waiting on the provider's cap", so the two must agree - hence one
    /// constant.
    /// </summary>
    public const string ThrottledReason = "acs_429";

    private readonly AcsOptions _options = options.Value;
    private readonly AppOptions _app = appOptions.Value;
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

        if (IsReservedDomain(to))
        {
            // The test-data club's members all live on example.test. Handing ACS those addresses
            // spent the send quota on mail that cannot arrive, until the throttle stalled delivery
            // for everyone else.
            logger.LogInformation("Email not sent: {Recipient} is on a reserved domain.", to);
            return DeliveryResult.Dropped("reserved_domain");
        }

        try
        {
            // WaitUntil.Started, not Completed: we only need ACS to accept the message. Blocking a
            // worker pass on provider-side delivery would stall the whole batch.
            await client.SendAsync(
                WaitUntil.Started,
                BuildMessage(_options.SenderAddress, to, subject, body, _app.BaseUrl),
                cancellationToken);

            return DeliveryResult.Success();
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            // Over quota. Not a failure of this message: wait as long as ACS asks, and do not spend
            // an attempt on it (see DeliveryOutcome.Throttled).
            var retryAfter = ParseRetryAfter(
                ex.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var header) == true ? header : null,
                DateTimeOffset.UtcNow);

            logger.LogWarning("Email throttled by ACS; retrying after {RetryAfter}.", retryAfter);
            return DeliveryResult.Throttled(ThrottledReason, retryAfter);
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's network timeout, not a shutdown. Without this it escapes as a
            // cancellation and strands the row Claimed until its lease expires.
            logger.LogWarning("Email send timed out.");
            return DeliveryResult.Transient("acs_timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Email failed with an unexpected error.");
            return DeliveryResult.Transient("acs_unexpected");
        }
    }

    /// <summary>
    /// The message as ACS receives it: the body in the branded frame (<see cref="EmailLayout"/>),
    /// with the same text and footer as the plain-text alternative, and the logo attached inline.
    /// </summary>
    public static EmailMessage BuildMessage(
        string sender, string to, string subject, string body, string? appUrl)
    {
        var content = new EmailContent(subject)
        {
            Html = EmailLayout.Html(subject, body, appUrl),
            PlainText = EmailLayout.PlainText(body, appUrl),
        };

        var message = new EmailMessage(sender, to, content);
        message.Attachments.Add(new EmailAttachment(
            "po-prostu-silka.png", EmailLayout.LogoContentType, EmailLayout.Logo)
        {
            ContentId = EmailLayout.LogoContentId,
        });

        return message;
    }

    /// <summary>
    /// The client the worker sends through. Retries are OFF: the SDK's default policy honours a
    /// 429's Retry-After silently, which parked the single worker loop for up to an hour per pass
    /// with nothing in the logs. The outbox already owns retrying, with backoff and an attempt cap,
    /// so a throttle is reported to it at once instead.
    /// </summary>
    public static EmailClient CreateClient(string connectionString)
    {
        var options = new EmailClientOptions();
        options.Retry.MaxRetries = 0;
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);

        return new EmailClient(connectionString, options);
    }

    /// <summary>
    /// A Retry-After header — delay-seconds or an HTTP date, RFC 9110 — as a wait. One minute when
    /// absent or unreadable, and clamped to [15 s, 1 h]: a zero would retry on the very next pass,
    /// and a provider asking for a day should not silence mail for a day without /health noticing.
    /// </summary>
    public static TimeSpan ParseRetryAfter(string? header, DateTimeOffset now)
    {
        TimeSpan? wait = null;

        if (int.TryParse(header, out var seconds))
        {
            wait = TimeSpan.FromSeconds(seconds);
        }
        else if (DateTimeOffset.TryParse(
            header,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var at))
        {
            wait = at - now;
        }

        var value = wait ?? TimeSpan.FromMinutes(1);

        return value < MinRetryAfter ? MinRetryAfter
            : value > MaxRetryAfter ? MaxRetryAfter
            : value;
    }

    private static readonly TimeSpan MinRetryAfter = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    /// <summary>
    /// True for an address that cannot exist: the RFC 2606 / RFC 6761 reserved names — the
    /// .test, .example, .invalid and .localhost top-level domains, and example.com / .net / .org.
    /// </summary>
    public static bool IsReservedDomain(string address)
    {
        var at = address.LastIndexOf('@');
        if (at < 0)
        {
            return false;
        }

        var domain = address[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();

        return ReservedDomains.Any(reserved => domain == reserved || domain.EndsWith("." + reserved));
    }

    private static readonly string[] ReservedDomains =
        ["test", "example", "invalid", "localhost", "example.com", "example.net", "example.org"];

    /// <summary>4xx other than throttling and timeout will not change on retry.</summary>
    private static bool IsPermanent(int status) =>
        status is >= 400 and < 500 && status is not 408 and not 429;
}
