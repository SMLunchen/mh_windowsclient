using System;
using System.Threading.Tasks;

namespace MeshhessenClient.Services;

/// <summary>
/// Connection type for Meshtastic devices
/// </summary>
public enum ConnectionType
{
    /// <summary>Serial/USB connection via COM port</summary>
    Serial,

    /// <summary>Bluetooth Low Energy connection</summary>
    Bluetooth,

    /// <summary>TCP/WiFi network connection</summary>
    Tcp
}

/// <summary>
/// Common interface for all connection types (Serial, Bluetooth, TCP)
/// Provides transport-agnostic communication with Meshtastic devices
/// </summary>
public interface IConnectionService : IDisposable
{
    /// <summary>
    /// Connection type identifier for UI and logging
    /// </summary>
    ConnectionType Type { get; }

    /// <summary>
    /// Display name for current connection (e.g., "COM3", "T-Deck BLE", "192.168.1.100:4403")
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// True if connection is established and ready for data transfer
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Fired when connection state changes (true = connected, false = disconnected)
    /// </summary>
    event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>
    /// Fired when data is received from the device
    /// </summary>
    event EventHandler<byte[]>? DataReceived;

    /// <summary>
    /// Establish connection with the given parameters
    /// </summary>
    /// <param name="parameters">Connection-specific parameters</param>
    /// <exception cref="ArgumentException">If parameters are invalid for this connection type</exception>
    /// <exception cref="InvalidOperationException">If already connected</exception>
    Task ConnectAsync(ConnectionParameters parameters);

    /// <summary>
    /// Close the connection and clean up resources
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Write data to the connected device
    /// </summary>
    /// <param name="data">Data to send</param>
    /// <exception cref="InvalidOperationException">If not connected</exception>
    Task WriteAsync(byte[] data);
}

/// <summary>
/// Base class for connection parameters
/// </summary>
public abstract class ConnectionParameters
{
    /// <summary>
    /// Connection type
    /// </summary>
    public ConnectionType Type { get; init; }
}

/// <summary>
/// Parameters for serial port connection
/// </summary>
public class SerialConnectionParameters : ConnectionParameters
{
    /// <summary>
    /// COM port name (e.g., "COM3")
    /// </summary>
    public string PortName { get; init; } = string.Empty;

    /// <summary>
    /// Baud rate (default: 115200)
    /// </summary>
    public int BaudRate { get; init; } = 115200;

    public SerialConnectionParameters()
    {
        Type = ConnectionType.Serial;
    }
}

/// <summary>
/// Parameters for Bluetooth LE connection
/// </summary>
public class BluetoothConnectionParameters : ConnectionParameters
{
    /// <summary>
    /// Bluetooth device address (64-bit)
    /// </summary>
    public ulong DeviceAddress { get; init; }

    /// <summary>
    /// Device name for display purposes
    /// </summary>
    public string DeviceName { get; init; } = string.Empty;

    public BluetoothConnectionParameters()
    {
        Type = ConnectionType.Bluetooth;
    }
}

/// <summary>
/// Parameters for TCP/WiFi connection
/// </summary>
public class TcpConnectionParameters : ConnectionParameters
{
    /// <summary>
    /// Hostname or IP address
    /// </summary>
    public string Hostname { get; init; } = string.Empty;

    /// <summary>
    /// TCP port (default: 4403 - standard Meshtastic port)
    /// </summary>
    public int Port { get; init; } = 4403;

    public TcpConnectionParameters()
    {
        Type = ConnectionType.Tcp;
    }
}
