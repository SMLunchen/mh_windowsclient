using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;

namespace MeshhessenClient.Tests;

/// <summary>
/// Regression tests for the "positions keep disappearing" bug: NodeInfo snapshots
/// (RF User broadcasts and device-NodeDB entries) used to REPLACE the known-node
/// entry, wiping every field the snapshot didn't carry — most visibly the position.
/// </summary>
public class NodePositionRetentionTests
{
    private const byte Start1 = 0x94;
    private const byte Start2 = 0xC3;

    private static (MeshtasticProtocolService svc, FakeConnectionService fake) NewService()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        return (new MeshtasticProtocolService(fake), fake);
    }

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

    private static FromRadio UserBroadcast(uint from, string shortName = "TST") => new()
    {
        Packet = new MeshPacket
        {
            From = from, To = 0xFFFFFFFF, Id = 7, HopStart = 3, HopLimit = 3,
            Decoded = new Data
            {
                Portnum = PortNum.NodeinfoApp,
                Payload = new User
                {
                    Id = $"!{from:x8}", ShortName = shortName, LongName = $"{shortName} Node",
                }.ToByteString(),
            },
        },
    };

    private static FromRadio PositionPacket(uint from, double lat, double lon) => new()
    {
        Packet = new MeshPacket
        {
            From = from, To = 0xFFFFFFFF, Id = 8, HopStart = 3, HopLimit = 3,
            Decoded = new Data
            {
                Portnum = PortNum.PositionApp,
                Payload = new Position
                {
                    LatitudeI = (int)(lat * 1e7), LongitudeI = (int)(lon * 1e7), Altitude = 123,
                }.ToByteString(),
            },
        },
    };

    private static FromRadio DeviceNodeInfo(uint num, bool withPosition, bool favorite = false)
    {
        var ni = new NodeInfo
        {
            Num = num,
            User = new User { Id = $"!{num:x8}", ShortName = "TST", LongName = "TST Node" },
            IsFavorite = favorite,
        };
        if (withPosition)
            ni.Position = new Position { LatitudeI = 506000000, LongitudeI = 91000000 };
        return new FromRadio { NodeInfo = ni };
    }

    [Fact]
    public void Rf_nodeinfo_broadcast_keeps_known_position()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(UserBroadcast(0x42)));               // node becomes known
        fake.Receive(Frame(PositionPacket(0x42, 50.1, 8.7)));   // position arrives

        fake.Receive(Frame(UserBroadcast(0x42)));               // periodic broadcast (no position!)

        var node = svc.GetKnownNodes()[0x42];
        Assert.Equal(50.1, node.Latitude!.Value, 3);
        Assert.Equal(8.7, node.Longitude!.Value, 3);
    }

    [Fact]
    public void Device_nodedb_entry_without_position_keeps_known_position()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(UserBroadcast(0x42)));
        fake.Receive(Frame(PositionPacket(0x42, 50.1, 8.7)));

        // reconnect scenario: device NodeDB evicted the position
        fake.Receive(Frame(DeviceNodeInfo(0x42, withPosition: false)));

        var node = svc.GetKnownNodes()[0x42];
        Assert.Equal(50.1, node.Latitude!.Value, 3);
        Assert.Equal(8.7, node.Longitude!.Value, 3);
    }

    [Fact]
    public void Device_nodedb_entry_with_position_updates_it()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(UserBroadcast(0x42)));               // known, no position yet
        fake.Receive(Frame(DeviceNodeInfo(0x42, withPosition: true)));

        var node = svc.GetKnownNodes()[0x42];
        Assert.Equal(50.6, node.Latitude!.Value, 3);
        Assert.Equal(9.1, node.Longitude!.Value, 3);
    }

    [Fact]
    public void Buffered_live_position_wins_over_nodedb_snapshot()
    {
        // A live position packet buffered before the node is known is FRESHER than the
        // device's NodeDB snapshot — it must win when both arrive.
        var (svc, fake) = NewService();
        fake.Receive(Frame(PositionPacket(0x42, 50.1, 8.7)));   // buffered (node unknown)
        fake.Receive(Frame(DeviceNodeInfo(0x42, withPosition: true)));

        var node = svc.GetKnownNodes()[0x42];
        Assert.Equal(50.1, node.Latitude!.Value, 3);
        Assert.Equal(8.7, node.Longitude!.Value, 3);
    }

    [Fact]
    public void Rf_user_packet_does_not_reset_device_flags()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(DeviceNodeInfo(0x42, withPosition: true, favorite: true)));

        fake.Receive(Frame(UserBroadcast(0x42)));   // RF packet carries no favorite flag

        Assert.True(svc.GetKnownNodes()[0x42].IsFavorite);
    }
}
