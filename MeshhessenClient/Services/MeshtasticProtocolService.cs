using System.Text;
using Google.Protobuf;
using Meshtastic.Protobufs;
using MeshhessenClient.Models;
using ProtoNodeInfo = Meshtastic.Protobufs.NodeInfo;
using ModelNodeInfo = MeshhessenClient.Models.NodeInfo;

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
    public event EventHandler<DeviceInfo>? DeviceInfoReceived;
    public event EventHandler<int>? PacketCountChanged;

    private const byte PACKET_START_BYTE_1 = 0x94;
    private const byte PACKET_START_BYTE_2 = 0xC3;

    public MeshtasticProtocolService(IConnectionService connectionService)
    {
        _connectionService = connectionService;
        _connectionService.DataReceived += OnDataReceived;
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
                Psk = channel.Settings?.Psk != null ? Convert.ToBase64String(channel.Settings.Psk.ToByteArray()) : "",
                Uplink = channel.Settings?.UplinkEnabled ?? 0,
                Downlink = channel.Settings?.DownlinkEnabled ?? 0
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

            case FromRadio.PayloadVariantOneofCase.ChannelsCompleteId:
                Logger.WriteLine("ChannelsCompleteId received");
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

            case FromRadio.PayloadVariantOneofCase.ModuleconfigCompleteId:
                lock (_dataLock)
                {
                    _configComplete = true;
                }
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
            }
        }
        else if (packet.PayloadVariantCase == MeshPacket.PayloadVariantOneofCase.Encrypted)
        {
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
                Message = "[Verschlüsselte Nachricht - PSK erforderlich]",
                Channel = FormatChannelDisplay(packet.Channel),
                IsEncrypted = true,
                IsViaMqtt = packet.ViaMqtt
            };
            MessageReceived?.Invoke(this, messageItem);
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
            string messageText = Encoding.UTF8.GetString(data.Payload.ToByteArray());

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
                    Logger.WriteLine($"  Node {nodeInfo.Id}: Position vorhanden aber LatI=LonI=0 (kein GPS-Fix)");
                }
            }
            else
            {
                Logger.WriteLine($"  Node {nodeInfo.Id}: Keine Positionsdaten");
            }

            if (protoNodeInfo.DeviceMetrics != null)
            {
                nodeInfo.Battery = $"{protoNodeInfo.DeviceMetrics.BatteryLevel}%";
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

            var nodeInfo = new ModelNodeInfo
            {
                NodeId = packet.From,
                Id = $"!{packet.From:x8}",
                Name = user.LongName ?? user.ShortName ?? $"Node-{packet.From:x4}",
                ShortName = user.ShortName ?? "",
                LongName = user.LongName ?? "",
                Snr = packet.RxSnr.ToString("F1"),
                Rssi = packet.RxRssi.ToString(),
                LastSeen = DateTime.Now.ToString("HH:mm:ss")
            };

            // Update DeviceInfo if this is our own node
            if (packet.From == _myNodeId && _myDeviceInfo != null)
            {
                lock (_dataLock)
                {
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

            Logger.WriteLine($"Position-Packet von !{packet.From:x8}: LatI={position.LatitudeI}, LonI={position.LongitudeI}, Alt={position.Altitude}");

            lock (_dataLock)
            {
                if (_knownNodes.TryGetValue(packet.From, out var nodeInfo))
                {
                    if (position.LatitudeI != 0 || position.LongitudeI != 0)
                    {
                        nodeInfo.Latitude = position.LatitudeI / 1e7;
                        nodeInfo.Longitude = position.LongitudeI / 1e7;
                        nodeInfo.Altitude = position.Altitude;
                        Logger.WriteLine($"  Position aktualisiert: lat={nodeInfo.Latitude:F6}, lon={nodeInfo.Longitude:F6}");
                    }
                    else
                    {
                        Logger.WriteLine($"  Position ignoriert (LatI=LonI=0, kein GPS-Fix)");
                    }
                    nodeInfo.LastSeen = DateTime.Now.ToString("HH:mm:ss");
                    nodeToFire = nodeInfo;
                    shouldFireEvent = !_isInitializing;
                }
                else
                {
                    Logger.WriteLine($"  Node !{packet.From:x8} unbekannt, Position verworfen");
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

            lock (_dataLock)
            {
                if (_knownNodes.TryGetValue(packet.From, out var nodeInfo))
                {
                    nodeInfo.LastSeen = DateTime.Now.ToString("HH:mm:ss");
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
                        Uplink = channel.Settings?.UplinkEnabled ?? 0,
                        Downlink = channel.Settings?.DownlinkEnabled ?? 0
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

    public async Task SendTextMessageAsync(string text, uint destinationId = 0xFFFFFFFF, uint channel = 0)
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
                    Payload = ByteString.CopyFromUtf8(text)
                },
                Id = (uint)Random.Shared.Next(),
                WantAck = false,
                HopLimit = 7,
                HopStart = 0
            };

            var toRadio = new ToRadio
            {
                Packet = meshPacket
            };

            await SendToRadioAsync(toRadio);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error sending text message: {ex.Message}");
            throw;
        }
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

                case AdminMessage.PayloadVariantOneofCase.GetConfigResponse:
                    var config = adminMsg.GetConfigResponse;

                    if (config.PayloadVariantCase == Config.PayloadVariantOneofCase.Lora)
                    {
                        var loraConfig = config.Lora;

                        bool shouldFireLoRaEvent;
                        lock (_dataLock)
                        {
                            _currentLoRaConfig = loraConfig;
                            shouldFireLoRaEvent = !_isInitializing;
                        }

                        if (shouldFireLoRaEvent)
                        {
                            LoRaConfigReceived?.Invoke(this, loraConfig);
                        }
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
