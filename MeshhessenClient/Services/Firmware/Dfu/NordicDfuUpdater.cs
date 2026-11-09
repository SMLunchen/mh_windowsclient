using System.Diagnostics;

namespace MeshhessenClient.Services.Firmware.Dfu;

/// <summary>
/// Drives an nRF52 BLE firmware update end to end: release the mesh link, trigger the buttonless reboot
/// into the bootloader (Secure FE59 or Legacy 1530 service of the app firmware), find the bootloader
/// (same MAC or MAC+1), detect which DFU protocol it speaks and run the matching transport with bounded
/// retries. Legacy has no resume, so a failed Legacy session is retried whole; a bootloader wedged in a
/// stale session (START → INVALID_STATE) is reset over a fresh connection first.
/// </summary>
public sealed class NordicDfuUpdater
{
    private static readonly TimeSpan GattReleaseDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan DfuRebootWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DetectScanTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SessionRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ResetPrimeWait = TimeSpan.FromSeconds(4);
    /// <summary>Bootloader 2.6.0 boots the old application after 6 minutes idle in DFU; stop retrying before that.</summary>
    private static readonly TimeSpan PreStartBudget = TimeSpan.FromMinutes(5);
    private const int ConnectAttempts = 4;
    private const int LegacySessionAttempts = 3;
    private const int MaxStaleResets = 2;

    public enum Protocol { Legacy, Secure }

    private readonly ulong _meshAddress;
    private readonly Func<Task> _releaseMeshLink;
    private readonly IProgress<FirmwareProgress> _progress;

    public NordicDfuUpdater(ulong meshAddress, Func<Task> releaseMeshLink, IProgress<FirmwareProgress> progress)
    {
        _meshAddress = meshAddress;
        _releaseMeshLink = releaseMeshLink;
        _progress = progress;
    }

    /// <param name="alreadyInBootloader">Skip the buttonless trigger because the device is already advertising its bootloader (recovery).</param>
    public async Task RunAsync(DfuPackage package, bool alreadyInBootloader, CancellationToken ct)
    {
        if (!alreadyInBootloader)
        {
            _progress.Report(new FirmwareProgress("StrFwPhaseDisconnecting"));
            await _releaseMeshLink();
            await Task.Delay(GattReleaseDelay, ct);

            _progress.Report(new FirmwareProgress("StrFwPhaseEnablingDfu"));
            await TriggerButtonlessAsync(ct);
            await Task.Delay(DfuRebootWait, ct);
        }

        var budget = Stopwatch.StartNew();
        bool started = false;

        _progress.Report(new FirmwareProgress("StrFwPhaseWaitingReboot"));
        var targets = new HashSet<ulong> { _meshAddress, BleAddress.PlusOne(_meshAddress) };
        var hit = await BleScanner.ScanWithRetriesAsync(h => targets.Contains(h.Address), 3, DetectScanTimeout,
            TimeSpan.FromSeconds(2), "DFU detect", ct);
        if (hit == null)
            throw new FirmwareUpdateException("StrFwErrDfuDeviceNotFound", BleAddress.Format(_meshAddress), BleAddress.Format(BleAddress.PlusOne(_meshAddress)));

        var protocol = await DetectProtocolAsync(hit, ct);
        Logger.WriteLine($"[DFU] Bootloader at {BleAddress.Format(hit.Address)} speaks {protocol}");

        // Legacy: whole-session retries; Secure: one session (it resumes internally, object retries inside).
        int attempts = protocol == Protocol.Legacy ? LegacySessionAttempts : 1;
        int staleResets = 0;
        bool midStreamSeen = false;
        Exception? last = null;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            if (!started && budget.Elapsed > PreStartBudget)
                throw new FirmwareUpdateException("StrFwErrStartTimedOut", new DfuStartTimedOutException(PreStartBudget));
            if (attempt > 1) _progress.Report(new FirmwareProgress("StrFwPhaseRetryingUpload", null, null, new object[] { attempt, attempts }));

            IDfuTransport transport = protocol == Protocol.Legacy
                ? new LegacyDfuTransport(hit.Address, midStreamSeen ? 5 : 10, hit.AddressType)
                : new SecureDfuTransport(hit.Address, hit.AddressType);
            bool completed = false;
            Exception? failure = null;
            try
            {
                await ConnectWithRetryAsync(transport, ct);
                await transport.TransferAsync(package,
                    phase =>
                    {
                        if (phase != "StrFwPhasePreparingFlash") started = true;
                        _progress.Report(new FirmwareProgress(phase, phase == "StrFwPhaseUploading" ? 0 : null,
                            transport.IsLowSpeedTransfer ? "slow" : null));
                    },
                    new Progress<double>(p =>
                    {
                        started = true;
                        _progress.Report(new FirmwareProgress("StrFwPhaseUploading", p,
                            FormatTransfer(p, package.Firmware.Length)));
                    }), ct);
                completed = true;
                _progress.Report(new FirmwareProgress("StrFwPhaseValidating", 1.0));
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { failure = null; throw; }
            catch (DfuStartTimedOutException ex) { throw new FirmwareUpdateException("StrFwErrStartTimedOut", ex); }
            catch (DfuStaleSessionException ex)
            {
                failure = ex;
                staleResets++;
                attempt--; // a cleanup cycle never tried to upload
                if (staleResets > MaxStaleResets)
                    throw new FirmwareUpdateException("StrFwErrDfuStale", ex);
                Logger.WriteLine($"[DFU] Stale session — reset-priming the bootloader ({staleResets}/{MaxStaleResets})");
            }
            catch (DfuMidStreamDisconnectException ex)
            {
                failure = ex;
                last = ex;
                midStreamSeen = true;
                Logger.WriteLine($"[DFU] Mid-stream disconnect at {ex.BytesSent}/{ex.TotalBytes}; next attempt uses the recovery profile");
            }
            catch (Exception ex)
            {
                failure = ex;
                last = ex;
                Logger.WriteLine($"[DFU] Upload attempt {attempt}/{attempts} failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                // No abort after a stale response (some bootloaders wedge) or a dropped link (nothing to talk to).
                if (!completed && failure is not DfuStaleSessionException && failure is not DfuMidStreamDisconnectException)
                    await transport.AbortAsync();
                await transport.DisposeAsync();
            }

            if (failure is DfuStaleSessionException)
            {
                await ResetStaleBootloaderAsync(protocol, hit, ct);
                continue;
            }
            if (attempt < attempts) await Task.Delay(SessionRetryDelay, ct);
        }

        throw last switch
        {
            DfuConnectionException c => new FirmwareUpdateException("StrFwErrDfuConnect", c, c.Message),
            DfuException d => new FirmwareUpdateException("StrFwErrDfuFailed", d, d.Message),
            null => new FirmwareUpdateException("StrFwErrDfuFailed", "no attempt ran"),
            _ => new FirmwareUpdateException("StrFwErrDfuFailed", last, last.Message)
        };
    }

    /// <summary>Writes the buttonless trigger to the app firmware's DFU helper service (Secure FE59 first, then
    /// Legacy 1530 — Meshtastic's default build exposes the latter). The device reboots and drops the link, so a
    /// write that never ACKs is the normal success path; the bootloader scan is the real confirmation.</summary>
    private async Task TriggerButtonlessAsync(CancellationToken ct)
    {
        BleGattLink link;
        try { link = await BleGattLink.OpenAsync(_meshAddress, ct); }
        catch (BleException ex) { throw new FirmwareUpdateException("StrFwErrDfuTrigger", ex, ex.Message); }
        await using (link)
        {
            try
            {
                var secure = await link.TryGetServiceAsync(SecureDfuUuids.Service, TimeSpan.FromSeconds(15), ct);
                if (secure != null)
                {
                    var ch = await link.GetCharacteristicAsync(secure, SecureDfuUuids.ButtonlessNoBonds, ct);
                    var q = await link.SubscribeAsync(ch, indicate: true, ct);
                    await Task.Delay(500, ct);
                    Logger.WriteLine("[DFU] Writing Secure buttonless trigger (0x01)");
                    await WriteIgnoringRebootAsync(link, ch, new byte[] { 0x01 }, ct);
                    var resp = await q.ReceiveAsync(TimeSpan.FromSeconds(3), ct);
                    if (resp != null) Logger.WriteLine($"[DFU] Buttonless indication {Convert.ToHexString(resp)}");
                    return;
                }
                var legacy = await link.TryGetServiceAsync(LegacyDfuUuids.Service, TimeSpan.FromSeconds(15), ct);
                if (legacy != null)
                {
                    var ch = await link.GetCharacteristicAsync(legacy, LegacyDfuUuids.ControlPoint, ct);
                    await link.SubscribeAsync(ch, indicate: false, ct);
                    await Task.Delay(500, ct);
                    Logger.WriteLine("[DFU] Writing Legacy buttonless trigger (0x01 0x04)");
                    await WriteIgnoringRebootAsync(link, ch, new byte[] { 0x01, 0x04 }, ct);
                    await Task.Delay(500, ct);
                    return;
                }
                throw new FirmwareUpdateException("StrFwErrNoDfuService");
            }
            catch (BleException ex)
            {
                throw new FirmwareUpdateException("StrFwErrDfuTrigger", ex, ex.Message);
            }
        }
    }

    private static async Task WriteIgnoringRebootAsync(BleGattLink link, Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristic ch, byte[] payload, CancellationToken ct)
    {
        try
        {
            await link.WriteAsync(ch, payload, withResponse: true, TimeSpan.FromSeconds(5), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Logger.WriteLine($"[DFU] Buttonless write did not ACK ({ex.GetType().Name}: {ex.Message}) — expected when the device reboots");
        }
    }

    private static async Task<Protocol> DetectProtocolAsync(BleScanHit hit, CancellationToken ct)
    {
        if (hit.ServiceUuids.Contains(LegacyDfuUuids.Service)) return Protocol.Legacy;
        if (hit.ServiceUuids.Contains(SecureDfuUuids.Service)) return Protocol.Secure;

        // Advertisement did not carry a DFU UUID (or we missed the scan response) — ask the GATT table.
        Logger.WriteLine("[DFU] No DFU UUID in advertisement — enumerating GATT services");
        try
        {
            await using var link = await BleGattLink.OpenAsync(hit.Address, ct, hit.AddressType);
            var uuids = await link.ListServiceUuidsAsync(TimeSpan.FromSeconds(15), ct);
            Logger.WriteLine($"[DFU] Services: {string.Join(", ", uuids)}");
            if (uuids.Contains(LegacyDfuUuids.Service)) return Protocol.Legacy;
            if (uuids.Contains(SecureDfuUuids.Service)) return Protocol.Secure;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.WriteLine($"[DFU] Service enumeration failed: {ex.Message} — assuming Legacy");
            return Protocol.Legacy;
        }
        throw new FirmwareUpdateException("StrFwErrNoDfuService");
    }

    private async Task ConnectWithRetryAsync(IDfuTransport transport, CancellationToken ct)
    {
        Exception? last = null;
        for (int i = 1; i <= ConnectAttempts; i++)
        {
            _progress.Report(new FirmwareProgress("StrFwPhaseConnectingAttempt", null, null, new object[] { i, ConnectAttempts }));
            try
            {
                await transport.ConnectAsync(ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (DfuException ex) when (ex is not DfuConnectionException) { throw; }
            catch (Exception ex)
            {
                last = ex;
                Logger.WriteLine($"[DFU] Connect attempt {i}/{ConnectAttempts} failed: {BleDiag.Describe(ex)}");
                if (i < ConnectAttempts) await Task.Delay(SessionRetryDelay, ct);
            }
        }
        throw new DfuConnectionException($"Failed to connect to the bootloader after {ConnectAttempts} attempts: {last?.Message}", last);
    }

    private static async Task ResetStaleBootloaderAsync(Protocol protocol, BleScanHit hit, CancellationToken ct)
    {
        IDfuTransport t = protocol == Protocol.Legacy
            ? new LegacyDfuTransport(hit.Address, 10, hit.AddressType)
            : new SecureDfuTransport(hit.Address, hit.AddressType);
        try
        {
            await t.ConnectAsync(ct);
            await t.AbortAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.WriteLine($"[DFU] Reset-prime connect failed: {ex.Message}");
        }
        finally
        {
            await t.DisposeAsync();
        }
        await Task.Delay(ResetPrimeWait, ct);
    }

    internal static string FormatTransfer(double fraction, int total)
    {
        var done = (long)(fraction * total);
        return $"{fraction * 100:0}% ({done / 1024.0:0} / {total / 1024.0:0} kB)";
    }
}
