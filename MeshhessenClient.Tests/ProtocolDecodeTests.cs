using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;
// Alias the app models so `NodeInfo` still resolves to the protobuf type below
using ModelNodeInfo = MeshhessenClient.Models.NodeInfo;
using MessageItem = MeshhessenClient.Models.MessageItem;

namespace MeshhessenClient.Tests;

/// <summary>
/// End-to-end decode tests: raw framed bytes in → typed events out. Exercises the
/// serial framing (0x94 0xC3 + length prefix), FromRadio protobuf parsing, portnum
/// routing and payload decode. This is the layer where protobuf field-number
/// regressions have bitten historically.
/// </summary>
public class ProtocolDecodeTests
{
    private const byte Start1 = 0x94;
    private const byte Start2 = 0xC3;

    private static (MeshtasticProtocolService svc, FakeConnectionService fake) NewService()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        return (new MeshtasticProtocolService(fake), fake);
    }

    /// <summary>Wraps a FromRadio in the Meshtastic serial frame the parser expects.</summary>
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

    private static FromRadio TextPacket(uint from, uint to, uint id, string text,
        uint hopStart = 3, uint hopLimit = 3, float snr = 0f) => new()
    {
        Packet = new MeshPacket
        {
            From = from,
            To = to,
            Id = id,
            Channel = 0,
            HopStart = hopStart,
            HopLimit = hopLimit,
            RxSnr = snr,
            Decoded = new Data
            {
                Portnum = PortNum.TextMessageApp,   // official proto: portnum is the PortNum enum
                Payload = ByteString.CopyFromUtf8(text),
            }
        }
    };

    [Fact]
    public void TextMessage_DecodesIntoMessageReceived()
    {
        var (svc, fake) = NewService();
        MessageItem? got = null;
        svc.MessageReceived += (_, m) => got = m;

        // hopStart==hopLimit => 0 hops (direct), so RxSnr is surfaced
        fake.Receive(Frame(TextPacket(0x11223344, 0xFFFFFFFF, 0x0A0B0C0D, "hello mesh", snr: 5.25f)));

        Assert.NotNull(got);
        Assert.Equal("hello mesh", got!.Message);
        Assert.Equal(0x11223344u, got.FromId);
        Assert.Equal(0xFFFFFFFFu, got.ToId);
        Assert.Equal(0x0A0B0C0Du, got.Id);
        Assert.Equal(0, got.HopCount);
        Assert.Equal(5.25f, got.RxSnr);
    }

    [Fact]
    public void TextMessage_WithHops_ReportsHopCountAndNoSnr()
    {
        var (svc, fake) = NewService();
        MessageItem? got = null;
        svc.MessageReceived += (_, m) => got = m;

        // hopStart 3, hopLimit 1 => 2 hops (relayed): SNR/RSSI must be suppressed
        fake.Receive(Frame(TextPacket(0x22, 0x33, 0x44, "relayed", hopStart: 3, hopLimit: 1, snr: 9f)));

        Assert.NotNull(got);
        Assert.Equal(2, got!.HopCount);
        Assert.Null(got.RxSnr);
    }

    [Fact]
    public void Reaction_DecodesIntoReactionReceived()
    {
        var (svc, fake) = NewService();
        (uint ReplyId, string Emoji, uint From)? got = null;
        svc.ReactionReceived += (_, r) => got = r;

        var fr = new FromRadio
        {
            Packet = new MeshPacket
            {
                From = 0xAABBCCDD,
                To = 0x1,
                Id = 0x99,
                Decoded = new Data
                {
                    Portnum = PortNum.TextMessageApp,   // official proto: portnum is the PortNum enum
                    Emoji = 1,                 // reaction flag
                    ReplyId = 0x0000CAFE,
                    Payload = ByteString.CopyFromUtf8("👍"),
                }
            }
        };
        fake.Receive(Frame(fr));

        Assert.NotNull(got);
        Assert.Equal(0x0000CAFEu, got!.Value.ReplyId);
        Assert.Equal("👍", got.Value.Emoji);
        Assert.Equal(0xAABBCCDDu, got.Value.From);
    }

    [Fact]
    public void NodeInfo_DecodesIntoNodeInfoReceived()
    {
        var (svc, fake) = NewService();
        ModelNodeInfo? got = null;
        svc.NodeInfoReceived += (_, n) => got = n;

        var fr = new FromRadio
        {
            NodeInfo = new NodeInfo
            {
                Num = 0x11223344,
                User = new User { Id = "!11223344", LongName = "Test Node", ShortName = "TEST" },
            }
        };
        fake.Receive(Frame(fr));

        Assert.NotNull(got);
        Assert.Equal(0x11223344u, got!.NodeId);
        Assert.Equal("TEST", got.ShortName);
        Assert.Equal("Test Node", got.LongName);
    }

    [Fact]
    public void Framing_SkipsLeadingAsciiGarbageBeforeFrame()
    {
        var (svc, fake) = NewService();
        MessageItem? got = null;
        svc.MessageReceived += (_, m) => got = m;

        var frame = Frame(TextPacket(0x1, 0x2, 0x3, "after garbage"));
        var garbage = System.Text.Encoding.ASCII.GetBytes("DEBUG boot ok\r\n");
        fake.Receive(garbage.Concat(frame).ToArray());

        Assert.NotNull(got);
        Assert.Equal("after garbage", got!.Message);
    }

    [Fact]
    public void Framing_TwoBackToBackFramesBothDecode()
    {
        var (svc, fake) = NewService();
        var texts = new List<string>();
        svc.MessageReceived += (_, m) => texts.Add(m.Message);

        var buf = Frame(TextPacket(0x1, 0x2, 0x10, "first"))
            .Concat(Frame(TextPacket(0x1, 0x2, 0x11, "second"))).ToArray();
        fake.Receive(buf);

        Assert.Equal(new[] { "first", "second" }, texts);
    }

    [Fact]
    public void Framing_FrameSplitAcrossTwoReceivesReassembles()
    {
        var (svc, fake) = NewService();
        MessageItem? got = null;
        svc.MessageReceived += (_, m) => got = m;

        var frame = Frame(TextPacket(0x1, 0x2, 0x3, "split packet"));
        fake.Receive(frame.Take(3).ToArray());     // partial: mid-header, nothing yet
        Assert.Null(got);
        fake.Receive(frame.Skip(3).ToArray());     // remainder completes the frame
        Assert.NotNull(got);
        Assert.Equal("split packet", got!.Message);
    }

    [Fact]
    public void Framing_MalformedProtobufInValidFrame_DoesNotThrowOrEmit()
    {
        var (svc, fake) = NewService();
        var emitted = false;
        svc.MessageReceived += (_, _) => emitted = true;

        // Valid frame header, length 3, but the "protobuf" body is garbage
        var frame = new byte[] { Start1, Start2, 0x00, 0x03, 0xFF, 0xFF, 0xFF };
        var ex = Record.Exception(() => fake.Receive(frame));

        Assert.Null(ex);        // ProcessPacket swallows the parse error
        Assert.False(emitted);  // and emits nothing
    }
}
