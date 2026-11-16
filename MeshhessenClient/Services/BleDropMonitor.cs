namespace MeshhessenClient.Services;

/// <summary>
/// Counts Bluetooth links the <em>device</em> dropped (not the user, not the client) and says when that
/// has become a storm worth telling the user about. Bug report 2026-11-14: 140 drops in one evening from a
/// ThinkNode M6 on firmware 2.7.26 — the node itself shut Bluetooth down every time a USB-serial client
/// opened the API (PowerFSM <c>serialEnter()</c> → <c>setBluetoothEnable(false)</c> on nRF52, fixed in
/// firmware 2.8.0 by PR #10349). The client cannot fix that, but it can name it instead of silently
/// reconnecting all evening. Pure logic, no timers — the main window drives it.
/// </summary>
public sealed class BleDropMonitor
{
    /// <summary>Drops are counted within this sliding window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    /// <summary>This many device-initiated drops inside <see cref="Window"/> is a storm.</summary>
    public const int Threshold = 3;

    private readonly List<DateTime> _drops = new();
    private bool _reported;

    public int DropsInWindow(DateTime nowUtc)
    {
        _drops.RemoveAll(t => nowUtc - t > Window);
        return _drops.Count;
    }

    /// <summary>Records a device-initiated drop. True exactly once per session when the storm threshold is
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
        if (!IsNrf52(hardwareModel)) return false;
        var v = FirmwareCaps.Parse(firmwareVersion);
        return v != null && !FirmwareCaps.AtLeast(firmwareVersion, 2, 8);
    }

    // HardwareModel names of the nRF52840 boards (firmware variants/nrf52840/*). Names arrive as
    // THINKNODE_M6 (proto) or ThinknodeM6 (C# enum ToString) — compared without underscores.
    private static readonly string[] Nrf52Patterns =
    {
        "RAK4631", "RAK2560", "RAK3", "RAK_WISMESH", "T_ECHO", "TECHO", "T114", "HELTEC_MESH_NODE", "NANO_G2",
        "WIO_WM1110", "WIO_TRACKER", "TRACKER_T1000", "T1000", "CANARYONE", "NRF52", "PPR", "XIAO_NRF52", "MESHLINK",
        "THINKNODE_M1", "THINKNODE_M3", "THINKNODE_M4", "THINKNODE_M6", "THINKNODE_M8", "ME25LS01", "MS24SF1",
        "SEEED_SOLAR", "T_IMPULSE", "MUZI", "R1_NEO", "FEATHER_DIY",
    };

    public static bool IsNrf52(string? hardwareModel)
    {
        if (string.IsNullOrWhiteSpace(hardwareModel)) return false;
        var n = hardwareModel.ToUpperInvariant().Replace("_", "");
        return Nrf52Patterns.Any(p => n.Contains(p.Replace("_", "")));
    }
}
