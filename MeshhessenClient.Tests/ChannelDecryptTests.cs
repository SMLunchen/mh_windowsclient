using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using MessageItem = MeshhessenClient.Models.MessageItem;

namespace MeshhessenClient.Tests;

/// <summary>
/// Client-side channel-PSK decryption (AES-CTR). MQTT-relayed packets arrive still
/// channel-encrypted (the device only decrypts its own radio traffic), so the client
/// must decrypt them with the channel key whose hash matches the packet's channel field.
/// </summary>
public class ChannelDecryptTests
{
    private const byte Start1 = 0x94;
    private const byte Start2 = 0xC3;
    private const uint FromNode = 0x9e9f3118;
    private const uint PacketId = 0xEA01136D;

    // A fixed 16-byte channel PSK for the test channel.
    private static readonly byte[] Psk =
        { 0x01, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff };
    private const string ChannelName = "TestCh";

    private static byte[] Frame(FromRadio fr)
    {
        var payload = fr.ToByteArray();
        var frame = new byte[4 + payload.Length];
        frame[0] = Start1; frame[1] = Start2;
        frame[2] = (byte)(payload.Length >> 8);
        frame[3] = (byte)(payload.Length & 0xFF);
        System.Array.Copy(payload, 0, frame, 4, payload.Length);
        return frame;
    }

    private static int ChannelHash()
    {
        byte h = 0;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(ChannelName)) h ^= b;
        foreach (var b in Psk) h ^= b;
        return h;
    }

    // Meshtastic channel crypto: AES-CTR, nonce = [packetId 8 LE][fromNode 4 LE][0 4].
    private static byte[] ChannelEncrypt(byte[] plain)
    {
        var nonce = new byte[16];
        System.BitConverter.GetBytes((ulong)PacketId).CopyTo(nonce, 0);
        System.BitConverter.GetBytes(FromNode).CopyTo(nonce, 8);
        var ctr = new SicBlockCipher(new AesEngine());
        ctr.Init(true, new ParametersWithIV(new KeyParameter(Psk), nonce));
        var outBuf = new byte[plain.Length];
        int full = (plain.Length / 16) * 16;
        for (int i = 0; i < full; i += 16) ctr.ProcessBlock(plain, i, outBuf, i);
        int rem = plain.Length - full;
        if (rem > 0)
        {
            var ib = new byte[16]; System.Array.Copy(plain, full, ib, 0, rem);
            var ob = new byte[16]; ctr.ProcessBlock(ib, 0, ob, 0);
            System.Array.Copy(ob, 0, outBuf, full, rem);
        }
        return outBuf;
    }

    [Fact]
    public void ChannelEncryptedPacket_IsDecryptedWithMatchingChannelKey()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);

        MessageItem? got = null;
        svc.MessageReceived += (_, m) => got = m;

        // 1) The device tells us about the channel (name + PSK).
        fake.Receive(Frame(new FromRadio
        {
            Channel = new Channel
            {
                Index = 1,
                Role = Channel.Types.Role.Primary,
                Settings = new ChannelSettings { Name = ChannelName, Psk = ByteString.CopyFrom(Psk) }
            }
        }));

        // 2) A channel-encrypted text packet arrives (channel field = the channel HASH).
        var inner = new Data { Portnum = PortNum.TextMessageApp, Payload = ByteString.CopyFromUtf8("hallo welt") };
        var cipher = ChannelEncrypt(inner.ToByteArray());
        fake.Receive(Frame(new FromRadio
        {
            Packet = new MeshPacket
            {
                From = FromNode,
                To = 0xFFFFFFFF,
                Id = PacketId,
                Channel = (uint)ChannelHash(),
                Encrypted = ByteString.CopyFrom(cipher)
            }
        }));

        Assert.NotNull(got);
        Assert.Equal("hallo welt", got!.Message);
        Assert.False(got.IsEncrypted);
    }

    [Fact]
    public void ChannelEncryptedPacket_StaysEncrypted_WhenChannelUnknown()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);

        MessageItem? got = null;
        svc.MessageReceived += (_, m) => got = m;

        // No channel configured → cannot decrypt; a hash that isn't ours.
        var inner = new Data { Portnum = PortNum.TextMessageApp, Payload = ByteString.CopyFromUtf8("secret") };
        var cipher = ChannelEncrypt(inner.ToByteArray());
        fake.Receive(Frame(new FromRadio
        {
            Packet = new MeshPacket
            {
                From = FromNode, To = 0xFFFFFFFF, Id = PacketId,
                Channel = 200, Encrypted = ByteString.CopyFrom(cipher)
            }
        }));

        // Broadcast placeholder is still delivered but marked encrypted (not the plaintext).
        Assert.True(got == null || got.IsEncrypted);
    }
}
