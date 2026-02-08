using System.IO.Ports;

namespace MeshtasticClient.Services;

public class SerialPortService : IDisposable
{
    private SerialPort? _serialPort;
    private bool _isConnected;
    private readonly object _lock = new();
    private Thread? _readerThread;
    private bool _wantExit = false;

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

    public async Task ConnectAsync(string portName, int baudRate = 115200)
    {
        await Task.Run(() =>
        {
            try
            {
                Logger.WriteLine($"Connecting to {portName}...");
                _serialPort = new SerialPort(portName, baudRate)
                {
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = 500,
                    WriteTimeout = -1
                };

                _serialPort.Open();

                // DTR und RTS setzen für USB-Serial Chips
                _serialPort.DtrEnable = true;
                _serialPort.RtsEnable = true;

                // Flush buffers
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                IsConnected = true;
                Logger.WriteLine($"Connected to {portName}");

                // Starte Reader-Thread
                _wantExit = false;
                _readerThread = new Thread(ReaderThreadFunc)
                {
                    IsBackground = true,
                    Name = "SerialReader"
                };
                _readerThread.Start();
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"ERROR: Connection failed: {ex.Message}");
                _serialPort?.Dispose();
                _serialPort = null;
                throw;
            }
        });
    }

    public void Disconnect()
    {
        try
        {
            Logger.WriteLine("Disconnecting...");

            // Stoppe Reader Thread zuerst
            _wantExit = true;

            // Warte auf Thread mit längerem Timeout
            if (_readerThread != null && _readerThread.IsAlive)
            {
                if (!_readerThread.Join(3000))
                {
                    Logger.WriteLine("WARNING: Reader thread did not stop, forcing");
                    // Thread läuft noch - aber wir können nichts tun außer weiter zu machen
                }
            }

            // Jetzt Serial Port schließen und disposen
            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
                        // Clear DTR/RTS vor Close
                        _serialPort.DtrEnable = false;
                        _serialPort.RtsEnable = false;

                        _serialPort.Close();
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"ERROR closing serial port: {ex.Message}");
                }

                try
                {
                    _serialPort.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"ERROR disposing serial port: {ex.Message}");
                }
                finally
                {
                    _serialPort = null;
                }
            }

            _readerThread = null;
            Logger.WriteLine("Disconnected");
        }
        finally
        {
            IsConnected = false;
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
                            Logger.WriteLine($"ERROR: DataReceived event failed: {ex.Message}");
                        }
                    }
                }
                catch (TimeoutException)
                {
                    // Normal - Read timeout (500ms), weiter machen
                    continue;
                }
                catch (InvalidOperationException)
                {
                    // Port geschlossen
                    break;
                }
                catch (Exception ex)
                {
                    if (!_wantExit)
                    {
                        Logger.WriteLine($"ERROR: Reader thread: {ex.GetType().Name}: {ex.Message}");
                    }
                    break;
                }
            }

            Logger.WriteLine("Serial reader thread stopped");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"FATAL: Reader thread crashed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
