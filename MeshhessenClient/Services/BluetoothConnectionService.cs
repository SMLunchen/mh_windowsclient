using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation.Metadata;
using Windows.Storage.Streams;

namespace MeshhessenClient.Services;

/// <summary>
/// Bluetooth LE connection to a Meshtastic node (WinRT GATT).
///
/// Reading follows the firmware's contract, the way Android and the Python CLI do it: FROMNUM notifies when the node
/// has queued something, then we read FROMRADIO until it returns an empty packet; after every write we read as well
/// (config requests and admin messages are answered on FROMRADIO); a slow safety poll covers a lost notification. One
/// reader (<see cref="BleReadPump"/>) — not the 1 ms polling loop plus two side readers of earlier versions.
///
/// Connection interval: the fastest Windows offers while config and node list load, released once the client's
/// initialisation is complete so the firmware's own preference (30–50 ms, latency 2) can take over. The "gentle" mode
/// (user setting) asks for Balanced from the start instead.
///
/// Pairing: this class never removes a Windows bond. When the stored bond no longer fits the node it throws
/// <see cref="BleStaleBondException"/> and the window asks the user; an unreachable node is
/// <see cref="BleUnreachableException"/> — retry material, nothing else.
/// </summary>
public class BluetoothConnectionService : IConnectionService
{
    private BluetoothLEDevice? _device;
    private GattSession? _gattSession;
    private BluetoothLEPreferredConnectionParametersRequest? _connParamsRequest;
    private GattDeviceService? _service;
    private GattCharacteristic? _toRadioChar;
    private GattCharacteristic? _fromRadioChar;
    private GattCharacteristic? _fromNumChar;
    private GattCharacteristic? _logRadioChar;
    private bool _isConnected;
    private readonly object _lock = new();
    private string _displayName = string.Empty;

    private BleReadPump? _pump;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private Timer? _steadyFallbackTimer;
    private Timer? _statsLogTimer;
    private readonly BleLinkStats _stats = new();

    /// <summary>Safety poll when FROMNUM notifications are subscribed — one empty read every 1.5 s costs nothing and
    /// catches a notification Windows swallowed.</summary>
    public static readonly TimeSpan PollIntervalWithNotify = TimeSpan.FromMilliseconds(1500);
    /// <summary>Poll cadence when the FROMNUM subscription failed and polling is the only way to learn about data.</summary>
    public static readonly TimeSpan PollIntervalWithoutNotify = TimeSpan.FromMilliseconds(250);
    /// <summary>If the window never reports "init complete" (e.g. init failed), the fast interval is released anyway.</summary>
    public static readonly TimeSpan SteadyFallbackDelay = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan StatsLogInterval = TimeSpan.FromMinutes(10);

    /// <summary>User setting "gentle Bluetooth mode": Balanced interval from the start, never ThroughputOptimized.
    /// Applied to every new link; <see cref="SetGentleMode"/> changes a live one.</summary>
    public static bool GentleModeDefault;
    private bool _gentle = GentleModeDefault;

    // Debug option "node log over Bluetooth": the firmware pushes its own log as LogRecord protobufs on the
    // LOGRADIO characteristic (only with config.security.debug_log_api_enabled). Off by default — every line
    // costs airtime on a link that may already be weak.
    public static bool NodeLogDefault;
    private bool _nodeLogEnabled = NodeLogDefault;
    private bool _nodeLogSubscribed;
    private int _nodeLogRecords;

    /// <summary>Advertising signal strength of the node heard while this connection was being set up, in dBm.
    /// Null when no advertisement was caught (Windows may connect before the first one arrives). Windows has
    /// no RSSI for a connected device, so this is the one number we can give a user with a flaky link.</summary>
    public int? ConnectRssiDbm => _stats.RssiDbm;
    public int ConnectAdvertisementsHeard => _stats.AdvertisementsHeard;

    public BleLinkMode Mode => _gentle ? BleLinkMode.Gentle : BleLinkMode.Fast;

    /// <summary>Counters of this link for the log line and the header tooltip.</summary>
    public BleLinkStatsSnapshot GetStats() => _stats.Snapshot(DateTime.UtcNow);

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

    // Windows 11 22H2 (build 22621) added the connection-parameter APIs. Older Windows: the calls do not exist.
    private static readonly bool HasPreferredParamsApi =
        ApiInformation.IsMethodPresent("Windows.Devices.Bluetooth.BluetoothLEDevice", "RequestPreferredConnectionParameters");
    private static readonly bool HasConnParamsApi =
        ApiInformation.IsMethodPresent("Windows.Devices.Bluetooth.BluetoothLEDevice", "GetConnectionParameters");
    private static readonly bool HasConnParamsEvent =
        ApiInformation.IsEventPresent("Windows.Devices.Bluetooth.BluetoothLEDevice", "ConnectionParametersChanged");

    // Meshtastic BLE UUIDs (from Python/Web clients)
    public static readonly Guid ServiceUuid = Guid.Parse("6ba1b218-15a8-461f-9fa8-5dcae273eafd");
    private static readonly Guid ToRadioUuid = Guid.Parse("f75c76d2-129e-4dad-a1dd-7866124401e7");
    private static readonly Guid FromRadioUuid = Guid.Parse("2c55e69e-4993-11ed-b878-0242ac120002");
    private static readonly Guid FromNumUuid = Guid.Parse("ed9da18c-a800-4f66-a670-aa7547e34453");
    // Firmware BluetoothCommon.h: LOGRADIO_UUID (current) and LEGACY_LOGRADIO_UUID (pre-2.5)
    private static readonly Guid LogRadioUuid = Guid.Parse("5a3d6e49-06e6-4423-9944-e9de8cdf9547");
    private static readonly Guid LegacyLogRadioUuid = Guid.Parse("6c6fd238-78fa-436b-aacf-15c5be1ef2e2");

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
        _stats.Mode = Mode;
        _stats.Phase = BleLinkPhase.Connecting;

        // Signal strength: the node advertises until the link is up — listen for its advertisements while we
        // connect. Runs alongside the connect, adds no waiting time; stopped as soon as the link is up or failed.
        var rssiWatch = StartRssiWatch(btParams.DeviceAddress);

        try
        {
            Logger.WriteLine($"[BLE] Connecting to {btParams.DeviceName} ({btParams.DeviceAddress:X}), mode {Mode}");

            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(btParams.DeviceAddress);
            if (_device == null)
                throw new Exception("Failed to connect to Bluetooth device");

            _device.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;

            // Evidence only, no action: Windows keeps the GATT table of a bonded device and may serve a stale one
            // after a firmware update or an OTA loader under the same address. Whether that is a problem is decided
            // by the live discovery below — and removing a bond is the user's call, never ours.
            bool paired = _device.DeviceInformation.Pairing.IsPaired;
            bool cachedStale = paired && await Firmware.BleGattLink.IsCachedGattTableStaleAsync(_device, ServiceUuid);

            await EnsurePairedAsync();

            LogDebug("[BLE] Discovering services...");
            var serviceResult = await _device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached);
            int serviceCount = serviceResult.Status == GattCommunicationStatus.Success ? serviceResult.Services.Count : 0;
            var status = MapStatus(serviceResult.Status);
            var verdict = BleStaleBondClassifier.Classify(_device.DeviceInformation.Pairing.IsPaired, status, serviceCount);
            switch (verdict)
            {
                case BleBondVerdict.ServiceFound:
                    if (cachedStale)
                        Logger.WriteLine("[BLE] Windows' cached service table lacked the Meshtastic service, but live discovery found it — the pairing is fine");
                    break;
                case BleBondVerdict.Unreachable:
                    Logger.WriteLine($"[BLE] Node not reachable (service discovery: {serviceResult.Status}) — pairing untouched");
                    throw new BleUnreachableException($"Node not reachable over Bluetooth (service discovery: {serviceResult.Status})");
                case BleBondVerdict.StaleBond:
                {
                    var evidence = BleStaleBondClassifier.DescribeEvidence(status, serviceCount, cachedStale);
                    Logger.WriteLine($"[BLE] Pairing looks stale — leaving the decision to the user: {evidence}");
                    throw new BleStaleBondException(evidence);
                }
                default:
                    throw new Exception($"Meshtastic service not found (status: {serviceResult.Status})");
            }

            var service = serviceResult.Services.First();
            _service = service; // keep so Disconnect can dispose it (else Windows holds the link → next connect fails until app restart)
            foreach (var extra in serviceResult.Services.Skip(1))
                try { extra.Dispose(); } catch { }
            LogDebug("[BLE] Found Meshtastic service");

            // Maintain a persistent GATT session: Windows otherwise drops the link between operations.
            try
            {
                _gattSession = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId);
                if (_gattSession != null)
                {
                    _gattSession.MaintainConnection = true;
                    LogDebug($"[BLE] GATT Session established. MaxPduSize: {_gattSession.MaxPduSize}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"[BLE] WARNING: Could not establish GATT session: {ex.Message} — continuing anyway");
            }

            if (HasConnParamsEvent)
            {
                try { _device.ConnectionParametersChanged += OnConnectionParametersChanged; }
                catch (Exception ex) { LogDebug($"[BLE] ConnectionParametersChanged not available: {ex.Message}"); }
            }
            LogConnectionParameters("at connect");
            _stats.Phase = BleLinkPhase.Init;
            RequestInitConnParams();

            // Get characteristics
            var toRadioResult = await service.GetCharacteristicsForUuidAsync(ToRadioUuid, BluetoothCacheMode.Uncached);
            var fromRadioResult = await service.GetCharacteristicsForUuidAsync(FromRadioUuid, BluetoothCacheMode.Uncached);
            var fromNumResult = await service.GetCharacteristicsForUuidAsync(FromNumUuid, BluetoothCacheMode.Uncached);

            if (toRadioResult.Status != GattCommunicationStatus.Success || !toRadioResult.Characteristics.Any())
                throw new Exception($"TORADIO characteristic not found (status: {toRadioResult.Status})");
            if (fromRadioResult.Status != GattCommunicationStatus.Success || !fromRadioResult.Characteristics.Any())
                throw new Exception($"FROMRADIO characteristic not found (status: {fromRadioResult.Status})");
            if (fromNumResult.Status != GattCommunicationStatus.Success || !fromNumResult.Characteristics.Any())
                throw new Exception($"FROMNUM characteristic not found (status: {fromNumResult.Status})");

            _toRadioChar = toRadioResult.Characteristics.First();
            _fromRadioChar = fromRadioResult.Characteristics.First();
            _fromNumChar = fromNumResult.Characteristics.First();
            _logRadioChar = await FindLogRadioCharacteristicAsync(service);

            LogDebug($"[BLE] All characteristics found (node log characteristic: {(_logRadioChar != null ? "yes" : "no")})");
            LogDebug($"[BLE] TORADIO properties: {_toRadioChar.CharacteristicProperties}");
            LogDebug($"[BLE] FROMRADIO properties: {_fromRadioChar.CharacteristicProperties}");
            LogDebug($"[BLE] FROMNUM properties: {_fromNumChar.CharacteristicProperties}");

            // FROMNUM tells us when FROMRADIO has data. (FROMRADIO itself has no notify — the firmware says so.)
            bool notificationsEnabled = false;
            if (_fromNumChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
            {
                try
                {
                    var cccdResult = await _fromNumChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    if (cccdResult == GattCommunicationStatus.Success)
                    {
                        _fromNumChar.ValueChanged += OnFromNumValueChanged;
                        notificationsEnabled = true;
                    }
                    else
                        Logger.WriteLine($"[BLE] FROMNUM subscription refused ({cccdResult}) — falling back to polling every {PollIntervalWithoutNotify.TotalMilliseconds:0} ms");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"[BLE] FROMNUM subscription failed ({ex.Message}) — falling back to polling every {PollIntervalWithoutNotify.TotalMilliseconds:0} ms");
                }
            }
            else
                Logger.WriteLine("[BLE] FROMNUM has no notify property — polling");

            _stats.NotifySubscribed = notificationsEnabled;
            _stats.PollInterval = notificationsEnabled ? PollIntervalWithNotify : PollIntervalWithoutNotify;

            // One reader for the whole link. Connected BEFORE it starts so a drop reported during the first reads
            // is handled as a real drop (the 1.7.2.2 ordering fix stays).
            _pumpCts = new CancellationTokenSource();
            var pump = new BleReadPump(ReadOnceAsync, data => DataReceived?.Invoke(this, data), _stats,
                                       _stats.PollInterval, log: Logger.WriteLine);
            pump.LinkLost += why =>
            {
                Logger.WriteLine($"[BLE] Link lost: {why} — disconnecting so the reconnect can take over");
                _ = Task.Run(Disconnect);
            };
            _pump = pump;

            StopRssiWatch(rssiWatch);
            _stats.ConnectedUtc = DateTime.UtcNow;
            IsConnected = true;
            _pumpTask = Task.Run(() => pump.RunAsync(_pumpCts.Token));
            pump.Signal(BleDrainTrigger.Initial);   // packets may already be queued (like Web Bluetooth's first read)

            _steadyFallbackTimer = new Timer(_ =>
            {
                if (_stats.Phase == BleLinkPhase.Init)
                {
                    Logger.WriteLine($"[BLE] No init-complete signal after {SteadyFallbackDelay.TotalSeconds:0} s — releasing the fast connection interval anyway");
                    InitComplete();
                }
            }, null, SteadyFallbackDelay, Timeout.InfiniteTimeSpan);
            _statsLogTimer = new Timer(_ => Logger.WriteLine(_stats.Snapshot(DateTime.UtcNow).ToLogLine()),
                                       null, StatsLogInterval, StatsLogInterval);

            Logger.WriteLine($"[BLE] Connected to {btParams.DeviceName} — notifications: {(notificationsEnabled ? "on" : "off")}, " +
                             $"safety poll every {_stats.PollInterval.TotalSeconds:0.#} s, mode {Mode}");

            if (_nodeLogEnabled)
                await SubscribeNodeLogAsync();   // never throws — a debug feature must not fail the connect
        }
        catch (Exception ex)
        {
            StopRssiWatch(rssiWatch);
            if (ex is not BleUnreachableException and not BleStaleBondException)
            {
                LogDebug($"[BLE] ERROR: Connection failed: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    LogDebug($"[BLE] Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                LogDebug($"[BLE] Stack trace: {ex.StackTrace}");
            }
            Disconnect();
            throw;
        }
    }

    private static BleGattStatus MapStatus(GattCommunicationStatus s) => s switch
    {
        GattCommunicationStatus.Success => BleGattStatus.Success,
        GattCommunicationStatus.Unreachable => BleGattStatus.Unreachable,
        GattCommunicationStatus.AccessDenied => BleGattStatus.AccessDenied,
        _ => BleGattStatus.ProtocolError,
    };

    private void OnDeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected) return;
        if (!IsConnected)
        {
            // Still inside ConnectAsync (pairing / discovery toggles the link). Tearing down here disposed the
            // device under ConnectAsync's feet ("Cannot access a disposed object") and cost a reconnect
            // attempt; Windows re-establishes the link on the next GATT call, or ConnectAsync fails on its own.
            LogDebug("[BLE] Device reported disconnected while connecting — ignoring");
            return;
        }
        Logger.WriteLine("[BLE] Device disconnected");
        Disconnect();
    }

    /// <summary>Pairs when Windows does not hold a bond yet (Windows shows its PIN prompt if the node wants one).</summary>
    private async Task EnsurePairedAsync()
    {
        if (_device!.DeviceInformation.Pairing.IsPaired)
        {
            LogDebug("[BLE] Device is already paired");
            return;
        }
        Logger.WriteLine("[BLE] Device is not paired — asking Windows to pair (PIN prompt may appear)");
        var pairingResult = await _device.DeviceInformation.Pairing.PairAsync();
        switch (pairingResult.Status)
        {
            case Windows.Devices.Enumeration.DevicePairingResultStatus.Paired:
                Logger.WriteLine("[BLE] Device paired");
                break;
            case Windows.Devices.Enumeration.DevicePairingResultStatus.AlreadyPaired:
                LogDebug("[BLE] Device was already paired");
                break;
            default:
                throw new Exception($"Pairing failed: {pairingResult.Status}");
        }
    }

    // ---- Connection parameters ----------------------------------------------------------------------------

    /// <summary>Asks Windows for the init-phase interval: ThroughputOptimized (fast) or, in gentle mode, Balanced.
    /// The request object must stay alive or Windows reverts; <see cref="InitComplete"/> releases it.</summary>
    private void RequestInitConnParams()
    {
        if (_device == null) return;
        if (!HasPreferredParamsApi)
        {
            _stats.ConnParamsRequest = "API not available (Windows before 11 22H2)";
            Logger.WriteLine("[BLE] Connection-parameter API not available on this Windows — using the system default interval");
            return;
        }
        try
        {
            var label = _gentle ? "Balanced" : "ThroughputOptimized";
            var pref = _gentle ? BluetoothLEPreferredConnectionParameters.Balanced
                               : BluetoothLEPreferredConnectionParameters.ThroughputOptimized;
            _connParamsRequest?.Dispose();
            _connParamsRequest = _device.RequestPreferredConnectionParameters(pref);
            _stats.ConnParamsRequest = $"{label}: {_connParamsRequest?.Status}";
            Logger.WriteLine($"[BLE] Requested {label} connection parameters for the init phase (status: {_connParamsRequest?.Status})");
            _ = Task.Run(async () => { await Task.Delay(1500); LogConnectionParameters("1.5 s after request"); });
        }
        catch (Exception ex)
        {
            _stats.ConnParamsRequest = $"request failed: {ex.Message}";
            Logger.WriteLine($"[BLE] Could not request connection parameters: {ex.Message}");
        }
    }

    private void ReleaseConnParams(string why)
    {
        var req = _connParamsRequest;
        _connParamsRequest = null;
        if (req == null) return;
        try { req.Dispose(); } catch { }
        _stats.ConnParamsRequest += " → released";
        Logger.WriteLine($"[BLE] Released the init-phase connection-parameter request ({why}) — the node's own preference applies now");
    }

    /// <summary>The window calls this once config and node list are loaded: the fast interval is no longer needed,
    /// the firmware wants 30–50 ms with latency afterwards, and a weak link is happier with it.</summary>
    public void InitComplete()
    {
        if (_stats.Phase == BleLinkPhase.Steady || !IsConnected) return;
        _stats.Phase = BleLinkPhase.Steady;
        ReleaseConnParams("init complete");
        _ = Task.Run(async () => { await Task.Delay(2500); LogConnectionParameters("2.5 s after release"); });
    }

    /// <summary>Switches the interval policy. A live link in its init phase re-requests; a steady link has already
    /// released its request and is not touched. New links read <see cref="GentleModeDefault"/>.</summary>
    public void SetGentleMode(bool gentle)
    {
        if (_gentle == gentle) return;
        _gentle = gentle;
        _stats.Mode = Mode;
        if (IsConnected && _stats.Phase == BleLinkPhase.Init)
            RequestInitConnParams();
    }

    private void OnConnectionParametersChanged(BluetoothLEDevice sender, object args) => LogConnectionParameters("changed");

    private void LogConnectionParameters(string when)
    {
        var dev = _device;
        if (!HasConnParamsApi || dev == null) return;
        try
        {
            var p = dev.GetConnectionParameters();
            if (p == null) return;
            int intervalMs = (int)Math.Round(p.ConnectionInterval * 1.25);
            int timeoutMs = p.LinkTimeout * 10;
            _stats.IntervalMs = intervalMs;
            _stats.LinkTimeoutMs = timeoutMs;
            _stats.Latency = p.ConnectionLatency;
            Logger.WriteLine($"[BLE] Connection parameters {when}: interval {intervalMs} ms, latency {p.ConnectionLatency}, supervision timeout {timeoutMs} ms");
        }
        catch (Exception ex)
        {
            LogDebug($"[BLE] GetConnectionParameters failed ({when}): {ex.Message}");
        }
    }

    // ---- Reading ------------------------------------------------------------------------------------------

    private void OnFromNumValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            _stats.CountNotify();
            if (_debugEnabled)
            {
                using var reader = DataReader.FromBuffer(args.CharacteristicValue);
                var length = reader.UnconsumedBufferLength;
                uint fromNum = 0;
                if (length >= 4)
                {
                    var data = new byte[length];
                    reader.ReadBytes(data);
                    fromNum = BitConverter.ToUInt32(data, 0);
                }
                LogDebug($"[BLE] FROMNUM notify: {fromNum} ({length} bytes) — draining FROMRADIO");
            }
            _pump?.Signal(BleDrainTrigger.Notify);
        }
        catch (Exception ex)
        {
            LogDebug($"[BLE] ERROR in FROMNUM handler: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>One uncached FROMRADIO read for the pump. Cached mode would hand back Windows' copy, not the node's.</summary>
    private async Task<BleReadResult> ReadOnceAsync(CancellationToken ct)
    {
        var chr = _fromRadioChar;
        if (chr == null || !IsConnected) return BleReadResult.Failed("characteristic gone");
        var result = await chr.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(ct);
        switch (result.Status)
        {
            case GattCommunicationStatus.Success:
            {
                using var reader = DataReader.FromBuffer(result.Value);
                var length = reader.UnconsumedBufferLength;
                if (length == 0) return BleReadResult.Empty;
                var data = new byte[length];
                reader.ReadBytes(data);
                LogDebug($"[BLE] Received {length} bytes: {BitConverter.ToString(data)}");
                return BleReadResult.Of(data);
            }
            case GattCommunicationStatus.Unreachable:
                return BleReadResult.Unreachable("GATT read: Unreachable");
            default:
                return BleReadResult.Failed($"GATT read: {result.Status}" +
                                            (result.ProtocolError is { } pe ? $" (ATT 0x{pe:X2})" : ""));
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

            GattCommunicationStatus status;
            if (_toRadioChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
            {
                status = await _toRadioChar.WriteValueAsync(writer.DetachBuffer());
            }
            else if (_toRadioChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write))
            {
                var result = await _toRadioChar.WriteValueWithResultAsync(writer.DetachBuffer());
                status = result.Status;
                if (result.Status != GattCommunicationStatus.Success && result.ProtocolError.HasValue)
                    LogDebug($"[BLE] Protocol Error: {result.ProtocolError.Value}");
            }
            else
            {
                throw new Exception($"TORADIO characteristic doesn't support writing! Properties: {_toRadioChar.CharacteristicProperties}");
            }

            if (status != GattCommunicationStatus.Success)
            {
                _stats.CountWrite(false);
                throw new Exception($"Write failed: {status}");
            }

            _stats.CountWrite(true);
            // The node answers config requests and admin messages on FROMRADIO without a FROMNUM notify in the
            // config phase — the pump reads right after every write (Python: sleep 10 ms, then should_read).
            _pump?.Signal(BleDrainTrigger.Write);
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

    // ---- Signal strength during connect -------------------------------------------------------------------

    private sealed class RssiWatch
    {
        public BluetoothLEAdvertisementWatcher? Watcher;
        public int Count;
        public int LastRssi;
        public readonly System.Diagnostics.Stopwatch Elapsed = System.Diagnostics.Stopwatch.StartNew();
    }

    private RssiWatch StartRssiWatch(ulong address)
    {
        var w = new RssiWatch();
        _stats.RssiDbm = null;
        _stats.AdvertisementsHeard = 0;
        try
        {
            w.Watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
            w.Watcher.Received += (_, a) =>
            {
                if (a.BluetoothAddress != address) return;
                w.LastRssi = a.RawSignalStrengthInDBm;
                Interlocked.Increment(ref w.Count);
            };
            w.Watcher.Start();
        }
        catch (Exception ex)
        {
            LogDebug($"[BLE] Advertisement watcher unavailable: {ex.Message}");
            w.Watcher = null;
        }
        return w;
    }

    private void StopRssiWatch(RssiWatch w)
    {
        try { w.Watcher?.Stop(); } catch { }
        w.Elapsed.Stop();
        int n = Volatile.Read(ref w.Count);
        _stats.AdvertisementsHeard = n;
        if (n > 0)
        {
            _stats.RssiDbm = w.LastRssi;
            Logger.WriteLine($"[BLE] Node signal during connect: {w.LastRssi} dBm ({n} advertisement(s) in {w.Elapsed.Elapsed.TotalSeconds:0.0} s)");
        }
        else if (w.Watcher != null)
        {
            Logger.WriteLine($"[BLE] No advertisement from the node heard during connect ({w.Elapsed.Elapsed.TotalSeconds:0.0} s) — " +
                             "either connected before the first one, or the node is out of range / not advertising");
        }
    }

    // ---- Node log over Bluetooth (debug option) -----------------------------------------------------------

    private async Task<GattCharacteristic?> FindLogRadioCharacteristicAsync(GattDeviceService service)
    {
        foreach (var uuid in new[] { LogRadioUuid, LegacyLogRadioUuid })
        {
            try
            {
                var r = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached);
                if (r.Status == GattCommunicationStatus.Success && r.Characteristics.Any())
                    return r.Characteristics.First();
            }
            catch (Exception ex)
            {
                LogDebug($"[BLE] LOGRADIO lookup failed: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>Switches the node-log mirror on or off for this link (and remembers the wish for a link that
    /// is still being set up).</summary>
    public void SetNodeLogEnabled(bool enabled)
    {
        _nodeLogEnabled = enabled;
        if (!IsConnected) return;
        _ = Task.Run(async () =>
        {
            if (enabled) await SubscribeNodeLogAsync();
            else await UnsubscribeNodeLogAsync();
        });
    }

    private async Task SubscribeNodeLogAsync()
    {
        if (_nodeLogSubscribed) return;
        var chr = _logRadioChar;
        if (chr == null)
        {
            Logger.WriteLine("[NODE] Node log requested, but the device has no LOGRADIO characteristic (firmware too old?)");
            return;
        }
        try
        {
            var status = await chr.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (status != GattCommunicationStatus.Success)
            {
                Logger.WriteLine($"[NODE] Could not subscribe to the node log: {status}");
                return;
            }
            chr.ValueChanged += OnLogRadioValueChanged;
            _nodeLogSubscribed = true;
            _nodeLogRecords = 0;
            Logger.WriteLine("[NODE] Node log over Bluetooth enabled — the node must have 'Debug log over API' (Security config) switched on, otherwise nothing arrives");
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(45));
                if (_nodeLogSubscribed && IsConnected && Volatile.Read(ref _nodeLogRecords) == 0)
                    Logger.WriteLine("[NODE] No log record in 45 s — check Security → 'Debug log over API' on the node " +
                                     "(the firmware also pauses this log while it sends the config)");
            });
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[NODE] Subscribing to the node log failed: {ex.Message}");
        }
    }

    private async Task UnsubscribeNodeLogAsync()
    {
        var chr = _logRadioChar;
        if (!_nodeLogSubscribed || chr == null)
        {
            _nodeLogSubscribed = false;
            return;
        }
        chr.ValueChanged -= OnLogRadioValueChanged;
        _nodeLogSubscribed = false;
        try
        {
            await chr.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.None);
        }
        catch { /* link may be gone */ }
        Logger.WriteLine("[NODE] Node log over Bluetooth disabled");
    }

    private void OnLogRadioValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            using var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            var rec = Meshtastic.Protobufs.LogRecord.Parser.ParseFrom(bytes);
            Interlocked.Increment(ref _nodeLogRecords);
            var level = rec.Level switch
            {
                Meshtastic.Protobufs.LogRecord.Types.Level.Critical => "CRIT",
                Meshtastic.Protobufs.LogRecord.Types.Level.Error    => "ERROR",
                Meshtastic.Protobufs.LogRecord.Types.Level.Warning  => "WARN",
                Meshtastic.Protobufs.LogRecord.Types.Level.Info     => "INFO",
                Meshtastic.Protobufs.LogRecord.Types.Level.Debug    => "DEBUG",
                Meshtastic.Protobufs.LogRecord.Types.Level.Trace    => "TRACE",
                _                                                   => "LOG",
            };
            var source = string.IsNullOrEmpty(rec.Source) ? "" : $" {rec.Source}";
            Logger.WriteLine($"[NODE] {level}{source}: {rec.Message.TrimEnd('\r', '\n')}");
        }
        catch (Exception ex)
        {
            LogDebug($"[NODE] Unparseable log record: {ex.Message}");
        }
    }

    // ---- Teardown -----------------------------------------------------------------------------------------

    private int _disconnectRunning;

    public void Disconnect()
    {
        // Re-entrancy: the device's ConnectionStatusChanged, the pump's LinkLost and the user's Disconnect/Close can
        // all arrive within milliseconds of each other. Only one teardown runs; the others return (the first one's
        // finally sets IsConnected = false for everyone).
        if (Interlocked.CompareExchange(ref _disconnectRunning, 1, 0) != 0)
        {
            LogDebug("[BLE] Disconnect already in progress — skipping");
            return;
        }
        try
        {
            if (_device == null && !IsConnected)
                return; // nothing to tear down (Dispose after Disconnect, or never connected)

            Logger.WriteLine("[BLE] Disconnecting...");

            _steadyFallbackTimer?.Dispose();
            _steadyFallbackTimer = null;
            _statsLogTimer?.Dispose();
            _statsLogTimer = null;

            // Stop the reader first; it may be inside a GATT read, so give it a moment.
            try { _pumpCts?.Cancel(); } catch { }
            var pumpTask = _pumpTask;
            if (pumpTask != null && !pumpTask.IsCompleted)
            {
                try { if (!pumpTask.Wait(3000)) Logger.WriteLine("[BLE] WARNING: Reader did not stop within 3 s"); }
                catch { /* cancelled — expected */ }
            }
            _pump = null;
            _pumpTask = null;
            _pumpCts?.Dispose();
            _pumpCts = null;

            if (_stats.ConnectedUtc != null)
                Logger.WriteLine(_stats.Snapshot(DateTime.UtcNow).ToLogLine());

            if (_fromNumChar != null)
                _fromNumChar.ValueChanged -= OnFromNumValueChanged;
            if (_logRadioChar != null)
            {
                _logRadioChar.ValueChanged -= OnLogRadioValueChanged;
                _logRadioChar = null;
            }
            _nodeLogSubscribed = false;

            _fromNumChar = null;
            _toRadioChar = null;
            _fromRadioChar = null;

            // Order matters: drop the GATT session's MaintainConnection + dispose it, THEN the
            // service, THEN the device. Leaving the GattDeviceService undisposed keeps the BLE link
            // open in Windows, so the next connect in this process fails until the app is restarted.
            if (_connParamsRequest != null)
            {
                try { _connParamsRequest.Dispose(); } catch { }
                _connParamsRequest = null;
            }
            if (_gattSession != null)
            {
                try { _gattSession.MaintainConnection = false; _gattSession.Dispose(); } catch { }
                _gattSession = null;
            }
            if (_service != null)
            {
                try { _service.Dispose(); } catch { }
                _service = null;
            }

            if (_device != null)
            {
                _device.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                if (HasConnParamsEvent)
                {
                    try { _device.ConnectionParametersChanged -= OnConnectionParametersChanged; } catch { }
                }
                _device.Dispose();
                _device = null;
            }

            Logger.WriteLine("[BLE] Disconnected");
        }
        catch (Exception ex)
        {
            LogDebug($"[BLE] ERROR during disconnect: {ex.Message}");
        }
        finally
        {
            IsConnected = false;
            Volatile.Write(ref _disconnectRunning, 0);
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
