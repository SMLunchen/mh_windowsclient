using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using Meshtastic.Protobufs;
using SysChannel = System.Threading.Channels.Channel;
using SysChannelOptions = System.Threading.Channels.BoundedChannelOptions;
using SysChannelFullMode = System.Threading.Channels.BoundedChannelFullMode;
using SysChannelT = System.Threading.Channels.Channel<byte[]>;

namespace MeshhessenClient.Services;

public class VirtualNodeService : IDisposable
{
    private const byte START1 = 0x94;
    private const byte START2 = 0xC3;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _nextClientId = 1;

    private readonly Dictionary<string, VnClient> _clients = new();
    private readonly object _clientsLock = new();

    // Config cache: stores the last known framed bytes for each config category
    private byte[]? _frameMyInfo;
    private byte[]? _frameMetadata;
    private readonly Dictionary<int, byte[]> _frameChannels = new();      // channel index → frame
    private readonly Dictionary<int, byte[]> _frameConfigs = new();       // Config.PayloadVariantCase → frame
    private readonly Dictionary<int, byte[]> _frameModuleConfigs = new(); // ModuleConfig.PayloadVariantCase → frame
    private readonly Dictionary<uint, byte[]> _frameNodes = new();        // nodeNum → frame
    private uint _configCompleteId;
    private bool _configCacheReady;

    // Queue for sending to physical node (max 100, drops oldest on overflow)
    private readonly SysChannelT _toPhysicalQueue = SysChannel.CreateBounded<byte[]>(
        new SysChannelOptions(100) { FullMode = SysChannelFullMode.DropOldest });

    private readonly IConnectionService _physicalConnection;
    private Task? _sendTask;

    public int Port { get; set; } = 4404;
    public bool BlockAdminCommands { get; set; } = false;
    public bool IsRunning { get; private set; }

    public event EventHandler<int>? ClientCountChanged;
    public event EventHandler<string>? LogMessage;
    // Fires with FromRadio bytes when a VN client sends a MeshPacket, so the main app can display it
    public event EventHandler<byte[]>? ClientPacketReceived;

    public VirtualNodeService(IConnectionService physicalConnection)
    {
        _physicalConnection = physicalConnection;
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();

        try
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            IsRunning = true;
            Log($"Listening on port {Port}");
            _sendTask = ProcessSendQueueAsync(_cts.Token);
            _ = AcceptClientsAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            IsRunning = false;
            _cts.Cancel();
            Log($"Start failed: {ex.Message}");
            throw;
        }

        await Task.CompletedTask;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        _listener?.Stop();
        IsRunning = false;

        lock (_clientsLock)
        {
            foreach (var c in _clients.Values) c.Dispose();
            _clients.Clear();
        }
        ClientCountChanged?.Invoke(this, 0);
        Log("Stopped");
    }

    // Called by MeshtasticProtocolService with each complete raw frame from the physical node
    public void OnRawFrameFromPhysical(byte[] frame)
    {
        try
        {
            var payload = new byte[frame.Length - 4];
            Array.Copy(frame, 4, payload, 0, payload.Length);
            var fromRadio = FromRadio.Parser.ParseFrom(payload);
            UpdateConfigCache(fromRadio, frame);

            // Don't forward ConfigComplete — we send our own during replay
            if (fromRadio.PayloadVariantCase == FromRadio.PayloadVariantOneofCase.ConfigCompleteId)
                return;
        }
        catch { /* ignore parse errors, still broadcast raw bytes */ }

        BroadcastToClients(frame);
    }

    private void UpdateConfigCache(FromRadio fr, byte[] rawFrame)
    {
        switch (fr.PayloadVariantCase)
        {
            case FromRadio.PayloadVariantOneofCase.MyInfo:
                _frameMyInfo = rawFrame;
                break;
            case FromRadio.PayloadVariantOneofCase.Metadata:
                _frameMetadata = rawFrame;
                break;
            case FromRadio.PayloadVariantOneofCase.Channel:
                _frameChannels[fr.Channel.Index] = rawFrame;
                break;
            case FromRadio.PayloadVariantOneofCase.Config:
                _frameConfigs[(int)fr.Config.PayloadVariantCase] = rawFrame;
                break;
            case FromRadio.PayloadVariantOneofCase.ModuleConfig:
                _frameModuleConfigs[(int)fr.ModuleConfig.PayloadVariantCase] = rawFrame;
                break;
            case FromRadio.PayloadVariantOneofCase.NodeInfo:
                _frameNodes[fr.NodeInfo.Num] = rawFrame;
                break;
            case FromRadio.PayloadVariantOneofCase.ConfigCompleteId:
                _configCompleteId = fr.ConfigCompleteId;
                _configCacheReady = true;
                break;
        }
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var tcpClient = await _listener!.AcceptTcpClientAsync(ct);
                var clientId = $"vn-{_nextClientId++}";
                var client = new VnClient(clientId, tcpClient);

                lock (_clientsLock)
                    _clients[clientId] = client;

                var remoteIp = ((IPEndPoint?)tcpClient.Client.RemoteEndPoint)?.Address.ToString() ?? "?";
                Log($"Client {clientId} connected from {remoteIp}");
                NotifyClientCount();
                _ = HandleClientAsync(client, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                Log($"Accept error: {ex.Message}");
        }
    }

    private async Task HandleClientAsync(VnClient client, CancellationToken ct)
    {
        try
        {
            var stream = client.TcpClient.GetStream();
            var buffer = new byte[4096];
            var frameBuffer = new List<byte>(1024);

            while (!ct.IsCancellationRequested && client.TcpClient.Connected)
            {
                int bytesRead;
                try { bytesRead = await stream.ReadAsync(buffer, ct); }
                catch { break; }
                if (bytesRead == 0) break;

                frameBuffer.AddRange(buffer.AsSpan(0, bytesRead).ToArray());
                await ProcessClientFramesAsync(client, frameBuffer, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            RemoveClient(client.Id);
        }
    }

    private async Task ProcessClientFramesAsync(VnClient client, List<byte> buf, CancellationToken ct)
    {
        while (buf.Count >= 4)
        {
            // Find frame start
            int start = -1;
            for (int i = 0; i < buf.Count - 1; i++)
            {
                if (buf[i] == START1 && buf[i + 1] == START2) { start = i; break; }
            }
            if (start == -1) { buf.Clear(); return; }
            if (start > 0) buf.RemoveRange(0, start);
            if (buf.Count < 4) return;

            int len = (buf[2] << 8) | buf[3];
            if (len == 0 || len > 512) { buf.RemoveRange(0, 2); continue; }
            if (buf.Count < 4 + len) return;

            var payload = buf.GetRange(4, len).ToArray();
            buf.RemoveRange(0, 4 + len);

            await HandleClientMessageAsync(client, payload, ct);
        }
    }

    private async Task HandleClientMessageAsync(VnClient client, byte[] payload, CancellationToken ct)
    {
        try
        {
            var toRadio = ToRadio.Parser.ParseFrom(payload);

            switch (toRadio.PayloadVariantCase)
            {
                case ToRadio.PayloadVariantOneofCase.WantConfigId:
                    // Rate-limit repeated WantConfigId from same client (same ID within 5s = ignore)
                    if (client.IsDuplicateConfigRequest(toRadio.WantConfigId))
                    {
                        Log($"Client {client.Id}: duplicate WantConfigId {toRadio.WantConfigId}, ignoring");
                        return;
                    }
                    await SendConfigReplayAsync(client, toRadio.WantConfigId, ct);
                    return;

                case ToRadio.PayloadVariantOneofCase.Disconnect:
                    return;

                case ToRadio.PayloadVariantOneofCase.Heartbeat:
                    // Respond locally with a minimal QueueStatus so the app doesn't time out
                    var qs = new FromRadio { QueueStatus = new QueueStatus { Res = 0, Free = 16, Maxlen = 32 } };
                    await client.SendAsync(BuildFrame(qs.ToByteArray()), ct);
                    return;

                case ToRadio.PayloadVariantOneofCase.Packet:
                    if (BlockAdminCommands && toRadio.Packet.Decoded != null)
                    {
                        var portnum = (PortNum)toRadio.Packet.Decoded.Portnum;
                        if (portnum == PortNum.AdminApp)
                        {
                            Log($"Client {client.Id}: blocked admin command");
                            return;
                        }
                    }
                    // Inject into main app so sent messages appear there too
                    try
                    {
                        var fromRadioBytes = new FromRadio { Packet = toRadio.Packet }.ToByteArray();
                        ClientPacketReceived?.Invoke(this, fromRadioBytes);
                        // Broadcast to all other VN clients so they also see the message
                        BroadcastToClientsExcept(client.Id, BuildFrame(fromRadioBytes));
                    }
                    catch { }
                    break;
            }

            // Forward to physical node via queue
            await _toPhysicalQueue.Writer.WriteAsync(BuildFrame(payload), ct);
        }
        catch (Exception ex)
        {
            Log($"Client {client.Id} message error: {ex.Message}");
        }
    }

    private async Task SendConfigReplayAsync(VnClient client, uint wantConfigId, CancellationToken ct)
    {
        try
        {
            async Task Send(byte[]? frame)
            {
                if (frame == null) return;
                await client.SendAsync(frame, ct);
                await Task.Delay(10, ct);
            }

            await Send(_frameMyInfo);
            await Send(_frameMetadata);

            foreach (var ch in _frameChannels.OrderBy(kv => kv.Key).Select(kv => kv.Value))
                await Send(ch);
            foreach (var cfg in _frameConfigs.Values)
                await Send(cfg);
            foreach (var mod in _frameModuleConfigs.Values)
                await Send(mod);
            foreach (var ni in _frameNodes.Values)
                await Send(ni);

            // Always echo the client's own wantConfigId — the physical node's ID is irrelevant here
            var cc = new FromRadio { ConfigCompleteId = wantConfigId };
            await client.SendAsync(BuildFrame(cc.ToByteArray()), ct);

            Log($"Client {client.Id}: config replay done ({_frameChannels.Count} ch, {_frameNodes.Count} nodes)");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"Client {client.Id} replay error: {ex.Message}");
        }
    }

    private async Task ProcessSendQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _toPhysicalQueue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await _physicalConnection.WriteAsync(frame);
                    await Task.Delay(10, ct);
                }
                catch (Exception ex)
                {
                    Log($"Forward to physical node failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void BroadcastToClients(byte[] frame)
    {
        List<VnClient> snapshot;
        lock (_clientsLock)
            snapshot = [.. _clients.Values];

        foreach (var client in snapshot)
            _ = client.SendAsync(frame, CancellationToken.None);
    }

    private void BroadcastToClientsExcept(string excludeId, byte[] frame)
    {
        List<VnClient> snapshot;
        lock (_clientsLock)
            snapshot = _clients.Values.Where(c => c.Id != excludeId).ToList();

        foreach (var client in snapshot)
            _ = client.SendAsync(frame, CancellationToken.None);
    }

    private void RemoveClient(string id)
    {
        lock (_clientsLock)
        {
            if (_clients.TryGetValue(id, out var c))
            {
                c.Dispose();
                _clients.Remove(id);
            }
        }
        Log($"Client {id} disconnected");
        NotifyClientCount();
    }

    private void NotifyClientCount()
    {
        int count;
        lock (_clientsLock) count = _clients.Count;
        ClientCountChanged?.Invoke(this, count);
    }

    public List<(string Id, string Ip)> GetConnectedClients()
    {
        lock (_clientsLock)
            return _clients.Values.Select(c => (c.Id, c.RemoteIp)).ToList();
    }

    private static byte[] BuildFrame(byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        frame[0] = START1;
        frame[1] = START2;
        frame[2] = (byte)(payload.Length >> 8);
        frame[3] = (byte)(payload.Length & 0xFF);
        Array.Copy(payload, 0, frame, 4, payload.Length);
        return frame;
    }

    private void Log(string msg)
    {
        Logger.WriteLine($"[VirtualNode] {msg}");
        LogMessage?.Invoke(this, msg);
    }

    public void Dispose() => Stop();

    private sealed class VnClient : IDisposable
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private uint _lastConfigId;
        private DateTime _lastConfigAt = DateTime.MinValue;

        public string Id { get; }
        public TcpClient TcpClient { get; }
        public string RemoteIp { get; }

        public VnClient(string id, TcpClient tcpClient)
        {
            Id = id;
            TcpClient = tcpClient;
            RemoteIp = ((IPEndPoint?)tcpClient.Client.RemoteEndPoint)?.Address.ToString() ?? "?";
        }

        public bool IsDuplicateConfigRequest(uint configId)
        {
            var now = DateTime.UtcNow;
            if (configId == _lastConfigId && (now - _lastConfigAt).TotalSeconds < 5)
                return true;
            _lastConfigId = configId;
            _lastConfigAt = now;
            return false;
        }

        public async Task SendAsync(byte[] frame, CancellationToken ct)
        {
            if (!TcpClient.Connected) return;
            await _sendLock.WaitAsync(ct);
            try
            {
                await TcpClient.GetStream().WriteAsync(frame, ct);
            }
            catch { }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            try { TcpClient.Close(); } catch { }
        }
    }
}
