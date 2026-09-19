using System.Collections.Concurrent;

namespace po_prostu_silka.Application.Auth;

/// <summary>
/// How often one address may be mailed a reset link.
///
/// <para>
/// SEPARATE FROM THE RATE LIMITER, AND BOTH ARE NEEDED. The limiter in Program.cs partitions on
/// client IP, which caps how fast one caller can ask but does nothing about one victim: an attacker
/// spread across addresses can still fill a single mailbox, and a member who impatiently clicks
/// "send again" six times gets six identical emails. This partitions on the address instead.
/// </para>
///
/// <para>
/// A THROTTLED REQUEST STILL ANSWERS NORMALLY. This decides what is SENT, never what is RETURNED —
/// see ForgotPasswordAsync. If the response ever varied with the throttle, an attacker could probe
/// which addresses are registered by watching for the difference, which is the exact oracle the
/// endpoint's whole shape exists to close.
/// </para>
/// </summary>
public interface IPasswordResetThrottle
{
    /// <summary>
    /// Records an attempt for this address and reports whether an email should be sent now.
    /// Not a pure query — calling it consumes the window.
    /// </summary>
    /// <param name="email">Any casing; normalised internally.</param>
    bool TryAcquire(string email);
}
