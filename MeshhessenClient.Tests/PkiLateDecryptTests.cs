using System.IO;
using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;
using ModelMessageItem = MeshhessenClient.Models.MessageItem;

namespace MeshhessenClient.Tests;

/// <summary>
/// End-to-end test for the retroactive PKI-DM decryption:
/// an encrypted DM to us that we can't decrypt (missing sender key) is buffered,
/// a NodeInfo request goes out, and once the sender's key arrives the message is
/// decrypted and surfaced via <see cref="MeshtasticProtocolService.PkiMessageDecrypted"/>.
///
/// Uses the RFC 7748 §5.2 X25519 test vectors so no key generation is needed:
/// Alice = us, Bob = the sender. The ciphertext is produced with a second
/// PkiDecryptionService seeded with Bob's private key (AES-CTR is symmetric and
/// the X25519 shared secret is the same in both directions).
/// </summary>
public class PkiLateDecryptTests
{
    private const byte Start1 = 0x94;
    private const byte Start2 = 0xC3;

    private const uint OurNode    = 0xA11CE;
    private const uint SenderNode = 0x0B0B;
    private const uint PacketId   = 0x12345678;

    private static readonly byte[] AlicePriv = FromHex("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a");
    private static readonly byte[] AlicePub  = FromHex("8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a");
    private static readonly byte[] BobPriv   = FromHex("5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb");
    private static readonly byte[] BobPub    = FromHex("de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f");

    private static byte[] FromHex(string hex)
    {
        var b = new byte[hex.Length / 2];
        for (int i = 0; i < b.Length; i++)
            b[i] = System.Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return b;
    }

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

    // Produce a real PKC blob (AES-CCM + tag + extra nonce) of a plaintext
    // addressed to us (Alice), sent from Bob.
    private static byte[] Encrypt(byte[] plaintext)
    {
        var enc = new PkiDecryptionService();
        enc.SetPrivateKey(BobPriv);               // sender's private key
        return enc.Encrypt(plaintext, AlicePub, SenderNode, PacketId, extraNonce: 0x11223344)!;
    }

    [Fact]
    public void EncryptedDm_IsBufferedAndDecryptedOnceSenderKeyArrives()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc  = new MeshtasticProtocolService(fake);
        svc.SetNodeKeyService(new NodeKeyService(Path.Combine(Path.GetTempPath(), $"nk_{System.Guid.NewGuid():N}.csv")));

        ModelMessageItem? shown = null;
        svc.MessageReceived += (_, m) => shown = m;
        PkiLateDecryptedEventArgs? decrypted = null;
        svc.PkiMessageDecrypted += (_, e) => decrypted = e;

        // 1) Our identity (MyInfo) + private key. The key loads from a local admin
        //    GetConfigResponse for the SecurityConfig (as the real device delivers it).
        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = OurNode } }));
        var secResp = new AdminMessage
        {
            GetConfigResponse = new Config
            {
                Security = new Config.Types.SecurityConfig
                {
                    PrivateKey = ByteString.CopyFrom(AlicePriv),
                    PublicKey  = ByteString.CopyFrom(AlicePub)
                }
            }
        };
        fake.Receive(Frame(new FromRadio
        {
            Packet = new MeshPacket
            {
                From = OurNode,   // local response (from our own node)
                To = OurNode,
                Decoded = new Data { Portnum = (PortNum)6, Payload = secResp.ToByteString() } // ADMIN_APP
            }
        }));

        // 2) An encrypted PKI DM to us from Bob — we have no key for Bob yet.
        var innerData = new Data { Portnum = PortNum.TextMessageApp, Payload = ByteString.CopyFromUtf8("geheim") };
        var ciphertext = Encrypt(innerData.ToByteArray());
        fake.Receive(Frame(new FromRadio
        {
            Packet = new MeshPacket
            {
                From = SenderNode,
                To = OurNode,
                Id = PacketId,
                PkiEncrypted = true,
                Encrypted = ByteString.CopyFrom(ciphertext)
            }
        }));

        // Shown as an encrypted placeholder; not yet decrypted.
        Assert.NotNull(shown);
        Assert.True(shown!.IsEncrypted);
        Assert.Null(decrypted);

        // A NodeInfo request (NODEINFO_APP, want_response) went out to Bob.
        Assert.Contains(fake.Written, w => IsNodeInfoRequestTo(w, SenderNode));

        // 3) Bob's NodeInfo arrives with his public key → retroactive decryption.
        fake.Receive(Frame(new FromRadio
        {
            NodeInfo = new NodeInfo
            {
                Num = SenderNode,
                User = new User
                {
                    Id = $"!{SenderNode:x8}",
                    ShortName = "BOB",
                    LongName = "Bob",
                    PublicKey = ByteString.CopyFrom(BobPub)
                }
            }
        }));

        Assert.NotNull(decrypted);
        Assert.Equal("geheim", decrypted!.Text);
        Assert.Equal(SenderNode, decrypted.FromId);
        Assert.Equal(PacketId, decrypted.PacketId);
        Assert.Same(shown, decrypted.Item);   // updates the same message in place
    }

    // Build a service with our identity + private key already loaded.
    private static (MeshtasticProtocolService svc, FakeConnectionService fake) NewServiceWithOurKey()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc  = new MeshtasticProtocolService(fake);
        svc.SetNodeKeyService(new NodeKeyService(Path.Combine(Path.GetTempPath(), $"nk_{System.Guid.NewGuid():N}.csv")));
        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = OurNode } }));
        var secResp = new AdminMessage
        {
            GetConfigResponse = new Config
            {
                Security = new Config.Types.SecurityConfig
                {
                    PrivateKey = ByteString.CopyFrom(AlicePriv),
                    PublicKey  = ByteString.CopyFrom(AlicePub)
                }
            }
        };
        fake.Receive(Frame(new FromRadio
        {
            Packet = new MeshPacket { From = OurNode, To = OurNode, Decoded = new Data { Portnum = (PortNum)6, Payload = secResp.ToByteString() } }
        }));
        return (svc, fake);
    }

    private static ModelMessageItem EncryptedItem(string text) => new()
    {
        FromId = SenderNode,
        Id = PacketId,
        IsEncrypted = true,
        PkiCipher = Encrypt(new Data { Portnum = PortNum.TextMessageApp, Payload = ByteString.CopyFromUtf8(text) }.ToByteArray())
    };

    [Fact]
    public void RetryOrRequestPendingDm_DecryptsImmediately_WhenSenderKeyKnown()
    {
        var (svc, fake) = NewServiceWithOurKey();
        // Sender's key is already known (a NodeInfo was seen earlier).
        fake.Receive(Frame(new FromRadio
        {
            NodeInfo = new NodeInfo { Num = SenderNode, User = new User { Id = $"!{SenderNode:x8}", PublicKey = ByteString.CopyFrom(BobPub) } }
        }));

        PkiLateDecryptedEventArgs? dec = null;
        svc.PkiMessageDecrypted += (_, e) => dec = e;

        var item = EncryptedItem("später");
        svc.RetryOrRequestPendingDm(item);

        Assert.NotNull(dec);
        Assert.Equal("später", dec!.Text);
        Assert.Same(item, dec.Item);
    }

    [Fact]
    public void RetryOrRequestPendingDm_RequestsKey_WhenSenderKeyUnknown()
    {
        var (svc, fake) = NewServiceWithOurKey();
        fake.Written.Clear();

        PkiLateDecryptedEventArgs? dec = null;
        svc.PkiMessageDecrypted += (_, e) => dec = e;

        svc.RetryOrRequestPendingDm(EncryptedItem("später"), force: true);

        Assert.Null(dec);   // no key yet → not decrypted
        Assert.Contains(fake.Written, w => IsNodeInfoRequestTo(w, SenderNode));
    }

    private static bool IsNodeInfoRequestTo(byte[] framed, uint dest)
    {
        try
        {
            var tr = ToRadio.Parser.ParseFrom(framed[4..]);
            var d = tr.Packet?.Decoded;
            return tr.Packet?.To == dest && d != null && (int)d.Portnum == 4 && d.WantResponse;
        }
        catch { return false; }
    }
}
