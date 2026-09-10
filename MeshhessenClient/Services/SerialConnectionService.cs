using System.IO;
using System.IO.Ports;

namespace MeshhessenClient.Services;

/// <summary>
/// Serial port connection implementation for Meshtastic devices
/// </summary>
public class SerialConnectionService : IConnectionService
{
    private SerialPort? _serialPort;
    private bool _isConnected;
    private int _disconnecting = 0;   // reentrancy guard: reader/watchdog/user may all call Disconnect
    private readonly object _lock = new();
    private Thread? _readerThread;
    private bool _wantExit = false;
    private DateTime _lastReaderHeartbeat = DateTime.MinValue;
    private System.Threading.Timer? _watchdogTimer;
    private string _displayName = string.Empty;

    public ConnectionType Type => ConnectionType.Serial;
    public string DisplayName => _displayName;

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<byte[]>? DataReceived;

    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _isConnected;
            }
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

    public async Task ConnectAsync(ConnectionParameters parameters)
    {
        if (parameters is not SerialConnectionParameters serialParams)
            throw new ArgumentException("Invalid connection parameters for Serial connection", nameof(parameters));

        if (string.IsNullOrEmpty(serialParams.PortName))
            throw new ArgumentException("Port name cannot be empty", nameof(parameters));

        _displayName = serialParams.PortName;

        await Task.Run(() =>
        {
            try
            {
                Logger.WriteLine($"[SERIAL] Connecting to {serialParams.PortName}...");
                _serialPort = new SerialPort(serialParams.PortName, serialParams.BaudRate)
                {
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = 500,
                    // Finite write timeout: an infinite (-1) timeout lets a write to a
                    // non-Meshtastic / unresponsive port block forever, which in turn makes
                    // SerialPort.Close() deadlock on the pending write when disconnecting.
                    WriteTimeout = 2000
                };

                _serialPort.Open();

                // DTR und RTS setzen für USB-Serial Chips
                _serialPort.DtrEnable = true;
                _serialPort.RtsEnable = true;

                // Flush buffers
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                IsConnected = true;
                Logger.WriteLine($"[SERIAL] Connected to {serialParams.PortName} " +
                    $"(baud={serialParams.BaudRate}, 8N1, DTR=on, RTS=on, handshake=none)");

                // Starte Reader-Thread
                _wantExit = false;
                _lastReaderHeartbeat = DateTime.Now;
                _readerThread = new Thread(ReaderThreadFunc)
                {
                    IsBackground = true,
                    Name = "SerialReader"
                };
                _readerThread.Start();

                // Start watchdog timer (checks every 10 seconds)
                _watchdogTimer = new System.Threading.Timer(WatchdogCallback, null, 10000, 10000);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"[SERIAL] ERROR: Connection failed: {ex.Message}");
                _serialPort?.Dispose();
                _serialPort = null;
                throw;
            }
        });
    }

    public void Disconnect()
    {
        // Reader thread, watchdog and the user's disconnect click can all land here at
        // once — only the first caller performs the teardown; the rest return immediately.
        if (System.Threading.Interlocked.Exchange(ref _disconnecting, 1) == 1)
            return;

        try
        {
            Logger.WriteLine("[SERIAL] Disconnecting...");

            // Stop watchdog timer
            _watchdogTimer?.Dispose();
            _watchdogTimer = null;

            // Stoppe Reader Thread zuerst
            _wantExit = true;

            // Warte auf Thread mit längerem Timeout
            if (_readerThread != null && _readerThread.IsAlive)
            {
                if (!_readerThread.Join(3000))
                {
                    Logger.WriteLine("[SERIAL] WARNING: Reader thread did not stop, forcing");
                    // Thread läuft noch - aber wir können nichts tun außer weiter zu machen
                }
            }

            // Jetzt Serial Port schließen und disposen.
            // WICHTIG: SerialPort.Close()/Dispose() kann bei einem hängenden oder
            // Nicht-Meshtastic-Port (z. B. ausstehender Write, entferntes Gerät) auf
            // unbestimmte Zeit blockieren. Deshalb auf einem Wegwerf-Thread schließen
            // und nur begrenzt darauf warten — sonst wedged der ganze Disconnect.
            var port = _serialPort;
            _serialPort = null;
            if (port != null)
            {
                var closeThread = new Thread(() =>
                {
                    try
                    {
                        if (port.IsOpen)
                        {
                            try { port.DtrEnable = false; port.RtsEnable = false; } catch { }
                            port.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine($"[SERIAL] ERROR closing serial port: {ex.Message}");
                    }
                    finally
                    {
                        try { port.Dispose(); } catch { }
                    }
                })
                { IsBackground = true, Name = "SerialClose" };
                closeThread.Start();

                if (!closeThread.Join(3000))
                    Logger.WriteLine("[SERIAL] WARNING: serial port close did not finish in 3s — abandoning port object (leaked) to avoid a hang");
            }

            _readerThread = null;
            Logger.WriteLine("[SERIAL] Disconnected");
        }
        finally
        {
            IsConnected = false;
            System.Threading.Interlocked.Exchange(ref _disconnecting, 0);
        }
    }

    public async Task WriteAsync(byte[] data)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not connected");
        }

        await _serialPort.BaseStream.WriteAsync(data, 0, data.Length);
        await _serialPort.BaseStream.FlushAsync();

        // Delay nach Write wie Python (verhindert Puffer-Überlauf am Gerät)
        await Task.Delay(100);  // 100ms Standard-Delay
    }


    // Reader Thread - liest kontinuierlich Daten (wie Python)
    private void ReaderThreadFunc()
    {
        try
        {
            byte[] buffer = new byte[1024];

            while (!_wantExit)
            {
                try
                {
                    // Update heartbeat
                    _lastReaderHeartbeat = DateTime.Now;

                    // Prüfe Port
                    if (_serialPort == null || !_serialPort.IsOpen)
                    {
                        break;
                    }

                    // Versuche 1 Byte zu lesen (blockiert mit ReadTimeout)
                    int bytesRead = _serialPort.Read(buffer, 0, 1);

                    if (bytesRead > 0)
                    {
                        // Wir haben Daten! Prüfe ob mehr verfügbar
                        int available = _serialPort.BytesToRead;
                        if (available > 0)
                        {
                            int additionalBytes = _serialPort.Read(buffer, 1, Math.Min(available, buffer.Length - 1));
                            bytesRead += additionalBytes;
                        }

                        // Erstelle Daten-Array
                        byte[] data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);

                        // Event auslösen
                        try
                        {
                            DataReceived?.Invoke(this, data);
                        }
                        catch (Exception ex)
                        {
                            Logger.WriteLine($"[SERIAL] ERROR: DataReceived event failed: {ex.Message}");
                        }
                    }
                }
                catch (TimeoutException)
                {
                    // Normal - Read timeout (500ms), weiter machen
                    continue;
                }
                catch (InvalidOperationException ioe)
                {
                    // Port geschlossen
                    Logger.WriteLine($"[SERIAL] Reader thread: Port closed ({ioe.Message})");
                    break;
                }
                catch (IOException ioe)
                {
                    // I/O error - USB disconnect, cable unplugged, etc.
                    Logger.WriteLine($"[SERIAL] CRITICAL: Reader thread I/O error: {ioe.Message}");
                    Logger.WriteLine($"[SERIAL]   This usually indicates hardware disconnection");
                    break;
                }
                catch (Exception ex)
                {
                    if (!_wantExit)
                    {
                        Logger.WriteLine($"[SERIAL] CRITICAL: Reader thread unexpected exception: {ex.GetType().Name}: {ex.Message}");
                        Logger.WriteLine($"[SERIAL]   Stack trace: {ex.StackTrace}");
                    }
                    break;
                }
            }

            Logger.WriteLine($"[SERIAL] Reader thread stopped (wantExit={_wantExit}, port={((_serialPort?.IsOpen ?? false) ? "open" : "closed")})");

            // If thread is stopping unexpectedly, trigger disconnect to clean up state
            if (!_wantExit && IsConnected)
            {
                Logger.WriteLine("[SERIAL] WARNING: Reader thread stopped unexpectedly, forcing disconnect");
                Task.Run(() => Disconnect());
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[SERIAL] FATAL: Reader thread crashed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void WatchdogCallback(object? state)
    {
        try
        {
            // Check if reader thread is still alive and actively running
            if (_isConnected && _readerThread != null)
            {
                var timeSinceHeartbeat = DateTime.Now - _lastReaderHeartbeat;

                // If no heartbeat for more than 30 seconds, something is wrong
                if (timeSinceHeartbeat.TotalSeconds > 30)
                {
                    Logger.WriteLine($"[SERIAL] CRITICAL: Reader thread appears to be dead (no heartbeat for {timeSinceHeartbeat.TotalSeconds:F0}s)!");
                    Logger.WriteLine($"[SERIAL]   Thread state: {(_readerThread.IsAlive ? "Alive" : "Dead")}");

                    // Force disconnect to clean up state
                    Task.Run(() => Disconnect());
                }
                else if (timeSinceHeartbeat.TotalSeconds > 10)
                {
                    Logger.WriteLine($"[SERIAL] WARNING: Reader thread heartbeat delayed ({timeSinceHeartbeat.TotalSeconds:F0}s)");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[SERIAL] ERROR in watchdog: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
