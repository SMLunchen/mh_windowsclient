using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Windows.Devices.Bluetooth;
using Windows.Foundation;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace MeshhessenClient.Services.Firmware;

public sealed class BleException : Exception
{
    public GattCommunicationStatus? Status { get; }
    public BleException(string message, GattCommunicationStatus? status = null, Exception? inner = null) : base(message, inner) => Status = status;
}

/// <summary>Bluetooth address helpers (WinRT addresses are 48-bit MACs in a ulong, AA:BB:CC:DD:EE:FF = 0xAABBCCDDEEFF).</summary>
public static class BleAddress
{
    /// <summary>The address a device advertises from in OTA/DFU mode: last byte + 1, wrapping at FF.
    /// Both the ESP32 OTA loader and the Nordic bootloader may do this.</summary>
    public static ulong PlusOne(ulong address) => (address & ~0xFFUL) | ((address + 1) & 0xFF);

    public static string Format(ulong address) =>
        string.Join(":", BitConverter.GetBytes(address).Take(6).Reverse().Select(b => b.ToString("X2")));
}

/// <summary>One advertisement sighting, merged over all packets seen from that address during a scan.</summary>
public sealed record BleScanHit(ulong Address, string? Name, IReadOnlySet<Guid> ServiceUuids, short Rssi);

/// <summary>Advertisement scanning for devices that just rebooted into OTA/DFU mode.</summary>
public static class BleScanner
{
    private sealed class Seen
    {
        public string? Name;
        public short Rssi;
        public readonly HashSet<Guid> Uuids = new();
    }

    /// <summary>Scans until <paramref name="predicate"/> accepts a hit or the timeout elapses (null).</summary>
    public static Task<BleScanHit?> ScanAsync(Func<BleScanHit, bool> predicate, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<BleScanHit?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new ConcurrentDictionary<ulong, Seen>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };

        void OnReceived(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs a)
        {
            var s = seen.GetOrAdd(a.BluetoothAddress, _ => new Seen());
            BleScanHit hit;
            lock (s)
            {
                if (!string.IsNullOrEmpty(a.Advertisement.LocalName)) s.Name = a.Advertisement.LocalName;
                s.Rssi = a.RawSignalStrengthInDBm;
                foreach (var u in a.Advertisement.ServiceUuids) s.Uuids.Add(u);
                hit = new BleScanHit(a.BluetoothAddress, s.Name, new HashSet<Guid>(s.Uuids), s.Rssi);
            }
            if (predicate(hit)) tcs.TrySetResult(hit);
        }

        void OnStopped(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementWatcherStoppedEventArgs a)
        {
            if (a.Error != BluetoothError.Success)
                Logger.WriteLine($"[BLE scan] Watcher stopped with error {a.Error}");
            tcs.TrySetResult(null);
        }

        watcher.Received += OnReceived;
        watcher.Stopped += OnStopped;
        var timer = new CancellationTokenSource(timeout);
        var reg1 = timer.Token.Register(() => tcs.TrySetResult(null));
        var reg2 = ct.Register(() => tcs.TrySetCanceled(ct));
        try
        {
            watcher.Start();
        }
        catch (Exception ex)
        {
            tcs.TrySetException(new BleException($"Could not start BLE scan: {ex.Message}", null, ex));
        }

        return tcs.Task.ContinueWith(t =>
        {
            try { watcher.Received -= OnReceived; watcher.Stopped -= OnStopped; watcher.Stop(); } catch { }
            reg1.Dispose();
            reg2.Dispose();
            timer.Dispose();
            Logger.WriteLine($"[BLE scan] {seen.Count} devices seen; match={(t.Status == TaskStatus.RanToCompletion && t.Result != null)}");
            return t.GetAwaiter().GetResult();
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    public static async Task<BleScanHit?> ScanWithRetriesAsync(Func<BleScanHit, bool> predicate, int retries,
        TimeSpan scanTimeout, TimeSpan retryDelay, string tag, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= retries; attempt++)
        {
            Logger.WriteLine($"[{tag}] Scan attempt {attempt}/{retries}");
            var hit = await ScanAsync(predicate, scanTimeout, ct);
            if (hit != null)
            {
                Logger.WriteLine($"[{tag}] Found {BleAddress.Format(hit.Address)} '{hit.Name}' rssi={hit.Rssi} uuids={hit.ServiceUuids.Count}");
                return hit;
            }
            if (attempt < retries) await Task.Delay(retryDelay, ct);
        }
        return null;
    }
}

/// <summary>What the wizard shows about the Bluetooth radio Windows will use. Windows binds its BLE stack to
/// exactly one adapter; WinRT offers no per-adapter selection, hence <see cref="BluetoothRadioCount"/> drives
/// the "disable the other adapter in Device Manager" hint.</summary>
public sealed record BleAdapterInfo(string? RadioName, ulong Address, bool LowEnergySupported, int BluetoothRadioCount);

public static class BleAdapterQuery
{
    public static async Task<BleAdapterInfo?> GetAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter == null) return null;
            string? name = null;
            try { name = (await adapter.GetRadioAsync())?.Name; } catch { }
            int count = 0;
            try
            {
                var radios = await Radio.GetRadiosAsync();
                count = radios.Count(r => r.Kind == RadioKind.Bluetooth);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"[BLE] Radio enumeration failed: {ex.Message}");
            }
            return new BleAdapterInfo(name, adapter.BluetoothAddress, adapter.IsLowEnergySupported, count);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[BLE] Adapter query failed: {ex.Message}");
            return null;
        }
    }
}

/// <summary>FIFO of characteristic notifications with a timed receive.</summary>
public sealed class BleNotificationQueue
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
    internal void Push(byte[] data) => _channel.Writer.TryWrite(data);
    internal void Close() => _channel.Writer.TryComplete();

    /// <summary>Next notification, or null when <paramref name="timeout"/> elapses. Throws <see cref="BleException"/> if the queue was closed (link gone).</summary>
    public async Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (ChannelClosedException)
        {
            throw new BleException("BLE link closed while waiting for a notification");
        }
    }

    public bool TryDrain(out byte[]? last)
    {
        last = null;
        while (_channel.Reader.TryRead(out var d)) last = d;
        return last != null;
    }
}

/// <summary>
/// Thin WinRT GATT client used by the OTA/DFU transports: one device, uncached service discovery, timed
/// writes, notification queues, link-drop signalling. The Meshtastic mesh connection itself stays in
/// <see cref="BluetoothConnectionService"/>; this class is only ever used after that link was released.
/// </summary>
public sealed class BleGattLink : IAsyncDisposable
{
    private BluetoothLEDevice? _device;
    private GattSession? _session;
    private BluetoothLEPreferredConnectionParametersRequest? _connParams;
    private readonly List<GattDeviceService> _services = new();
    private readonly List<(GattCharacteristic Char, TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> Handler, BleNotificationQueue Queue)> _subs = new();
    private readonly TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    public ulong Address { get; }
    public string Name => _device?.Name ?? "";

    /// <summary>Completes once Windows reports the link dropped (never faults).</summary>
    public Task DisconnectedTask => _disconnected.Task;
    public bool IsDisconnected => _disconnected.Task.IsCompleted;

    /// <summary>Largest payload for a single write-without-response (ATT MTU − 3; 20 when unknown).</summary>
    public int MaxWriteNoResponse
    {
        get
        {
            try
            {
                var pdu = _session?.MaxPduSize ?? 23;
                return Math.Max(20, pdu - 3);
            }
            catch { return 20; }
        }
    }

    private BleGattLink(ulong address) => Address = address;

    /// <summary>Resolves the device object. The physical connection is made lazily by the first GATT operation.</summary>
    public static async Task<BleGattLink> OpenAsync(ulong address, CancellationToken ct)
    {
        var link = new BleGattLink(address);
        var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(ct);
        if (dev == null)
            throw new BleException($"BLE device {BleAddress.Format(address)} not found (not advertising / unknown to Windows)");
        link._device = dev;
        dev.ConnectionStatusChanged += link.OnConnectionStatusChanged;
        try
        {
            link._session = await GattSession.FromDeviceIdAsync(dev.BluetoothDeviceId).AsTask(ct);
            if (link._session != null) link._session.MaintainConnection = true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[BLE link] GattSession unavailable: {ex.Message}");
        }
        return link;
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Logger.WriteLine($"[BLE link] {BleAddress.Format(Address)} disconnected");
            _disconnected.TrySetResult();
            foreach (var s in _subs) s.Queue.Close();
        }
    }

    /// <summary>Uncached lookup of one service; null when the device does not expose it. Throws on link failure.</summary>
    public async Task<GattDeviceService?> TryGetServiceAsync(Guid uuid, TimeSpan timeout, CancellationToken ct)
    {
        var dev = _device ?? throw new BleException("link disposed");
        GattDeviceServicesResult result;
        try
        {
            result = await dev.GetGattServicesForUuidAsync(uuid, BluetoothCacheMode.Uncached).AsTask(ct).WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            throw new BleException($"Service discovery for {uuid} timed out after {timeout.TotalSeconds:0}s");
        }
        if (result.Status == GattCommunicationStatus.Unreachable)
            throw new BleException($"Device {BleAddress.Format(Address)} unreachable", result.Status);
        if (result.Status != GattCommunicationStatus.Success)
        {
            Logger.WriteLine($"[BLE link] GetGattServicesForUuid({uuid}) → {result.Status}");
            return null;
        }
        if (result.Services.Count == 0) return null;
        var svc = result.Services[0];
        foreach (var extra in result.Services.Skip(1)) try { extra.Dispose(); } catch { }
        _services.Add(svc);
        return svc;
    }

    public async Task<IReadOnlyList<Guid>> ListServiceUuidsAsync(TimeSpan timeout, CancellationToken ct)
    {
        var dev = _device ?? throw new BleException("link disposed");
        var result = await dev.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct).WaitAsync(timeout, ct);
        if (result.Status != GattCommunicationStatus.Success)
            throw new BleException($"Service enumeration failed: {result.Status}", result.Status);
        var list = result.Services.Select(s => s.Uuid).ToList();
        foreach (var s in result.Services) try { s.Dispose(); } catch { }
        return list;
    }

    public async Task<GattCharacteristic> GetCharacteristicAsync(GattDeviceService service, Guid uuid, CancellationToken ct)
    {
        var r = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached).AsTask(ct);
        if (r.Status != GattCommunicationStatus.Success || r.Characteristics.Count == 0)
            throw new BleException($"Characteristic {uuid} not found (status {r.Status})", r.Status);
        return r.Characteristics[0];
    }

    public async Task<GattCharacteristic?> TryGetCharacteristicAsync(GattDeviceService service, Guid uuid, CancellationToken ct)
    {
        try { return await GetCharacteristicAsync(service, uuid, ct); }
        catch (BleException) { return null; }
    }

    /// <summary>Enables notifications or indications and returns the queue they are delivered to.</summary>
    public async Task<BleNotificationQueue> SubscribeAsync(GattCharacteristic ch, bool indicate, CancellationToken ct)
    {
        var queue = new BleNotificationQueue();
        void Handler(GattCharacteristic s, GattValueChangedEventArgs a)
        {
            var data = new byte[a.CharacteristicValue.Length];
            using var reader = DataReader.FromBuffer(a.CharacteristicValue);
            reader.ReadBytes(data);
            queue.Push(data);
        }
        ch.ValueChanged += Handler;
        _subs.Add((ch, Handler, queue));
        var value = indicate
            ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
            : GattClientCharacteristicConfigurationDescriptorValue.Notify;
        var r = await ch.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(value).AsTask(ct);
        if (r.Status != GattCommunicationStatus.Success)
            throw new BleException($"CCCD write ({value}) failed: {r.Status} {(r.ProtocolError.HasValue ? $"att=0x{r.ProtocolError.Value:X2}" : "")}", r.Status);
        return queue;
    }

    /// <summary>Writes one value. <paramref name="timeout"/> bounds a with-response write whose ACK may never
    /// come (device rebooting); a <see cref="TimeoutException"/> is thrown in that case.</summary>
    public async Task WriteAsync(GattCharacteristic ch, ReadOnlyMemory<byte> data, bool withResponse, TimeSpan timeout, CancellationToken ct)
    {
        var buffer = data.ToArray().AsBuffer();
        var option = withResponse ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
        var r = await ch.WriteValueWithResultAsync(buffer, option).AsTask(ct).WaitAsync(timeout, ct);
        if (r.Status != GattCommunicationStatus.Success)
            throw new BleException($"GATT write failed: {r.Status}{(r.ProtocolError.HasValue ? $" att=0x{r.ProtocolError.Value:X2}" : "")}", r.Status);
    }

    public Task WriteAsync(GattCharacteristic ch, ReadOnlyMemory<byte> data, bool withResponse, CancellationToken ct) =>
        WriteAsync(ch, data, withResponse, TimeSpan.FromSeconds(30), ct);

    public async Task<byte[]?> TryReadAsync(GattCharacteristic ch, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var r = await ch.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(ct).WaitAsync(timeout, ct);
            if (r.Status != GattCommunicationStatus.Success) return null;
            var data = new byte[r.Value.Length];
            using var reader = DataReader.FromBuffer(r.Value);
            reader.ReadBytes(data);
            return data;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Logger.WriteLine($"[BLE link] Read {ch.Uuid} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Asks Windows for the short connection interval; sustained DFU/OTA streaming starves on the
    /// power-saving default. Guarded — a no-op on Windows builds without the API.</summary>
    public void RequestHighThroughput()
    {
        try
        {
            _connParams ??= _device?.RequestPreferredConnectionParameters(BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
            Logger.WriteLine($"[BLE link] ThroughputOptimized requested → {_connParams?.Status}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[BLE link] Connection-parameter request unavailable: {ex.Message}");
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        foreach (var (ch, handler, queue) in _subs)
        {
            try { ch.ValueChanged -= handler; } catch { }
            queue.Close();
        }
        _subs.Clear();
        try { _connParams?.Dispose(); } catch { }
        _connParams = null;
        if (_session != null)
        {
            try { _session.MaintainConnection = false; _session.Dispose(); } catch { }
            _session = null;
        }
        foreach (var s in _services) try { s.Dispose(); } catch { }
        _services.Clear();
        if (_device != null)
        {
            try { _device.ConnectionStatusChanged -= OnConnectionStatusChanged; _device.Dispose(); } catch { }
            _device = null;
        }
        _disconnected.TrySetResult();
        return ValueTask.CompletedTask;
    }
}
