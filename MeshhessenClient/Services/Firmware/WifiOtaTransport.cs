using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace MeshhessenClient.Services.Firmware;

/// <summary>Unified OTA over TCP (loader listens on port 3232). No per-chunk ACK wait — TCP paces the stream.</summary>
public sealed class WifiOtaTransport : IUnifiedOtaTransport
{
    public const int DefaultPort = 3232;
    public const int ChunkSize = 1024;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErasingTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly StringBuilder _lineBuf = new();

    public WifiOtaTransport(string host, int port = DefaultPort)
    {
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        Logger.WriteLine($"[OTA/WiFi] Connecting to {_host}:{_port}");
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ConnectTimeout);
            await client.ConnectAsync(_host, _port, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new OtaException(OtaErrorKind.ConnectionFailed, $"TCP connect to {_host}:{_port} timed out");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            throw new OtaException(OtaErrorKind.ConnectionFailed, $"TCP connect to {_host}:{_port} failed: {ex.Message}", ex);
        }
        _client = client;
        _stream = client.GetStream();
        Logger.WriteLine("[OTA/WiFi] Connected");
    }

    public async Task StartAsync(long sizeBytes, string sha256Hex, Action? onErasing, CancellationToken ct)
    {
        var s = _stream ?? throw new OtaException(OtaErrorKind.ConnectionFailed, "Not connected");
        var cmd = OtaResponse.StartCommand(sizeBytes, sha256Hex);
        Logger.WriteLine($"[OTA/WiFi] > {cmd.Trim()}");
        await s.WriteAsync(Encoding.ASCII.GetBytes(cmd), ct);
        await s.FlushAsync(ct);

        while (true)
        {
            var line = await ReadLineAsync(ErasingTimeout, ct);
            switch (OtaResponse.Parse(line))
            {
                case OtaResponse.Ok: return;
                case OtaResponse.Erasing: onErasing?.Invoke(); break;
                case OtaResponse.Error e when e.Message.Contains("Hash Rejected", StringComparison.OrdinalIgnoreCase):
                    throw new OtaException(OtaErrorKind.HashRejected, $"Device rejected hash {sha256Hex}");
                case OtaResponse.Error e:
                    throw new OtaException(OtaErrorKind.CommandFailed, $"OTA start failed: {e.Message}");
                default:
                    Logger.WriteLine($"[OTA/WiFi] Unexpected handshake response: {line}");
                    break;
            }
        }
    }

    public async Task StreamAsync(byte[] firmware, IProgress<double>? onProgress, CancellationToken ct)
    {
        var s = _stream ?? throw new OtaException(OtaErrorKind.ConnectionFailed, "Not connected");
        if (firmware.Length == 0) throw new OtaException(OtaErrorKind.TransferFailed, "Firmware is empty");
        int sent = 0;
        try
        {
            while (sent < firmware.Length)
            {
                var n = Math.Min(ChunkSize, firmware.Length - sent);
                await s.WriteAsync(firmware.AsMemory(sent, n), ct);
                await s.FlushAsync(ct);
                sent += n;
                onProgress?.Report((double)sent / firmware.Length);
                await Task.Delay(10, ct); // keep the loader's receive buffer comfortable
            }
        }
        catch (IOException ex)
        {
            throw new OtaException(OtaErrorKind.TransferFailed, $"Connection lost during transfer at {sent}/{firmware.Length}", ex);
        }
        Logger.WriteLine($"[OTA/WiFi] Streamed {sent} bytes, waiting for verification");

        while (true)
        {
            var line = await ReadLineAsync(VerificationTimeout, ct);
            switch (OtaResponse.Parse(line))
            {
                case OtaResponse.Ok: return;
                case OtaResponse.Ack: continue; // late ACKs are harmless
                case OtaResponse.Error e when e.Message.Contains("Hash Mismatch", StringComparison.OrdinalIgnoreCase):
                    throw new OtaException(OtaErrorKind.VerificationFailed, "Firmware hash mismatch after transfer");
                case OtaResponse.Error e:
                    throw new OtaException(OtaErrorKind.TransferFailed, $"Verification failed: {e.Message}");
                default:
                    throw new OtaException(OtaErrorKind.TransferFailed, $"Expected OK after transfer, got: {line}");
            }
        }
    }

    private async Task<string> ReadLineAsync(TimeSpan timeout, CancellationToken ct)
    {
        var s = _stream!;
        var buf = new byte[256];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var existing = _lineBuf.ToString();
                var nl = existing.IndexOf('\n');
                if (nl >= 0)
                {
                    _lineBuf.Remove(0, nl + 1);
                    var line = existing[..nl];
                    Logger.WriteLine($"[OTA/WiFi] < {line.Trim()}");
                    return line;
                }
                var n = await s.ReadAsync(buf, cts.Token);
                if (n == 0) throw new OtaException(OtaErrorKind.ConnectionFailed, "Connection closed by device");
                _lineBuf.Append(Encoding.ASCII.GetString(buf, 0, n));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OtaException(OtaErrorKind.Timeout, $"Timed out waiting for OTA response after {timeout.TotalSeconds:0}s");
        }
        catch (IOException ex)
        {
            throw new OtaException(OtaErrorKind.ConnectionFailed, $"Read failed: {ex.Message}", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _stream = null;
        _client = null;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Listens for the OTA loader's UDP beacon (<c>"Meshtastic_XXXX &lt;version&gt;"</c> broadcast to
/// 255.255.255.255:3232 every second) so the host learns the loader's IP — DHCP may hand the device a
/// different address in OTA mode. Returns null on timeout/bind failure; callers fall back to the known IP.
/// </summary>
public static class WifiOtaDiscovery
{
    private static readonly Regex BeaconPattern = new(@"^Meshtastic_[0-9A-Fa-f]{4}\s+\S{1,32}$", RegexOptions.CultureInvariant);

    public static bool IsBeacon(byte[] payload)
    {
        try { return BeaconPattern.IsMatch(Encoding.ASCII.GetString(payload).Trim()); }
        catch { return false; }
    }

    public static async Task<IPAddress?> DiscoverAsync(TimeSpan timeout, CancellationToken ct, int port = WifiOtaTransport.DefaultPort)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            Logger.WriteLine($"[OTA/WiFi] Listening for loader beacon on UDP {port} ({timeout.TotalSeconds:0}s)");
            while (true)
            {
                var r = await udp.ReceiveAsync(cts.Token);
                if (r.RemoteEndPoint.Address.AddressFamily == AddressFamily.InterNetwork && IsBeacon(r.Buffer))
                {
                    Logger.WriteLine($"[OTA/WiFi] Loader beacon from {r.RemoteEndPoint.Address}");
                    return r.RemoteEndPoint.Address;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[OTA/WiFi] Discovery ended without beacon: {ex.GetType().Name} {ex.Message}");
            return null;
        }
    }
}
