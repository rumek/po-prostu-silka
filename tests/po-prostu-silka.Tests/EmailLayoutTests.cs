using po_prostu_silka.Infrastructure.Notifications;

namespace po_prostu_silka.Tests;

/// <summary>
/// The branded frame every email is sent in. The serialiser alone — no fixture, no container.
/// </summary>
public class EmailLayoutTests
{
    private const string AppUrl = "https://po-prostu-silka.azurewebsites.net";

    /// <summary>
    /// The logo the HTML points at is the attachment that travels with it. A mismatched content id
    /// renders a broken-image box at the top of every email.
    /// </summary>
    [Fact]
    public void The_logo_is_attached_inline_under_the_id_the_html_references()
    {
        var message = AcsEmailSender.BuildMessage(
            "DoNotReply@example.azurecomm.net", "anna@example.pl", "Temat", "Treść", AppUrl);

        var logo = Assert.Single(message.Attachments);

        Assert.Equal(EmailLayout.LogoContentId, logo.ContentId);
        Assert.Equal("image/png", logo.ContentType);
        Assert.False(logo.Content.IsEmpty);
        Assert.Contains($"src=\"cid:{EmailLayout.LogoContentId}\"", message.Content.Html);
    }

    [Fact]
    public void Both_alternatives_carry_the_same_footer()
    {
        var html = EmailLayout.Html("Temat", "Treść", AppUrl);
        var text = EmailLayout.PlainText("Treść", AppUrl);

        Assert.Contains("wysłana automatycznie", html);
        Assert.Contains("wysłana automatycznie", text);
        Assert.Contains(AppUrl, html);
        Assert.EndsWith(AppUrl, text);
    }

    [Fact]
    public void Without_a_base_url_the_footer_leaves_the_app_link_out()
    {
        Assert.DoesNotContain("Otwórz aplikację", EmailLayout.Html("Temat", "Treść", ""));
        Assert.DoesNotContain("http", EmailLayout.PlainText("Treść", null));
    }

    /// <summary>
    /// The body is text the app interpolated names into — a class or a display name must never
    /// become markup.
    /// </summary>
    [Fact]
    public void The_body_is_encoded_not_interpreted()
    {
        var html = EmailLayout.Html("Temat", "Zajęcia <script>alert(1)</script> odwołane", AppUrl);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    /// <summary>
    /// The password-reset link has to stay clickable and intact: its query carries an ampersand,
    /// which must be encoded once in the attribute, not twice and not raw.
    /// </summary>
    [Fact]
    public void A_url_in_the_body_becomes_a_link_with_its_query_intact()
    {
        var html = EmailLayout.Html(
            "Temat", "Kliknij:\n\nhttps://app.test/reset-password?email=a%40b.pl&token=x%2By", AppUrl);

        Assert.Contains(
            "<a href=\"https://app.test/reset-password?email=a%40b.pl&amp;token=x%2By\"", html);
    }

    [Fact]
    public void Blank_lines_become_paragraphs_and_single_breaks_stay_breaks()
    {
        var html = EmailLayout.Html("Temat", "Pierwszy\ndrugi wiersz\n\nAkapit", AppUrl);

        Assert.Contains("Pierwszy<br>drugi wiersz</p>", html);
        Assert.Contains(">Akapit</p>", html);
    }
}
