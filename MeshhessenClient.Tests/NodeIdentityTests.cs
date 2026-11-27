using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;
using ModelNodeInfo = MeshhessenClient.Models.NodeInfo;

namespace MeshhessenClient.Tests;

/// <summary>
/// Node names must survive what the device NodeDB forgets. A NodeDB row without user (the device heard
/// the node by id only, e.g. PKI packets only this client could decode) or a User packet with empty
/// names used to turn a known node back into a placeholder ("Node-xxxx"); and a fresh connection lost
/// every name the device no longer carried. Also covers the per-packet signal refresh (issue #85).
/// </summary>
public class NodeIdentityTests : IDisposable
{
    private const byte Start1 = 0x94;
    private const byte Start2 = 0xC3;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mh_identity_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

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

    private static MeshPacket Direct(uint from, Data data, float snr = 7.5f, int rssi = -80, uint id = 7) => new()
    {
        From = from, To = 0xFFFFFFFF, Id = id, HopStart = 3, HopLimit = 3, RxSnr = snr, RxRssi = rssi,
        Decoded = data,
    };

    private static FromRadio UserBroadcast(uint from, string shortName, string longName,
        HardwareModel hw = HardwareModel.Unset,
        Config.Types.DeviceConfig.Types.Role role = Config.Types.DeviceConfig.Types.Role.Client) => new()
    {
        Packet = Direct(from, new Data
        {
            Portnum = PortNum.NodeinfoApp,
            Payload = new User { Id = $"!{from:x8}", ShortName = shortName, LongName = longName, HwModel = hw, Role = role }.ToByteString(),
        }),
    };

    private static FromRadio NodeDbRow(uint num, User? user) => new()
    {
        NodeInfo = new NodeInfo { Num = num, User = user },
    };

    private static FromRadio Text(uint from, string text, float snr, int rssi, uint hopStart = 3, uint hopLimit = 3, uint id = 9) => new()
    {
        Packet = new MeshPacket
        {
            From = from, To = 0xFFFFFFFF, Id = id, HopStart = hopStart, HopLimit = hopLimit, RxSnr = snr, RxRssi = rssi,
            Decoded = new Data { Portnum = PortNum.TextMessageApp, Payload = ByteString.CopyFromUtf8(text) },
        },
    };

    // ── placeholder detection ────────────────────────────────────────────────

    [Theory]
    [InlineData("", true)]
    [InlineData("Unknown", true)]
    [InlineData("!0000abcd", true)]
    [InlineData("Node-abcd", true)]
    [InlineData("Node-0000abcd", true)]
    [InlineData("Meshtastic abcd", true)]
    [InlineData("Heltec Dach", false)]
    [InlineData("Node-Frankfurt", false)]
    public void Placeholder_names_are_recognised(string name, bool placeholder)
    {
        Assert.Equal(placeholder, ModelNodeInfo.IsPlaceholderName(name, 0xabcd));
    }

    // ── names never regress ──────────────────────────────────────────────────

    [Fact]
    public void NodeDb_row_without_user_keeps_known_names()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(UserBroadcast(0x42, "ABC", "ABC Node")));
        fake.Receive(Frame(NodeDbRow(0x42, user: null)));   // reconnect: device knows the node by id only

        var node = svc.GetKnownNodes()[0x42];
        Assert.Equal("ABC Node", node.Name);
        Assert.Equal("ABC Node", node.LongName);
        Assert.Equal("ABC", node.ShortName);
    }

    [Fact]
    public void User_packet_with_empty_names_keeps_known_names()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(UserBroadcast(0x42, "ABC", "ABC Node")));
        fake.Receive(Frame(UserBroadcast(0x42, "", "")));   // protobuf strings are "" not null

        var node = svc.GetKnownNodes()[0x42];
        Assert.Equal("ABC Node", node.Name);
        Assert.Equal("ABC", node.ShortName);
    }

    [Fact]
    public void NodeDb_row_with_empty_user_keeps_known_names()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(UserBroadcast(0x42, "ABC", "ABC Node")));
        fake.Receive(Frame(NodeDbRow(0x42, new User { Id = "!00000042" })));

        Assert.Equal("ABC Node", svc.GetKnownNodes()[0x42].Name);
    }

    [Fact]
    public void Unknown_node_without_user_gets_the_meshtastic_placeholder()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(NodeDbRow(0x1234abcd, user: null)));

        var node = svc.GetKnownNodes()[0x1234abcd];
        Assert.Equal(ModelNodeInfo.DefaultName(0x1234abcd), node.Name);
        Assert.True(ModelNodeInfo.IsPlaceholderName(node.Name, node.NodeId));
        Assert.Null(svc.GetKnownNodeName(0x1234abcd));   // chat/DM history must not adopt it as a real name
    }

    // ── identity survives a new connection via the local DB ─────────────────

    [Fact]
    public void Identity_survives_a_new_connection_via_local_db()
    {
        using var db = new TelemetryDatabaseService(_dbPath);

        var (first, fakeA) = NewService();
        first.SetDatabase(db);
        fakeA.Receive(Frame(UserBroadcast(0x42, "ABC", "ABC Node",
            HardwareModel.HeltecV3, Config.Types.DeviceConfig.Types.Role.Router)));

        // New connection = new protocol service with an empty node list; the device only has the id.
        var (second, fakeB) = NewService();
        second.SetDatabase(db);
        fakeB.Receive(Frame(NodeDbRow(0x42, user: null)));

        var node = second.GetKnownNodes()[0x42];
        Assert.Equal("ABC Node", node.Name);
        Assert.Equal("ABC", node.ShortName);
        Assert.Equal("HeltecV3", node.HardwareModel);
        Assert.Equal("Router", node.Role);
    }

    [Fact]
    public void Stored_identity_does_not_override_a_newer_real_user()
    {
        using var db = new TelemetryDatabaseService(_dbPath);
        db.UpsertNodeIdentity(new TelemetryDatabaseService.NodeIdentity(0x42, "OLD", "Old Name", "", ""));

        var (svc, fake) = NewService();
        svc.SetDatabase(db);
        fake.Receive(Frame(NodeDbRow(0x42, new User { Id = "!00000042", ShortName = "NEW", LongName = "New Name" })));

        Assert.Equal("New Name", svc.GetKnownNodes()[0x42].Name);
        Assert.Equal("New Name", db.GetNodeIdentities()[0x42].LongName);   // and the store follows
    }

    [Fact]
    public void Identity_store_skips_rows_without_names_and_keeps_hardware()
    {
        using var db = new TelemetryDatabaseService(_dbPath);
        db.UpsertNodeIdentity(new TelemetryDatabaseService.NodeIdentity(1, "ABC", "ABC Node", "HeltecV3", "Router"));
        db.UpsertNodeIdentity(new TelemetryDatabaseService.NodeIdentity(1, "ABC", "ABC Node 2", "", ""));   // no hw/role → keep
        db.UpsertNodeIdentity(new TelemetryDatabaseService.NodeIdentity(2, "", "", "RakWireless", ""));      // nameless → skipped

        var ids = db.GetNodeIdentities();
        Assert.Equal("ABC Node 2", ids[1].LongName);
        Assert.Equal("HeltecV3", ids[1].HwModel);
        Assert.Equal("Router", ids[1].Role);
        Assert.False(ids.ContainsKey(2));
    }

    // ── manual "request user info" exchanges OUR user, as Android does ──────

    private static User OwnUser() => new()
    {
        Id = "!00000011", ShortName = "ME", LongName = "Me Node",
        HwModel = HardwareModel.HeltecV3, Role = Config.Types.DeviceConfig.Types.Role.Router,
    };

    private static async Task<User> SentUserInfoRequest(MeshtasticProtocolService svc, FakeConnectionService fake)
    {
        await svc.RequestNodeInfoAsync(0x42, MeshtasticProtocolService.InfoRequestType.UserInfo);

        var frame = fake.Written.Last();
        var packet = ToRadio.Parser.ParseFrom(frame.AsSpan(4).ToArray()).Packet;
        Assert.Equal(0x42u, packet.To);
        Assert.Equal(PortNum.NodeinfoApp, packet.Decoded.Portnum);
        Assert.True(packet.Decoded.WantResponse);
        // An empty payload made the peer overwrite our NodeDB entry with empty names (firmware
        // NodeDB::updateUser copies names, hardware and role unconditionally).
        Assert.False(packet.Decoded.Payload.IsEmpty);
        return User.Parser.ParseFrom(packet.Decoded.Payload);
    }

    [Fact]
    public async Task Manual_user_info_request_carries_our_own_names_hardware_and_role()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 0x11 } }));
        fake.Receive(Frame(NodeDbRow(0x11, OwnUser())));   // the device's NodeDB row of our own node

        var user = await SentUserInfoRequest(svc, fake);
        Assert.Equal("!00000011", user.Id);
        Assert.Equal("Me Node", user.LongName);             // used to be "!00000011" — names were never copied
        Assert.Equal("ME", user.ShortName);                 // used to be "0011"
        Assert.Equal(HardwareModel.HeltecV3, user.HwModel);
        Assert.Equal(Config.Types.DeviceConfig.Types.Role.Router, user.Role);
    }

    [Fact]
    public async Task Own_user_falls_back_to_the_known_node_entry()
    {
        // Config-only reconnect: my_info arrives after the (carried-over) node list, the NodeDB is not
        // downloaded again — the names must come from the known-node entry of our own node.
        var (svc, fake) = NewService();
        fake.Receive(Frame(NodeDbRow(0x11, OwnUser())));
        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 0x11 } }));

        var user = await SentUserInfoRequest(svc, fake);
        Assert.Equal("Me Node", user.LongName);
        Assert.Equal("ME", user.ShortName);
        Assert.Equal(HardwareModel.HeltecV3, user.HwModel);
        Assert.Equal(Config.Types.DeviceConfig.Types.Role.Router, user.Role);
    }

    [Fact]
    public async Task Own_user_without_any_names_still_identifies_us()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 0x11 } }));

        var user = await SentUserInfoRequest(svc, fake);
        Assert.Equal("!00000011", user.Id);
        Assert.False(string.IsNullOrEmpty(user.LongName));
        Assert.False(string.IsNullOrEmpty(user.ShortName));
    }

    // ── every decoded packet refreshes signal + last seen (issue #85) ───────

    [Fact]
    public void Text_and_traceroute_packets_refresh_signal_and_notify_the_ui()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(UserBroadcast(0x42, "ABC", "ABC Node")));

        int events = 0;
        svc.NodeInfoReceived += (_, n) => { if (n.NodeId == 0x42) events++; };

        fake.Receive(Frame(Text(0x42, "hi", snr: 7.5f, rssi: -80)));
        var node = svc.GetKnownNodes()[0x42];
        Assert.Equal(7.5f.ToString("F1"), node.Snr);
        Assert.Equal("-80", node.Rssi);
        Assert.Equal(7.5f, node.SnrValue);
        Assert.Equal(0, node.HopsToReach);
        Assert.True(node.LastSeenDateTime.HasValue && (DateTime.Now - node.LastSeenDateTime.Value).TotalSeconds < 5);
        Assert.Equal(1, events);

        // Traceroute reply from the node: no dedicated node update before → row stayed stale
        var reply = new FromRadio
        {
            Packet = Direct(0x42, new Data
            {
                Portnum = PortNum.TracerouteApp, RequestId = 77,
                Payload = new RouteDiscovery { SnrTowards = { 40 }, SnrBack = { 13 } }.ToByteString(),
            }, snr: 3.25f, rssi: -91, id: 11),
        };
        fake.Receive(Frame(reply));
        node = svc.GetKnownNodes()[0x42];
        Assert.Equal(3.25f.ToString("F1"), node.Snr);
        Assert.Equal("-91", node.Rssi);
        Assert.Equal(2, events);
    }

    [Fact]
    public void Position_packet_notifies_the_ui_once_only()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(UserBroadcast(0x42, "ABC", "ABC Node")));
        int events = 0;
        svc.NodeInfoReceived += (_, n) => { if (n.NodeId == 0x42) events++; };

        fake.Receive(Frame(new FromRadio
        {
            Packet = Direct(0x42, new Data
            {
                Portnum = PortNum.PositionApp,
                Payload = new Position { LatitudeI = 501000000, LongitudeI = 87000000 }.ToByteString(),
            }, snr: 5f, rssi: -70, id: 12),
        }));

        Assert.Equal(1, events);   // the position handler fires itself; the generic path must not double it
        Assert.Equal(5f.ToString("F1"), svc.GetKnownNodes()[0x42].Snr);
    }

    [Fact]
    public void Relayed_packet_updates_hops_but_not_the_direct_snr()
    {
        var (svc, fake) = NewService();
        fake.Receive(Frame(UserBroadcast(0x42, "ABC", "ABC Node")));
        var before = svc.GetKnownNodes()[0x42].SnrValue;

        fake.Receive(Frame(Text(0x42, "relayed", snr: 2f, rssi: -100, hopStart: 3, hopLimit: 1, id: 13)));

        var node = svc.GetKnownNodes()[0x42];
        Assert.Equal(2, node.HopsToReach);
        Assert.Equal(before, node.SnrValue);          // a relay's SNR is not this node's link quality
        Assert.Equal(2f.ToString("F1"), node.Snr);    // the raw last-hop reading is still recorded
    }
}
