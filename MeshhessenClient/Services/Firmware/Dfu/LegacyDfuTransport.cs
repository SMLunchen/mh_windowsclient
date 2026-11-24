using System.Diagnostics;
using System.Text;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace MeshhessenClient.Services.Firmware.Dfu;

/// <summary>
/// Nordic Legacy DFU (SDK 11 / Adafruit bootloader) over BLE — what most nRF52 Meshtastic boards speak,
/// including RAK4631 with the stock or "OTAFIX" bootloader. Sequence: START_DFU[app] + image sizes →
/// INIT_PARAMS start/bytes/complete → PRN request → RECEIVE_FIRMWARE_IMAGE → stream (packet receipts every
/// N packets) → VALIDATE → ACTIVATE_AND_RESET. Single-bank bootloaders erase the whole app region before
/// answering START (30–50 s of silence), hence the long START timeout.
/// </summary>
public sealed class LegacyDfuTransport : IDfuTransport
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartResponseTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ValidateTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ResetWriteTimeout = TimeSpan.FromSeconds(1);

    public const int LegacyPacketSize = 20;
    public const int MaxHighMtuPacketSize = 244;
    public const int MaxReasonableLegacyInitSize = 256;
    private const int MinSupportedDfuVersion = 5;

    private readonly int _prnInterval;
    private readonly Windows.Devices.Bluetooth.BluetoothAddressType _addressType;
    private BleGattLink? _link;
    private GattCharacteristic? _control;
    private GattCharacteristic? _packet;
    private BleNotificationQueue? _rx;
    private int _dfuVersion = -1;
    private string? _bootloaderRevision;

    public ulong Address { get; }
    public bool IsLowSpeedTransfer => StreamPacketSize() <= LegacyPacketSize;
    public string? BootloaderRevision => _bootloaderRevision;

    /// <param name="prnInterval">Packets between receipt notifications; 10 is Nordic's safe ceiling for SDK11
    /// bootloaders, 5 is used on a retry after a mid-stream drop.</param>
    public LegacyDfuTransport(ulong bootloaderAddress, int prnInterval = 10,
        Windows.Devices.Bluetooth.BluetoothAddressType addressType = Windows.Devices.Bluetooth.BluetoothAddressType.Unspecified)
    {
        Address = bootloaderAddress;
        _prnInterval = Math.Clamp(prnInterval, 1, 10);
        _addressType = addressType;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        BleGattLink link;
        try { link = await BleGattLink.OpenAsync(Address, ct, _addressType); }
        catch (BleException ex) { throw new DfuConnectionException(ex.Message, ex); }
        try
        {
            var svc = await link.TryGetServiceAsync(LegacyDfuUuids.Service, ConnectTimeout, ct)
                      ?? throw new DfuConnectionException("Legacy DFU service (1530) not exposed by the bootloader");
            _control = await link.GetCharacteristicAsync(svc, LegacyDfuUuids.ControlPoint, ct);
            _packet = await link.GetCharacteristicAsync(svc, LegacyDfuUuids.Packet, ct);
            _rx = await link.SubscribeAsync(_control, indicate: false, ct);
            await Task.Delay(500, ct);

            var versionChar = await link.TryGetCharacteristicAsync(svc, LegacyDfuUuids.Version, ct);
            if (versionChar != null)
            {
                var v = await link.TryReadAsync(versionChar, TimeSpan.FromSeconds(5), ct);
                if (v is { Length: >= 2 }) _dfuVersion = v[0] | (v[1] << 8);
            }
            Logger.WriteLine($"[DFU/Legacy] DFU version characteristic = {_dfuVersion} (-1 = absent)");
            if (_dfuVersion is >= 1 and < MinSupportedDfuVersion)
                throw new DfuException($"Legacy DFU bootloader version {_dfuVersion} is too old (need ≥ {MinSupportedDfuVersion}) — update the bootloader first");

            try
            {
                var dis = await link.TryGetServiceAsync(LegacyDfuUuids.DeviceInformationService, TimeSpan.FromSeconds(5), ct);
                var rev = dis == null ? null : await link.TryGetCharacteristicAsync(dis, LegacyDfuUuids.FirmwareRevision, ct);
                var bytes = rev == null ? null : await link.TryReadAsync(rev, TimeSpan.FromSeconds(5), ct);
                if (bytes != null) _bootloaderRevision = Encoding.UTF8.GetString(bytes).Trim('\0', ' ');
            }
            catch (Exception ex) { Logger.WriteLine($"[DFU/Legacy] DIS read skipped: {ex.Message}"); }
            Logger.WriteLine($"[DFU/Legacy] Connected to {BleAddress.Format(Address)} '{link.Name}', bootloader '{_bootloaderRevision ?? "?"}', payload {link.MaxWriteNoResponse} bytes");
            _link = link;
        }
        catch (BleException ex)
        {
            await link.DisposeAsync();
            throw new DfuConnectionException(ex.Message, ex);
        }
        catch
        {
            await link.DisposeAsync();
            throw;
        }
    }

    public async Task TransferAsync(DfuPackage package, Action<string>? onPhase, IProgress<double>? onProgress, CancellationToken ct)
    {
        var link = _link ?? throw new DfuConnectionException("Not connected");
        var init = package.InitPacket;
        var fw = package.Firmware;
        if (init.Length > MaxReasonableLegacyInitSize)
            throw new DfuException($"Init packet is {init.Length} bytes — this looks like a Secure DFU package; the Legacy bootloader will reject it");
        Logger.WriteLine($"[DFU/Legacy] Starting upload (init {init.Length} B, firmware {fw.Length} B, prn {_prnInterval})");

        // 1. START_DFU + image sizes; the bootloader erases the app bank before it answers.
        onPhase?.Invoke("StrFwPhasePreparingFlash");
        await WriteControlAsync(new[] { LegacyDfuOpcode.StartDfu, LegacyDfuOpcode.ImageTypeApplication }, ct);
        await WritePacketAsync(LegacyDfuPayloads.ImageSizes(fw.Length), ct);
        var start = await AwaitResponseAsync(StartResponseTimeout, ct);
        if (start is LegacyDfuResponse.Failure { Status: LegacyDfuStatus.InvalidState })
            throw new DfuStaleSessionException();
        RequireSuccess(LegacyDfuOpcode.StartDfu, start);

        // 2. Init packet bracketed by INIT_PARAMS start/complete.
        onPhase?.Invoke("StrFwPhaseStartingDfu");
        await WriteControlAsync(new[] { LegacyDfuOpcode.InitDfuParams, LegacyDfuOpcode.InitParamsStart }, ct);
        for (int pos = 0; pos < init.Length; pos += LegacyPacketSize)
            await WritePacketAsync(init.AsMemory(pos, Math.Min(LegacyPacketSize, init.Length - pos)), ct);
        await WriteControlAsync(new[] { LegacyDfuOpcode.InitDfuParams, LegacyDfuOpcode.InitParamsComplete }, ct);
        RequireSuccess(LegacyDfuOpcode.InitDfuParams, await AwaitResponseAsync(CommandTimeout, ct));

        link.RequestHighThroughput();

        // 3./4. PRN interval, then RECEIVE_FIRMWARE_IMAGE (neither is answered).
        await WriteControlAsync(LegacyDfuPayloads.PrnRequest(_prnInterval), ct);
        await WriteControlAsync(new[] { LegacyDfuOpcode.ReceiveFirmwareImage }, ct);

        // 5. Stream.
        onPhase?.Invoke("StrFwPhaseUploading");
        await StreamAsync(fw, onProgress, ct);

        // 6.–8. Final receive response, VALIDATE, ACTIVATE_AND_RESET.
        RequireSuccess(LegacyDfuOpcode.ReceiveFirmwareImage, await AwaitResponseAsync(ValidateTimeout, ct));
        onPhase?.Invoke("StrFwPhaseValidating");
        await WriteControlAsync(new[] { LegacyDfuOpcode.Validate }, ct);
        RequireSuccess(LegacyDfuOpcode.Validate, await AwaitResponseAsync(ValidateTimeout, ct));
        try
        {
            await _link!.WriteAsync(_control!, new[] { LegacyDfuOpcode.ActivateAndReset }, withResponse: true, TimeSpan.FromSeconds(3), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.WriteLine($"[DFU/Legacy] ACTIVATE write did not ACK (expected — device resets): {ex.Message}");
        }
        onProgress?.Report(1.0);
        Logger.WriteLine("[DFU/Legacy] Upload complete, device reboots into the new firmware");
    }

    private int StreamPacketSize()
    {
        var negotiated = _link?.MaxWriteNoResponse ?? LegacyPacketSize;
        var sized = Math.Clamp(negotiated, LegacyPacketSize, MaxHighMtuPacketSize);
        return sized - sized % 4; // the Adafruit bootloader rejects writes that are not a multiple of 4
    }

    private async Task StreamAsync(byte[] fw, IProgress<double>? onProgress, CancellationToken ct)
    {
        var link = _link!;
        int size = StreamPacketSize();
        int offset = 0;
        int sincePrn = 0;
        int lastConfirmed = -1;
        var sw = Stopwatch.StartNew();
        Logger.WriteLine($"[DFU/Legacy] Streaming {fw.Length} bytes with {size}-byte packets (dfuVersion={_dfuVersion})");

        while (offset < fw.Length)
        {
            if (link.IsDisconnected) throw new DfuMidStreamDisconnectException(offset, fw.Length);
            int end = Math.Min(offset + size, fw.Length);
            try
            {
                await link.WriteAsync(_packet!, fw.AsMemory(offset, end - offset), withResponse: false, TimeSpan.FromSeconds(15), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                if (link.IsDisconnected) throw new DfuMidStreamDisconnectException(end, fw.Length, ex);
                throw new DfuException($"Packet write failed at {offset}/{fw.Length}: {ex.Message}", ex);
            }
            offset = end;
            sincePrn++;

            if (sincePrn >= _prnInterval && offset < fw.Length)
            {
                var receipt = await AwaitPacketReceiptAsync(offset, fw.Length, ct);
                if (receipt != (uint)offset)
                    throw new DfuException($"Packet receipt mismatch: sent {offset} bytes, device reports {receipt}");
                lastConfirmed = offset;
                sincePrn = 0;
                onProgress?.Report((double)offset / fw.Length);
            }
        }
        Logger.WriteLine($"[DFU/Legacy] Streamed {offset} bytes in {sw.Elapsed.TotalSeconds:0.0}s (last receipt {lastConfirmed})");
    }

    // ── GATT helpers ───────────────────────────────────────────────────────

    private async Task WriteControlAsync(byte[] payload, CancellationToken ct)
    {
        try
        {
            await _link!.WriteAsync(_control!, payload, withResponse: true, CommandTimeout, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (_link!.IsDisconnected) throw new DfuConnectionException("BLE link dropped during Legacy DFU command", ex);
            throw new DfuException($"Control point write failed: {ex.Message}", ex);
        }
    }

    private async Task WritePacketAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        try
        {
            await _link!.WriteAsync(_packet!, payload, withResponse: false, TimeSpan.FromSeconds(15), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (_link!.IsDisconnected) throw new DfuConnectionException("BLE link dropped during Legacy DFU packet write", ex);
            throw new DfuException($"Packet write failed: {ex.Message}", ex);
        }
    }

    /// <summary>Next control-point response, skipping stray packet receipts. A link drop fails fast.</summary>
    private async Task<LegacyDfuResponse> AwaitResponseAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new DfuTimeoutException($"No response from Legacy DFU control point after {timeout.TotalSeconds:0}s");
            byte[]? data;
            try { data = await _rx!.ReceiveAsync(remaining, ct); }
            catch (BleException ex) { throw new DfuConnectionException("BLE link dropped during DFU handshake — some bootloaders drop the link instead of answering (stale bond?)", ex); }
            if (data == null) continue;
            var r = LegacyDfuResponse.Parse(data);
            Logger.WriteLine($"[DFU/Legacy] < {r}");
            if (r is LegacyDfuResponse.PacketReceipt) continue;
            return r;
        }
    }

    private async Task<uint> AwaitPacketReceiptAsync(int offset, int total, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + CommandTimeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new DfuTimeoutException($"No packet receipt notification after {CommandTimeout.TotalSeconds:0}s (offset {offset})");
            byte[]? data;
            try { data = await _rx!.ReceiveAsync(remaining, ct); }
            catch (BleException ex) { throw new DfuMidStreamDisconnectException(offset, total, ex); }
            if (data == null) continue;
            switch (LegacyDfuResponse.Parse(data))
            {
                case LegacyDfuResponse.PacketReceipt p: return p.BytesReceived;
                case LegacyDfuResponse.Failure f: throw new DfuProtocolException(f.RequestOpcode, f.Status, f.ToString());
                default: continue; // stray success / unknown
            }
        }
    }

    private static void RequireSuccess(byte expectedOpcode, LegacyDfuResponse r)
    {
        switch (r)
        {
            case LegacyDfuResponse.Success s when s.RequestOpcode == expectedOpcode: return;
            case LegacyDfuResponse.Success s:
                throw new DfuException($"Legacy DFU response opcode mismatch: expected 0x{expectedOpcode:X2}, got 0x{s.RequestOpcode:X2}");
            case LegacyDfuResponse.Failure f:
                throw new DfuProtocolException(f.RequestOpcode, f.Status, f.ToString());
            default:
                throw new DfuException($"Unexpected Legacy DFU response for opcode 0x{expectedOpcode:X2}: {r}");
        }
    }

    public async Task AbortAsync()
    {
        if (_link == null || _control == null || _link.IsDisconnected) return;
        try
        {
            await _link.WriteAsync(_control, new[] { LegacyDfuOpcode.Reset }, withResponse: true, ResetWriteTimeout, CancellationToken.None);
            Logger.WriteLine("[DFU/Legacy] RESET acknowledged");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[DFU/Legacy] RESET not acknowledged ({ex.GetType().Name}) — disconnecting anyway");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_link != null) await _link.DisposeAsync();
        _link = null;
        _control = null;
        _packet = null;
        _rx = null;
    }
}
