using System.IO;
using System.Text.RegularExpressions;

namespace MeshhessenClient.Services.Firmware;

/// <summary>A mounted UF2 bootloader drive, identified by its <c>INFO_UF2.TXT</c>.</summary>
public sealed record Uf2Drive(string Root, string? BoardId, string? BootloaderVersion, string? SoftDevice, string? Label, string InfoText);

/// <summary>
/// UF2 mass-storage flashing (nRF52 / RP2040): the device reboots into its bootloader (admin
/// <c>enter_dfu_mode_request</c>, or a double-tap on RESET), shows up as a removable drive carrying
/// <c>INFO_UF2.TXT</c>, consumes a <c>.uf2</c> copied onto it and reboots — the drive vanishing is the success signal.
/// </summary>
public static class Uf2UsbFlasher
{
    public const string InfoFileName = "INFO_UF2.TXT";

    public static IReadOnlyList<Uf2Drive> FindDrives()
    {
        var list = new List<Uf2Drive>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Removable or DriveType.Fixed)) continue;
                var info = Path.Combine(d.RootDirectory.FullName, InfoFileName);
                if (!File.Exists(info)) continue;
                var text = File.ReadAllText(info);
                var drive = Parse(d.RootDirectory.FullName, text) with { Label = SafeLabel(d) };
                list.Add(drive);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"[UF2] Probe {d.Name} failed: {ex.Message}");
            }
        }
        return list;
    }

    private static string? SafeLabel(DriveInfo d)
    {
        try { return d.VolumeLabel; } catch { return null; }
    }

    /// <summary>Parses INFO_UF2.TXT ("UF2 Bootloader 0.9.2 lib/nrfx …", "Board-ID: RAK4631", "SoftDevice: S140 version 6.1.1").</summary>
    public static Uf2Drive Parse(string root, string infoText)
    {
        string? boardId = null, version = null, softDevice = null;
        foreach (var raw in infoText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Board-ID:", StringComparison.OrdinalIgnoreCase)) boardId = line[9..].Trim();
            else if (line.StartsWith("SoftDevice:", StringComparison.OrdinalIgnoreCase)) softDevice = line[11..].Trim();
            else if (line.StartsWith("UF2 Bootloader", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(line, @"UF2 Bootloader\s+v?(\S+)", RegexOptions.IgnoreCase);
                if (m.Success) version = m.Groups[1].Value;
            }
        }
        return new Uf2Drive(root, boardId, version, softDevice, null, infoText);
    }

    /// <summary>Polls for a UF2 drive that was not in <paramref name="knownRoots"/>; null on timeout.</summary>
    public static async Task<Uf2Drive?> WaitForNewDriveAsync(ISet<string> knownRoots, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var fresh = FindDrives().FirstOrDefault(d => !knownRoots.Contains(d.Root));
            if (fresh != null) return fresh;
            await Task.Delay(500, ct);
        }
        return null;
    }

    /// <summary>Copies the image onto the drive (write-through, so the bootloader sees blocks as they are
    /// written) and waits for the drive to disappear. Returns true when the drive detached (image consumed),
    /// false when it is still mounted afterwards — the bytes then landed somewhere that is not a bootloader.</summary>
    public static async Task<bool> CopyAndWaitAsync(Uf2Drive drive, string uf2Path, IProgress<double>? progress,
        TimeSpan detachTimeout, CancellationToken ct)
    {
        var dest = Path.Combine(drive.Root, Path.GetFileName(uf2Path));
        var total = new FileInfo(uf2Path).Length;
        long written = 0;
        Logger.WriteLine($"[UF2] Copying {Path.GetFileName(uf2Path)} ({total} bytes) to {drive.Root} (Board-ID {drive.BoardId})");
        try
        {
            await using var src = File.OpenRead(uf2Path);
            await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.WriteThrough);
            var buf = new byte[64 * 1024];
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                written += n;
                progress?.Report((double)written / total);
            }
            await dst.FlushAsync(ct);
        }
        catch (IOException ex) when (written >= total)
        {
            // The bootloader often reboots the moment the last block lands; the final flush/close then fails.
            Logger.WriteLine($"[UF2] I/O error after the last block ({ex.Message}) — treating as consumed, verifying detach");
        }
        catch (IOException ex)
        {
            Logger.WriteLine($"[UF2] Copy failed at {written}/{total}: {ex.Message}");
            throw new FirmwareUpdateException("StrFwErrUf2Copy", ex, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new FirmwareUpdateException("StrFwErrUf2Copy", ex, ex.Message);
        }

        var deadline = DateTime.UtcNow + detachTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsMounted(drive.Root))
            {
                Logger.WriteLine("[UF2] Drive detached — image consumed by the bootloader");
                return true;
            }
            await Task.Delay(500, ct);
        }
        Logger.WriteLine("[UF2] Drive still mounted after the copy — the file did not land on a bootloader volume");
        return false;
    }

    private static bool IsMounted(string root)
    {
        try
        {
            var d = new DriveInfo(root);
            return d.IsReady && File.Exists(Path.Combine(root, InfoFileName));
        }
        catch
        {
            return false;
        }
    }
}
