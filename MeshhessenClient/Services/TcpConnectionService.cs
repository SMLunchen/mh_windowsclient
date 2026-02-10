using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MeshhessenClient.Services;

/// <summary>
/// TCP/WiFi connection implementation for Meshtastic devices
/// </summary>
public class TcpConnectionService : IConnectionService
{
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private bool _isConnected;
    private readonly object _lock = new();
    private Thread? _readerThread;
    private bool _wantExit = false;
    private string _displayName = string.Empty;

    public ConnectionType Type => ConnectionType.Tcp;
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
        if (parameters is not TcpConnectionParameters tcpParams)
            throw new ArgumentException("Invalid connection parameters for TCP connection", nameof(parameters));

        if (string.IsNullOrEmpty(tcpParams.Hostname))
            throw new ArgumentException("Hostname cannot be empty", nameof(parameters));

        _displayName = $"{tcpParams.Hostname}:{tcpParams.Port}";

        try
        {
            Logger.WriteLine($"[TCP] Connecting to {_displayName}...");

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(tcpParams.Hostname, tcpParams.Port);

            _networkStream = _tcpClient.GetStream();
            _networkStream.ReadTimeout = 500; // Same as Serial for consistency

            IsConnected = true;
            Logger.WriteLine($"[TCP] Connected to {_displayName}");

            // Start reader thread (similar to Serial)
            _wantExit = false;
            _readerThread = new Thread(ReaderThreadFunc)
            {
                IsBackground = true,
                Name = "TcpReader"
            };
            _readerThread.Start();
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[TCP] ERROR: Connection failed: {ex.Message}");
            _tcpClient?.Dispose();
            _tcpClient = null;
            throw;
        }
    }

    private void ReaderThreadFunc()
    {
        try
        {
            byte[] buffer = new byte[1024];

            while (!_wantExit)
            {
                try
                {
                    if (_networkStream == null || !_tcpClient!.Connected)
                    {
                        break;
                    }

                    // Try to read data (blocks with ReadTimeout)
                    int bytesRead = _networkStream.Read(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        // Create data array
                        byte[] data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);

                        // Fire event
                        try
                        {
                            DataReceived?.Invoke(this, data);
                        }
                        catch (Exception ex)
                        {
                            Logger.WriteLine($"[TCP] ERROR: DataReceived event failed: {ex.Message}");
                        }
                    }
                    else if (bytesRead == 0)
                    {
                        // Connection closed by remote host
                        Logger.WriteLine("[TCP] Connection closed by remote host");
                        break;
                    }
                }
                catch (IOException ex) when (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut)
                {
                    // Normal read timeout, continue
                    continue;
                }
                catch (IOException ex)
                {
                    Logger.WriteLine($"[TCP] CRITICAL: I/O error: {ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    if (!_wantExit)
                    {
                        Logger.WriteLine($"[TCP] CRITICAL: Reader thread exception: {ex.GetType().Name}: {ex.Message}");
                    }
                    break;
                }
            }

            Logger.WriteLine($"[TCP] Reader thread stopped (wantExit={_wantExit})");

            // If thread is stopping unexpectedly, trigger disconnect
            if (!_wantExit && IsConnected)
            {
                Logger.WriteLine("[TCP] WARNING: Reader thread stopped unexpectedly, forcing disconnect");
                Task.Run(() => Disconnect());
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[TCP] FATAL: Reader thread crashed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task WriteAsync(byte[] data)
    {
        if (_networkStream == null || !IsConnected)
            throw new InvalidOperationException("TCP connection is not established");

        await _networkStream.WriteAsync(data, 0, data.Length);
        await _networkStream.FlushAsync();

        // Delay after write (like Serial)
        await Task.Delay(100);
    }

    public void Disconnect()
    {
        try
        {
            Logger.WriteLine("[TCP] Disconnecting...");

            // Stop reader thread
            _wantExit = true;

            if (_readerThread != null && _readerThread.IsAlive)
            {
                if (!_readerThread.Join(3000))
                {
                    Logger.WriteLine("[TCP] WARNING: Reader thread did not stop");
                }
            }

            _networkStream?.Close();
            _networkStream?.Dispose();
            _networkStream = null;

            _tcpClient?.Close();
            _tcpClient?.Dispose();
            _tcpClient = null;

            _readerThread = null;
            Logger.WriteLine("[TCP] Disconnected");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[TCP] ERROR during disconnect: {ex.Message}");
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
