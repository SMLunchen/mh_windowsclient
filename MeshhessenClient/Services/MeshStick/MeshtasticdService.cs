using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;

namespace MeshhessenClient.Services.MeshStick;

public enum DaemonServiceState { NotInstalled, Stopped, StartPending, Running, StopPending, Unknown }

/// <summary>
/// The meshtasticd Windows service as installed by the firmware's MSI: <c>%ProgramFiles%\Meshtastic\meshtasticd.exe</c>
/// registered as service "meshtasticd" with <c>--service -c "%ProgramData%\Meshtastic\config.yaml" -d "%ProgramData%\Meshtastic\data"</c>.
/// The wizard writes that config.yaml (MeshStick pin map + API port), starts the service and lets standard users
/// start/stop it afterwards — a node "reboot" (commit of LoRa settings) ends the daemon process, so the client must
/// be able to bring it back without a UAC prompt.
/// </summary>
public static class MeshtasticdService
{
    public const string ServiceName = "meshtasticd";
    public const int DefaultApiPort = 4403;
    public const string LoopbackHost = "127.0.0.1";

    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Meshtastic");
    public static string ExePath => Path.Combine(InstallDir, "meshtasticd.exe");
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Meshtastic");
    public static string ConfigPath => Path.Combine(DataDir, "config.yaml");
    /// <summary>Downloads, the staged config and the setup log live next to the client EXE (portable, like firmware/).</summary>
    public static string ClientWorkDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meshtasticd");
    public static string SetupLogPath => Path.Combine(ClientWorkDir, "setup.log");

    /// <summary>Default service security descriptor plus start/stop/pause rights for Authenticated Users (AU).</summary>
    public const string ServiceSddlForUsers =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPDTLOCRRC;;;AU)" +
        "(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)";

    public static bool IsInstalled => File.Exists(ExePath);

    public static bool IsLoopback(string? host) =>
        host != null && (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.StartsWith("127.") || host == "::1");

    public static DaemonServiceState QueryState()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            return sc.Status switch
            {
                ServiceControllerStatus.Running => DaemonServiceState.Running,
                ServiceControllerStatus.Stopped => DaemonServiceState.Stopped,
                ServiceControllerStatus.StartPending => DaemonServiceState.StartPending,
                ServiceControllerStatus.StopPending => DaemonServiceState.StopPending,
                _ => DaemonServiceState.Unknown,
            };
        }
        catch (InvalidOperationException)
        {
            return DaemonServiceState.NotInstalled; // no such service
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MeshStick] service query failed: {ex.Message}");
            return DaemonServiceState.Unknown;
        }
    }

    /// <summary><c>meshtasticd.exe --version</c> prints the build version and exits; null when not installed or silent.</summary>
    public static async Task<string?> GetInstalledVersionAsync(CancellationToken ct)
    {
        if (!IsInstalled) return null;
        try
        {
            var psi = new ProcessStartInfo(ExePath, "--version")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                WorkingDirectory = InstallDir,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var output = await p.StandardOutput.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            return ParseVersionOutput(output);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MeshStick] --version failed: {ex.Message}");
            return null;
        }
    }

    public static string? ParseVersionOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(output, @"\d+\.\d+\.\d+(\.[0-9A-Za-z]+)?");
        return m.Success ? m.Value : output.Trim().Split('\n')[0].Trim();
    }

    public static async Task<bool> IsApiReachableAsync(int port, int timeoutMs = 800)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(LoopbackHost, port);
            if (await Task.WhenAny(connect, Task.Delay(timeoutMs)) != connect) return false;
            await connect; // surface exceptions
            return client.Connected;
        }
        catch { return false; }
    }

    public static async Task<bool> WaitForApiAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsApiReachableAsync(port)) return true;
            await Task.Delay(1000, ct);
        }
        return false;
    }

    /// <summary>Makes sure something answers on the API port: if nothing does and the service is stopped, start it
    /// (allowed for standard users after the wizard's setup) and wait. Never starts a second daemon next to a
    /// running one — the daemon restarts itself after a node reboot and may be running outside the SCM's view.</summary>
    public static async Task<bool> EnsureRunningAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        if (await IsApiReachableAsync(port)) return true;
        var state = QueryState();
        Logger.WriteLine($"[MeshStick] API port {port} closed, service state {state}");
        if (state == DaemonServiceState.Stopped)
        {
            try
            {
                using var sc = new ServiceController(ServiceName);
                sc.Start();
                Logger.WriteLine("[MeshStick] service start requested");
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"[MeshStick] could not start the service: {ex.Message}");
                return false;
            }
        }
        else if (state is DaemonServiceState.NotInstalled or DaemonServiceState.Unknown)
        {
            return false;
        }
        return await WaitForApiAsync(port, timeout, ct);
    }

    /// <summary>config.yaml for the MeshStick on Windows — the firmware's lora-usb-meshstick-1262.yaml pin map spelled
    /// out (the daemon's auto-detection would look for it under /etc/meshtasticd/available.d, which does not exist here),
    /// the API port, and no Linux paths.</summary>
    public static string BuildConfigYaml(int apiPort, int maxNodes = 200)
    {
        if (apiPort <= 0 || apiPort > 65535) throw new ArgumentOutOfRangeException(nameof(apiPort));
        var sb = new StringBuilder();
        sb.Append("# meshtasticd configuration written by the Meshhessen Client (Tools > MeshStick assistant)\n");
        sb.Append("# Elecrow MeshStick: CH341A USB-to-SPI bridge + SX1262 (firmware template lora-usb-meshstick-1262.yaml)\n");
        sb.Append("---\n");
        sb.Append("Lora:\n");
        sb.Append("  Module: sx1262\n");
        sb.Append("  CS: 0\n");
        sb.Append("  IRQ: 6\n");
        sb.Append("  Reset: 2\n");
        sb.Append("  Busy: 4\n");
        sb.Append("  RXen: 1\n");
        sb.Append("  DIO2_AS_RF_SWITCH: true\n");
        sb.Append("  DIO3_TCXO_VOLTAGE: true\n");
        sb.Append("  spidev: ch341\n");
        sb.Append("  USB_VID: 0x1A86\n");
        sb.Append("  USB_PID: 0x5512\n");
        sb.Append("  SX126X_MAX_POWER: 22\n");
        sb.Append("\n");
        sb.Append("Logging:\n");
        sb.Append("  LogLevel: info\n");
        sb.Append("\n");
        sb.Append("General:\n");
        sb.Append($"  MaxNodes: {maxNodes}\n");
        sb.Append("  MaxMessageQueue: 100\n");
        sb.Append($"  APIPort: {apiPort}\n");
        return sb.ToString();
    }

    private static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>The elevated part of the setup as one PowerShell script (one UAC prompt): config.yaml into ProgramData
    /// FIRST (the MSI starts the service right away and keeps an existing config.yaml — with its own default config
    /// the daemon would auto-detect the stick, look for /etc/meshtasticd/available.d, exit, and the installer would
    /// roll back with error 1920/1603), then the optional MSI install with a verbose log, the optional CH341 DLL next
    /// to the daemon, service rights for standard users, restart-on-exit, automatic start, then (re)start the
    /// service. Exit code 0 = everything done; 20 = MSI failed (the MSI log's error lines are in the setup log).</summary>
    public static string BuildSetupScript(string? msiPath, string stagedConfigPath, string? stagedDllPath, string logPath)
    {
        var msiLog = Path.Combine(Path.GetDirectoryName(logPath) ?? ClientWorkDir, "msi.log");
        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference = 'Continue'\n");
        sb.Append($"$log = {PsQuote(logPath)}\n");
        sb.Append("function Log($m) { (\"{0} {1}\" -f (Get-Date -Format 'HH:mm:ss'), $m) | Out-File -FilePath $log -Append -Encoding utf8 }\n");
        sb.Append("Log 'MeshStick setup started (elevated)'\n");
        sb.Append("try {\n");
        sb.Append($"  New-Item -ItemType Directory -Force -Path {PsQuote(DataDir)} | Out-Null\n");
        sb.Append($"  Copy-Item -LiteralPath {PsQuote(stagedConfigPath)} -Destination {PsQuote(ConfigPath)} -Force\n");
        sb.Append("  Log 'config.yaml written'\n");
        if (msiPath != null)
        {
            sb.Append($"  Log ('Installing ' + {PsQuote(msiPath)})\n");
            sb.Append($"  $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/i', ('\"' + {PsQuote(msiPath)} + '\"'), '/qn', '/norestart', '/l*v', ('\"' + {PsQuote(msiLog)} + '\"')) -Wait -PassThru\n");
            sb.Append("  Log ('msiexec exit code ' + $p.ExitCode)\n");
            // 3010 = done, reboot pending; 1638 = this version is already installed
            sb.Append("  if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010 -and $p.ExitCode -ne 1638) {\n");
            sb.Append($"    if (Test-Path -LiteralPath {PsQuote(msiLog)}) {{ Get-Content -LiteralPath {PsQuote(msiLog)} | Where-Object {{ $_ -match 'Error [0-9]{{4}}|failed to start|Rollback|return value 3' }} | Select-Object -First 8 | ForEach-Object {{ Log ('MSI: ' + $_.Trim()) }} }}\n");
            sb.Append("    Log 'MSI installation failed'; exit 20\n");
            sb.Append("  }\n");
        }
        sb.Append($"  if (-not (Test-Path -LiteralPath {PsQuote(ExePath)})) {{ Log 'meshtasticd.exe not found'; exit 21 }}\n");
        if (stagedDllPath != null)
        {
            sb.Append($"  Copy-Item -LiteralPath {PsQuote(stagedDllPath)} -Destination {PsQuote(Path.Combine(InstallDir, MeshStickDeviceProbe.DriverDllName))} -Force\n");
            sb.Append("  Log 'CH341DLLA64.DLL placed next to meshtasticd.exe'\n");
        }
        sb.Append($"  & sc.exe sdset {ServiceName} {PsQuote(ServiceSddlForUsers)} | Out-Null\n");
        sb.Append("  Log ('service rights for users: sc exit ' + $LASTEXITCODE)\n");
        sb.Append($"  & sc.exe failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null\n");
        sb.Append($"  & sc.exe failureflag {ServiceName} 1 | Out-Null\n");
        sb.Append($"  & sc.exe config {ServiceName} start= auto | Out-Null\n");
        sb.Append($"  $svc = Get-Service -Name {ServiceName} -ErrorAction Stop\n");
        sb.Append($"  if ($svc.Status -ne 'Stopped') {{ Log 'Stopping service'; Stop-Service -Name {ServiceName} -Force -ErrorAction SilentlyContinue; $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }}\n");
        sb.Append($"  Log 'Starting service'; Start-Service -Name {ServiceName} -ErrorAction Stop\n");
        sb.Append("  $svc.WaitForStatus('Running', [TimeSpan]::FromSeconds(60))\n");
        sb.Append($"  Log ('service state ' + (Get-Service -Name {ServiceName}).Status)\n");
        sb.Append("  exit 0\n");
        sb.Append("} catch { Log ('ERROR: ' + $_); exit 1 }\n");
        return sb.ToString();
    }

    public static string ExtractDir => Path.Combine(ClientWorkDir, "extract");
    public static string ProbeDataDir => Path.Combine(Path.GetTempPath(), "meshhessen-meshtasticd-probe");

    /// <summary>Pulls meshtasticd.exe out of the MSI without installing anything: an administrative install
    /// (<c>msiexec /a</c>) only copies the files and needs no administrator rights.</summary>
    public static async Task<string?> ExtractExeFromMsiAsync(string msiPath, CancellationToken ct)
    {
        Directory.CreateDirectory(ExtractDir);
        var psi = new ProcessStartInfo("msiexec.exe", $"/a \"{msiPath}\" /qn TARGETDIR=\"{ExtractDir}\"")
        {
            UseShellExecute = false, CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p == null) return null;
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
        {
            Logger.WriteLine($"[MeshStick] msiexec /a failed with {p.ExitCode}");
            return null;
        }
        return Directory.EnumerateFiles(ExtractDir, "meshtasticd.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    public sealed record DaemonProbeResult(bool StillRunning, int? ExitCode, string Output);

    /// <summary>Runs the daemon in the foreground for a short while with the given config and a scratch data
    /// directory, capturing its console output — the only place where it says why it cannot start (the service has
    /// no console, and Windows only logs "the pipe has been ended"). Still running at the end = the config works.</summary>
    public static async Task<DaemonProbeResult> RunForegroundProbeAsync(string exePath, string configPath, int port,
        TimeSpan duration, CancellationToken ct)
    {
        Directory.CreateDirectory(ProbeDataDir);
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? InstallDir,
        };
        foreach (var a in new[] { "-c", configPath, "-d", ProbeDataDir, "-p", port.ToString() }) psi.ArgumentList.Add(a);
        var sb = new StringBuilder();
        var gate = new object();
        using var p = new Process { StartInfo = psi };
        void Collect(object? _, DataReceivedEventArgs e) { if (e.Data != null) lock (gate) sb.AppendLine(e.Data); }
        p.OutputDataReceived += Collect;
        p.ErrorDataReceived += Collect;
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        bool exited = await Task.Run(() => p.WaitForExit((int)duration.TotalMilliseconds), ct);
        if (!exited)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
        }
        await Task.Delay(300, CancellationToken.None); // let the async readers drain
        string output;
        lock (gate) output = sb.ToString();
        return new DaemonProbeResult(!exited, exited ? p.ExitCode : null, output);
    }

    /// <summary>Runs the elevated setup. Returns the UAC outcome, the script's exit code and the setup log text.</summary>
    public static async Task<(ElevatedRunResult Result, int ExitCode, string Log)> RunSetupAsync(string? msiPath, int apiPort,
        string? stagedDllPath, CancellationToken ct)
    {
        Directory.CreateDirectory(ClientWorkDir);
        var stagedConfig = Path.Combine(ClientWorkDir, "config.yaml");
        await File.WriteAllTextAsync(stagedConfig, BuildConfigYaml(apiPort), new UTF8Encoding(false), ct);
        var script = Path.Combine(ClientWorkDir, "setup.ps1");
        await File.WriteAllTextAsync(script, BuildSetupScript(msiPath, stagedConfig, stagedDllPath, SetupLogPath), new UTF8Encoding(true), ct);
        try { if (File.Exists(SetupLogPath)) File.Delete(SetupLogPath); } catch { }

        Logger.WriteLine($"[MeshStick] running elevated setup (msi={(msiPath != null ? Path.GetFileName(msiPath) : "none")}, port={apiPort})");
        var (res, code) = await ElevatedProcess.RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"", hidden: true, ct);
        string log = "";
        try { if (File.Exists(SetupLogPath)) log = await File.ReadAllTextAsync(SetupLogPath, ct); } catch { }
        Logger.WriteLine($"[MeshStick] setup finished: {res}, exit {code}");
        return (res, code, log);
    }
}
