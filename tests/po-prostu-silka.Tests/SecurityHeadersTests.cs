using System.Net;
using System.Text.Json;
using po_prostu_silka.Api.Http;

namespace po_prostu_silka.Tests;

/// <summary>
/// The baseline security headers and the generic error body (S-28, GL-05), on each of the pipeline's
/// three branches: the SPA shell, a static file, and an API route - plus the exception handler, which
/// clears the response before it writes and is therefore the branch most likely to lose them.
///
/// <para>
/// The shell and the static file assert 200 and their content type, not only the headers. The
/// fallback answers every unmatched route, and <c>src/Api/wwwroot</c> is git-ignored and filled only
/// by the pipeline's staging step - so without those assertions an empty wwwroot would pass these
/// tests on a 404.
/// </para>
///
/// <para>
/// The "no Server header" assertions hold trivially under TestServer, which never writes one. What
/// they pin is that nothing in the app adds one back; that Kestrel's is gone is checked with
/// <c>curl -sI</c> against the live URL.
/// </para>
/// </summary>
[Collection(nameof(IntegrationCollection))]
public class SecurityHeadersTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Spa_shell_carries_every_security_header()
    {
        var response = await fixture.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Static_file_carries_every_security_header()
    {
        var response = await fixture.CreateClient().GetAsync("/favicon.ico");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/x-icon", response.Content.Headers.ContentType?.MediaType);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Api_route_carries_every_security_header()
    {
        // Anonymous on purpose: the refusal is an API response like any other, and the headers must
        // not depend on who is asking.
        var response = await fixture.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Unhandled_exception_answers_a_generic_problem_body_with_the_headers()
    {
        var response = await fixture.CreateClient().GetAsync("/test/throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("detail", out _), body);
        Assert.False(json.RootElement.TryGetProperty("exception", out _), body);

        // The probe's own message and type, and any stack frame, must never reach the caller.
        Assert.DoesNotContain("secret-detail", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain(" at ", body);

        AssertSecurityHeaders(response);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal(SecurityHeaders.StrictTransportSecurity, Header(response, "Strict-Transport-Security"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("camera=(), microphone=(), geolocation=(), payment=()", Header(response, "Permissions-Policy"));

        // Directive by directive rather than one string compare, so a failure names the directive.
        var csp = Header(response, "Content-Security-Policy")
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            [
                "default-src 'self'",
                "script-src 'self'",
                "style-src 'self' 'unsafe-inline'",
                "img-src 'self' data: https://img.youtube.com",
                "font-src 'self'",
                "connect-src 'self' https://img.youtube.com",
                "frame-src https://www.youtube-nocookie.com",
                "worker-src 'self'",
                "manifest-src 'self'",
                "object-src 'none'",
                "base-uri 'self'",
                "form-action 'self'",
                "frame-ancestors 'none'",
            ],
            csp);

        Assert.False(response.Headers.Contains("Server"), "The Server header must not be sent.");
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? string.Join(", ", values)
            : throw new Xunit.Sdk.XunitException($"Missing header {name}.");
}
