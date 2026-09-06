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

/// <summary>
/// In-memory sliding window.
///
/// <para>
/// PER-INSTANCE STATE, AND THEREFORE CORRECT ONLY WHILE THE APP RUNS SINGLE-INSTANCE. That is what
/// the Basic-tier App Service this deploys to actually is (infrastructure.md), so it holds today.
/// It would silently stop holding on scale-out: each instance would keep its own window and one
/// address could be mailed once per instance per window. If this app ever scales out, this needs to
/// move to a shared store — the failure is invisible, so it will not announce itself.
/// </para>
///
/// <para>
/// Registered as a singleton; the dictionary is the whole state. Entries are pruned opportunistically
/// on each call rather than by a timer, so an idle app holds nothing and there is no background work
/// to own.
/// </para>
/// </summary>
public class PasswordResetThrottle(TimeProvider timeProvider) : IPasswordResetThrottle
{
    /// <summary>
    /// One email per address per window. Long enough that impatient re-clicks collapse into one
    /// message, short enough that someone who genuinely lost the first email is not stuck for long.
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Pruning above this size only. Below it the dictionary is too small to be worth walking, and
    /// the sweep is O(n) on a request thread.
    /// </summary>
    private const int PruneThreshold = 1000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> lastSentAt = new();

    /// <remarks>
    /// The window is consumed here, BEFORE the caller enqueues and commits. If that save then fails,
    /// the member is held off for the full window with no email sent, and has to wait it out. That
    /// is the deliberate trade: releasing the window on failure would mean the caller could tell a
    /// failed send from a successful one by retrying, which is the disclosure this whole flow avoids.
    /// </remarks>
    public bool TryAcquire(string email)
    {
        var key = email.Trim().ToLowerInvariant();
        var now = timeProvider.GetUtcNow();

        Prune(now);

        // AddOrUpdate rather than a read followed by a write: two simultaneous requests for the same
        // address must not both observe "no recent send" and both proceed.
        var granted = false;

        lastSentAt.AddOrUpdate(
            key,
            _ =>
            {
                granted = true;
                return now;
            },
            (_, previous) =>
            {
                if (now - previous < Window)
                {
                    // Deliberately NOT refreshed to `now`. Sliding the window on a refused attempt
                    // would let a caller hold an address permanently throttled by polling it.
                    return previous;
                }

                granted = true;
                return now;
            });

        return granted;
    }

    /// <summary>
    /// Drops windows that have expired, once the dictionary is large enough to be worth walking.
    ///
    /// <para>
    /// O(n) on a request thread, and it repeats the sweep on every call while all entries are fresh.
    /// Fine at this app's scale: keys are bounded by the member count, because TryAcquire is reached
    /// only after the address has been matched to an account - an unknown address never allocates an
    /// entry, which is the property that keeps this from being a memory-growth vector.
    /// </para>
    /// </summary>
    private void Prune(DateTimeOffset now)
    {
        if (lastSentAt.Count < PruneThreshold)
        {
            return;
        }

        foreach (var entry in lastSentAt)
        {
            if (now - entry.Value >= Window)
            {
                // TryRemove, not Remove: another thread may have refreshed this entry between the
                // read above and here, and losing that race must not throw.
                lastSentAt.TryRemove(entry.Key, out _);
            }
        }
    }
}
