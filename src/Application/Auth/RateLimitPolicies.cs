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
}
