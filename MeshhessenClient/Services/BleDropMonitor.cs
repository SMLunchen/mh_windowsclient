namespace MeshhessenClient.Services;

/// <summary>
/// Counts Bluetooth links that went away without the client asking (Windows reported the drop; not the user,
/// not the client) and says when that has become a storm worth telling the user about. Bug report 2026-11-14:
/// 140 drops in one evening from a ThinkNode M6 on firmware 2.7.26, fixed in an attic and reachable over
/// Bluetooth only — the link died ~2 s after the last data, mostly in the middle of the node-list download,
/// and the node was unreachable for 10–50 s afterwards: a weak radio path. The client cannot fix that, but
/// it can name the usual causes instead of silently reconnecting all evening. Pure logic, no timers — the
/// main window drives it.
/// </summary>
public sealed class BleDropMonitor
{
    /// <summary>Drops are counted within this sliding window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    /// <summary>This many unexpected drops inside <see cref="Window"/> is a storm.</summary>
    public const int Threshold = 3;

    private readonly List<DateTime> _drops = new();
    private bool _reported;

    public int DropsInWindow(DateTime nowUtc)
    {
        _drops.RemoveAll(t => nowUtc - t > Window);
        return _drops.Count;
    }

    /// <summary>Records an unexpected drop. True exactly once per session when the storm threshold is
    /// crossed; the caller shows the hint then.</summary>
    public bool RecordDrop(DateTime nowUtc)
    {
        _drops.Add(nowUtc);
        if (_reported || DropsInWindow(nowUtc) < Threshold) return false;
        _reported = true;
        return true;
    }

    /// <summary>New session (the user connected by hand): forget the history and re-arm the hint.</summary>
    public void Reset()
    {
        _drops.Clear();
        _reported = false;
    }

    /// <summary>True when the connected device is an nRF52 board on firmware older than 2.8.0 — the
    /// combination whose firmware turns Bluetooth off while a serial (USB) API client is connected.
    /// Unknown hardware or version → false (the generic hint is shown instead).</summary>
    public static bool IsSerialKillsBleFirmware(string? hardwareModel, string? firmwareVersion)
    {
        if (string.IsNullOrWhiteSpace(hardwareModel)) return false;
        if (Firmware.FirmwareCatalogService.GuessArchitecture(hardwareModel) != "nrf52840") return false;
        var v = FirmwareCaps.Parse(firmwareVersion);
        return v != null && !FirmwareCaps.AtLeast(firmwareVersion, 2, 8);
    }
}
