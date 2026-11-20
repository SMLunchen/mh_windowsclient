using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MeshhessenClient.Services.MeshStick;

/// <summary>
/// Gets the WCH CH341PAR driver onto the PC. The client ships the Windows 10/11 x64 package as an embedded ZIP
/// (Resources/drivers, taken unchanged from WCH's CH341PAR.EXE, folder "WIN 1X") and installs it silently with
/// <c>pnputil /add-driver … /install</c> plus the two application DLLs that WCH's own setup copies into System32 /
/// SysWOW64 — meshtasticd loads CH341DLLA64.DLL at runtime. A newer package downloaded from WCH (EXE = WinRAR SFX with
/// an interactive setup, or ZIP) can be installed the same way; files in the Downloads folder are picked up automatically.
/// </summary>
public static class Ch341DriverSetup
{
    public const string WchDownloadPageExe = "https://www.wch-ic.com/downloads/CH341PAR_EXE.html";
    public const string WchDownloadPageZip = "https://www.wch-ic.com/downloads/CH341PAR_ZIP.html";

    public const string BundledDriverVersion = "2.6.2025.4";
    public const string BundledResourceName = "MeshhessenClient.Drivers.CH341PAR.zip";
    public const string InfName = "CH341WDM.INF";
    public const string Dll32Name = "CH341DLL.DLL";

    private static readonly Regex CandidateName = new(@"^CH341PAR[^\\/]*\.(exe|zip)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsCandidate(string path) => CandidateName.IsMatch(Path.GetFileName(path));

    public static string DownloadsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public static string ExtractRoot => Path.Combine(Path.GetTempPath(), "meshhessen-ch341par");
    public static string DriverLogPath => Path.Combine(MeshtasticdService.ClientWorkDir, "driver-setup.log");

    /// <summary>The CH341DLLA64.DLL of the last installed package — the daemon setup also copies it next to
    /// meshtasticd.exe, so the daemon finds it even if System32 was not writable for some reason.</summary>
    public static string? StagedDll { get; private set; }

    /// <summary>CH341PAR*.exe / *.zip in the given folder (default: the user's Downloads), newest first.</summary>
    public static IReadOnlyList<string> FindDownloadedInstallers(string? folder = null)
    {
        folder ??= DownloadsFolder;
        try
        {
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            return Directory.EnumerateFiles(folder)
                .Where(IsCandidate)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MeshStick] scanning {folder} failed: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>Picks the INF to hand to pnputil out of an extracted CH341PAR package. WCH ships one set for
    /// Windows 10/11 ("WIN 1X") and one for older Windows at the root; the Windows-10 / 64-bit one is preferred,
    /// a CH341-named INF over others.</summary>
    public static string? PickInf(IEnumerable<string> extractedFiles)
    {
        static int Score(string path)
        {
            var p = path.Replace('/', '\\').ToUpperInvariant();
            int s = 0;
            var name = Path.GetFileName(p);
            if (name.Contains("CH341")) s += 100;
            if (p.Contains("WIN 1X") || p.Contains("WIN10") || p.Contains("WIN11") || p.Contains("X64") || p.Contains("AMD64")) s += 10;
            if (p.Contains("X86\\") || p.Contains("WIN7") || p.Contains("WINXP") || p.Contains("WIN98")) s -= 20;
            s -= p.Count(c => c == '\\'); // shallower wins on ties
            return s;
        }
        return extractedFiles
            .Where(f => f.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Score)
            .FirstOrDefault();
    }

    /// <summary>The file with the given name that sits next to the chosen INF, else anywhere in the package.</summary>
    public static string? FindBeside(IEnumerable<string> extractedFiles, string? infPath, string fileName)
    {
        var all = extractedFiles.Where(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (infPath != null)
        {
            var dir = Path.GetDirectoryName(infPath) ?? "";
            var beside = all.FirstOrDefault(f => string.Equals(Path.GetDirectoryName(f), dir, StringComparison.OrdinalIgnoreCase));
            if (beside != null) return beside;
        }
        return all.FirstOrDefault();
    }

    public static string? FindDll(IEnumerable<string> extractedFiles) => FindBeside(extractedFiles, null, MeshStickDeviceProbe.DriverDllName);

    /// <summary>Unpacks the embedded driver package to %TEMP% and returns the folder.</summary>
    public static string ExtractBundled()
    {
        var dir = Path.Combine(ExtractRoot, "bundled-" + BundledDriverVersion);
        Directory.CreateDirectory(dir);
        using var stream = typeof(Ch341DriverSetup).Assembly.GetManifestResourceStream(BundledResourceName)
                           ?? throw new InvalidOperationException($"embedded resource {BundledResourceName} missing");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var target = Path.Combine(dir, entry.Name);   // flat package, no sub-folders
            entry.ExtractToFile(target, overwrite: true);
        }
        return dir;
    }

    /// <summary>The elevated part of a driver install as a PowerShell script: pnputil adds the package to the driver
    /// store and binds it to the plugged-in stick; the DLLs go where WCH's setup puts them (64-bit into System32,
    /// 32-bit into SysWOW64). Exit 0 = installed; pnputil 3010 (reboot pending) and 259 (no matching device right
    /// now) count as success.</summary>
    public static string BuildInstallScript(string infPath, string? dll64Path, string? dll32Path, string logPath)
    {
        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var wow64 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference = 'Continue'\n");
        sb.Append($"$log = {ElevatedProcess.PsQuote(logPath)}\n");
        sb.Append("function Log($m) { (\"{0} {1}\" -f (Get-Date -Format 'HH:mm:ss'), $m) | Out-File -FilePath $log -Append -Encoding utf8 }\n");
        sb.Append("Log 'CH341 driver setup started (elevated)'\n");
        sb.Append("try {\n");
        sb.Append($"  Log ('pnputil /add-driver ' + {ElevatedProcess.PsQuote(infPath)} + ' /install')\n");
        sb.Append($"  $out = & pnputil.exe /add-driver {ElevatedProcess.PsQuote(infPath)} /install 2>&1 | Out-String\n");
        sb.Append("  $code = $LASTEXITCODE\n");
        sb.Append("  Log $out.Trim()\n");
        sb.Append("  Log ('pnputil exit code ' + $code)\n");
        sb.Append("  if ($code -ne 0 -and $code -ne 3010 -and $code -ne 259) { exit $code }\n");
        if (dll64Path != null)
        {
            sb.Append($"  Copy-Item -LiteralPath {ElevatedProcess.PsQuote(dll64Path)} -Destination {ElevatedProcess.PsQuote(Path.Combine(sys32, MeshStickDeviceProbe.DriverDllName))} -Force\n");
            sb.Append($"  Log ('copied {MeshStickDeviceProbe.DriverDllName} to System32')\n");
        }
        if (dll32Path != null)
        {
            sb.Append($"  Copy-Item -LiteralPath {ElevatedProcess.PsQuote(dll32Path)} -Destination {ElevatedProcess.PsQuote(Path.Combine(wow64, Dll32Name))} -Force\n");
            sb.Append($"  Log ('copied {Dll32Name} to SysWOW64')\n");
        }
        sb.Append("  exit 0\n");
        sb.Append("} catch { Log ('ERROR: ' + $_); exit 1 }\n");
        return sb.ToString();
    }

    /// <summary>Installs an unpacked driver package (folder with INF/CAT/SYS/DLLs). One UAC prompt.</summary>
    public static async Task<(ElevatedRunResult Result, string Detail)> InstallPackageAsync(string packageDir, Action<string> log, CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(packageDir, "*", SearchOption.AllDirectories).ToList();
        var inf = PickInf(files);
        if (inf == null) return (ElevatedRunResult.Failed, "no .inf in the package");
        var dll64 = FindBeside(files, inf, MeshStickDeviceProbe.DriverDllName);
        var dll32 = FindBeside(files, inf, Dll32Name);
        StagedDll = dll64;
        log($"Driver package: {inf}" + (dll64 != null ? $" (+ {MeshStickDeviceProbe.DriverDllName})" : " (no CH341DLLA64.DLL in the package!)"));

        Directory.CreateDirectory(MeshtasticdService.ClientWorkDir);
        var script = Path.Combine(MeshtasticdService.ClientWorkDir, "driver-setup.ps1");
        await File.WriteAllTextAsync(script, BuildInstallScript(inf, dll64, dll32, DriverLogPath), new UTF8Encoding(true), ct);
        try { if (File.Exists(DriverLogPath)) File.Delete(DriverLogPath); } catch { }

        var (res, code) = await ElevatedProcess.RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"", hidden: true, ct);
        string setupLog = "";
        try { if (File.Exists(DriverLogPath)) setupLog = await File.ReadAllTextAsync(DriverLogPath, ct); } catch { }
        foreach (var line in setupLog.Split('\n', StringSplitOptions.RemoveEmptyEntries)) log(line.Trim());
        log($"driver setup finished: {res}, exit {code}");
        return (res, $"exit code {code}");
    }

    /// <summary>Installs the driver package that ships with the client.</summary>
    public static async Task<(ElevatedRunResult Result, string Detail)> InstallBundledAsync(Action<string> log, CancellationToken ct)
    {
        var dir = ExtractBundled();
        log($"Bundled WCH CH341PAR {BundledDriverVersion} unpacked to {dir}");
        return await InstallPackageAsync(dir, log, ct);
    }

    /// <summary>Installs a package downloaded from WCH: a ZIP is unpacked and installed silently like the bundled one;
    /// the EXE is WinRAR self-extractor whose setup has no silent mode, so it is started elevated and the user
    /// clicks "Install" in WCH's window.</summary>
    public static async Task<(ElevatedRunResult Result, string Detail)> InstallAsync(string path, Action<string> log, CancellationToken ct)
    {
        if (!File.Exists(path)) return (ElevatedRunResult.Failed, $"file not found: {path}");
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".exe")
        {
            log($"Starting WCH setup: {path}");
            var (res, code) = await ElevatedProcess.RunAsync(path, "", hidden: false, ct);
            log($"WCH setup exited with code {code}");
            // The WCH setup returns 0 even when the user closes it without installing; the device probe decides.
            return (res, $"exit code {code}");
        }
        if (ext == ".zip")
        {
            var dir = Path.Combine(ExtractRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);
            log($"Extracting {Path.GetFileName(path)} to {dir}");
            ZipFile.ExtractToDirectory(path, dir, overwriteFiles: true);
            return await InstallPackageAsync(dir, log, ct);
        }
        return (ElevatedRunResult.Failed, $"unsupported file type {ext}");
    }
}
