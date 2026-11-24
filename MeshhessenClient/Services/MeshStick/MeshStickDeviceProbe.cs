using System.IO;
using System.Management;

namespace MeshhessenClient.Services.MeshStick;

/// <summary>Where the USB stick stands on this PC, from the wizard's point of view.</summary>
public enum MeshStickState
{
    /// <summary>No device with the CH341 SPI/parallel ids (VID 1A86, PID 5512) is plugged in.</summary>
    NotPresent,
    /// <summary>Plugged in, but Windows reports a problem code — the usual one is 28, "drivers not installed".</summary>
    DriverMissing,
    /// <summary>The kernel driver is bound, but the CH341DLLA64.DLL that meshtasticd loads at runtime is missing
    /// (the ZIP variant of the WCH package installs the driver only, the EXE variant also drops the DLL).</summary>
    DriverLibraryMissing,
    Ready,
}

public sealed record MeshStickProbe(bool Present, int ProblemCode, string? InstanceId, string? Name, string? Service,
    bool DriverDllPresent)
{
    public MeshStickState State => MeshStickDeviceProbe.Classify(this);
}

/// <summary>
/// Finds the Elecrow MeshStick (CH341A USB-to-SPI bridge + SX1262) in the Plug-and-Play tree. The stick enumerates
/// as <c>USB\VID_1A86&amp;PID_5512\&lt;serial&gt;</c>; without the WCH CH341PAR driver Windows lists it with problem
/// code 28 and no class. meshtasticd talks to it through CH341DLLA64.DLL (shipped by CH341PAR), so that DLL is
/// part of the readiness check.
/// </summary>
public static class MeshStickDeviceProbe
{
    public const string HardwareIdPrefix = @"USB\VID_1A86&PID_5512";
    public const string DriverDllName = "CH341DLLA64.DLL";
    public const int ProblemDriverNotInstalled = 28;

    public static string SystemDllPath => Path.Combine(Environment.SystemDirectory, DriverDllName);

    /// <summary>The DLL is searched the way LoadLibrary does: next to meshtasticd.exe first, then System32.</summary>
    public static bool IsDriverDllPresent() =>
        File.Exists(Path.Combine(MeshtasticdService.InstallDir, DriverDllName)) || File.Exists(SystemDllPath);

    public static MeshStickState Classify(MeshStickProbe p)
    {
        if (!p.Present) return MeshStickState.NotPresent;
        if (p.ProblemCode != 0) return MeshStickState.DriverMissing;
        return p.DriverDllPresent ? MeshStickState.Ready : MeshStickState.DriverLibraryMissing;
    }

    /// <summary>Synchronous WMI query (100–500 ms); call from a background task.</summary>
    public static MeshStickProbe Probe()
    {
        bool dll = IsDriverDllPresent();
        try
        {
            // WQL needs the backslash doubled; the C# literal doubles it once more.
            using var searcher = new ManagementObjectSearcher(@"root\CIMV2",
                "SELECT DeviceID, Name, ConfigManagerErrorCode, Service FROM Win32_PnPEntity " +
                "WHERE DeviceID LIKE 'USB\\\\VID_1A86&PID_5512%'");
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                var id = mo["DeviceID"] as string;
                var name = mo["Name"] as string;
                var service = mo["Service"] as string;
                int code = 0;
                try { code = Convert.ToInt32(mo["ConfigManagerErrorCode"] ?? 0); } catch { }
                return new MeshStickProbe(true, code, id, name, service, dll);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MeshStick] WMI probe failed: {ex.Message}");
        }
        return new MeshStickProbe(false, 0, null, null, null, dll);
    }

    public static Task<MeshStickProbe> ProbeAsync() => Task.Run(Probe);

    public static string Describe(MeshStickProbe p) => p.Present
        ? $"present: {p.InstanceId} name='{p.Name}' service='{p.Service}' problem={p.ProblemCode} dll={(p.DriverDllPresent ? "yes" : "no")}"
        : $"not present (dll={(p.DriverDllPresent ? "yes" : "no")})";
}
