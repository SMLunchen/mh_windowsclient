using System.Diagnostics;
using System.IO;

namespace MeshhessenClient.Services;

public enum FormatResult { Success, Failed, Cancelled }

public record DriveFormatStatus(bool IsRecommended, string FileSystem, string Recommendation);

public static class DriveService
{
    public static List<DriveInfo> GetRemovableDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
            .ToList();
    }

    public static DriveFormatStatus CheckFormat(DriveInfo drive)
    {
        var fs = drive.DriveFormat?.ToUpperInvariant() ?? "UNKNOWN";
        bool ok = fs is "EXFAT" or "FAT32";
        string rec = fs switch
        {
            "EXFAT" => "exFAT",
            "FAT32" => "FAT32",
            _ => "exFAT"
        };
        return new DriveFormatStatus(ok, drive.DriveFormat ?? "Unknown", rec);
    }

    // Returns recommended file system name for PowerShell Format-Volume
    public static string RecommendedFileSystem(DriveInfo drive) => "exFAT";

    // Allocation unit: 4096 for exFAT (recommended for T-Deck tile storage)
    public static int GetAllocationUnit(string fileSystem) => fileSystem switch
    {
        "FAT32" => 512,
        _ => 4096  // exFAT default - saves space for small tiles
    };

    public static string FormatSizeString(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (1L << 30):F1} GB";
        if (bytes >= 1L << 20) return $"{bytes / (1L << 20):F1} MB";
        return $"{bytes / (1L << 10):F0} KB";
    }

    public static async Task<FormatResult> FormatDriveAsync(
        char driveLetter,
        string fileSystem,
        IProgress<string>? progress = null)
    {
        // Map friendly names to PowerShell filesystem param
        string psFs = fileSystem switch
        {
            "FAT32" => "FAT32",
            _ => "exFAT"
        };
        int allocUnit = GetAllocationUnit(psFs);

        // Build PowerShell command for Format-Volume (no interactive prompt)
        string psArgs = $"-NonInteractive -WindowStyle Hidden -Command " +
            $"\"Format-Volume -DriveLetter {driveLetter} " +
            $"-FileSystem {psFs} " +
            $"-AllocationUnitSize {allocUnit} " +
            $"-NewFileSystemLabel 'T-DECK' " +
            $"-Confirm:$false -Force\"";

        progress?.Report($"Formatting {driveLetter}:\\ as {psFs} ({allocUnit} bytes)...");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = psArgs,
            Verb = "runas",          // UAC elevation
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            var proc = Process.Start(psi);
            if (proc == null) return FormatResult.Failed;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? FormatResult.Success : FormatResult.Failed;
        }
        catch (Exception ex) when (ex.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
                                 || ex.HResult == unchecked((int)0x800704C7))
        {
            return FormatResult.Cancelled;
        }
        catch
        {
            return FormatResult.Failed;
        }
    }

    public static bool IsDriveReady(char driveLetter)
    {
        try
        {
            var info = new DriveInfo(driveLetter.ToString());
            return info.IsReady;
        }
        catch { return false; }
    }
}
