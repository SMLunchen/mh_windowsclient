namespace MeshhessenClient.Services;

/// <summary>
/// Pacing for the auto-reconnect loop. Bug report 2026-11-14 (ThinkNode M6, weak Bluetooth path): the
/// client reconnected within 5 s after every drop, re-downloaded the whole node list, dropped again mid
/// download and repeated that all evening — 66 of 140 log entries were artefacts of the loop itself. Two
/// knobs: the pause before the first attempt grows with the number of recent drops (a link that keeps
/// failing gets a breather), and the gap between failed attempts grows after the first few quick tries
/// (a rebooting node answers within seconds; a node that is gone for minutes is not helped by hammering).
/// Pure functions, no clock — the window drives them.
/// </summary>
public static class ReconnectBackoff
{
    /// <summary>Attempts the loop makes before it gives up.</summary>
    public const int MaxAttempts = 15;

    /// <summary>Quick attempts (3 s apart) before the gap starts doubling — covers a device reboot.</summary>
    public const int QuickAttempts = 5;

    public static readonly TimeSpan QuickRetry = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan BaseInitialDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxInitialDelay = TimeSpan.FromSeconds(60);

    /// <summary>Pause before the first attempt. 5 s for the first two drops in the monitor window (a reboot
    /// needs that long anyway), then 10 s more per additional recent drop, capped at 60 s.</summary>
    public static TimeSpan InitialDelay(int recentDrops)
    {
        int extra = Math.Max(0, recentDrops - 2);
        var d = BaseInitialDelay + TimeSpan.FromSeconds(10 * extra);
        return d > MaxInitialDelay ? MaxInitialDelay : d;
    }

    /// <summary>Pause after attempt <paramref name="failedAttempt"/> (1-based) failed: 3 s for the first
    /// <see cref="QuickAttempts"/>, then 6, 12, 24, 48 s, capped at 60 s.</summary>
    public static TimeSpan RetryDelay(int failedAttempt)
    {
        if (failedAttempt <= QuickAttempts) return QuickRetry;
        int doublings = failedAttempt - QuickAttempts;           // 1 → 6 s, 2 → 12 s, …
        double seconds = QuickRetry.TotalSeconds * Math.Pow(2, doublings);
        return seconds >= MaxRetry.TotalSeconds ? MaxRetry : TimeSpan.FromSeconds(seconds);
    }
}
