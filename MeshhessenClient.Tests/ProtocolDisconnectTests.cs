using Google.Protobuf;
using Meshtastic.Protobufs;
using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// The client used to just drop the transport. The firmware then kept the API session open until its
/// 15-minute idle timeout — and nRF52 firmware before 2.8.0 kept Bluetooth switched off for that long after a
/// USB-serial session ("Bluetooth geht nach serieller Verbindung nicht", bug report 2026-11-16). Disconnect now
/// announces the end of the session with <c>ToRadio.disconnect</c>, like the Python CLI and the Android app.
/// </summary>
public class ProtocolDisconnectTests
{
    private static ToRadio ParseFramed(byte[] frame)
    {
        Assert.Equal(0x94, frame[0]);
        Assert.Equal(0xC3, frame[1]);
        int len = (frame[2] << 8) | frame[3];
        Assert.Equal(frame.Length - 4, len);
        return ToRadio.Parser.ParseFrom(frame.AsSpan(4).ToArray());
    }

    [Fact]
    public async Task Disconnect_sends_ToRadio_disconnect_once_on_serial()
    {
        var fake = new FakeConnectionService(ConnectionType.Serial);
        var svc = new MeshtasticProtocolService(fake);
        await fake.ConnectAsync(new SerialConnectionParameters { PortName = "COM9" });

        svc.Disconnect();
        var toRadio = ParseFramed(Assert.Single(fake.Written));
        Assert.Equal(ToRadio.PayloadVariantOneofCase.Disconnect, toRadio.PayloadVariantCase);
        Assert.True(toRadio.Disconnect);

        svc.Disconnect();                                   // second call (close after link loss) is silent
        Assert.Single(fake.Written);
    }

    [Fact]
    public async Task Disconnect_sends_raw_protobuf_on_bluetooth()
    {
        var fake = new FakeConnectionService(ConnectionType.Bluetooth);
        var svc = new MeshtasticProtocolService(fake);
        await fake.ConnectAsync(new BluetoothConnectionParameters { DeviceAddress = 1, DeviceName = "n" });

        svc.Disconnect();
        var raw = Assert.Single(fake.Written);
        var toRadio = ToRadio.Parser.ParseFrom(raw);        // BLE: no 0x94 0xC3 framing
        Assert.True(toRadio.Disconnect);
    }

    [Fact]
    public void Disconnect_without_a_live_link_sends_nothing()
    {
        var fake = new FakeConnectionService(ConnectionType.Serial);
        var svc = new MeshtasticProtocolService(fake);     // never connected: the device already dropped us
        svc.Disconnect();
        Assert.Empty(fake.Written);
    }
}
