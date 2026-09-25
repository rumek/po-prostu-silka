using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace po_prostu_silka.Infrastructure.Notifications;

/// <summary>
/// The one frame every email is sent in: the logo on top, the message, and the same footer.
///
/// <para>
/// APPLIED AT SEND TIME, NOT AT ENQUEUE TIME. The notification services render only the message
/// itself, as plain text, and the outbox stores that. Wrapping it here keeps branding out of the
/// Application layer and out of the database, so a restyle needs no migration and reaches mail
/// already waiting in the queue.
/// </para>
///
/// <para>
/// THE LOGO IS AN INLINE ATTACHMENT (<c>cid:</c>), NOT A LINK. Gmail and Outlook hide remote images
/// until the reader allows them; an inline one shows at once and does not depend on the site being
/// up. The file is a 2x render of the PWA icon's wordmark, drawn at half size.
/// </para>
///
/// <para>
/// Table layout and inline styles only: that is all mail clients reliably render. Colours are the
/// SPA's tokens (<c>--page</c>, <c>--ink</c>, <c>--accent</c>, <c>--muted</c>) written out, since
/// no mail client resolves a custom property.
/// </para>
/// </summary>
public static partial class EmailLayout
{
    public const string LogoContentId = "logo";

    public const string LogoContentType = "image/png";

    private const string BrandName = "Po Prostu Siłka";

    private const string AutomatedNotice = "Ta wiadomość została wysłana automatycznie - prosimy na nią nie odpowiadać.";

    /// <summary>The wordmark, embedded in this assembly so the sender needs no file system.</summary>
    public static BinaryData Logo { get; } = LoadLogo();

    /// <summary>
    /// The message as plain text with the footer appended — the part every client can show.
    /// </summary>
    /// <param name="appUrl">The app's origin (App:BaseUrl), or blank to leave the link out.</param>
    public static string PlainText(string body, string? appUrl)
    {
        var footer = new StringBuilder()
            .Append("\n\n--\n")
            .Append(BrandName)
            .Append('\n')
            .Append(AutomatedNotice);

        if (!string.IsNullOrWhiteSpace(appUrl))
        {
            footer.Append('\n').Append(appUrl.TrimEnd('/'));
        }

        return body.TrimEnd() + footer;
    }

    /// <summary>
    /// The message in the branded frame. The body is plain text: it is HTML-encoded, blank lines
    /// become paragraphs, single line breaks stay line breaks, and URLs become links.
    /// </summary>
    public static string Html(string subject, string body, string? appUrl)
    {
        var paragraphs = body
            .Replace("\r\n", "\n")
            .Trim()
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(paragraph =>
                "<p style=\"margin:0 0 16px;\">"
                + Linkify(WebUtility.HtmlEncode(paragraph.Trim())).Replace("\n", "<br>")
                + "</p>");

        var appLink = string.IsNullOrWhiteSpace(appUrl)
            ? string.Empty
            : $"<br><a href=\"{WebUtility.HtmlEncode(appUrl.TrimEnd('/'))}\" style=\"color:#654b45;\">"
              + "Otwórz aplikację</a>";

        return $"""
            <!DOCTYPE html>
            <html lang="pl">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{WebUtility.HtmlEncode(subject)}</title>
            </head>
            <body style="margin:0;padding:0;background:#f7efe7;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#f7efe7;">
            <tr><td align="center" style="padding:24px 12px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;background:#ffffff;border-radius:10px;">
            <tr><td align="center" style="padding:28px 24px 8px;">
            <img src="cid:{LogoContentId}" width="160" alt="{BrandName}" style="display:block;border:0;width:160px;height:auto;">
            </td></tr>
            <tr><td style="padding:16px 28px 12px;font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:1.5;color:#272321;">
            {string.Join("\n", paragraphs)}
            </td></tr>
            </table>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;">
            <tr><td align="center" style="padding:16px 24px 0;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:1.5;color:#7a7674;">
            {BrandName}<br>{WebUtility.HtmlEncode(AutomatedNotice)}{appLink}
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Wraps each http(s) URL in an anchor. Runs on already-encoded text, so an <c>&amp;</c> inside
    /// the URL is correctly <c>&amp;amp;</c> in the attribute as well as in the visible text.
    /// </summary>
    private static string Linkify(string encoded) =>
        UrlPattern().Replace(
            encoded,
            match => $"<a href=\"{match.Value}\" style=\"color:#654b45;word-break:break-all;\">{match.Value}</a>");

    [GeneratedRegex(@"https?://[^\s<]+")]
    private static partial Regex UrlPattern();

    private static BinaryData LoadLogo()
    {
        using var stream = typeof(EmailLayout).Assembly.GetManifestResourceStream("email-logo.png")
            ?? throw new InvalidOperationException("The email logo is not embedded in the assembly.");

        return BinaryData.FromStream(stream);
    }
}
