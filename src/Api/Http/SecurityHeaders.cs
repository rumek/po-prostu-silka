namespace po_prostu_silka.Api.Http;

/// <summary>
/// The baseline security headers every response carries (S-28, GL-05): the API, the SPA shell, the
/// SPA fallback and every static file alike.
///
/// <para>
/// WRITTEN FROM <c>Response.OnStarting</c>, NOT BEFORE <c>next()</c>. <c>UseExceptionHandler</c>
/// calls <c>Response.Clear()</c> before it writes the generic 500, and that wipes every header
/// already set - but it keeps the OnStarting callbacks. Registered this way, the error body carries
/// the same set as everything else.
/// </para>
///
/// <para>
/// HSTS IS WRITTEN HERE, NOT BY <c>UseHsts()</c>. App Service terminates TLS and forwards plain
/// HTTP, and UseHsts only writes the header when <c>Request.IsHttps</c> is true, which here it never
/// is, since forwarded headers are not processed. The site's "HTTPS Only" setting guarantees the
/// browser only ever receives it over TLS. Skipped in Development, so localhost is not pinned.
/// </para>
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// The enforced Content-Security-Policy. A new external origin is a change to THIS constant,
    /// reviewed as such - every one below is tied to its caller:
    /// <list type="bullet">
    /// <item><c>https://img.youtube.com</c> - exercise thumbnails, <c>thumbnailUrl</c> in
    /// <c>src/app/src/app/core/training/youtube.ts</c>. In <c>connect-src</c> as well as
    /// <c>img-src</c>, deliberately: ngsw-config.json declares no groups, so once the service worker
    /// controls the page it answers every GET, the thumbnails included, with a <c>fetch()</c> - and a
    /// fetch is governed by connect-src. Without it the thumbnails vanish after the first reload.</item>
    /// <item><c>https://www.youtube-nocookie.com</c> - the exercise player iframe, <c>embedUrl</c> in
    /// the same file. <c>watchUrl</c> is a plain link, which CSP does not govern.</item>
    /// </list>
    /// <c>style-src 'unsafe-inline'</c> stays: Angular injects component styles as runtime
    /// <c>&lt;style&gt;</c> elements, and a nonce for them needs SSR, which this app does not ship.
    /// <c>script-src 'self'</c> holds only because angular.json turns critical-CSS inlining off - its
    /// <c>onload="this.media='all'"</c> is an inline script.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https://img.youtube.com; " +
        "font-src 'self'; " +
        "connect-src 'self' https://img.youtube.com; " +
        "frame-src https://www.youtube-nocookie.com; " +
        "worker-src 'self'; " +
        "manifest-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    /// <summary>
    /// A year, and no <c>includeSubDomains</c>: <c>azurewebsites.net</c> is not ours, and a future
    /// custom domain decides that for itself.
    /// </summary>
    public const string StrictTransportSecurity = "max-age=31536000";

    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=()";

    /// <summary>
    /// Must run before <c>UseDefaultFiles</c> / <c>UseStaticFiles</c>, or static files short-circuit
    /// the pipeline without passing through it.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IHostEnvironment environment)
    {
        var writeHsts = !environment.IsDevelopment();

        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;

                if (writeHsts)
                {
                    headers.StrictTransportSecurity = StrictTransportSecurity;
                }

                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = PermissionsPolicy;

                return Task.CompletedTask;
            });

            await next(context);
        });
    }
}
