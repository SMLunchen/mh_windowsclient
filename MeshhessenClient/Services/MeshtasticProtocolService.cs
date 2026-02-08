using System.Text;
using Google.Protobuf;
using Meshtastic.Protobufs;
using MeshhessenClient.Models;
using ProtoNodeInfo = Meshtastic.Protobufs.NodeInfo;
using ModelNodeInfo = MeshhessenClient.Models.NodeInfo;

namespace MeshhessenClient.Services;

public class MeshtasticProtocolService
{
    private readonly SerialPortService _serialPort;
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

    public event EventHandler<MessageItem>? MessageReceived;
    public event EventHandler<ModelNodeInfo>? NodeInfoReceived;
    public event EventHandler<ChannelInfo>? ChannelInfoReceived;
    public event EventHandler<LoRaConfig>? LoRaConfigReceived;
    public event EventHandler<DeviceInfo>? DeviceInfoReceived;

    private const byte PACKET_START_BYTE_1 = 0x94;
    private const byte PACKET_START_BYTE_2 = 0xC3;

    public MeshtasticProtocolService(SerialPortService serialPort)
    {
        _serialPort = serialPort;
        _serialPort.DataReceived += OnDataReceived;
    }

    public async Task InitializeAsync()
    {
        Logger.WriteLine("=== Initializing ===");

        lock (_dataLock)
        {
            _isInitializing = true;
            _isDisconnecting = false;
        }

        await Task.Delay(1000);

        if (_isDisconnecting)
        {
            return;
        }

        // Sende Wakeup-Sequenz
        byte[] wakeup = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            wakeup[i] = PACKET_START_BYTE_2; // 0xC3
        }
        await _serialPort.WriteAsync(wakeup);
        await Task.Delay(100);

        if (_isDisconnecting)
        {
            return;
        }

        // Fordere Config an
        await RequestConfigAsync();

        // Warte auf config_complete
        for (int i = 0; i < 100; i++)
        {
            bool isComplete;
            lock (_dataLock)
            {
                isComplete = _configComplete;
            }

            if (isComplete)
            {
                break;
            }
            await Task.Delay(100);
        }

        // Warte auf weitere Daten
        await Task.Delay(2000);

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
            var channelInfo = new ChannelInfo
            {
                Index = channel.Index,
                Name = channel.Settings?.Name ?? $"Channel {channel.Index}",
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

        // Device sendet keine Channels automatisch - fordere sie manuell an
        if (channelCount == 0 && _myNodeId != 0)
        {
            Logger.WriteLine("Requesting channels manually...");
            for (int i = 0; i < 8; i++)
            {
                if (_isDisconnecting) break;
                await RequestChannelAsync(i);
                await Task.Delay(300);
            }
        }
    }

    private void OnDataReceived(object? sender, byte[] data)
    {
        lock (_receiveBuffer)
        {
            _receiveBuffer.AddRange(data);
            ProcessBuffer();
        }
    }

    private void ProcessBuffer()
    {
        try
        {
            while (_receiveBuffer.Count >= 4)
            {
                int startIndex = FindPacketStart();
                if (startIndex == -1)
                {
                    _receiveBuffer.Clear();
                    break;
                }

                if (startIndex > 0)
                {
                    _receiveBuffer.RemoveRange(0, startIndex);
                }

                if (_receiveBuffer.Count < 4)
                {
                    break;
                }

                int packetLength = (_receiveBuffer[2] << 8) | _receiveBuffer[3];

                if (_receiveBuffer.Count < 4 + packetLength)
                {
                    break;
                }

                byte[] packet = _receiveBuffer.GetRange(4, packetLength).ToArray();
                _receiveBuffer.RemoveRange(0, 4 + packetLength);

                try
                {
                    ProcessPacket(packet);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"ERROR: Packet processing failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"FATAL: ProcessBuffer crashed: {ex.Message}");
            _receiveBuffer.Clear();
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
            Logger.WriteLine($"Received FromRadio packet, type: {fromRadio.PayloadVariantCase}");
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
                        // User-Daten werden später aus NodeInfo ergänzt
                    };
                }
                break;

            case FromRadio.PayloadVariantOneofCase.NodeInfo:
                HandleNodeInfo(fromRadio.NodeInfo);
                break;

            case FromRadio.PayloadVariantOneofCase.Channel:
                Logger.WriteLine($"Channel packet received: Index={fromRadio.Channel.Index}");
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
                Channel = packet.Channel.ToString(),
                IsEncrypted = true
            };
            MessageReceived?.Invoke(this, messageItem);
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
                Channel = packet.Channel.ToString()
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
                Name = protoNodeInfo.User?.LongName ?? protoNodeInfo.User?.ShortName ?? $"Node-{protoNodeInfo.Num:x4}",
                Snr = protoNodeInfo.Snr.ToString("F1"),
                LastSeen = protoNodeInfo.LastHeard > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(protoNodeInfo.LastHeard).LocalDateTime.ToString("HH:mm:ss")
                    : DateTime.Now.ToString("HH:mm:ss")
            };

            if (protoNodeInfo.Position != null)
            {
                nodeInfo.Latitude = protoNodeInfo.Position.LatitudeI / 1e7;
                nodeInfo.Longitude = protoNodeInfo.Position.LongitudeI / 1e7;
                nodeInfo.Altitude = protoNodeInfo.Position.Altitude;
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
                Snr = packet.RxSnr.ToString("F1"),
                LastSeen = DateTime.Now.ToString("HH:mm:ss")
            };

            bool shouldFireEvent;
            lock (_dataLock)
            {
                _knownNodes[packet.From] = nodeInfo;
                shouldFireEvent = !_isInitializing;
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

            lock (_dataLock)
            {
                if (_knownNodes.TryGetValue(packet.From, out var nodeInfo))
                {
                    nodeInfo.Latitude = position.LatitudeI / 1e7;
                    nodeInfo.Longitude = position.LongitudeI / 1e7;
                    nodeInfo.Altitude = position.Altitude;
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

            if (shouldFireEvent && channel.Role != ChannelRole.Disabled)
            {
                var channelInfo = new ChannelInfo
                {
                    Index = channel.Index,
                    Name = string.IsNullOrWhiteSpace(channel.Settings?.Name) ? $"Channel {channel.Index}" : channel.Settings.Name,
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
                WantAck = false
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

        byte[] frame = new byte[4 + protoData.Length];
        frame[0] = PACKET_START_BYTE_1;
        frame[1] = PACKET_START_BYTE_2;
        frame[2] = (byte)(protoData.Length >> 8);
        frame[3] = (byte)(protoData.Length & 0xFF);
        Array.Copy(protoData, 0, frame, 4, protoData.Length);

        await _serialPort.WriteAsync(frame);
    }

    private async Task RequestConfigAsync()
    {
        var toRadio = new ToRadio
        {
            WantConfigId = (uint)Random.Shared.Next()
        };

        await SendToRadioAsync(toRadio);
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

        await SendToRadioAsync(toRadio);
    }

    public void Disconnect()
    {
        lock (_dataLock)
        {
            _isDisconnecting = true;
            _isInitializing = false;
        }

        // Unsubscribe vom Serial Port Event
        _serialPort.DataReceived -= OnDataReceived;
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
                    // get_channel_response ist direkt ein Channel
                    var channel = adminMsg.GetChannelResponse;

                    // WORKAROUND: Parse Name manuell aus den RAW Bytes
                    // Das Device sendet den Namen bei Tag 3 als String (nicht bei Tag 2!)
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
                                        channelName = System.Text.Encoding.UTF8.GetString(nameBytes).Trim();
                                        Logger.WriteLine($"  DEBUG Channel {channel.Index}: Found name at tag 3: '{channelName}'");
                                        break;
                                    }
                                }
                            }
                        }

                        // Fallback: Versuche channel.Settings.Name (kann korrupt sein)
                        if (string.IsNullOrEmpty(channelName) && channel.Settings != null && !string.IsNullOrEmpty(channel.Settings.Name))
                        {
                            var rawName = channel.Settings.Name;

                            // Validierung
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
                                Logger.WriteLine($"  DEBUG Channel {channel.Index}: Using tag 2 name: '{channelName}'");
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
                        Logger.WriteLine($"  DEBUG Channel {channel.Index}: Using preset name '{channelName}'");
                    }

                    string debugName = string.IsNullOrEmpty(channelName) ? "empty" : channelName;
                    Logger.WriteLine($"  Channel response: Index={channel.Index}, Role={channel.Role}, Name='{debugName}'");

                    bool shouldFireChannelEvent;
                    lock (_dataLock)
                    {
                        var existing = _tempChannels.FirstOrDefault(c => c.Index == channel.Index);
                        if (existing != null)
                        {
                            _tempChannels.Remove(existing);
                        }
                        _tempChannels.Add(channel);
                        shouldFireChannelEvent = !_isInitializing;
                    }

                    if (shouldFireChannelEvent)
                    {
                        // Erstelle ChannelInfo nur für gültige Channels (nicht DISABLED)
                        if (channel.Role != ChannelRole.Disabled)
                        {
                            var channelInfo = new ChannelInfo
                            {
                                Index = channel.Index,
                                Name = string.IsNullOrEmpty(channelName) ? $"Channel {channel.Index}" : channelName,
                                Role = channel.Role.ToString(),
                                Psk = channel.Settings?.Psk != null && channel.Settings.Psk.Length > 0
                                    ? Convert.ToBase64String(channel.Settings.Psk.ToByteArray())
                                    : ""
                            };
                            ChannelInfoReceived?.Invoke(this, channelInfo);
                        }
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
}
