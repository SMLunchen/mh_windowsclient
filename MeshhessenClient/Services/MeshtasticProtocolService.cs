using System.Text;
using Google.Protobuf;
using Meshtastic.Protobufs;
using MeshhessenClient.Models;
using ProtoNodeInfo = Meshtastic.Protobufs.NodeInfo;
using ModelNodeInfo = MeshhessenClient.Models.NodeInfo;
using TracerouteResult = MeshhessenClient.Models.TracerouteResult;

namespace MeshhessenClient.Services;

public class MeshtasticProtocolService
{
    private readonly IConnectionService _connectionService;
    private readonly List<byte> _receiveBuffer = new();
    private uint _myNodeId;
    private DeviceInfo? _myDeviceInfo;
    private readonly Dictionary<uint, ModelNodeInfo> _knownNodes = new();
    private readonly List<Channel> _tempChannels = new();
    private bool _configComplete = false;
    private LoRaConfig? _currentLoRaConfig;
    private bool _isInitializing = false;
    private bool _isDisconnecting = false; // Flag für sauberes Beenden
    private readonly object _dataLock = new(); // Lock für Thread-Safety
    private int _packetCount = 0;
    private DateTime _lastPacketTime = DateTime.MinValue;
    private bool _debugSerial = false;
    private bool _debugDevice = false;
    private readonly HashSet<int> _receivedChannelResponses = new(); // Tracks which channel indices we got via GetChannelResponse
    private byte[] _sessionPasskey = Array.Empty<byte>(); // Session key from admin responses (required for write operations)
    private DateTime _lastValidPacketTime = DateTime.MinValue;
    private int _consecutiveTextChunks = 0;
    private bool _recoveryInProgress = false;
    private readonly StringBuilder _textLineBuffer = new(); // Sammelt unvollständige Textzeilen
    private DateTime _bufferWaitingSince = DateTime.MinValue; // Tracks when we started waiting for a partial packet
    private const int MAX_PACKET_LENGTH = 512; // Per Meshtastic spec: >512 = corrupted

    public event EventHandler<MessageItem>? MessageReceived;
    public event EventHandler<ModelNodeInfo>? NodeInfoReceived;
    public event EventHandler<ChannelInfo>? ChannelInfoReceived;
    public event EventHandler<LoRaConfig>? LoRaConfigReceived;
    public event EventHandler<DeviceConfig>? DeviceConfigReceived;
    public event EventHandler<PositionConfig>? PositionConfigReceived;
    public event EventHandler<MQTTConfig>? MqttConfigReceived;
    public event EventHandler<TelemetryConfig>? TelemetryConfigReceived;
    public event EventHandler<BluetoothConfig>? BluetoothConfigReceived;
    public event EventHandler<User>? OwnerReceived;
    public event EventHandler<DeviceInfo>? DeviceInfoReceived;
    public event EventHandler<int>? PacketCountChanged;
    public event EventHandler<TracerouteResult>? TracerouteReceived;
    public event EventHandler<(uint ReplyId, string Emoji, uint FromId)>? ReactionReceived;
    public event EventHandler<(uint NodeId, float BatteryPercent, float Voltage)>? DeviceTelemetryReceived;

    private TelemetryDatabaseService? _db;
    private NodeKeyService? _nodeKeyService;
    private PskMismatchAction _pskMismatchAction = PskMismatchAction.Overwrite;
    private readonly PkiDecryptionService _pkiDecrypt = new();

    private const byte PACKET_START_BYTE_1 = 0x94;
    private const byte PACKET_START_BYTE_2 = 0xC3;

    public MeshtasticProtocolService(IConnectionService connectionService)
    {
        _connectionService = connectionService;
        _connectionService.DataReceived += OnDataReceived;
    }

    public void SetDatabase(TelemetryDatabaseService db)
    {
        _db = db;
    }

    public void SetNodeKeyService(NodeKeyService service)
    {
        _nodeKeyService = service;
    }

    public void SetPskMismatchAction(PskMismatchAction action)
    {
        _pskMismatchAction = action;
    }

    public async Task InitializeAsync()
    {
        Logger.WriteLine("=== Initializing ===");

        lock (_dataLock)
        {
            _isInitializing = true;
            _isDisconnecting = false;

            // Clear all data from previous device
            _tempChannels.Clear();
            _knownNodes.Clear();
            _myNodeId = 0;
            _myDeviceInfo = null;
            _currentLoRaConfig = null;
            _configComplete = false;
            _packetCount = 0;
            _receivedChannelResponses.Clear();
            _sessionPasskey = Array.Empty<byte>();
            _pkiDecrypt.ClearPrivateKey();
            Logger.WriteLine("Cleared all data from previous session");
        }

        // Clear receive buffer
        lock (_receiveBuffer)
        {
            _receiveBuffer.Clear();
            Logger.WriteLine("Cleared receive buffer");
        }

        await Task.Delay(1000);

        if (_isDisconnecting)
        {
            return;
        }

        // Sende Wakeup-Sequenz (nur für Serial, nicht für BLE!)
        if (_connectionService.Type != ConnectionType.Bluetooth)
        {
            byte[] wakeup = new byte[64];
            for (int i = 0; i < 64; i++)
            {
                wakeup[i] = PACKET_START_BYTE_2; // 0xC3
            }
            Logger.WriteLine("Sending wakeup sequence...");
            if (_debugSerial)
            {
                Logger.WriteLine($"[SERIAL TX] Wakeup {wakeup.Length} bytes:\n    {ToHexString(wakeup)}");
            }
            await _connectionService.WriteAsync(wakeup);
            await Task.Delay(500);

            if (_isDisconnecting) return;
        }
        else
        {
            Logger.WriteLine("[BLE] Skipping wakeup sequence (not needed for BLE)");
        }

        // Fordere Config an
        Logger.WriteLine("Requesting config...");
        await RequestConfigAsync();

        // Warte auf config_complete (max 15 Sekunden)
        Logger.WriteLine("Waiting for config_complete (max 15s)...");
        bool configReceivedInTime = false;
        for (int i = 0; i < 150; i++)
        {
            bool isComplete;
            lock (_dataLock)
            {
                isComplete = _configComplete;
            }

            if (isComplete)
            {
                Logger.WriteLine($"Config_complete received after {i * 100}ms");
                configReceivedInTime = true;
                break;
            }
            await Task.Delay(100);
        }

        if (!configReceivedInTime)
        {
            Logger.WriteLine("WARNING: config_complete NOT received within 15 seconds!");
        }

        // Dynamisch warten: Warte bis 3 Sekunden lang keine neuen Nodes mehr kommen
        // (T-Deck sendet 250+ Nodes, das kann dauern)
        Logger.WriteLine("Waiting for data stream to finish...");
        int lastNodeCount = 0;
        int stableCount = 0;
        for (int i = 0; i < 60; i++) // max 30 Sekunden
        {
            if (_isDisconnecting) break;
            await Task.Delay(500);

            int currentNodeCount;
            lock (_dataLock)
            {
                currentNodeCount = _knownNodes.Count;
            }

            if (currentNodeCount == lastNodeCount)
            {
                stableCount++;
                if (stableCount >= 6) // 3 Sekunden keine neuen Daten
                {
                    Logger.WriteLine($"Data stream stable for 3s ({currentNodeCount} nodes)");
                    break;
                }
            }
            else
            {
                stableCount = 0;
                lastNodeCount = currentNodeCount;
            }
        }

        if (_isDisconnecting) return;

        // SecurityConfig + DeviceMetadata anfordern nach Init
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500); // kurz warten bis Gerät bereit
                var secReq = new AdminMessage { GetConfigRequest = (uint)AdminMessage.Types.ConfigType.SecurityConfig };
                await SendAdminMessageAsync(secReq);
                Logger.WriteLine("SecurityConfig requested for PKI decryption");

                await Task.Delay(200);
                var metaReq = new AdminMessage { GetDeviceMetadataRequest = true };
                await SendAdminMessageAsync(metaReq);
                Logger.WriteLine("DeviceMetadata requested for firmware version");
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Post-init admin request failed: {ex.Message}");
            }
        });

        // Kopiere Daten thread-safe
        List<ModelNodeInfo> nodesToFire;
        List<Channel> channelsToFire;
        LoRaConfig? loraConfigToFire;
        DeviceInfo? deviceInfoToFire;
        int nodeCount, channelCount;

        lock (_dataLock)
        {
            nodesToFire = new List<ModelNodeInfo>(_knownNodes.Values);
            channelsToFire = new List<Channel>(_tempChannels);
            loraConfigToFire = _currentLoRaConfig;
            deviceInfoToFire = _myDeviceInfo;
            nodeCount = _knownNodes.Count;
            channelCount = _tempChannels.Count;
        }

        Logger.WriteLine($"Init complete: {nodeCount} nodes, {channelCount} channels");

        if (_debugSerial)
        {
            Logger.WriteLine($"[DEBUG] About to fire {nodesToFire.Count} node events");
        }
        if (channelCount > 0)
        {
            Logger.WriteLine($"  Received channels during init: {string.Join(", ", channelsToFire.Select(c => $"[{c.Index}]{c.Role}"))}");
        }
        else
        {
            Logger.WriteLine("  WARNING: NO channels received during init!");
        }

        // Events erlauben
        lock (_dataLock)
        {
            _isInitializing = false;
        }

        // Feuere gespeicherte Events mit Delays um UI nicht zu überlasten
        foreach (var node in nodesToFire)
        {
            if (_isDisconnecting) break;
            NodeInfoReceived?.Invoke(this, node);

            // Kleine Pause alle 10 Nodes um UI nicht zu blockieren
            if (nodesToFire.IndexOf(node) % 10 == 9)
            {
                await Task.Delay(10);
            }
        }

        // Feuere DeviceInfo Event NACH allen NodeInfo Events
        // damit die eigene Node bereits in der Liste ist
        if (deviceInfoToFire != null)
        {
            DeviceInfoReceived?.Invoke(this, deviceInfoToFire);
            Logger.WriteLine($"DeviceInfo event fired for NodeId={deviceInfoToFire.NodeIdHex}");
        }

        foreach (var channel in channelsToFire)
        {
            if (_isDisconnecting) break;
            if (channel.Role == ChannelRole.Disabled) continue;

            var channelInfo = new ChannelInfo
            {
                Index = channel.Index,
                Name = ExtractChannelName(channel),
                Role = channel.Role.ToString(),
                Psk = channel.Settings?.Psk != null && channel.Settings.Psk.Length > 0
                    ? Convert.ToBase64String(channel.Settings.Psk.ToByteArray())
                    : "",
                Uplink = channel.Settings?.UplinkEnabled ?? false,
                Downlink = channel.Settings?.DownlinkEnabled ?? false
            };
            ChannelInfoReceived?.Invoke(this, channelInfo);
            await Task.Delay(50);
        }

        if (loraConfigToFire != null)
        {
            LoRaConfigReceived?.Invoke(this, loraConfigToFire);
        }

        // T-Deck/Plus sendet Channels NICHT in der Config-Sequenz
        // Fordere IMMER alle Channels einzeln per AdminMessage an
        // Retry-Logik: T-Deck antwortet inkonsistent, bis zu 3 Runden
        if (_myNodeId != 0 && channelCount < 8)
        {
            lock (_dataLock)
            {
                _receivedChannelResponses.Clear();
            }

            const int maxRetries = 3;
            for (int round = 1; round <= maxRetries; round++)
            {
                if (_isDisconnecting) break;

                // Bestimme welche Channels noch fehlen
                List<int> missingChannels;
                lock (_dataLock)
                {
                    missingChannels = Enumerable.Range(0, 8)
                        .Where(i => !_receivedChannelResponses.Contains(i))
                        .ToList();
                }

                if (missingChannels.Count == 0)
                {
                    Logger.WriteLine($"All 8 channels received after {round - 1} round(s)");
                    break;
                }

                Logger.WriteLine($"Channel request round {round}/{maxRetries}: requesting {missingChannels.Count} missing channels [{string.Join(",", missingChannels)}]...");

                foreach (int ch in missingChannels)
                {
                    if (_isDisconnecting) break;
                    await RequestChannelAsync(ch);
                    await Task.Delay(1500); // Großzügige Pause für T-Deck
                }

                // Warte auf Antworten (5s pro Runde)
                Logger.WriteLine($"Waiting for channel responses (5s)...");
                await Task.Delay(5000);

                // Status loggen
                int receivedCount;
                lock (_dataLock)
                {
                    receivedCount = _receivedChannelResponses.Count;
                    var received = string.Join(",", _receivedChannelResponses.OrderBy(x => x));
                    Logger.WriteLine($"  After round {round}: received channels [{received}] ({receivedCount}/8)");
                }
            }

            // Finale Zusammenfassung
            int finalChannelCount;
            lock (_dataLock)
            {
                finalChannelCount = _receivedChannelResponses.Count;
            }

            if (finalChannelCount > 0)
            {
                Logger.WriteLine($"Channel loading complete: {finalChannelCount}/8 channels received");
            }
            else
            {
                Logger.WriteLine($"WARNING: No channels received after {maxRetries} retry rounds!");
            }
        }
        else if (channelCount >= 8)
        {
            Logger.WriteLine($"All {channelCount} channels received during init.");
        }
    }

    private void OnDataReceived(object? sender, byte[] data)
    {
        try
        {
            // Ignore data if disconnecting
            if (_isDisconnecting)
            {
                if (_debugSerial)
                    Logger.WriteLine($"[DEBUG RX] Ignoring {data.Length} bytes (disconnecting)");
                return;
            }

            if (_debugSerial && data.Length > 0)
            {
                Logger.WriteLine($"[SERIAL RX] {data.Length} bytes:\n    {ToHexString(data)}");
            }

            // BLE sends raw protobuf without framing - parse directly
            if (_connectionService.Type == ConnectionType.Bluetooth)
            {
                if (data.Length > 0)
                {
                    ProcessPacket(data);
                }
            }
            else
            {
                // Serial/TCP use framing - buffer and process
                lock (_receiveBuffer)
                {
                    _receiveBuffer.AddRange(data);
                    ProcessBuffer();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"FATAL: OnDataReceived crashed: {ex.Message}");
            Logger.WriteLine($"  Stack trace: {ex.StackTrace}");
            // Try to recover by clearing the buffer
            try
            {
                lock (_receiveBuffer)
                {
                    _receiveBuffer.Clear();
                }
            }
            catch
            {
                // Ignore
            }
        }
    }

    private void ProcessBuffer()
    {
        try
        {
            // Safety: If buffer grows too large, clear it to prevent memory issues
            if (_receiveBuffer.Count > 100000) // 100KB limit
            {
                Logger.WriteLine($"WARNING: Receive buffer exceeded 100KB, clearing buffer");
                _receiveBuffer.Clear();
                _bufferWaitingSince = DateTime.MinValue;
                return;
            }

            while (_receiveBuffer.Count >= 4)
            {
                int startIndex = FindPacketStart();

                if (startIndex == -1)
                {
                    // Kein Protobuf-Paket gefunden - prüfe ob es ASCII-Text ist
                    // Letztes Byte behalten falls es 0x94 ist (könnte Start eines Headers sein)
                    int bytesToProcess = _receiveBuffer.Count;
                    if (_receiveBuffer[^1] == PACKET_START_BYTE_1)
                    {
                        bytesToProcess = _receiveBuffer.Count - 1;
                    }

                    if (bytesToProcess > 0)
                    {
                        ExtractAndLogAsciiText(bytesToProcess);
                        _receiveBuffer.RemoveRange(0, bytesToProcess);
                    }
                    _bufferWaitingSince = DateTime.MinValue;
                    break;
                }

                if (startIndex > 0)
                {
                    // Bytes VOR dem Paket-Start - könnten ASCII-Debug-Ausgaben sein
                    ExtractAndLogAsciiText(startIndex);
                    _receiveBuffer.RemoveRange(0, startIndex);
                }

                if (_receiveBuffer.Count < 4)
                {
                    break;
                }

                int packetLength = (_receiveBuffer[2] << 8) | _receiveBuffer[3];

                // Per Meshtastic spec: length > 512 = corrupted packet, skip this false start
                if (packetLength > MAX_PACKET_LENGTH)
                {
                    Logger.WriteLine($"WARNING: Packet length {packetLength} exceeds max {MAX_PACKET_LENGTH}, skipping false start");
                    // Nur die 2 Start-Bytes überspringen, danach weiter suchen
                    _receiveBuffer.RemoveRange(0, 2);
                    _bufferWaitingSince = DateTime.MinValue;
                    continue;
                }

                if (packetLength == 0)
                {
                    // Leeres Paket - überspringen
                    _receiveBuffer.RemoveRange(0, 4);
                    _bufferWaitingSince = DateTime.MinValue;
                    continue;
                }

                if (_receiveBuffer.Count < 4 + packetLength)
                {
                    // Warten auf restliche Bytes - aber mit Timeout
                    if (_bufferWaitingSince == DateTime.MinValue)
                    {
                        _bufferWaitingSince = DateTime.Now;
                    }
                    else if ((DateTime.Now - _bufferWaitingSince).TotalSeconds > 5)
                    {
                        // 5 Sekunden gewartet - Paket wird nie komplett, ist wohl ein falscher Start
                        Logger.WriteLine($"WARNING: Incomplete packet (need {4 + packetLength}, have {_receiveBuffer.Count}) timed out after 5s, skipping false start");
                        _receiveBuffer.RemoveRange(0, 2); // Skip false start bytes
                        _bufferWaitingSince = DateTime.MinValue;
                        continue;
                    }
                    break;
                }

                // Paket komplett - Timer zurücksetzen
                _bufferWaitingSince = DateTime.MinValue;

                byte[] packet = _receiveBuffer.GetRange(4, packetLength).ToArray();
                _receiveBuffer.RemoveRange(0, 4 + packetLength);

                // Gültiges Protobuf-Paket empfangen - Text-Modus-Zähler zurücksetzen
                _consecutiveTextChunks = 0;
                _lastValidPacketTime = DateTime.Now;

                try
                {
                    ProcessPacket(packet);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"ERROR: Packet processing failed: {ex.Message}");
                    Logger.WriteLine($"  Stack trace: {ex.StackTrace}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"FATAL: ProcessBuffer crashed: {ex.Message}");
            Logger.WriteLine($"  Stack trace: {ex.StackTrace}");
            _receiveBuffer.Clear();
            _bufferWaitingSince = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Prüft ob Bytes im Buffer ASCII-Text sind (Device-Debug-Ausgabe) und loggt sie.
    /// Erkennt Zeilen wie "DEBUG | ...", "INFO | ..." etc.
    /// Wenn zu viel Text ohne Protobuf kommt, wird Recovery ausgelöst.
    /// </summary>
    private void ExtractAndLogAsciiText(int count)
    {
        if (count <= 0) return;

        // Prüfe ob die Bytes überwiegend druckbares ASCII sind
        // 0x1B = ESC (ANSI color codes vom Device-Debug-Output)
        int printableCount = 0;
        for (int i = 0; i < count; i++)
        {
            byte b = _receiveBuffer[i];
            if ((b >= 0x20 && b <= 0x7E) || b == 0x0A || b == 0x0D || b == 0x09 || b == 0x1B)
            {
                printableCount++;
            }
        }

        // Mindestens 80% druckbar = wahrscheinlich ASCII-Text
        if (printableCount < count * 0.8)
        {
            // Nicht-druckbare Bytes - normaler Datenmüll, nur bei Debug loggen
            if (_debugSerial)
            {
                Logger.WriteLine($"[SERIAL] Discarding {count} non-protobuf bytes");
            }
            return;
        }

        // ASCII-Text extrahieren und loggen
        byte[] textBytes = _receiveBuffer.GetRange(0, count).ToArray();
        string text = Encoding.UTF8.GetString(textBytes);

        // In Zeilen aufteilen und loggen
        _textLineBuffer.Append(text);
        string bufferedText = _textLineBuffer.ToString();

        // Nur vollständige Zeilen verarbeiten
        int lastNewline = bufferedText.LastIndexOf('\n');
        if (lastNewline >= 0)
        {
            string completeLines = bufferedText.Substring(0, lastNewline + 1);
            _textLineBuffer.Clear();
            if (lastNewline + 1 < bufferedText.Length)
            {
                _textLineBuffer.Append(bufferedText.Substring(lastNewline + 1));
            }

            foreach (string line in completeLines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim('\r', ' ');
                if (!string.IsNullOrEmpty(trimmed))
                {
                    // ANSI color codes entfernen (z.B. \x1B[34m, \x1B[0m)
                    string clean = StripAnsiCodes(trimmed);
                    if (!string.IsNullOrEmpty(clean))
                    {
                        // Kritische Fehler IMMER loggen, auch wenn DebugDevice aus
                        CheckForCriticalErrors(clean);

                        if (_debugDevice)
                        {
                            Logger.WriteLine($"[DEVICE] {clean}");
                        }
                    }
                }
            }
        }

        // Text-Modus Erkennung: Nur wenn LANGE kein Protobuf-Paket mehr kam
        // (Device sendet normalerweise Debug-Text NEBEN Protobuf - das ist normal)
        _consecutiveTextChunks++;
        if (!_recoveryInProgress && !_isInitializing
            && _lastValidPacketTime != DateTime.MinValue  // Mindestens 1 Paket empfangen
            && (DateTime.Now - _lastValidPacketTime).TotalSeconds > 60) // 60s ohne Protobuf
        {
            Logger.WriteLine($"WARNING: No protobuf packets for {(DateTime.Now - _lastValidPacketTime).TotalSeconds:F0}s while receiving text. Attempting recovery...");
            _recoveryInProgress = true;
            Task.Run(async () => await RecoverProtobufModeAsync());
        }
    }

    /// <summary>
    /// Sendet Wakeup-Sequenz und WantConfigId um das Device zurück in den Protobuf-Modus zu bringen.
    /// </summary>
    private async Task RecoverProtobufModeAsync()
    {
        try
        {
            Logger.WriteLine("[RECOVERY] Sending wakeup sequence...");

            // Wakeup-Sequenz senden
            byte[] wakeup = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                wakeup[i] = PACKET_START_BYTE_2; // 0xC3
            }
            await _connectionService.WriteAsync(wakeup);
            await Task.Delay(500);

            if (_isDisconnecting) return;

            // WantConfigId senden um Protobuf-Modus zu erzwingen
            Logger.WriteLine("[RECOVERY] Sending WantConfigId to force protobuf mode...");
            var toRadio = new ToRadio
            {
                WantConfigId = (uint)Random.Shared.Next()
            };
            await SendToRadioAsync(toRadio);

            // Warte und prüfe ob Recovery erfolgreich war
            await Task.Delay(3000);

            if (_consecutiveTextChunks == 0)
            {
                Logger.WriteLine("[RECOVERY] Success - protobuf mode restored");
            }
            else
            {
                Logger.WriteLine("[RECOVERY] WARNING - still receiving text after recovery attempt");
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[RECOVERY] ERROR: {ex.Message}");
        }
        finally
        {
            _recoveryInProgress = false;
        }
    }

    private int FindPacketStart()
    {
        for (int i = 0; i < _receiveBuffer.Count - 1; i++)
        {
            if (_receiveBuffer[i] == PACKET_START_BYTE_1 && _receiveBuffer[i + 1] == PACKET_START_BYTE_2)
            {
                return i;
            }
        }
        return -1;
    }

    private void ProcessPacket(byte[] packet)
    {
        try
        {
            var fromRadio = FromRadio.Parser.ParseFrom(packet);

            if (_debugSerial)
            {
                Logger.WriteLine($"[DEBUG] Received FromRadio packet, type: {fromRadio.PayloadVariantCase}, isInit={_isInitializing}, isDisc={_isDisconnecting}");
            }

            // Update packet counter
            _packetCount++;
            _lastPacketTime = DateTime.Now;
            PacketCountChanged?.Invoke(this, _packetCount);

            HandleFromRadio(fromRadio);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error parsing FromRadio: {ex.Message}");
        }
    }

    private void HandleFromRadio(FromRadio fromRadio)
    {
        switch (fromRadio.PayloadVariantCase)
        {
            case FromRadio.PayloadVariantOneofCase.Packet:
                HandleMeshPacket(fromRadio.Packet);
                break;

            case FromRadio.PayloadVariantOneofCase.MyInfo:
                _myNodeId = fromRadio.MyInfo.MyNodeNum;
                Logger.WriteLine($"My Node ID: {_myNodeId:X8}");

                // DeviceInfo speichern, aber Event erst nach Init feuern
                lock (_dataLock)
                {
                    _myDeviceInfo = new DeviceInfo
                    {
                        NodeId = _myNodeId,
                        // Hardware and firmware info will be filled from other sources
                        // User-Daten werden später aus NodeInfo ergänzt
                    };
                }
                break;

            case FromRadio.PayloadVariantOneofCase.NodeInfo:
                HandleNodeInfo(fromRadio.NodeInfo);
                break;

            case FromRadio.PayloadVariantOneofCase.Channel:
                Logger.WriteLine($"Channel packet received: Index={fromRadio.Channel.Index}, Role={fromRadio.Channel.Role}");
                HandleChannel(fromRadio.Channel);
                break;

            case FromRadio.PayloadVariantOneofCase.Config:
                HandleConfig(fromRadio.Config);
                break;

            case FromRadio.PayloadVariantOneofCase.ConfigCompleteId:
                lock (_dataLock)
                {
                    _configComplete = true;
                }
                Logger.WriteLine("Config complete");
                break;

            case FromRadio.PayloadVariantOneofCase.ModuleConfig:
                break;
        }
    }

    private void HandleMeshPacket(MeshPacket packet)
    {
        if (packet.PayloadVariantCase == MeshPacket.PayloadVariantOneofCase.Decoded)
        {
            var data = packet.Decoded;

            // Record every decoded packet for telemetry analysis
            if (packet.From != 0 && !_isInitializing)
            {
                try
                {
                    int? hops = (packet.HopStart == 0 || packet.HopLimit > packet.HopStart)
                        ? null
                        : (int)(packet.HopStart - packet.HopLimit);
                    _db?.InsertPacketRx(
                        nodeId:    packet.From,
                        packetId:  packet.Id,
                        timestamp: DateTime.UtcNow,
                        snr:       packet.RxSnr,
                        rssi:      packet.RxRssi,
                        hopCount:  hops,
                        wantAck:   packet.WantAck);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"TelemetryDB packet_rx insert failed: {ex.Message}");
                }
            }

            RouteDecodedData(packet, data);
        }
        else if (packet.PayloadVariantCase == MeshPacket.PayloadVariantOneofCase.Encrypted)
        {
            // --- PKI fallback: try client-side decryption ---
            if (packet.PkiEncrypted && _pkiDecrypt.HasPrivateKey)
            {
                // Prefer the public key embedded in the packet (field 16); fall back to CSV
                byte[]? senderPublicKey = null;
                if (packet.PublicKey != null && packet.PublicKey.Length == 32)
                    senderPublicKey = packet.PublicKey.ToByteArray();
                else
                    senderPublicKey = _nodeKeyService?.GetPublicKey(packet.From) is { } b64
                        ? Convert.FromBase64String(b64) : null;

                if (senderPublicKey != null)
                {
                    var plaintext = _pkiDecrypt.TryDecrypt(
                        packet.Encrypted.ToByteArray(),
                        senderPublicKey,
                        packet.From,
                        packet.Id);

                    if (plaintext != null)
                    {
                        try
                        {
                            var data = Data.Parser.ParseFrom(plaintext);
                            Logger.WriteLine($"PKI decrypt OK: portnum={data.Portnum} from=!{packet.From:x8}");
                            // Record packet_rx for telemetry (same as firmware-decoded path)
                            try
                            {
                                int? hops = (packet.HopStart == 0 || packet.HopLimit > packet.HopStart)
                                    ? null : (int)(packet.HopStart - packet.HopLimit);
                                _db?.InsertPacketRx(packet.From, packet.Id, DateTime.UtcNow,
                                    packet.RxSnr, packet.RxRssi, hops, packet.WantAck);
                            }
                            catch { /* non-critical */ }
                            RouteDecodedData(packet, data);
                            return;
                        }
                        catch (Exception ex)
                        {
                            Logger.WriteLine($"PKI decrypt: proto parse failed: {ex.Message}");
                        }
                    }
                }
            }

            // Zeige verschlüsselte Nachricht (MainWindow filtert basierend auf Einstellung)
            string fromName = $"!{packet.From:x8}";
            lock (_dataLock)
            {
                if (_knownNodes.TryGetValue(packet.From, out var node))
                {
                    fromName = node.Name;
                }
            }

            var messageItem = new MessageItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                From = fromName,
                FromId = packet.From,
                ToId = packet.To,
                Message = System.Windows.Application.Current?.Resources["StrEncryptedMessage"] as string ?? "[Encrypted message – PSK required]",
                Channel = FormatChannelDisplay(packet.Channel),
                IsEncrypted = true,
                IsViaMqtt = packet.ViaMqtt
            };
            MessageReceived?.Invoke(this, messageItem);
        }
    }

    private void RouteDecodedData(MeshPacket packet, Data data)
    {
        switch (data.Portnum)
        {
            case 1: // TEXT_MESSAGE_APP
                HandleTextMessage(packet, data);
                break;

            case 4: // NODEINFO_APP
                HandleNodeInfoPacket(packet, data);
                break;

            case 3: // POSITION_APP
                HandlePositionPacket(packet, data);
                break;

            case 6: // ADMIN_APP
                HandleAdminMessage(packet, data);
                break;

            case 67: // TELEMETRY_APP
                HandleTelemetryPacket(packet, data);
                break;

            case 70: // TRACEROUTE_APP
                HandleTraceroutePacket(packet, data);
                break;
        }
    }

    private string FormatChannelDisplay(uint channelValue)
    {
        // In Meshtastic: Channel 0-7 are valid indices
        // Higher values (>7) are channel hashes
        if (channelValue <= 7)
        {
            return channelValue.ToString();
        }
        else
        {
            // This is a channel hash - the message was sent on a different channel
            // where we don't have the matching PSK to decrypt it
            // This is normal when nodes in the network use additional channels
            return $"Anderer Kanal ({channelValue & 0xFF})";
        }
    }

    private void HandleTextMessage(MeshPacket packet, Data data)
    {
        try
        {
            // Check if this is a tap-back reaction (emoji flag != 0, reply_id set)
            // emoji is a fixed32 indicator flag (=1); actual emoji string is in payload
            if (data.Emoji != 0 && data.ReplyId != 0)
            {
                string emoji = Encoding.UTF8.GetString(data.Payload.ToByteArray());
                Logger.WriteLine($"Reaction received: '{emoji}' from !{packet.From:x8} for msg {data.ReplyId}");
                ReactionReceived?.Invoke(this, (data.ReplyId, emoji, packet.From));
                return;
            }

            byte[] payloadBytes = data.Payload.ToByteArray();
            string messageText = Encoding.UTF8.GetString(payloadBytes);

            // Debug: Log raw bytes for alert bell debugging (only log first few bytes to avoid spam)
            if (payloadBytes.Length > 0 && payloadBytes[0] == 0x07)
            {
                var hexDump = string.Join(" ", payloadBytes.Take(Math.Min(20, payloadBytes.Length)).Select(b => $"{b:X2}"));
                Logger.WriteLine($"[MSG DEBUG] Alert Bell detected! First {Math.Min(20, payloadBytes.Length)} bytes from !{packet.From:x8}: {hexDump}");
            }

            string fromName = "Unknown";
            lock (_dataLock)
            {
                if (_knownNodes.TryGetValue(packet.From, out var node))
                {
                    fromName = node.Name;
                }
            }

            var messageItem = new MessageItem
            {
                Id = packet.Id,
                Time = DateTime.Now.ToString("HH:mm:ss"),
                From = fromName,
                FromId = packet.From,
                ToId = packet.To,
                Message = messageText,
                Channel = FormatChannelDisplay(packet.Channel),
                IsViaMqtt = packet.ViaMqtt
            };

            MessageReceived?.Invoke(this, messageItem);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR: Text message handling failed: {ex.Message}");
        }
    }

    private void HandleTraceroutePacket(MeshPacket packet, Data data)
    {
        try
        {
            var routeDiscovery = RouteDiscovery.Parser.ParseFrom(data.Payload);
            Logger.WriteLine($"Traceroute received: {routeDiscovery.Route.Count} forward hops, {routeDiscovery.RouteBack.Count} return hops, from !{packet.From:x8}");

            var result = new TracerouteResult
            {
                RequestId = data.RequestId,
                DestinationNodeId = packet.From, // response comes FROM the destination
                SourceNodeId = _myNodeId,
                IsViaMqtt = packet.ViaMqtt,
                RouteForward = routeDiscovery.Route.ToList(),
                SnrTowards = routeDiscovery.SnrTowards.ToList(),
                RouteBack = routeDiscovery.RouteBack.ToList(),
                SnrBack = routeDiscovery.SnrBack.ToList(),
            };

            TracerouteReceived?.Invoke(this, result);

            // Persist hops for telemetry analysis
            try { _db?.InsertTracerouteHops(result); }
            catch (Exception ex) { Logger.WriteLine($"TelemetryDB traceroute insert failed: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR: Traceroute handling failed: {ex.Message}");
        }
    }

    public async Task SendTracerouteAsync(uint destinationId)
    {
        try
        {
            var routeDiscovery = new RouteDiscovery();
            var meshPacket = new MeshPacket
            {
                From = _myNodeId,
                To = destinationId,
                Channel = 0,
                Decoded = new Data
                {
                    Portnum = 70, // TRACEROUTE_APP
                    Payload = routeDiscovery.ToByteString(),
                    WantResponse = true,
                },
                Id = (uint)Random.Shared.Next(),
                WantAck = false,
                HopLimit = 7,
                HopStart = 7,
            };

            var toRadio = new ToRadio { Packet = meshPacket };
            await SendToRadioAsync(toRadio);
            Logger.WriteLine($"Traceroute request sent to !{destinationId:x8}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error sending traceroute: {ex.Message}");
            throw;
        }
    }

    public async Task SendReactionAsync(string emoji, uint replyId, uint destinationId, uint channel = 0)
    {
        try
        {
            var meshPacket = new MeshPacket
            {
                From = _myNodeId,
                To = destinationId,
                Channel = channel,
                Decoded = new Data
                {
                    Portnum = 1, // TEXT_MESSAGE_APP
                    Payload = ByteString.CopyFromUtf8(emoji), // emoji string in payload
                    ReplyId = replyId,
                    Emoji = 1, // fixed32 indicator flag: marks this as a tap-back reaction
                },
                Id = (uint)Random.Shared.Next(),
                WantAck = false,
                HopLimit = 7,
                HopStart = 0,
            };

            var toRadio = new ToRadio { Packet = meshPacket };
            await SendToRadioAsync(toRadio);
            Logger.WriteLine($"Reaction '{emoji}' sent for msg {replyId} to !{destinationId:x8}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error sending reaction: {ex.Message}");
            throw;
        }
    }

    public Dictionary<uint, ModelNodeInfo> GetKnownNodes()
    {
        lock (_dataLock)
        {
            return new Dictionary<uint, ModelNodeInfo>(_knownNodes);
        }
    }

    public uint GetMyNodeId() => _myNodeId;

    private void HandleNodeInfo(Meshtastic.Protobufs.NodeInfo protoNodeInfo)
    {
        try
        {
            var nodeInfo = new ModelNodeInfo
            {
                NodeId = protoNodeInfo.Num,
                Id = $"!{protoNodeInfo.Num:x8}",
                ShortName = protoNodeInfo.User?.ShortName ?? $"{protoNodeInfo.Num:x4}",
                LongName = protoNodeInfo.User?.LongName ?? protoNodeInfo.User?.ShortName ?? $"Node-{protoNodeInfo.Num:x4}",
                Name = protoNodeInfo.User?.LongName ?? protoNodeInfo.User?.ShortName ?? $"Node-{protoNodeInfo.Num:x4}",
                Snr = protoNodeInfo.Snr.ToString("F1"),
                LastSeen = protoNodeInfo.LastHeard > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(protoNodeInfo.LastHeard).LocalDateTime.ToString("HH:mm:ss")
                    : DateTime.Now.ToString("HH:mm:ss")
            };

            if (protoNodeInfo.Position != null)
            {
                if (protoNodeInfo.Position.LatitudeI != 0 || protoNodeInfo.Position.LongitudeI != 0)
                {
                    nodeInfo.Latitude = protoNodeInfo.Position.LatitudeI / 1e7;
                    nodeInfo.Longitude = protoNodeInfo.Position.LongitudeI / 1e7;
                    nodeInfo.Altitude = protoNodeInfo.Position.Altitude;
                    Logger.WriteLine($"  Node {nodeInfo.Id}: GPS lat={nodeInfo.Latitude:F6}, lon={nodeInfo.Longitude:F6}, alt={nodeInfo.Altitude}m");
                }
                else
                {
                    Logger.WriteLine($"  Node {nodeInfo.Id}: Position present but LatI=LonI=0 (no GPS fix)");
                }
            }
            else
            {
                Logger.WriteLine($"  Node {nodeInfo.Id}: No position data");
            }

            if (protoNodeInfo.DeviceMetrics != null)
            {
                nodeInfo.Battery = $"{protoNodeInfo.DeviceMetrics.BatteryLevel}%";
            }

            if (protoNodeInfo.User?.HwModel is HardwareModel hw && hw != HardwareModel.Unset)
                nodeInfo.HardwareModel = hw.ToString();

            if (_nodeKeyService != null && protoNodeInfo.User?.PublicKey.Length > 0)
            {
                _nodeKeyService.CheckAndUpdate(
                    protoNodeInfo.Num,
                    protoNodeInfo.User.ShortName ?? "",
                    protoNodeInfo.User.LongName ?? "",
                    protoNodeInfo.User.PublicKey.ToByteArray(),
                    _pskMismatchAction);
            }

            nodeInfo.PkiKeyKnown = _nodeKeyService?.GetPublicKey(protoNodeInfo.Num) != null;

            // Update DeviceInfo if this is our own node
            if (protoNodeInfo.Num == _myNodeId && _myDeviceInfo != null && !string.IsNullOrEmpty(nodeInfo.HardwareModel))
            {
                lock (_dataLock)
                {
                    _myDeviceInfo.HardwareModel = nodeInfo.HardwareModel;
                }
            }

            bool shouldFireEvent;
            lock (_dataLock)
            {
                _knownNodes[protoNodeInfo.Num] = nodeInfo;
                shouldFireEvent = !_isInitializing;
            }

            if (_debugSerial)
            {
                Logger.WriteLine($"[DEBUG] HandleNodeInfo: Node {nodeInfo.Id} stored, total nodes={_knownNodes.Count}, shouldFireEvent={shouldFireEvent}");
            }

            // Nur Events feuern wenn NICHT initialisierend (außerhalb des Locks!)
            if (shouldFireEvent)
            {
                NodeInfoReceived?.Invoke(this, nodeInfo);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error handling node info: {ex.Message}");
        }
    }

    private void HandleNodeInfoPacket(MeshPacket packet, Data data)
    {
        try
        {
            var user = User.Parser.ParseFrom(data.Payload);

            if (_nodeKeyService != null && user.PublicKey.Length > 0)
            {
                _nodeKeyService.CheckAndUpdate(
                    packet.From,
                    user.ShortName ?? "",
                    user.LongName ?? "",
                    user.PublicKey.ToByteArray(),
                    _pskMismatchAction);
            }

            var nodeInfo = new ModelNodeInfo
            {
                NodeId = packet.From,
                Id = $"!{packet.From:x8}",
                Name = user.LongName ?? user.ShortName ?? $"Node-{packet.From:x4}",
                ShortName = user.ShortName ?? "",
                LongName = user.LongName ?? "",
                Snr = packet.RxSnr != 0f ? packet.RxSnr.ToString("F1") : "-",
                Rssi = packet.RxRssi != 0 ? packet.RxRssi.ToString() : "-",
                LastSeen = DateTime.Now.ToString("HH:mm:ss"),
                HardwareModel = user.HwModel != HardwareModel.Unset ? user.HwModel.ToString() : "",
                PkiKeyKnown = _nodeKeyService?.GetPublicKey(packet.From) != null
            };

            // Update DeviceInfo if this is our own node
            if (packet.From == _myNodeId && _myDeviceInfo != null)
            {
                lock (_dataLock)
                {
                    if (user.HwModel != HardwareModel.Unset)
                        _myDeviceInfo.HardwareModel = user.HwModel.ToString();
                    _myDeviceInfo.ShortName = user.ShortName ?? "";
                    _myDeviceInfo.LongName = user.LongName ?? "";
                    Logger.WriteLine($"Updated own DeviceInfo: HW={_myDeviceInfo.HardwareModel}, Name={_myDeviceInfo.LongName}");
                }
            }

            bool shouldFireEvent;
            lock (_dataLock)
            {
                _knownNodes[packet.From] = nodeInfo;
                shouldFireEvent = !_isInitializing;
            }

            if (_debugSerial)
            {
                Logger.WriteLine($"[DEBUG] HandleNodeInfoPacket: Node {nodeInfo.Id} stored, total nodes={_knownNodes.Count}, shouldFireEvent={shouldFireEvent}");
            }

            // Nur Events feuern wenn NICHT initialisierend (außerhalb des Locks!)
            if (shouldFireEvent)
            {
                NodeInfoReceived?.Invoke(this, nodeInfo);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error handling node info packet: {ex.Message}");
        }
    }

    private void HandlePositionPacket(MeshPacket packet, Data data)
    {
        try
        {
            var position = Position.Parser.ParseFrom(data.Payload);

            ModelNodeInfo? nodeToFire = null;
            bool shouldFireEvent;

            Logger.WriteLine($"Position packet from !{packet.From:x8}: LatI={position.LatitudeI}, LonI={position.LongitudeI}, Alt={position.Altitude}");

            lock (_dataLock)
            {
                if (_knownNodes.TryGetValue(packet.From, out var nodeInfo))
                {
                    if (position.LatitudeI != 0 || position.LongitudeI != 0)
                    {
                        nodeInfo.Latitude = position.LatitudeI / 1e7;
                        nodeInfo.Longitude = position.LongitudeI / 1e7;
                        nodeInfo.Altitude = position.Altitude;
                        Logger.WriteLine($"  Position updated: lat={nodeInfo.Latitude:F6}, lon={nodeInfo.Longitude:F6}");
                    }
                    else
                    {
                        Logger.WriteLine($"  Position ignored (LatI=LonI=0, no GPS fix)");
                    }
                    nodeInfo.LastSeen = DateTime.Now.ToString("HH:mm:ss");
                    if (packet.RxRssi != 0) nodeInfo.Rssi = packet.RxRssi.ToString();
                    if (packet.RxSnr != 0f) nodeInfo.Snr = packet.RxSnr.ToString("F1");
                    nodeToFire = nodeInfo;
                    shouldFireEvent = !_isInitializing;
                }
                else
                {
                    Logger.WriteLine($"  Node !{packet.From:x8} unknown, position discarded");
                    shouldFireEvent = false;
                }
            }

            // Nur Events feuern wenn NICHT initialisierend (außerhalb des Locks!)
            if (shouldFireEvent && nodeToFire != null)
            {
                NodeInfoReceived?.Invoke(this, nodeToFire);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error handling position packet: {ex.Message}");
        }
    }

    private void HandleTelemetryPacket(MeshPacket packet, Data data)
    {
        try
        {
            ModelNodeInfo? nodeToFire = null;
            bool shouldFireEvent;
            float batteryPercent = 0f, voltage = 0f;

            // Parse telemetry payload
            Telemetry? telemetry = null;
            try { telemetry = Telemetry.Parser.ParseFrom(data.Payload); }
            catch { /* ignore parse errors for unknown variants */ }

            if (telemetry?.DeviceMetrics is { } dm)
            {
                batteryPercent = dm.BatteryLevel;
                voltage        = dm.Voltage;

                _db?.InsertDeviceTelemetry(
                    nodeId:          packet.From,
                    timestamp:       DateTime.UtcNow,
                    batteryPercent:  dm.BatteryLevel,
                    voltage:         dm.Voltage,
                    channelUtil:     dm.ChannelUtilization,
                    airTxUtil:       dm.AirUtilTx,
                    uptimeSeconds:   dm.UptimeSeconds);
            }

            if (telemetry?.EnvironmentMetrics is { } em && (em.Temperature != 0 || em.RelativeHumidity != 0))
            {
                _db?.InsertEnvironmentTelemetry(
                    nodeId:      packet.From,
                    timestamp:   DateTime.UtcNow,
                    temp:        em.Temperature,
                    humidity:    em.RelativeHumidity,
                    pressure:    em.BarometricPressure,
                    iaq:         (int)em.Iaq);
            }

            lock (_dataLock)
            {
                if (_knownNodes.TryGetValue(packet.From, out var nodeInfo))
                {
                    nodeInfo.LastSeen = DateTime.Now.ToString("HH:mm:ss");
                    if (packet.RxRssi != 0) nodeInfo.Rssi = packet.RxRssi.ToString();
                    if (packet.RxSnr != 0f) nodeInfo.Snr = packet.RxSnr.ToString("F1");
                    // Live-update battery from telemetry payload
                    if (batteryPercent > 0)
                        nodeInfo.Battery = $"{batteryPercent:F0}%";
                    nodeToFire = nodeInfo;
                    shouldFireEvent = !_isInitializing;
                }
                else
                {
                    shouldFireEvent = false;
                }
            }

            // Nur Events feuern wenn NICHT initialisierend (außerhalb des Locks!)
            if (shouldFireEvent && nodeToFire != null)
            {
                NodeInfoReceived?.Invoke(this, nodeToFire);
                if (batteryPercent > 0 || voltage > 0)
                    DeviceTelemetryReceived?.Invoke(this, (packet.From, batteryPercent, voltage));
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error handling telemetry packet: {ex.Message}");
        }
    }

    private void HandleChannel(Channel channel)
    {
        try
        {
            // DEBUG: Log channel name details
            if (channel.Settings != null && !string.IsNullOrEmpty(channel.Settings.Name))
            {
                var rawName = channel.Settings.Name;
                var nameBytes = System.Text.Encoding.UTF8.GetBytes(rawName);
                string hexDump = BitConverter.ToString(nameBytes).Replace("-", " ");
                Logger.WriteLine($"Channel auto-received: Index={channel.Index}, Role={channel.Role}");
                Logger.WriteLine($"  DEBUG: Name length={rawName.Length}, Bytes=[{hexDump}]");

                var charInfo = string.Join(" ", rawName.Select((c, i) => $"[{i}]={((int)c):X2}"));
                Logger.WriteLine($"  DEBUG: Chars={charInfo}");
            }
            else
            {
                Logger.WriteLine($"Channel auto-received: Index={channel.Index}, Role={channel.Role}, Name=empty");
            }

            bool shouldFireEvent;
            lock (_dataLock)
            {
                var existing = _tempChannels.FirstOrDefault(c => c.Index == channel.Index);
                if (existing != null)
                {
                    _tempChannels.Remove(existing);
                }
                _tempChannels.Add(channel);
                shouldFireEvent = !_isInitializing;
            }

            if (shouldFireEvent)
            {
                if (channel.Role == ChannelRole.Disabled)
                {
                    Logger.WriteLine($"  Channel {channel.Index} is DISABLED, not firing event");
                }
                else
                {
                    var channelName = channel.Settings?.Name ?? "";

                    // Fallback: Use ModemPreset name for PRIMARY channel without name
                    if (string.IsNullOrWhiteSpace(channelName) && channel.Role == ChannelRole.Primary && _currentLoRaConfig != null)
                    {
                        channelName = _currentLoRaConfig.ModemPreset.ToString().Replace("_", " ");
                        Logger.WriteLine($"  Using preset name for primary channel: '{channelName}'");
                    }
                    else if (string.IsNullOrWhiteSpace(channelName))
                    {
                        channelName = $"Channel {channel.Index}";
                    }

                    Logger.WriteLine($"  Firing ChannelInfoReceived event: Index={channel.Index}, Name='{channelName}', Role={channel.Role}");

                    var channelInfo = new ChannelInfo
                    {
                        Index = channel.Index,
                        Name = channelName,
                        Role = channel.Role.ToString(),
                        Psk = channel.Settings?.Psk != null && channel.Settings.Psk.Length > 0
                            ? Convert.ToBase64String(channel.Settings.Psk.ToByteArray())
                            : "",
                        Uplink = channel.Settings?.UplinkEnabled ?? false,
                        Downlink = channel.Settings?.DownlinkEnabled ?? false
                    };
                    ChannelInfoReceived?.Invoke(this, channelInfo);
                }
            }
            else
            {
                Logger.WriteLine($"  Channel {channel.Index} stored during init, event will fire later");
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR: Channel handling failed: {ex.Message}");
        }
    }

    public async Task<uint> SendTextMessageAsync(string text, uint destinationId = 0xFFFFFFFF, uint channel = 0)
    {
        try
        {
            uint packetId = (uint)Random.Shared.Next(1, int.MaxValue); // never 0
            var meshPacket = new MeshPacket
            {
                From = _myNodeId,
                To = destinationId,
                Channel = channel,
                Decoded = new Data
                {
                    Portnum = 1, // TEXT_MESSAGE_APP
                    Payload = ByteString.CopyFromUtf8(text)
                },
                Id = packetId,
                WantAck = false,
                HopLimit = 7,
                HopStart = 0
            };

            var toRadio = new ToRadio
            {
                Packet = meshPacket
            };

            await SendToRadioAsync(toRadio);
            return packetId;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error sending text message: {ex.Message}");
            throw;
        }
    }

    private async Task EnsureSessionKeyAsync()
    {
        if (_sessionPasskey.Length > 0) return;

        // Request session key via SESSIONKEY_CONFIG (value 8)
        Logger.WriteLine("Requesting session key...");
        var adminMsg = new AdminMessage { GetConfigRequest = 8 };
        await SendAdminMessageAsync(adminMsg);

        // Wait for session key response
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(200);
            if (_sessionPasskey.Length > 0)
            {
                Logger.WriteLine("Session key received");
                return;
            }
        }
        Logger.WriteLine("Warning: No session key received after timeout, proceeding anyway");
    }

    public async Task SetChannelAsync(int channelIndex, string name, byte[] psk, bool secondary = true, bool uplinkEnabled = false, bool downlinkEnabled = false)
    {
        try
        {
            await EnsureSessionKeyAsync();
            var channel = new Channel
            {
                Index = channelIndex,
                Settings = new ChannelSettings
                {
                    Name = name,
                    Psk = ByteString.CopyFrom(psk),
                    UplinkEnabled = uplinkEnabled,
                    DownlinkEnabled = downlinkEnabled
                },
                Role = secondary ? ChannelRole.Secondary : ChannelRole.Primary
            };
            var setChannel = new AdminMessage { SetChannel = channel };
            await SendAdminMessageAsync(setChannel);

            Logger.WriteLine($"Channel {channelIndex} ('{name}') set successfully");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error setting channel: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Deletes a channel by shifting subsequent channels up and disabling the last slot.
    /// This matches the Meshtastic Python reference implementation.
    /// </summary>
    public async Task DeleteChannelAsync(int channelIndex)
    {
        try
        {
            await EnsureSessionKeyAsync();
            // Get current channels sorted by index
            List<Channel> channels;
            lock (_dataLock)
            {
                channels = _tempChannels.OrderBy(c => c.Index).ToList();
            }

            // Find the highest used index
            int maxUsedIndex = channels.Where(c => c.Role != ChannelRole.Disabled).Max(c => c.Index);

            // Shift channels: move each subsequent channel one index down
            for (int i = channelIndex; i < maxUsedIndex; i++)
            {
                var nextChannel = channels.FirstOrDefault(c => c.Index == i + 1);
                if (nextChannel != null && nextChannel.Role != ChannelRole.Disabled)
                {
                    var shifted = new Channel
                    {
                        Index = i,
                        Settings = nextChannel.Settings?.Clone() ?? new ChannelSettings(),
                        Role = nextChannel.Role
                    };
                    var msg = new AdminMessage { SetChannel = shifted };
                    await SendAdminMessageAsync(msg);
                    await Task.Delay(300);
                    Logger.WriteLine($"  Shifted channel {i + 1} -> {i} ('{shifted.Settings.Name}')");
                }
            }

            // Disable the last slot
            var disabledChannel = new Channel
            {
                Index = maxUsedIndex,
                Settings = new ChannelSettings(),
                Role = ChannelRole.Disabled
            };
            var disableMsg = new AdminMessage { SetChannel = disabledChannel };
            await SendAdminMessageAsync(disableMsg);
            Logger.WriteLine($"  Disabled channel slot {maxUsedIndex}");

            Logger.WriteLine($"Channel {channelIndex} deleted successfully (shifted {maxUsedIndex - channelIndex} channels)");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error deleting channel: {ex.Message}");
            throw;
        }
    }

    public async Task RefreshChannelAsync(int channelIndex)
    {
        await RequestChannelAsync(channelIndex);
    }

    public async Task RefreshAllChannelsAsync()
    {
        for (int i = 0; i < 8; i++)
        {
            await RequestChannelAsync(i);
            await Task.Delay(300);
        }
    }

    private async Task SendAdminMessageAsync(AdminMessage adminMsg)
    {
        // Include session passkey for write operations (required by firmware)
        if (_sessionPasskey.Length > 0)
        {
            adminMsg.SessionPasskey = ByteString.CopyFrom(_sessionPasskey);
        }

        var meshPacket = new MeshPacket
        {
            From = _myNodeId,
            To = _myNodeId,
            Decoded = new Data
            {
                Portnum = 6, // ADMIN_APP
                Payload = adminMsg.ToByteString(),
                WantResponse = true
            },
            Id = (uint)Random.Shared.Next()
        };

        var toRadio = new ToRadio { Packet = meshPacket };
        await SendToRadioAsync(toRadio);
    }

    private async Task SendToRadioAsync(ToRadio toRadio)
    {
        byte[] protoData = toRadio.ToByteArray();

        // BLE sends raw protobufs, Serial needs framing
        byte[] dataToSend;
        if (_connectionService.Type == ConnectionType.Bluetooth)
        {
            // BLE: Send raw protobuf directly
            dataToSend = protoData;
            if (_debugSerial)
            {
                Logger.WriteLine($"[BLE TX] ToRadio {dataToSend.Length} bytes (raw protobuf):\n    {ToHexString(dataToSend)}");
            }
        }
        else
        {
            // Serial/TCP: Add framing
            byte[] frame = new byte[4 + protoData.Length];
            frame[0] = PACKET_START_BYTE_1;
            frame[1] = PACKET_START_BYTE_2;
            frame[2] = (byte)(protoData.Length >> 8);
            frame[3] = (byte)(protoData.Length & 0xFF);
            Array.Copy(protoData, 0, frame, 4, protoData.Length);
            dataToSend = frame;

            if (_debugSerial)
            {
                Logger.WriteLine($"[SERIAL TX] ToRadio {frame.Length} bytes (payload {protoData.Length}):\n    {ToHexString(frame)}");
            }
        }

        await _connectionService.WriteAsync(dataToSend);
    }

    private async Task RequestConfigAsync()
    {
        var configId = (uint)Random.Shared.Next();
        Logger.WriteLine($"Sending WantConfigId request (ID={configId})...");

        var toRadio = new ToRadio
        {
            WantConfigId = configId
        };

        await SendToRadioAsync(toRadio);
        Logger.WriteLine("WantConfigId sent successfully");
    }

    private async Task RequestChannelAsync(int channelIndex)
    {
        var adminMsg = new AdminMessage
        {
            GetChannelRequest = (uint)(channelIndex + 1) // Protocol uses 1-based indexing
        };

        var meshPacket = new MeshPacket
        {
            From = _myNodeId,
            To = _myNodeId, // Send to self for local requests
            Decoded = new Data
            {
                Portnum = 6, // ADMIN_APP
                Payload = adminMsg.ToByteString(),
                WantResponse = true
            },
            Id = (uint)Random.Shared.Next()
        };

        var toRadio = new ToRadio
        {
            Packet = meshPacket
        };

        Logger.WriteLine($"  Requesting channel {channelIndex}...");
        await SendToRadioAsync(toRadio);
    }

    // ========== Config Request Methods ==========

    public async Task RequestOwnerAsync()
    {
        var adminMsg = new AdminMessage { GetOwnerRequest = true };
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task RequestDeviceConfigAsync()
    {
        var adminMsg = new AdminMessage { GetConfigRequest = 0 }; // DEVICE = 0
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task RequestPositionConfigAsync()
    {
        var adminMsg = new AdminMessage { GetConfigRequest = 1 }; // POSITION = 1
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task RequestLoRaConfigAsync()
    {
        var adminMsg = new AdminMessage { GetConfigRequest = 5 }; // LORA = 5
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task RequestMqttConfigAsync()
    {
        var adminMsg = new AdminMessage { GetModuleConfigRequest = 0 }; // MQTT = 0
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task RequestTelemetryConfigAsync()
    {
        var adminMsg = new AdminMessage { GetModuleConfigRequest = 5 }; // TELEMETRY = 5
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task RequestBluetoothConfigAsync()
    {
        var adminMsg = new AdminMessage { GetConfigRequest = 6 }; // BLUETOOTH = 6
        await SendAdminMessageAsync(adminMsg);
    }

    // ========== Config Set Methods ==========

    public async Task SetOwnerAsync(User user)
    {
        await EnsureSessionKeyAsync();
        var adminMsg = new AdminMessage { SetOwner = user };
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task SetDeviceConfigAsync(DeviceConfig config)
    {
        await EnsureSessionKeyAsync();
        var adminMsg = new AdminMessage { SetConfig = new Config { Device = config } };
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task SetPositionConfigAsync(PositionConfig config)
    {
        await EnsureSessionKeyAsync();
        var adminMsg = new AdminMessage { SetConfig = new Config { Position = config } };
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task SetLoRaConfigAsync(LoRaConfig config)
    {
        await EnsureSessionKeyAsync();
        var adminMsg = new AdminMessage { SetConfig = new Config { Lora = config } };
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task SetMqttConfigAsync(MQTTConfig config)
    {
        await EnsureSessionKeyAsync();
        var adminMsg = new AdminMessage { SetModuleConfig = new ModuleConfig { Mqtt = config } };
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task SetTelemetryConfigAsync(TelemetryConfig config)
    {
        await EnsureSessionKeyAsync();
        var adminMsg = new AdminMessage { SetModuleConfig = new ModuleConfig { Telemetry = config } };
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task SetBluetoothConfigAsync(BluetoothConfig config)
    {
        await EnsureSessionKeyAsync();
        var adminMsg = new AdminMessage { SetConfig = new Config { Bluetooth = config } };
        await SendAdminMessageAsync(adminMsg);
    }

    public async Task ResetNodeDbAsync()
    {
        await EnsureSessionKeyAsync();
        var adminMsg = new AdminMessage { NodedbReset = true };
        await SendAdminMessageAsync(adminMsg);
    }

    public void Disconnect()
    {
        Logger.WriteLine("MeshtasticProtocolService: Disconnecting...");

        lock (_dataLock)
        {
            _isDisconnecting = true;
            _isInitializing = false;
        }

        // Event-Handler bleibt registriert - _isDisconnecting verhindert Verarbeitung
        Logger.WriteLine("MeshtasticProtocolService: Disconnect complete");
    }

    private void HandleConfig(Config config)
    {
        switch (config.PayloadVariantCase)
        {
            case Config.PayloadVariantOneofCase.Lora:
                Logger.WriteLine($"LoRa: {config.Lora.Region}, {config.Lora.ModemPreset}");

                bool shouldFireEvent;
                lock (_dataLock)
                {
                    _currentLoRaConfig = config.Lora;
                    shouldFireEvent = !_isInitializing;
                }

                if (shouldFireEvent)
                {
                    LoRaConfigReceived?.Invoke(this, config.Lora);
                }
                break;
        }
    }

    private void HandleAdminMessage(MeshPacket packet, Data data)
    {
        try
        {
            // DEBUG: Log RAW payload bytes
            var payloadBytes = data.Payload.ToByteArray();
            string payloadHex = BitConverter.ToString(payloadBytes).Replace("-", " ");
            Logger.WriteLine($"Admin message RAW payload ({payloadBytes.Length} bytes): [{payloadHex}]");

            var adminMsg = AdminMessage.Parser.ParseFrom(data.Payload);
            Logger.WriteLine($"Admin message: {adminMsg.PayloadVariantCase}");

            // Store session passkey from every admin response (required for write operations)
            if (adminMsg.SessionPasskey != null && adminMsg.SessionPasskey.Length > 0)
            {
                _sessionPasskey = adminMsg.SessionPasskey.ToByteArray();
                Logger.WriteLine($"  Session passkey updated ({_sessionPasskey.Length} bytes)");
            }

            switch (adminMsg.PayloadVariantCase)
            {
                case AdminMessage.PayloadVariantOneofCase.GetChannelResponse:
                    var channel = adminMsg.GetChannelResponse;
                    var channelName = ExtractChannelName(channel);
                    Logger.WriteLine($"  Channel response: Index={channel.Index}, Role={channel.Role}, Name='{channelName}'");

                    bool shouldFireChannelEvent;
                    lock (_dataLock)
                    {
                        var existing = _tempChannels.FirstOrDefault(c => c.Index == channel.Index);
                        if (existing != null)
                        {
                            _tempChannels.Remove(existing);
                        }
                        _tempChannels.Add(channel);
                        _receivedChannelResponses.Add(channel.Index);
                        shouldFireChannelEvent = !_isInitializing;
                    }

                    if (shouldFireChannelEvent && channel.Role != ChannelRole.Disabled)
                    {
                        var channelInfo = new ChannelInfo
                        {
                            Index = channel.Index,
                            Name = channelName,
                            Role = channel.Role.ToString(),
                            Psk = channel.Settings?.Psk != null && channel.Settings.Psk.Length > 0
                                ? Convert.ToBase64String(channel.Settings.Psk.ToByteArray())
                                : ""
                        };
                        ChannelInfoReceived?.Invoke(this, channelInfo);
                    }
                    break;

                case AdminMessage.PayloadVariantOneofCase.GetOwnerResponse:
                    Logger.WriteLine($"  Owner response received");
                    OwnerReceived?.Invoke(this, adminMsg.GetOwnerResponse);
                    break;

                case AdminMessage.PayloadVariantOneofCase.GetConfigResponse:
                    var config = adminMsg.GetConfigResponse;
                    Logger.WriteLine($"  Config response type: {config.PayloadVariantCase}");

                    switch (config.PayloadVariantCase)
                    {
                        case Config.PayloadVariantOneofCase.Lora:
                            bool shouldFireLoRaEvent;
                            lock (_dataLock)
                            {
                                _currentLoRaConfig = config.Lora;
                                shouldFireLoRaEvent = !_isInitializing;
                            }
                            if (shouldFireLoRaEvent)
                                LoRaConfigReceived?.Invoke(this, config.Lora);
                            break;

                        case Config.PayloadVariantOneofCase.Device:
                            DeviceConfigReceived?.Invoke(this, config.Device);
                            break;

                        case Config.PayloadVariantOneofCase.Position:
                            PositionConfigReceived?.Invoke(this, config.Position);
                            break;

                        case Config.PayloadVariantOneofCase.Bluetooth:
                            BluetoothConfigReceived?.Invoke(this, config.Bluetooth);
                            break;

                        case Config.PayloadVariantOneofCase.Security:
                            var sec = config.Security;
                            if (sec.PrivateKey != null && sec.PrivateKey.Length == 32)
                            {
                                _pkiDecrypt.SetPrivateKey(sec.PrivateKey.ToByteArray());
                                Logger.WriteLine("PKI private key loaded — client-side decryption active");
                            }
                            else
                            {
                                Logger.WriteLine("SecurityConfig received but private key missing/invalid");
                            }
                            break;
                    }
                    break;

                case AdminMessage.PayloadVariantOneofCase.GetDeviceMetadataResponse:
                    var meta = adminMsg.GetDeviceMetadataResponse;
                    Logger.WriteLine($"DeviceMetadata: FW={meta.FirmwareVersion}, HW={meta.HwModel}");
                    lock (_dataLock)
                    {
                        if (_myDeviceInfo != null)
                        {
                            _myDeviceInfo.FirmwareVersion = meta.FirmwareVersion;
                            if (meta.HwModel != HardwareModel.Unset)
                                _myDeviceInfo.HardwareModel = meta.HwModel.ToString();
                        }
                    }
                    if (_myDeviceInfo != null)
                        DeviceInfoReceived?.Invoke(this, _myDeviceInfo);
                    break;

                case AdminMessage.PayloadVariantOneofCase.GetModuleConfigResponse:
                    var moduleConfig = adminMsg.GetModuleConfigResponse;
                    Logger.WriteLine($"  ModuleConfig response type: {moduleConfig.PayloadVariantCase}");

                    switch (moduleConfig.PayloadVariantCase)
                    {
                        case ModuleConfig.PayloadVariantOneofCase.Mqtt:
                            MqttConfigReceived?.Invoke(this, moduleConfig.Mqtt);
                            break;

                        case ModuleConfig.PayloadVariantOneofCase.Telemetry:
                            TelemetryConfigReceived?.Invoke(this, moduleConfig.Telemetry);
                            break;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR: Admin message failed: {ex.Message}");
        }
    }

    public void SetDebugSerial(bool enabled)
    {
        _debugSerial = enabled;
        Logger.WriteLine($"Serial debug {(enabled ? "enabled" : "disabled")}");
    }

    public void SetDebugDevice(bool enabled)
    {
        _debugDevice = enabled;
        Logger.WriteLine($"Device debug logging {(enabled ? "enabled" : "disabled")}");
    }

    // Meshtastic Critical Error Codes (from protobufs CriticalErrorCode enum)
    private static readonly Dictionary<string, string> CriticalErrors = new()
    {
        { "TxWatchdog", "Software-Bug beim LoRa-Senden erkannt" },
        { "SleepEnterWait", "Software-Bug beim Einschlafen erkannt" },
        { "NoRadio", "Kein LoRa-Radio gefunden" },
        { "UBloxInitFailed", "UBlox GPS Initialisierung fehlgeschlagen" },
        { "NoAXP192", "Power-Management-Chip fehlt oder defekt" },
        { "InvalidRadioSetting", "Ungültige Radio-Einstellung, Kommunikation undefiniert" },
        { "TransmitFailed", "Radio-Sendehardware-Fehler" },
        { "Brownout", "CPU-Spannung unter Minimum gefallen" },
        { "SX1262Failure", "SX1262 Radio Selbsttest fehlgeschlagen" },
        { "RadioSpiBug", "SPI-Fehler beim Senden" },
        { "FlashCorruptionRecoverable", "Flash-Korruption erkannt (repariert)" },
        { "FlashCorruptionUnrecoverable", "Flash-Korruption erkannt (NICHT reparierbar, Neukonfiguration nötig)" },
    };

    /// <summary>
    /// Prüft Device-Debug-Zeilen auf kritische Fehlermeldungen und loggt sie immer.
    /// </summary>
    private void CheckForCriticalErrors(string line)
    {
        // Device meldet kritische Fehler als z.B. "CRITICAL ERROR" oder "fault" Zeilen
        bool isCritical = line.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase)
                       || line.Contains("FAULT", StringComparison.OrdinalIgnoreCase)
                       || line.Contains("ASSERT", StringComparison.OrdinalIgnoreCase)
                       || line.Contains("PANIC", StringComparison.OrdinalIgnoreCase)
                       || line.Contains("Brownout", StringComparison.OrdinalIgnoreCase)
                       || line.Contains("reboot", StringComparison.OrdinalIgnoreCase);

        if (isCritical)
        {
            Logger.WriteLine($"[DEVICE CRITICAL] {line}");

            // Bekannte Error Codes prüfen
            foreach (var kvp in CriticalErrors)
            {
                if (line.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.WriteLine($"  >> Meshtastic Error: {kvp.Value}");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Extrahiert den Channel-Namen sicher aus einem Channel-Objekt.
    /// Verwendet manuelles Tag-3-Parsing als Workaround für korrupte Protobuf-Namen.
    /// </summary>
    private string ExtractChannelName(Channel channel)
    {
        string channelName = "";

        try
        {
            if (channel.Settings != null)
            {
                var settingsBytes = channel.Settings.ToByteArray();

                // Suche nach Tag 3 mit wire type 2 (length-delimited string)
                // Tag 3 = (3 << 3) | 2 = 26 = 0x1A
                for (int i = 0; i < settingsBytes.Length - 2; i++)
                {
                    if (settingsBytes[i] == 0x1A) // Tag 3, wire type 2
                    {
                        int nameLength = settingsBytes[i + 1];
                        if (i + 2 + nameLength <= settingsBytes.Length)
                        {
                            byte[] nameBytes = new byte[nameLength];
                            Array.Copy(settingsBytes, i + 2, nameBytes, 0, nameLength);
                            channelName = Encoding.UTF8.GetString(nameBytes).Trim();
                            break;
                        }
                    }
                }
            }

            // Fallback: Versuche channel.Settings.Name (kann korrupt sein)
            if (string.IsNullOrEmpty(channelName) && channel.Settings != null && !string.IsNullOrEmpty(channel.Settings.Name))
            {
                var rawName = channel.Settings.Name;
                bool isValid = true;
                foreach (char c in rawName)
                {
                    if (c < 32 && c != '\n' && c != '\r' && c != '\t')
                    {
                        isValid = false;
                        break;
                    }
                }
                if (isValid && !rawName.Contains('\uFFFD'))
                {
                    channelName = rawName.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"  ERROR parsing channel name: {ex.Message}");
        }

        // Fallback: Verwende Preset-Name für PRIMARY Channel ohne Namen
        if (string.IsNullOrEmpty(channelName) && channel.Role == ChannelRole.Primary && _currentLoRaConfig != null)
        {
            channelName = _currentLoRaConfig.ModemPreset.ToString().Replace("_", " ");
        }

        if (string.IsNullOrEmpty(channelName))
        {
            channelName = $"Channel {channel.Index}";
        }

        return channelName;
    }

    /// <summary>
    /// Entfernt ANSI escape sequences (z.B. \x1B[34m, \x1B[0m) aus einem String.
    /// </summary>
    private static string StripAnsiCodes(string input)
    {
        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '\x1B' && i + 1 < input.Length && input[i + 1] == '[')
            {
                // Skip bis zum Terminierungszeichen (Buchstabe)
                i += 2;
                while (i < input.Length && !char.IsLetter(input[i]))
                {
                    i++;
                }
                // Das Terminierungszeichen selbst wird auch übersprungen
                continue;
            }
            sb.Append(input[i]);
        }
        return sb.ToString().Trim();
    }

    private static string ToHexString(byte[] data)
    {
        if (data == null || data.Length == 0)
            return "";

        var sb = new StringBuilder(data.Length * 3);
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0 && i % 16 == 0)
                sb.Append("\n    ");
            else if (i > 0)
                sb.Append(" ");
            sb.Append(data[i].ToString("X2"));
        }
        return sb.ToString();
    }
}
