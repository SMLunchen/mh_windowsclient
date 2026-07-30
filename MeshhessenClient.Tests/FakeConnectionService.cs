using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// In-memory <see cref="IConnectionService"/> for decode tests: no real device.
/// <see cref="Receive"/> raises DataReceived synchronously, so the protocol
/// service has fully processed the bytes (and fired its events) by the time it
/// returns. Bytes passed to WriteAsync are captured for send-side assertions.
/// </summary>
public sealed class FakeConnectionService : IConnectionService
{
    public FakeConnectionService(ConnectionType type = ConnectionType.Tcp) => Type = type;

    public ConnectionType Type { get; }
    public string DisplayName => "fake";
    public bool IsConnected { get; private set; }

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<byte[]>? DataReceived;

    public readonly List<byte[]> Written = new();

    /// <summary>Feed bytes to the protocol service as if the device sent them.</summary>
    public void Receive(byte[] data) => DataReceived?.Invoke(this, data);

    public Task ConnectAsync(ConnectionParameters parameters)
    {
        IsConnected = true;
        ConnectionStateChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        IsConnected = false;
        ConnectionStateChanged?.Invoke(this, false);
    }

    public Task WriteAsync(byte[] data)
    {
        Written.Add(data);
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
