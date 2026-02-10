using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace MeshhessenClient.Services;

/// <summary>
/// Bluetooth LE connection implementation for Meshtastic devices
/// </summary>
public class BluetoothConnectionService : IConnectionService
{
    private BluetoothLEDevice? _device;
    private GattCharacteristic? _toRadioChar;
    private GattCharacteristic? _fromRadioChar;
    private GattCharacteristic? _fromNumChar;
    private bool _isConnected;
    private readonly object _lock = new();
    private string _displayName = string.Empty;
    private System.Threading.Thread? _readerThread;
    private bool _wantExit = false;

    // Debug logging control
    private static bool _debugEnabled = false;
    public static void SetDebugEnabled(bool enabled) => _debugEnabled = enabled;
    private static void LogDebug(string message)
    {
        if (_debugEnabled)
        {
            Logger.WriteLine(message);
        }
    }

    // Meshtastic BLE UUIDs (from Python/Web clients)
    private static readonly Guid ServiceUuid = Guid.Parse("6ba1b218-15a8-461f-9fa8-5dcae273eafd");
    private static readonly Guid ToRadioUuid = Guid.Parse("f75c76d2-129e-4dad-a1dd-7866124401e7");
    private static readonly Guid FromRadioUuid = Guid.Parse("2c55e69e-4993-11ed-b878-0242ac120002");
    private static readonly Guid FromNumUuid = Guid.Parse("ed9da18c-a800-4f66-a670-aa7547e34453");

    public ConnectionType Type => ConnectionType.Bluetooth;
    public string DisplayName => _displayName;

    public bool IsConnected
    {
        get
        {
            lock (_lock) { return _isConnected; }
        }
        private set
        {
            lock (_lock)
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    ConnectionStateChanged?.Invoke(this, value);
                }
            }
        }
    }

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<byte[]>? DataReceived;

    public async Task ConnectAsync(ConnectionParameters parameters)
    {
        if (parameters is not BluetoothConnectionParameters btParams)
            throw new ArgumentException("Invalid connection parameters for Bluetooth connection", nameof(parameters));

        if (btParams.DeviceAddress == 0)
            throw new ArgumentException("Device address cannot be zero", nameof(parameters));

        _displayName = btParams.DeviceName;

        try
        {
            LogDebug($"[BLE] Connecting to {btParams.DeviceName} ({btParams.DeviceAddress:X})...");

            // Connect to BLE device
            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(btParams.DeviceAddress);
            if (_device == null)
                throw new Exception("Failed to connect to Bluetooth device");

            // Subscribe to connection status changes
            _device.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;

            LogDebug($"[BLE] Device connected, checking pairing status...");

            // Check if device is paired, if not, request pairing
            if (_device.DeviceInformation.Pairing.IsPaired)
            {
                LogDebug($"[BLE] Device is already paired");
            }
            else
            {
                LogDebug($"[BLE] Device is not paired, requesting pairing...");
                var pairingResult = await _device.DeviceInformation.Pairing.PairAsync();

                if (pairingResult.Status == Windows.Devices.Enumeration.DevicePairingResultStatus.Paired)
                {
                    LogDebug($"[BLE] Device paired successfully");
                }
                else if (pairingResult.Status == Windows.Devices.Enumeration.DevicePairingResultStatus.AlreadyPaired)
                {
                    LogDebug($"[BLE] Device was already paired");
                }
                else
                {
                    throw new Exception($"Pairing failed: {pairingResult.Status}");
                }
            }

            LogDebug($"[BLE] Discovering services...");

            // Get Meshtastic service
            var serviceResult = await _device.GetGattServicesForUuidAsync(ServiceUuid, Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
            if (serviceResult.Status != GattCommunicationStatus.Success || !serviceResult.Services.Any())
            {
                throw new Exception($"Meshtastic service not found (status: {serviceResult.Status})");
            }

            var service = serviceResult.Services.First();
            LogDebug($"[BLE] Found Meshtastic service");

            // Maintain persistent GATT session for better reliability
            // Note: UWP doesn't allow explicit MTU setting, but maintaining the session
            // helps Windows negotiate optimal parameters automatically
            try
            {
                var session = await Windows.Devices.Bluetooth.GenericAttributeProfile.GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId);
                if (session != null)
                {
                    session.MaintainConnection = true;
                    LogDebug($"[BLE] GATT Session established. Current MaxPduSize: {session.MaxPduSize}");
                    LogDebug($"[BLE] Note: Meshtastic recommends MTU 512, but UWP negotiates this automatically");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"[BLE] WARNING: Could not establish GATT session: {ex.Message}");
                LogDebug($"[BLE] Continuing anyway...");
            }

            // Get characteristics
            var toRadioResult = await service.GetCharacteristicsForUuidAsync(ToRadioUuid, Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
            var fromRadioResult = await service.GetCharacteristicsForUuidAsync(FromRadioUuid, Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
            var fromNumResult = await service.GetCharacteristicsForUuidAsync(FromNumUuid, Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);

            if (toRadioResult.Status != GattCommunicationStatus.Success || !toRadioResult.Characteristics.Any())
                throw new Exception($"TORADIO characteristic not found (status: {toRadioResult.Status})");
            if (fromRadioResult.Status != GattCommunicationStatus.Success || !fromRadioResult.Characteristics.Any())
                throw new Exception($"FROMRADIO characteristic not found (status: {fromRadioResult.Status})");
            if (fromNumResult.Status != GattCommunicationStatus.Success || !fromNumResult.Characteristics.Any())
                throw new Exception($"FROMNUM characteristic not found (status: {fromNumResult.Status})");

            _toRadioChar = toRadioResult.Characteristics.First();
            _fromRadioChar = fromRadioResult.Characteristics.First();
            _fromNumChar = fromNumResult.Characteristics.First();

            LogDebug($"[BLE] All characteristics found");

            // Check characteristic properties
            LogDebug($"[BLE] TORADIO properties: {_toRadioChar.CharacteristicProperties}");
            LogDebug($"[BLE] FROMRADIO properties: {_fromRadioChar.CharacteristicProperties}");
            LogDebug($"[BLE] FROMNUM properties: {_fromNumChar.CharacteristicProperties}");

            // Subscribe to FROMNUM notifications (NOT FROMRADIO!)
            // FROMNUM notifies when new data is available in FROMRADIO
            bool notificationsEnabled = false;
            if (_fromNumChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
            {
                try
                {
                    LogDebug($"[BLE] Subscribing to FROMNUM notifications...");
                    var cccdResult = await _fromNumChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);

                    if (cccdResult == GattCommunicationStatus.Success)
                    {
                        // Attach notification handler (but we don't rely on it - polling is primary method)
                        _fromNumChar.ValueChanged += OnFromNumValueChanged;
                        LogDebug($"[BLE] Successfully subscribed to FROMNUM notifications (not relying on it)");
                        notificationsEnabled = true;
                    }
                    else
                    {
                        LogDebug($"[BLE] WARNING: Failed to subscribe to FROMNUM: {cccdResult}");
                        LogDebug($"[BLE] Falling back to polling mode");
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"[BLE] WARNING: Exception subscribing to FROMNUM: {ex.Message}");
                    LogDebug($"[BLE] Falling back to polling mode");
                }
            }
            else
            {
                LogDebug($"[BLE] FROMNUM does not support notifications, using polling mode");
            }

            // EXPERIMENTAL: Try subscribing to FROMRADIO as well (even though firmware says NOTIFY is "broken")
            // Maybe it works on UWP or on newer firmware?
            if (_fromRadioChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
            {
                try
                {
                    LogDebug($"[BLE] EXPERIMENTAL: Attempting to subscribe to FROMRADIO notifications...");
                    var cccdResult = await _fromRadioChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);

                    if (cccdResult == GattCommunicationStatus.Success)
                    {
                        _fromRadioChar.ValueChanged += OnFromRadioValueChanged;
                        LogDebug($"[BLE] EXPERIMENTAL: Successfully subscribed to FROMRADIO notifications!");
                    }
                    else
                    {
                        LogDebug($"[BLE] EXPERIMENTAL: Failed to subscribe to FROMRADIO: {cccdResult}");
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"[BLE] EXPERIMENTAL: Exception subscribing to FROMRADIO: {ex.Message}");
                }
            }
            else
            {
                LogDebug($"[BLE] FROMRADIO does not support notifications (expected)");
            }

            // Prime the read once in case data is already queued (like Web Bluetooth does)
            LogDebug($"[BLE] Performing initial read to check for queued data...");
            await ReadAvailablePacketsAsync();

            // CRITICAL FIX: Python client uses AGGRESSIVE POLLING, NO notifications!
            // Windows BLE notifications appear to be unreliable - use polling like Python does
            LogDebug($"[BLE] Starting AGGRESSIVE polling thread (1ms interval) like Python client...");
            _wantExit = false;
            _readerThread = new System.Threading.Thread(ReaderThreadFunc)
            {
                IsBackground = true,
                Name = "BleReader"
            };
            _readerThread.Start();

            IsConnected = true;
            LogDebug($"[BLE] Connected to {btParams.DeviceName}, notifications: {notificationsEnabled}");
        }
        catch (Exception ex)
        {
            LogDebug($"[BLE] ERROR: Connection failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
            {
                LogDebug($"[BLE] Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            LogDebug($"[BLE] Stack trace: {ex.StackTrace}");
            Disconnect();
            throw;
        }
    }

    private void OnDeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Logger.WriteLine("[BLE] Device disconnected");
            Disconnect();
        }
    }

    // Handle FROMNUM notifications (tells us data is available in FROMRADIO)
    private void OnFromNumValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        LogDebug($"[BLE] !!!!! FROMNUM NOTIFICATION RECEIVED !!!!!");
        try
        {
            using var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var length = reader.UnconsumedBufferLength;
            LogDebug($"[BLE] FROMNUM: notification buffer has {length} bytes");

            if (length >= 4)
            {
                var data = new byte[length];
                reader.ReadBytes(data);
                // FROMNUM contains a uint32 telling us how many packets are available
                uint packetCount = BitConverter.ToUInt32(data, 0);
                LogDebug($"[BLE] FROMNUM: {packetCount} packets available - triggering FROMRADIO read");

                // Now read from FROMRADIO repeatedly
                Task.Run(async () => await ReadAvailablePacketsAsync());
            }
            else if (length > 0)
            {
                var data = new byte[length];
                reader.ReadBytes(data);
                LogDebug($"[BLE] WARNING: FROMNUM notification with unexpected length: {length} bytes, data: {BitConverter.ToString(data)}");
            }
            else
            {
                LogDebug($"[BLE] WARNING: FROMNUM notification with ZERO bytes");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"[BLE] ERROR in FROMNUM handler: {ex.GetType().Name}: {ex.Message}");
            LogDebug($"[BLE] Stack: {ex.StackTrace}");
        }
    }

    // EXPERIMENTAL: Handle FROMRADIO notifications (firmware says this is "broken" but let's try)
    private void OnFromRadioValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        LogDebug($"[BLE] !!!!! FROMRADIO NOTIFICATION RECEIVED (UNEXPECTED!) !!!!!");
        try
        {
            using var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var length = reader.UnconsumedBufferLength;
            LogDebug($"[BLE] FROMRADIO notification: {length} bytes");

            if (length > 0)
            {
                var data = new byte[length];
                reader.ReadBytes(data);
                LogDebug($"[BLE] FROMRADIO notification data: {BitConverter.ToString(data)}");

                // Fire DataReceived event
                try
                {
                    DataReceived?.Invoke(this, data);
                }
                catch (Exception ex)
                {
                    LogDebug($"[BLE] ERROR: DataReceived event failed: {ex.Message}");
                }
            }
            else
            {
                LogDebug($"[BLE] FROMRADIO notification with ZERO bytes");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"[BLE] ERROR in FROMRADIO handler: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Read all available packets from FROMRADIO
    private async Task ReadAvailablePacketsAsync()
    {
        try
        {
            if (_fromRadioChar == null || !IsConnected)
                return;

            // Read until we get an empty response
            while (true)
            {
                // CRITICAL: Use Uncached mode to force actual BLE read from device!
                var readResult = await _fromRadioChar.ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
                if (readResult.Status != GattCommunicationStatus.Success)
                {
                    LogDebug($"[BLE] ReadAvailablePackets: Read failed: {readResult.Status}");
                    break;
                }

                using var reader = DataReader.FromBuffer(readResult.Value);
                var length = reader.UnconsumedBufferLength;

                if (length == 0)
                {
                    // Empty response means no more data
                    LogDebug($"[BLE] ReadAvailablePackets: No more data");
                    break;
                }

                var data = new byte[length];
                reader.ReadBytes(data);
                LogDebug($"[BLE] ReadAvailablePackets: Read {length} bytes: {BitConverter.ToString(data)}");

                // Fire DataReceived event
                try
                {
                    DataReceived?.Invoke(this, data);
                }
                catch (Exception ex)
                {
                    LogDebug($"[BLE] ERROR: DataReceived event failed: {ex.Message}");
                }

                // Small delay between reads
                await Task.Delay(100);
            }
        }
        catch (Exception ex)
        {
            LogDebug($"[BLE] ERROR in ReadAvailablePackets: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Reader thread - continuously polls FROMRADIO for data (like Python client)
    private void ReaderThreadFunc()
    {
        try
        {
            Logger.WriteLine("[BLE] Reader thread started");
            int readCount = 0;
            int emptyReadCount = 0;
            int dataPackets = 0;
            bool detailedLogging = true; // Detailed logging for first 10 reads

            while (!_wantExit)
            {
                try
                {
                    if (_fromRadioChar == null || !IsConnected)
                    {
                        LogDebug($"[BLE] Reader thread stopping: _fromRadioChar={_fromRadioChar != null}, IsConnected={IsConnected}");
                        break;
                    }

                    readCount++;

                    // Disable detailed logging after first 10 reads
                    if (readCount == 11)
                    {
                        LogDebug($"[BLE] Switching to periodic logging (every 100 reads or when data received)");
                        detailedLogging = false;
                    }

                    // Periodic status update every 100 reads
                    if (readCount % 100 == 0)
                    {
                        LogDebug($"[BLE] Reader alive: {readCount} reads, {dataPackets} packets received, {emptyReadCount} empty");
                    }

                    // Detailed logging for first 10 reads
                    if (detailedLogging)
                    {
                        LogDebug($"[BLE] Read #{readCount}: Starting ReadValueAsync...");
                    }

                    // Read from FROMRADIO characteristic
                    // CRITICAL: Use Uncached mode to force actual BLE read from device, not cache!
                    var readTask = _fromRadioChar.ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached).AsTask();
                    readTask.Wait();
                    var readResult = readTask.Result;

                    if (detailedLogging)
                    {
                        LogDebug($"[BLE] Read #{readCount}: Status={readResult.Status}");
                    }

                    if (readResult.Status == GattCommunicationStatus.Success)
                    {
                        using var reader = DataReader.FromBuffer(readResult.Value);
                        var length = reader.UnconsumedBufferLength;

                        if (length > 0)
                        {
                            dataPackets++;
                            var data = new byte[length];
                            reader.ReadBytes(data);
                            LogDebug($"[BLE] Received {length} bytes (packet #{dataPackets}): {BitConverter.ToString(data)}");

                            // Fire DataReceived event
                            try
                            {
                                DataReceived?.Invoke(this, data);
                            }
                            catch (Exception ex)
                            {
                                LogDebug($"[BLE] ERROR: DataReceived event failed: {ex.Message}");
                            }

                            // If we got data, read again immediately (there might be more)
                            continue;
                        }
                        else
                        {
                            emptyReadCount++;
                            if (detailedLogging)
                            {
                                LogDebug($"[BLE] Read #{readCount}: Empty (0 bytes)");
                            }
                        }
                    }
                    else
                    {
                        LogDebug($"[BLE] Read #{readCount}: FAILED with status {readResult.Status}");
                        // Don't break on single read failure, might be temporary
                    }

                    // No data available, wait a SHORT time before next poll
                    // Firmware's onRead handler blocks up to 20s waiting for data
                    // Aggressive polling like Python client (no reliance on notifications)
                    if (detailedLogging)
                    {
                        LogDebug($"[BLE] Read #{readCount}: Sleeping 1ms...");
                    }
                    System.Threading.Thread.Sleep(1);  // Aggressive polling like Python - 1ms interval
                }
                catch (AggregateException ae) when (ae.InnerException is System.IO.IOException)
                {
                    LogDebug($"[BLE] CRITICAL: I/O error (device disconnected?): {ae.InnerException.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    if (!_wantExit)
                    {
                        LogDebug($"[BLE] CRITICAL: Reader thread exception: {ex.GetType().Name}: {ex.Message}");
                        LogDebug($"[BLE] Stack trace: {ex.StackTrace}");
                        if (ex.InnerException != null)
                        {
                            LogDebug($"[BLE] Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                        }
                    }
                    break;
                }
            }

            LogDebug($"[BLE] Reader thread stopped (wantExit={_wantExit}, readCount={readCount}, dataPackets={dataPackets}, emptyReadCount={emptyReadCount})");

            // If thread is stopping unexpectedly, trigger disconnect
            if (!_wantExit && IsConnected)
            {
                Logger.WriteLine("[BLE] WARNING: Reader thread stopped unexpectedly, forcing disconnect");
                Task.Run(() => Disconnect());
            }
        }
        catch (Exception ex)
        {
            LogDebug($"[BLE] FATAL: Reader thread crashed: {ex.GetType().Name}: {ex.Message}");
            LogDebug($"[BLE] Stack trace: {ex.StackTrace}");
        }
    }

    public async Task WriteAsync(byte[] data)
    {
        if (_toRadioChar == null || !IsConnected)
        {
            LogDebug($"[BLE] Write FAILED: toRadioChar={_toRadioChar != null}, IsConnected={IsConnected}");
            throw new InvalidOperationException("Bluetooth device is not connected");
        }

        try
        {
            LogDebug($"[BLE] Writing {data.Length} bytes: {BitConverter.ToString(data)}");

            using var writer = new DataWriter();
            writer.WriteBytes(data);

            // Try WriteWithoutResponse first - some devices handle this better
            GattCommunicationStatus status;
            if (_toRadioChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
            {
                LogDebug($"[BLE] Using WriteValueAsync (WriteWithoutResponse)...");
                status = await _toRadioChar.WriteValueAsync(writer.DetachBuffer());
                LogDebug($"[BLE] Write result: Status={status}");
            }
            else if (_toRadioChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write))
            {
                LogDebug($"[BLE] Using WriteValueWithResultAsync (WriteWithResponse)...");
                var result = await _toRadioChar.WriteValueWithResultAsync(writer.DetachBuffer());
                status = result.Status;
                LogDebug($"[BLE] Write result: Status={status}");

                if (result.Status != GattCommunicationStatus.Success && result.ProtocolError.HasValue)
                {
                    LogDebug($"[BLE] Protocol Error: {result.ProtocolError.Value}");
                }
            }
            else
            {
                throw new Exception($"TORADIO characteristic doesn't support writing! Properties: {_toRadioChar.CharacteristicProperties}");
            }

            if (status != GattCommunicationStatus.Success)
            {
                var errorMsg = $"Write failed: {status}";
                LogDebug($"[BLE] ERROR: {errorMsg}");
                throw new Exception(errorMsg);
            }

            LogDebug($"[BLE] Write successful, immediately reading response...");
            // Immediately read response after write (like Web Bluetooth does)
            // This ensures we read any response the device queues
            await ReadAvailablePacketsAsync();
            LogDebug($"[BLE] Write complete");
        }
        catch (Exception ex)
        {
            var errorDetails = $"[BLE] ERROR writing data: Type={ex.GetType().Name}, Message={ex.Message}";
            if (ex.InnerException != null)
                errorDetails += $", InnerException={ex.InnerException.Message}";
            Logger.WriteLine(errorDetails);
            LogDebug($"[BLE] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    public void Disconnect()
    {
        try
        {
            Logger.WriteLine("[BLE] Disconnecting...");

            // Stop reader thread first
            _wantExit = true;

            if (_readerThread != null && _readerThread.IsAlive)
            {
                if (!_readerThread.Join(3000))
                {
                    Logger.WriteLine("[BLE] WARNING: Reader thread did not stop");
                }
            }

            // Unsubscribe from notifications
            if (_fromNumChar != null)
            {
                _fromNumChar.ValueChanged -= OnFromNumValueChanged;
            }

            _fromNumChar = null;
            _toRadioChar = null;
            _fromRadioChar = null;

            if (_device != null)
            {
                _device.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                _device.Dispose();
                _device = null;
            }

            _readerThread = null;
            Logger.WriteLine("[BLE] Disconnected");
        }
        catch (Exception ex)
        {
            LogDebug($"[BLE] ERROR during disconnect: {ex.Message}");
        }
        finally
        {
            IsConnected = false;
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
