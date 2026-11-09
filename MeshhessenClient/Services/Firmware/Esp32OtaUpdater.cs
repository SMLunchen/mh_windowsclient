using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MeshhessenClient.Services.Firmware;

/// <summary>What the ESP32 updater needs from the live mesh connection.</summary>
public interface IFirmwareDeviceLink
{
    /// <summary>Text of every ClientNotification the device sends (the firmware confirms or refuses the OTA request this way).</summary>
    event EventHandler<string>? ClientNotificationReceived;

    /// <summary>AdminMessage.ota_request — mode 1 = BLE, 2 = WiFi; the loader verifies the image against the hash.</summary>
    Task SendOtaRequestAsync(int mode, byte[] sha256);
}

/// <summary>
/// ESP32 Unified-OTA flow: hash → <c>ota_request</c> → wait for the firmware's confirmation notification →
/// release the mesh link → connect to the loader (BLE scan or TCP, with UDP beacon discovery) → start → stream.
/// Firmware older than 2.7.17 has no <c>ota_request</c>; the wizard gates on that before calling this.
/// </summary>
public sealed class Esp32OtaUpdater
{
    public const int ModeBle = 1;
    public const int ModeWifi = 2;

    private static readonly TimeSpan PreflightTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan GattReleaseDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BleRebootDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WifiDiscoveryWindow = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WifiReadinessDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly Regex OtaWord = new(@"\bOTA\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IFirmwareDeviceLink _link;
    private readonly Func<Task> _releaseMeshLink;
    private readonly IProgress<FirmwareProgress> _progress;

    public Esp32OtaUpdater(IFirmwareDeviceLink link, Func<Task> releaseMeshLink, IProgress<FirmwareProgress> progress)
    {
        _link = link;
        _releaseMeshLink = releaseMeshLink;
        _progress = progress;
    }

    /// <summary>True when a ClientNotification is the firmware's answer to an OTA request.</summary>
    public static bool IsOtaStatusNotification(string? message) =>
        !string.IsNullOrWhiteSpace(message) && OtaWord.IsMatch(message);

    /// <summary>"Rebooting to BLE OTA" / "Rebooting to WiFi OTA" confirm entry; anything else with "OTA" is a refusal.</summary>
    public static bool IsOtaConfirmation(string message, int mode) =>
        message.StartsWith("Rebooting to", StringComparison.OrdinalIgnoreCase) &&
        message.Contains(mode == ModeBle ? "BLE" : "WiFi", StringComparison.OrdinalIgnoreCase);

    public Task RunBleAsync(byte[] firmware, ulong meshAddress, CancellationToken ct) =>
        RunAsync(firmware, ModeBle, () => new BleOtaTransport(meshAddress), null, 5, ct);

    /// <param name="knownHost">Host/IP of the running device (TCP connection) — the fallback when no beacon arrives; null when unknown.</param>
    public Task RunWifiAsync(byte[] firmware, string? knownHost, CancellationToken ct) =>
        RunAsync(firmware, ModeWifi, null, knownHost, 10, ct);

    private async Task RunAsync(byte[] firmware, int mode, Func<IUnifiedOtaTransport>? bleFactory, string? knownHost,
        int connectAttempts, CancellationToken ct)
    {
        if (firmware.Length == 0) throw new FirmwareUpdateException("StrFwErrEmptyFile");
        var shaBytes = SHA256.HashData(firmware);
        var shaHex = Convert.ToHexString(shaBytes).ToLowerInvariant();
        Logger.WriteLine($"[OTA] Image {firmware.Length} bytes sha256={shaHex} mode={(mode == ModeBle ? "BLE" : "WiFi")}");

        // 1. Ask the firmware to reboot into the loader and race its confirmation against a timeout.
        _progress.Report(new FirmwareProgress("StrFwPhaseRequestingOta"));
        var confirmation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNotification(object? s, string msg)
        {
            if (IsOtaStatusNotification(msg)) confirmation.TrySetResult(msg);
        }
        _link.ClientNotificationReceived += OnNotification;
        try
        {
            await _link.SendOtaRequestAsync(mode, shaBytes);
            var winner = await Task.WhenAny(confirmation.Task, Task.Delay(PreflightTimeout, ct));
            if (winner == confirmation.Task)
            {
                var msg = confirmation.Task.Result;
                if (!IsOtaConfirmation(msg, mode))
                {
                    Logger.WriteLine($"[OTA] Firmware refused OTA entry: {msg}");
                    throw new FirmwareUpdateException("StrFwErrOtaRejected", msg);
                }
                Logger.WriteLine($"[OTA] Firmware confirmed: {msg}");
            }
            else
            {
                ct.ThrowIfCancellationRequested();
                Logger.WriteLine("[OTA] No confirmation notification within the preflight window — continuing (older firmware is silent)");
            }
        }
        finally
        {
            _link.ClientNotificationReceived -= OnNotification;
        }

        // 2. Release the mesh connection so the loader's GATT/TCP service is reachable.
        _progress.Report(new FirmwareProgress("StrFwPhaseDisconnecting"));
        await _releaseMeshLink();
        await Task.Delay(GattReleaseDelay, ct);

        // 3. Locate the loader.
        Func<IUnifiedOtaTransport> factory;
        if (mode == ModeBle)
        {
            _progress.Report(new FirmwareProgress("StrFwPhaseWaitingReboot"));
            await Task.Delay(BleRebootDelay, ct);
            factory = bleFactory!;
        }
        else
        {
            _progress.Report(new FirmwareProgress("StrFwPhaseSearchingDevice"));
            var ip = await WifiOtaDiscovery.DiscoverAsync(WifiDiscoveryWindow, ct);
            string host;
            if (ip != null)
            {
                host = ip.ToString();
            }
            else if (!string.IsNullOrWhiteSpace(knownHost))
            {
                host = await ResolveHostAsync(knownHost!, ct);
                Logger.WriteLine($"[OTA] No beacon — falling back to configured host {host}");
                await Task.Delay(WifiReadinessDelay, ct);
            }
            else
            {
                throw new FirmwareUpdateException("StrFwErrWifiNotFound");
            }
            factory = () => new WifiOtaTransport(host);
        }

        // 4. Connect with retries.
        IUnifiedOtaTransport? transport = null;
        Exception? last = null;
        for (int i = 1; i <= connectAttempts; i++)
        {
            _progress.Report(new FirmwareProgress("StrFwPhaseConnectingAttempt", null, null, new object[] { i, connectAttempts }));
            var t = factory();
            try
            {
                await t.ConnectAsync(ct);
                transport = t;
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { await t.DisposeAsync(); throw; }
            catch (Exception ex)
            {
                last = ex;
                Logger.WriteLine($"[OTA] Connect attempt {i}/{connectAttempts} failed: {ex.Message}");
                await t.DisposeAsync();
                if (i < connectAttempts) await Task.Delay(RetryDelay, ct);
            }
        }
        if (transport == null)
            throw new FirmwareUpdateException(mode == ModeBle ? "StrFwErrBleOtaConnect" : "StrFwErrWifiOtaConnect",
                last ?? new Exception("no attempt"), connectAttempts, last?.Message ?? "");

        // 5. Handshake + stream.
        await using (transport)
        {
            try
            {
                _progress.Report(new FirmwareProgress("StrFwPhaseStartingOta"));
                await transport.StartAsync(firmware.Length, shaHex,
                    () => _progress.Report(new FirmwareProgress("StrFwPhaseErasing")), ct);

                var sw = Stopwatch.StartNew();
                _progress.Report(new FirmwareProgress("StrFwPhaseUploading", 0));
                await transport.StreamAsync(firmware, new Progress<double>(p =>
                {
                    var secs = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    var done = p * firmware.Length;
                    var rate = done / secs;
                    var eta = rate > 0 ? (firmware.Length - done) / rate : 0;
                    _progress.Report(new FirmwareProgress("StrFwPhaseUploading", p,
                        $"{p * 100:0}% · {rate / 1024:0.0} kB/s · ETA {TimeSpan.FromSeconds(eta):m\\:ss}"));
                }), ct);
                _progress.Report(new FirmwareProgress("StrFwPhaseValidating", 1.0));
                Logger.WriteLine($"[OTA] Stream completed in {sw.Elapsed.TotalSeconds:0}s");
            }
            catch (OtaException ex)
            {
                Logger.WriteLine($"[OTA] {ex.Kind}: {ex.Message}");
                throw ex.Kind switch
                {
                    OtaErrorKind.HashRejected => new FirmwareUpdateException("StrFwErrHashRejected", ex),
                    OtaErrorKind.VerificationFailed => new FirmwareUpdateException("StrFwErrHashMismatch", ex),
                    _ => new FirmwareUpdateException("StrFwErrOtaFailed", ex, ex.Message)
                };
            }
        }
    }

    private static async Task<string> ResolveHostAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out _)) return host;
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, ct);
            var v4 = addrs.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return v4?.ToString() ?? host;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[OTA] DNS for {host} failed ({ex.Message}); using name as-is");
            return host;
        }
    }
}
