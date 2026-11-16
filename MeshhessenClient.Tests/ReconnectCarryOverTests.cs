using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;
using ModelNodeInfo = MeshhessenClient.Models.NodeInfo;

namespace MeshhessenClient.Tests;

/// <summary>
/// Config-only reconnect: when the auto-reconnect talks to the same node again, the protocol service keeps the
/// node list from the previous session and asks for config + channels only (want_config_id 69420, firmware
/// SPECIAL_NONCE_ONLY_CONFIG). On a weak Bluetooth path the full node-list download was where most drops
/// happened (bug report 2026-11-14). A different node on the same link gets the full download instead.
/// </summary>
public class ReconnectCarryOverTests
{
    private const uint SameNode = 0x11223344;

    private static byte[] Frame(FromRadio fr)
    {
        var payload = fr.ToByteArray();
        var frame = new byte[4 + payload.Length];
        frame[0] = 0x94; frame[1] = 0xC3;
        frame[2] = (byte)(payload.Length >> 8);
        frame[3] = (byte)(payload.Length & 0xFF);
        Array.Copy(payload, 0, frame, 4, payload.Length);
        return frame;
    }

    private static ToRadio? ParseFramed(byte[] frame)
    {
        if (frame.Length < 4 || frame[0] != 0x94 || frame[1] != 0xC3) return null;   // wakeup bytes etc.
        try { return ToRadio.Parser.ParseFrom(frame.AsSpan(4).ToArray()); }
        catch { return null; }
    }

    private static List<uint> WantConfigIds(FakeConnectionService fake) =>
        fake.Written.Select(ParseFramed)
            .Where(t => t?.PayloadVariantCase == ToRadio.PayloadVariantOneofCase.WantConfigId)
            .Select(t => t!.WantConfigId).ToList();

    private static async Task<List<uint>> WaitForWantConfigAsync(FakeConnectionService fake, int expectedCount)
    {
        for (int i = 0; i < 200; i++)              // up to 10 s (init sleeps 1.5 s before sending)
        {
            var ids = WantConfigIds(fake);
            if (ids.Count >= expectedCount) return ids;
            await Task.Delay(50);
        }
        return WantConfigIds(fake);
    }

    /// <summary>my_info + all 8 channels + config_complete — what the firmware sends for ONLY_CONFIG.</summary>
    private static void FeedConfig(FakeConnectionService fake, uint nodeNum, uint nonce)
    {
        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = nodeNum } }));
        for (int i = 0; i < 8; i++)
        {
            fake.Receive(Frame(new FromRadio
            {
                Channel = new Channel
                {
                    Index = i,
                    Role = i == 0 ? Channel.Types.Role.Primary : Channel.Types.Role.Disabled,
                    Settings = new ChannelSettings { Psk = ByteString.CopyFrom(new byte[] { 1 }) }
                }
            }));
        }
        fake.Receive(Frame(new FromRadio { ConfigCompleteId = nonce }));
    }

    private static ReconnectCarryOver Carry(uint nodeNum) => new(nodeNum, new[]
    {
        new ModelNodeInfo { NodeId = 0xAAAA0001, Id = "!aaaa0001", Name = "Carried One" },
        new ModelNodeInfo { NodeId = 0xAAAA0002, Id = "!aaaa0002", Name = "Carried Two" },
    });

    [Fact]
    public async Task Same_node_requests_config_only_and_keeps_the_carried_nodes()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        await fake.ConnectAsync(new TcpConnectionParameters { Hostname = "h" });
        var fired = new List<uint>();
        int cleared = 0;
        svc.NodeInfoReceived += (_, n) => fired.Add(n.NodeId);
        svc.NodeDbCleared += (_, _) => cleared++;

        var init = svc.InitializeAsync(Carry(SameNode));
        var ids = await WaitForWantConfigAsync(fake, 1);
        Assert.Equal(MeshtasticProtocolService.WantConfigOnlyNonce, Assert.Single(ids));

        FeedConfig(fake, SameNode, MeshtasticProtocolService.WantConfigOnlyNonce);
        await init.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(0, cleared);
        Assert.Single(WantConfigIds(fake));                          // no second (full) request
        var known = svc.GetKnownNodes();
        Assert.True(known.ContainsKey(0xAAAA0001) && known.ContainsKey(0xAAAA0002), "carried nodes kept");
        Assert.DoesNotContain(0xAAAA0001u, fired);                  // already in the UI — not re-fired
        Assert.DoesNotContain(0xAAAA0002u, fired);
    }

    [Fact]
    public async Task Different_node_discards_the_carried_list_and_requests_everything()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        await fake.ConnectAsync(new TcpConnectionParameters { Hostname = "h" });
        int cleared = 0;
        svc.NodeDbCleared += (_, _) => cleared++;

        var init = svc.InitializeAsync(Carry(SameNode));
        await WaitForWantConfigAsync(fake, 1);
        FeedConfig(fake, 0x55667788, MeshtasticProtocolService.WantConfigOnlyNonce);   // a different node answers

        var ids = await WaitForWantConfigAsync(fake, 2);
        Assert.Equal(2, ids.Count);
        Assert.NotEqual(MeshtasticProtocolService.WantConfigOnlyNonce, ids[1]);           // full download now
        Assert.Equal(1, cleared);
        var known = svc.GetKnownNodes().Keys;
        Assert.DoesNotContain(0xAAAA0001u, known);
        Assert.DoesNotContain(0xAAAA0002u, known);

        fake.Receive(Frame(new FromRadio { ConfigCompleteId = ids[1] }));
        await init.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Without_carry_over_a_random_nonce_is_used()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        await fake.ConnectAsync(new TcpConnectionParameters { Hostname = "h" });

        var init = svc.InitializeAsync();
        var ids = await WaitForWantConfigAsync(fake, 1);
        Assert.NotEqual(MeshtasticProtocolService.WantConfigOnlyNonce, Assert.Single(ids));

        FeedConfig(fake, SameNode, ids[0]);
        await init.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
