using System.Net;

namespace po_prostu_silka.Application.Auth;

/// <summary>
/// Names of the rate-limiting policies configured in Program.cs.
///
/// A constant rather than a literal at each end because the two ends are in different files: a typo
/// in <c>RequireRateLimiting</c> throws at startup, but only if someone runs the app — and the whole
/// point of the policy is that it protects an endpoint nobody exercises on a happy path.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Per-client-IP cap on <c>POST /api/auth/forgot-password</c>. The ONLY rate-limited endpoint in
    /// this app: it is the only anonymous one that sends mail on demand. Adding a global limiter is
    /// explicitly out of scope — see the plan.
    /// </summary>
    public const string ForgotPassword = "forgot-password";

    /// <summary>
    /// The partition key for <see cref="ForgotPassword"/>: the caller's IP address, without the port.
    ///
    /// <para>
    /// THE PORT IS WHY THIS IS NOT ONE LINE. App Service and Front Door write X-Forwarded-For as
    /// "client-ip:ephemeral-port", and that port changes on every connection. Taking the header
    /// segment verbatim therefore puts each request from one caller in a partition of its own, which
    /// silently turns the limiter off in exactly the environment it exists for - while the tests,
    /// which send a bare address, keep passing. Parsing the address back out is what makes the cap
    /// real.
    /// </para>
    ///
    /// <para>
    /// The header is attacker controlled, so this stays a courtesy cap on volume and NOT an
    /// authorization control. <c>IPasswordResetThrottle</c> is what protects an individual mailbox,
    /// and it cannot be sidestepped by changing IP.
    /// </para>
    /// </summary>
    public static string PartitionKey(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var candidate = forwardedFor?.Split(',').FirstOrDefault()?.Trim();

        // Empty rather than null is what a header sent with no value produces, and ?? does not catch
        // it - every such caller would share one partition.
        if (!string.IsNullOrEmpty(candidate))
        {
            // Parses "203.0.113.5:51422" and "[2001:db8::1]:51422" as well as a bare address. Anything
            // unparseable is used as-is: a junk value still partitions its sender consistently, which
            // is all the limiter needs.
            return IPEndPoint.TryParse(candidate, out var endpoint)
                ? endpoint.Address.ToString()
                : candidate;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
