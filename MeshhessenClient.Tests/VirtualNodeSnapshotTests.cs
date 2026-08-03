using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;

namespace MeshhessenClient.Tests;

/// <summary>
/// Guards the Virtual Node config-replay fix: the ProtocolService records a
/// transport-agnostic snapshot of the device's config/node set (my_info, metadata,
/// configs, channels, nodes) from the FromRadio stream, so the Virtual Node can
/// replay it to a TCP client that connects at any time. The historic bug was that
/// this snapshot filled only over serial/TCP and only if captured live during init —
/// over BLE it never filled, so replay was a permanent 0 ch / 0 nodes.
/// </summary>
public class VirtualNodeSnapshotTests
{
    private const byte Start1 = 0x94;
    private const byte Start2 = 0xC3;

    private static byte[] Frame(FromRadio fr)
    {
        var payload = fr.ToByteArray();
        var frame = new byte[4 + payload.Length];
        frame[0] = Start1;
        frame[1] = Start2;
        frame[2] = (byte)(payload.Length >> 8);
        frame[3] = (byte)(payload.Length & 0xFF);
        Array.Copy(payload, 0, frame, 4, payload.Length);
        return frame;
    }

    // A representative want_config response: my_info, metadata, one config, one
    // module config, two channels, three nodes, then config_complete.
    private static FromRadio[] ConfigStream() =>
    [
        new() { MyInfo = new MyNodeInfo { MyNodeNum = 0x1111 } },
        new() { Metadata = new DeviceMetadata { FirmwareVersion = "2.7.0" } },
        new() { Config = new Config { Lora = new Config.Types.LoRaConfig() } },
        new() { ModuleConfig = new ModuleConfig { Mqtt = new ModuleConfig.Types.MQTTConfig() } },
        new() { Channel = new Channel { Index = 0 } },
        new() { Channel = new Channel { Index = 1 } },
        new() { NodeInfo = new NodeInfo { Num = 0x1111 } },
        new() { NodeInfo = new NodeInfo { Num = 0x2222 } },
        new() { NodeInfo = new NodeInfo { Num = 0x3333 } },
        new() { ConfigCompleteId = 42 },
    ];

    [Fact]
    public void Snapshot_FillsFromFramedSerialTcpStream()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);

        foreach (var fr in ConfigStream())
            fake.Receive(Frame(fr));

        var snap = svc.GetVirtualNodeSnapshot();
        Assert.True(snap.IsReady);
        Assert.NotNull(snap.MyInfo);
        Assert.NotNull(snap.Metadata);
        Assert.Single(snap.Configs);
        Assert.Single(snap.ModuleConfigs);
        Assert.Equal(2, snap.Channels.Count);
        Assert.Equal(3, snap.Nodes.Count);
    }

    [Fact]
    public void Snapshot_FillsIdenticallyOverBle()
    {
        // BLE delivers unframed FromRadio payloads (one per read). This is the
        // exact case that used to leave the snapshot — and thus the VN replay —
        // permanently empty.
        var fake = new FakeConnectionService(ConnectionType.Bluetooth);
        var svc = new MeshtasticProtocolService(fake);

        foreach (var fr in ConfigStream())
            fake.Receive(fr.ToByteArray());

        var snap = svc.GetVirtualNodeSnapshot();
        Assert.True(snap.IsReady);
        Assert.NotNull(snap.MyInfo);
        Assert.Equal(2, snap.Channels.Count);
        Assert.Equal(3, snap.Nodes.Count);
    }

    [Fact]
    public void Ble_AlsoFiresRawFrameReceived_ForLiveBroadcast()
    {
        // The VN live-broadcast path forwards RawFrameReceived to connected clients;
        // over BLE this event never fired before the fix.
        var fake = new FakeConnectionService(ConnectionType.Bluetooth);
        var svc = new MeshtasticProtocolService(fake);
        int framed = 0;
        byte[]? last = null;
        svc.RawFrameReceived += (_, f) => { framed++; last = f; };

        fake.Receive(new FromRadio { NodeInfo = new NodeInfo { Num = 7 } }.ToByteArray());

        Assert.Equal(1, framed);
        Assert.NotNull(last);
        Assert.Equal(Start1, last![0]);   // properly framed for the VN wire format
        Assert.Equal(Start2, last[1]);
    }

    [Fact]
    public void Snapshot_KeepsOnlyLatestNodeInfoPerNode()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);

        fake.Receive(Frame(new FromRadio { NodeInfo = new NodeInfo { Num = 5 } }));
        fake.Receive(Frame(new FromRadio { NodeInfo = new NodeInfo { Num = 5 } }));
        fake.Receive(Frame(new FromRadio { NodeInfo = new NodeInfo { Num = 6 } }));

        Assert.Equal(2, svc.GetVirtualNodeSnapshot().Nodes.Count);
    }

    [Fact]
    public void Snapshot_SynthesizesOwnNode_WhenDeviceOmitsIt()
    {
        // Device sends my_info (own num 0x99) but never a NodeInfo for itself.
        // Android needs its own node in the DB to enable the node list + sending,
        // so the snapshot must add a synthesized own node.
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);

        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 0x99 } }));
        fake.Receive(Frame(new FromRadio { NodeInfo = new NodeInfo { Num = 0x1 } }));
        fake.Receive(Frame(new FromRadio { NodeInfo = new NodeInfo { Num = 0x2 } }));
        fake.Receive(Frame(new FromRadio { ConfigCompleteId = 1 }));

        var snap = svc.GetVirtualNodeSnapshot();
        Assert.Equal(3, snap.Nodes.Count);                       // 2 device + 1 synthesized
        Assert.Contains(snap.Nodes, f => NodeNumOf(f) == 0x99);  // own node present
    }

    [Fact]
    public void Snapshot_DoesNotSynthesize_WhenDevicePresentsOwnNode()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);

        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 0x1111 } }));
        fake.Receive(Frame(new FromRadio { NodeInfo = new NodeInfo { Num = 0x1111 } }));
        fake.Receive(Frame(new FromRadio { NodeInfo = new NodeInfo { Num = 0x2222 } }));
        fake.Receive(Frame(new FromRadio { ConfigCompleteId = 1 }));

        Assert.Equal(2, svc.GetVirtualNodeSnapshot().Nodes.Count); // no duplicate own node
    }

    private static uint NodeNumOf(byte[] frame)
    {
        var fr = FromRadio.Parser.ParseFrom(frame[4..]);
        return fr.PayloadVariantCase == FromRadio.PayloadVariantOneofCase.NodeInfo ? fr.NodeInfo.Num : 0;
    }
}
