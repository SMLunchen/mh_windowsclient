using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace MeshhessenClient.Services.Firmware.Dfu;

/// <summary>
/// Nordic Secure DFU (SDK 12+) over BLE: object-based transfer with CRC-32 checkpoints and resume.
/// Sequence: SET_PRN 0 → init packet as COMMAND object → SET_PRN 10 → firmware as DATA objects
/// (SELECT → CREATE → packets → CALCULATE_CHECKSUM → EXECUTE per object).
/// </summary>
public sealed class SecureDfuTransport : IDfuTransport
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FirstChunkDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private const int PrnInterval = 10;
    private const int ObjectRetryCount = 3;
    private const int DefaultMaxObjectSize = 4096;

    private BleGattLink? _link;
    private GattCharacteristic? _control;
    private GattCharacteristic? _packet;
    private BleNotificationQueue? _rx;

    public ulong Address { get; }
    public bool IsLowSpeedTransfer => (_link?.MaxWriteNoResponse ?? 20) <= 20;

    public SecureDfuTransport(ulong bootloaderAddress) => Address = bootloaderAddress;

    public async Task ConnectAsync(CancellationToken ct)
    {
        var link = await OpenLinkAsync(ct);
        try
        {
            var svc = await link.TryGetServiceAsync(SecureDfuUuids.Service, ConnectTimeout, ct)
                      ?? throw new DfuConnectionException("Secure DFU service (FE59) not exposed by the bootloader");
            _control = await link.GetCharacteristicAsync(svc, SecureDfuUuids.ControlPoint, ct);
            _packet = await link.GetCharacteristicAsync(svc, SecureDfuUuids.Packet, ct);
            _rx = await link.SubscribeAsync(_control, indicate: false, ct);
            await Task.Delay(500, ct);
            _link = link;
            Logger.WriteLine($"[DFU/Secure] Connected to {BleAddress.Format(Address)}, payload {link.MaxWriteNoResponse} bytes");
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

    private async Task<BleGattLink> OpenLinkAsync(CancellationToken ct)
    {
        try { return await BleGattLink.OpenAsync(Address, ct); }
        catch (BleException ex) { throw new DfuConnectionException(ex.Message, ex); }
    }

    public async Task TransferAsync(DfuPackage package, Action<string>? onPhase, IProgress<double>? onProgress, CancellationToken ct)
    {
        var link = _link ?? throw new DfuConnectionException("Not connected");
        Logger.WriteLine($"[DFU/Secure] Init packet {package.InitPacket.Length} B, firmware {package.Firmware.Length} B");
        onPhase?.Invoke("StrFwPhaseStartingDfu");
        await SetPrnAsync(0, ct);
        await TransferObjectWithRetryAsync(SecureDfuObjectType.Command, package.InitPacket, 0, null, ct);
        Logger.WriteLine("[DFU/Secure] Init packet accepted");

        link.RequestHighThroughput();
        onPhase?.Invoke("StrFwPhaseUploading");
        await SetPrnAsync(PrnInterval, ct);
        await TransferObjectWithRetryAsync(SecureDfuObjectType.Data, package.Firmware, PrnInterval, onProgress, ct);
        Logger.WriteLine("[DFU/Secure] Firmware transferred and executed; bootloader activates the image");
    }

    public async Task AbortAsync()
    {
        if (_link == null || _control == null || _link.IsDisconnected) return;
        try
        {
            await _link.WriteAsync(_control, new[] { SecureDfuOpcode.Abort }, withResponse: true, TimeSpan.FromSeconds(2), CancellationToken.None);
            Logger.WriteLine("[DFU/Secure] ABORT sent");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[DFU/Secure] ABORT failed (device may be gone): {ex.Message}");
        }
    }

    // ── Object transfer ────────────────────────────────────────────────────

    private async Task TransferObjectWithRetryAsync(byte type, byte[] data, int prn, IProgress<double>? progress, CancellationToken ct)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= ObjectRetryCount; attempt++)
        {
            try
            {
                await TransferObjectAsync(type, data, prn, progress, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (DfuConnectionException) { throw; }
            catch (Exception ex)
            {
                last = ex;
                Logger.WriteLine($"[DFU/Secure] Object transfer failed (attempt {attempt}/{ObjectRetryCount}): {ex.Message}");
                if (_link!.IsDisconnected) throw new DfuConnectionException("BLE link dropped during object transfer", ex);
                if (attempt < ObjectRetryCount) await Task.Delay(RetryDelay, ct);
            }
        }
        throw last ?? new DfuException("Object transfer failed");
    }

    private async Task TransferObjectAsync(byte type, byte[] data, int prn, IProgress<double>? progress, CancellationToken ct)
    {
        var select = await SendSelectAsync(type, ct);
        int maxObject = select.MaxSize > 0 ? select.MaxSize : DefaultMaxObjectSize;
        int total = data.Length;
        int offset = 0;
        bool firstChunk = true;
        int currentPrn = prn;

        // Resume: the bootloader reports how much of this object type it already holds and the CRC up to there.
        if (select.Offset > 0 && select.Offset <= total)
        {
            var expected = DfuCrc32.Compute(data.AsSpan(0, select.Offset));
            if (expected == select.Crc32)
            {
                int executed = maxObject * (select.Offset / maxObject);
                int pending = select.Offset - executed;
                if (select.Offset == total)
                {
                    Logger.WriteLine($"[DFU/Secure] Device already holds all {total} bytes (CRC ok) — executing");
                    await SendExecuteAsync(ct);
                    progress?.Report(1.0);
                    return;
                }
                if (pending == 0 && executed > 0)
                {
                    Logger.WriteLine($"[DFU/Secure] Resuming at object boundary {executed}; executing last object");
                    try { await SendExecuteAsync(ct); }
                    catch (DfuProtocolException e) when (e.ResultCode == SecureDfuResult.OperationNotPermitted) { }
                    offset = executed;
                    firstChunk = false;
                }
                else if (pending > 0)
                {
                    Logger.WriteLine($"[DFU/Secure] Resuming at {executed} (partial object of {pending} bytes is re-sent)");
                    offset = executed;
                    firstChunk = false;
                }
            }
            else
            {
                Logger.WriteLine($"[DFU/Secure] Offset {select.Offset} CRC mismatch — restarting object from 0");
            }
        }

        while (offset < total)
        {
            int size = Math.Min(maxObject, total - offset);
            await SendCreateAsync(type, size, ct);
            if (firstChunk)
            {
                await Task.Delay(FirstChunkDelay, ct); // older bootloaders need time to prepare flash after CREATE
                firstChunk = false;
            }
            int end = offset + size;
            await WritePacketsAsync(data, offset, end, currentPrn, ct);

            var crc = await SendCalculateChecksumAsync(ct);
            var expected = DfuCrc32.Compute(data.AsSpan(0, end));
            if (crc.Offset < end)
            {
                // Packets were lost in the stack: verify what arrived, then resend the rest with PRN=1.
                int lost = end - crc.Offset;
                Logger.WriteLine($"[DFU/Secure] {lost} bytes lost (device at {crc.Offset}, sent to {end}); tightening PRN and resending");
                var partial = DfuCrc32.Compute(data.AsSpan(0, crc.Offset));
                if (crc.Crc32 != partial) throw new DfuChecksumException(partial, crc.Crc32);
                currentPrn = 1;
                await WritePacketsAsync(data, crc.Offset, end, currentPrn, ct);
                var recheck = await SendCalculateChecksumAsync(ct);
                if (recheck.Offset != end || recheck.Crc32 != expected)
                    throw new DfuException($"Recovery after lost bytes failed: offset {recheck.Offset}/{end} crc 0x{recheck.Crc32:X8}/0x{expected:X8}");
            }
            else if (crc.Offset != end)
            {
                throw new DfuException($"Offset mismatch after object: expected {end}, got {crc.Offset}");
            }
            else if (crc.Crc32 != expected)
            {
                throw new DfuChecksumException(expected, crc.Crc32);
            }

            try
            {
                await SendExecuteAsync(ct);
            }
            catch (DfuProtocolException e) when (e.ResultCode == SecureDfuResult.InvalidObject && end >= total)
            {
                Logger.WriteLine("[DFU/Secure] EXECUTE returned INVALID_OBJECT on the final object — retrying once");
                await Task.Delay(RetryDelay, ct);
                await SendExecuteAsync(ct);
            }

            offset = end;
            progress?.Report((double)offset / total);
        }
    }

    private async Task WritePacketsAsync(byte[] data, int from, int until, int prn, CancellationToken ct)
    {
        var link = _link!;
        int mtu = link.MaxWriteNoResponse;
        int pos = from;
        int sincePrn = 0;
        while (pos < until)
        {
            if (link.IsDisconnected) throw new DfuMidStreamDisconnectException(pos, until);
            int end = Math.Min(pos + mtu, until);
            try
            {
                await link.WriteAsync(_packet!, data.AsMemory(pos, end - pos), withResponse: false, TimeSpan.FromSeconds(15), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                if (link.IsDisconnected) throw new DfuMidStreamDisconnectException(pos, until, ex);
                throw new DfuException($"Packet write failed at {pos}: {ex.Message}", ex);
            }
            pos = end;
            sincePrn++;
            if (prn > 0 && sincePrn >= prn && pos < until)
            {
                var resp = await AwaitResponseAsync(CommandTimeout, ct);
                if (resp is SecureDfuResponse.ChecksumResult c)
                {
                    var expected = DfuCrc32.Compute(data.AsSpan(0, pos));
                    if (c.Offset != pos || c.Crc32 != expected) throw new DfuChecksumException(expected, c.Crc32);
                }
                sincePrn = 0;
            }
        }
    }

    // ── Control point commands ─────────────────────────────────────────────

    private async Task<SecureDfuResponse> SendCommandAsync(byte[] payload, CancellationToken ct)
    {
        try
        {
            await _link!.WriteAsync(_control!, payload, withResponse: true, CommandTimeout, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (_link!.IsDisconnected) throw new DfuConnectionException("BLE link dropped during DFU command", ex);
            throw new DfuException($"Control point write failed: {ex.Message}", ex);
        }
        return await AwaitResponseAsync(CommandTimeout, ct);
    }

    private async Task<SecureDfuResponse> AwaitResponseAsync(TimeSpan timeout, CancellationToken ct)
    {
        byte[]? data;
        try { data = await _rx!.ReceiveAsync(timeout, ct); }
        catch (BleException ex) { throw new DfuConnectionException("BLE link dropped while waiting for the control point", ex); }
        if (data == null) throw new DfuTimeoutException($"No response from DFU control point after {timeout.TotalSeconds:0}s");
        var r = SecureDfuResponse.Parse(data);
        if (r is SecureDfuResponse.Unknown u)
            Logger.WriteLine($"[DFU/Secure] Unknown notification {Convert.ToHexString(u.Raw)}");
        return r;
    }

    private async Task SetPrnAsync(int value, CancellationToken ct)
    {
        var r = await SendCommandAsync(new[] { SecureDfuOpcode.SetPrn, (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) }, ct);
        RequireSuccess(r, SecureDfuOpcode.SetPrn);
    }

    private async Task<SecureDfuResponse.SelectResult> SendSelectAsync(byte type, CancellationToken ct)
    {
        var r = await SendCommandAsync(new[] { SecureDfuOpcode.Select, type }, ct);
        return r switch
        {
            SecureDfuResponse.SelectResult s => s,
            SecureDfuResponse.Failure f => throw new DfuProtocolException(f.Opcode, f.ResultCode, f.ToString()),
            _ => throw new DfuException($"Unexpected response to SELECT: {r}")
        };
    }

    private async Task SendCreateAsync(byte type, int size, CancellationToken ct)
    {
        var payload = new byte[6];
        payload[0] = SecureDfuOpcode.Create;
        payload[1] = type;
        BitConverter.TryWriteBytes(payload.AsSpan(2, 4), size);
        RequireSuccess(await SendCommandAsync(payload, ct), SecureDfuOpcode.Create);
    }

    private async Task<SecureDfuResponse.ChecksumResult> SendCalculateChecksumAsync(CancellationToken ct)
    {
        var r = await SendCommandAsync(new[] { SecureDfuOpcode.CalculateChecksum }, ct);
        return r switch
        {
            SecureDfuResponse.ChecksumResult c => c,
            SecureDfuResponse.Failure f => throw new DfuProtocolException(f.Opcode, f.ResultCode, f.ToString()),
            _ => throw new DfuException($"Unexpected response to CALCULATE_CHECKSUM: {r}")
        };
    }

    private async Task SendExecuteAsync(CancellationToken ct) =>
        RequireSuccess(await SendCommandAsync(new[] { SecureDfuOpcode.Execute }, ct), SecureDfuOpcode.Execute);

    private static void RequireSuccess(SecureDfuResponse r, byte expectedOpcode)
    {
        switch (r)
        {
            case SecureDfuResponse.Success s when s.Opcode == expectedOpcode: return;
            case SecureDfuResponse.Success s:
                throw new DfuException($"Response opcode mismatch: expected 0x{expectedOpcode:X2}, got 0x{s.Opcode:X2}");
            case SecureDfuResponse.Failure f:
                throw new DfuProtocolException(f.Opcode, f.ResultCode, f.ToString());
            default:
                throw new DfuException($"Unexpected response for opcode 0x{expectedOpcode:X2}: {r}");
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
