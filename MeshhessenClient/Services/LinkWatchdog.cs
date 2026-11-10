namespace MeshhessenClient.Services;

/// <summary>
/// Liveness bookkeeping for the device link. The firmware closes its client API after 15 minutes
/// without any ToRadio from us (serial: <c>SERIAL_CONNECTION_TIMEOUT</c>, TCP: <c>TCP_IDLE_TIMEOUT_MS</c>)
/// and then silently stops delivering packets — so the client sends a <c>heartbeat</c> every
/// <see cref="HeartbeatInterval"/>. Firmware ≥ 2.8.0 answers each heartbeat with a <c>queueStatus</c>
/// frame, which doubles as a probe: two unanswered probes in a row mean the link is dead even though
/// the port/socket looks open. Older firmware never answers, so for those only a long total silence
/// (<see cref="SilentFallback"/>) counts as stale. Pure logic, no timers — the protocol service drives it.
/// </summary>
public sealed class LinkWatchdog
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
    /// <summary>Stale verdict for devices that never answered a heartbeat: no frame at all for this long.</summary>
    public static readonly TimeSpan SilentFallback = TimeSpan.FromMinutes(45);
    /// <summary>Max heartbeats without any frame before a device known to answer them is considered gone.</summary>
    public const int MaxUnansweredProbes = 2;

    private DateTime? _lastFrameUtc;
    private DateTime? _lastHeartbeatUtc;
    private int _heartbeatsSinceLastFrame;
    private bool _deviceAnswersHeartbeats;
    private bool _staleReported;

    public DateTime? LastFrameUtc => _lastFrameUtc;
    public bool DeviceAnswersHeartbeats => _deviceAnswersHeartbeats;
    public int HeartbeatsSinceLastFrame => _heartbeatsSinceLastFrame;

    public void Reset()
    {
        _lastFrameUtc = null;
        _lastHeartbeatUtc = null;
        _heartbeatsSinceLastFrame = 0;
        _deviceAnswersHeartbeats = false;
        _staleReported = false;
    }

    /// <summary>Any FromRadio frame arrived. <paramref name="isQueueStatus"/> marks the heartbeat reply.</summary>
    public void OnFrameReceived(DateTime nowUtc, bool isQueueStatus)
    {
        _lastFrameUtc = nowUtc;
        // A queueStatus shortly after our heartbeat proves this firmware answers probes.
        if (isQueueStatus && _lastHeartbeatUtc is { } hb && nowUtc - hb < TimeSpan.FromMinutes(1))
            _deviceAnswersHeartbeats = true;
        _heartbeatsSinceLastFrame = 0;
        _staleReported = false;
    }

    public void OnHeartbeatSent(DateTime nowUtc)
    {
        _lastHeartbeatUtc = nowUtc;
        _heartbeatsSinceLastFrame++;
    }

    /// <summary>True exactly once per silence when the link should be considered dead.</summary>
    public bool CheckStale(DateTime nowUtc)
    {
        if (_staleReported || _lastFrameUtc is not { } last) return false;
        var silence = nowUtc - last;
        bool stale = _deviceAnswersHeartbeats
            ? _heartbeatsSinceLastFrame >= MaxUnansweredProbes && silence > HeartbeatInterval
            : silence > SilentFallback;
        if (stale) _staleReported = true;
        return stale;
    }

    public string Describe(DateTime nowUtc) =>
        _lastFrameUtc is { } last
            ? $"last frame {(nowUtc - last).TotalMinutes:0.0} min ago, {_heartbeatsSinceLastFrame} heartbeat(s) unanswered, answersHeartbeats={_deviceAnswersHeartbeats}"
            : "no frame received yet";
}
