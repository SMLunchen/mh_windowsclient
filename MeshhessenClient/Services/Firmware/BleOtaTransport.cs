using System.Text;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace MeshhessenClient.Services.Firmware;

/// <summary>
/// Unified OTA over the ESP32 loader's GATT service. The loader is an ACK-paced byte stream: one write
/// → exactly one response (ACK while more data is expected, terminal OK after the last byte verified), so
/// each chunk is a single write no larger than the negotiated payload — a chunk that fragmented into two
/// writes would be coalesced into one ACK and desync the accounting.
/// </summary>
public sealed class BleOtaTransport : IUnifiedOtaTransport
{
    public static readonly Guid ServiceUuid = Guid.Parse("4FAFC201-1FB5-459E-8FCC-C5C9C331914B");
    public static readonly Guid WriteUuid = Guid.Parse("62ec0272-3ec5-11eb-b378-0242ac130005");
    public static readonly Guid NotifyUuid = Guid.Parse("62ec0272-3ec5-11eb-b378-0242ac130003");

    public const int RecommendedChunkSize = 512;
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ErasingTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(10);

    private readonly ulong _address;
    private BleGattLink? _link;
    private GattCharacteristic? _write;
    private BleNotificationQueue? _rx;

    public BleOtaTransport(ulong meshAddress) => _address = meshAddress;

    public async Task ConnectAsync(CancellationToken ct)
    {
        var targets = new HashSet<ulong> { _address, BleAddress.PlusOne(_address) };
        var hit = await BleScanner.ScanWithRetriesAsync(
            h => targets.Contains(h.Address) || h.ServiceUuids.Contains(ServiceUuid),
            retries: 2, ScanTimeout, TimeSpan.FromSeconds(1), "OTA/BLE", ct);
        if (hit == null)
            throw new OtaException(OtaErrorKind.ConnectionFailed,
                "OTA device not found. Ensure the device rebooted into OTA mode and is advertising.");

        var link = await BleGattLink.OpenAsync(hit.Address, ct);
        try
        {
            var svc = await link.TryGetServiceAsync(ServiceUuid, DiscoveryTimeout, ct)
                      ?? throw new OtaException(OtaErrorKind.ConnectionFailed,
                          "Connected, but the device exposes no OTA service (still in mesh mode or stale GATT cache?)");
            _write = await link.GetCharacteristicAsync(svc, WriteUuid, ct);
            var notify = await link.GetCharacteristicAsync(svc, NotifyUuid, ct);
            _rx = await link.SubscribeAsync(notify, indicate: false, ct);
            link.RequestHighThroughput();
            await Task.Delay(500, ct);
            _link = link;
            Logger.WriteLine($"[OTA/BLE] Service ready, write payload {link.MaxWriteNoResponse} bytes");
        }
        catch
        {
            await link.DisposeAsync();
            throw;
        }
    }

    public async Task StartAsync(long sizeBytes, string sha256Hex, Action? onErasing, CancellationToken ct)
    {
        var link = _link ?? throw new OtaException(OtaErrorKind.ConnectionFailed, "Not connected");
        var cmd = Encoding.ASCII.GetBytes(OtaResponse.StartCommand(sizeBytes, sha256Hex));
        Logger.WriteLine($"[OTA/BLE] > OTA {sizeBytes} {sha256Hex}");
        // The command may exceed a small MTU; split into with-response writes. The loader answers once.
        var max = link.MaxWriteNoResponse;
        for (int off = 0; off < cmd.Length; off += max)
            await WriteAsync(cmd.AsMemory(off, Math.Min(max, cmd.Length - off)), withResponse: true, ct);

        while (true)
        {
            var line = await ReceiveAsync(ErasingTimeout, ct);
            switch (OtaResponse.Parse(line))
            {
                case OtaResponse.Ok: return;
                case OtaResponse.Erasing: onErasing?.Invoke(); break;
                case OtaResponse.Error e when e.Message.Contains("Hash Rejected", StringComparison.OrdinalIgnoreCase):
                    throw new OtaException(OtaErrorKind.HashRejected, $"Device rejected hash {sha256Hex}");
                case OtaResponse.Error e:
                    throw new OtaException(OtaErrorKind.CommandFailed, $"OTA start failed: {e.Message}");
                default:
                    Logger.WriteLine($"[OTA/BLE] Unexpected handshake response: {line}");
                    break;
            }
        }
    }

    public async Task StreamAsync(byte[] firmware, IProgress<double>? onProgress, CancellationToken ct)
    {
        var link = _link ?? throw new OtaException(OtaErrorKind.ConnectionFailed, "Not connected");
        if (firmware.Length == 0) throw new OtaException(OtaErrorKind.TransferFailed, "Firmware is empty");
        var chunk = Math.Max(1, Math.Min(RecommendedChunkSize, link.MaxWriteNoResponse));
        Logger.WriteLine($"[OTA/BLE] Streaming {firmware.Length} bytes in {chunk}-byte writes");
        int sent = 0;
        while (sent < firmware.Length)
        {
            if (link.IsDisconnected)
                throw new OtaException(OtaErrorKind.TransferFailed, $"Connection lost during transfer at {sent}/{firmware.Length}");
            var n = Math.Min(chunk, firmware.Length - sent);
            var isLast = sent + n >= firmware.Length;
            await WriteAsync(firmware.AsMemory(sent, n), withResponse: false, ct);

            var line = await ReceiveAsync(AckTimeout, ct);
            switch (OtaResponse.Parse(line))
            {
                case OtaResponse.Ack:
                    break;
                case OtaResponse.Ok:
                    if (!isLast)
                        throw new OtaException(OtaErrorKind.TransferFailed, "Device reported OK before the final chunk (size accounting mismatch)");
                    onProgress?.Report(1.0);
                    return;
                case OtaResponse.Error e:
                    throw TransferError(e);
                default:
                    throw new OtaException(OtaErrorKind.TransferFailed, $"Unexpected response during transfer: {line}");
            }
            sent += n;
            onProgress?.Report((double)sent / firmware.Length);
        }

        // Every chunk was ACKed but no terminal OK yet — wait for the loader's verification verdict.
        var final = OtaResponse.Parse(await ReceiveAsync(VerificationTimeout, ct));
        if (final is OtaResponse.Ok) return;
        if (final is OtaResponse.Error err) throw TransferError(err);
        throw new OtaException(OtaErrorKind.TransferFailed, $"Expected OK after transfer, got: {final}");
    }

    private static OtaException TransferError(OtaResponse.Error e) =>
        e.Message.Contains("Hash Mismatch", StringComparison.OrdinalIgnoreCase)
            ? new OtaException(OtaErrorKind.VerificationFailed, "Firmware hash mismatch after transfer")
            : new OtaException(OtaErrorKind.TransferFailed, $"Transfer failed: {e.Message}");

    private async Task WriteAsync(ReadOnlyMemory<byte> data, bool withResponse, CancellationToken ct)
    {
        try
        {
            await _link!.WriteAsync(_write!, data, withResponse, TimeSpan.FromSeconds(15), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new OtaException(OtaErrorKind.TransferFailed, $"BLE write failed: {ex.Message}", ex);
        }
    }

    private async Task<string> ReceiveAsync(TimeSpan timeout, CancellationToken ct)
    {
        byte[]? data;
        try { data = await _rx!.ReceiveAsync(timeout, ct); }
        catch (BleException ex) { throw new OtaException(OtaErrorKind.ConnectionFailed, "Connection lost while waiting for a response", ex); }
        if (data == null)
            throw new OtaException(OtaErrorKind.Timeout, $"Timeout waiting for response after {timeout.TotalSeconds:0}s");
        var text = Encoding.ASCII.GetString(data);
        Logger.WriteLine($"[OTA/BLE] < {text.Trim()}");
        return text;
    }

    public async ValueTask DisposeAsync()
    {
        if (_link != null) await _link.DisposeAsync();
        _link = null;
        _write = null;
        _rx = null;
    }
}
