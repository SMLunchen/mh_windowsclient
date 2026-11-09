using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace MeshhessenClient.Services;

/// <summary>
/// Authenticated channel encryption (ChannelSettings.use_aead, firmware 2.8, experimental):
/// AES-CCM with a 12-byte tag appended to the ciphertext. Mirrors the firmware's
/// CryptoEngine::encryptPacketCCM/decryptPacketCCM (origin/develop):
///   nonce (13 bytes, CCM L=2) = [packetId as uint64 LE (8)] [fromNode LE (4)] [0]
///   AAD   (8 bytes)           = [fromNode LE (4)] [toNode LE (4)]
/// Hop fields stay out of the AAD because relays rewrite them.
/// </summary>
public static class ChannelAead
{
    public const int TagSize = 12;

    private static byte[] Nonce(uint fromNode, uint packetId)
    {
        var n = new byte[13];
        BitConverter.GetBytes((ulong)packetId).CopyTo(n, 0); // bytes 0-7 (high 4 = 0)
        BitConverter.GetBytes(fromNode).CopyTo(n, 8);        // bytes 8-11; byte 12 stays 0
        return n;
    }

    private static byte[] Aad(uint fromNode, uint toNode)
    {
        var a = new byte[8];
        BitConverter.GetBytes(fromNode).CopyTo(a, 0);
        BitConverter.GetBytes(toNode).CopyTo(a, 4);
        return a;
    }

    /// <summary>Decrypt ciphertext||tag. Null on a wrong key, forged tag or bit-flip —
    /// CCM authenticates, so unlike CTR a failed decrypt is detected, not garbage.</summary>
    public static byte[]? Decrypt(byte[] key, uint fromNode, uint toNode, uint packetId, byte[] cipherWithTag)
    {
        if (cipherWithTag.Length <= TagSize) return null;
        try
        {
            var ccm = new CcmBlockCipher(new AesEngine());
            ccm.Init(false, new AeadParameters(new KeyParameter(key), TagSize * 8,
                Nonce(fromNode, packetId), Aad(fromNode, toNode)));
            var output = new byte[ccm.GetOutputSize(cipherWithTag.Length)];
            int len = ccm.ProcessBytes(cipherWithTag, 0, cipherWithTag.Length, output, 0);
            ccm.DoFinal(output, len);
            return output;
        }
        catch { return null; }
    }

    /// <summary>Encrypt plaintext → ciphertext||tag (12 bytes appended).</summary>
    public static byte[]? Encrypt(byte[] key, uint fromNode, uint toNode, uint packetId, byte[] plaintext)
    {
        try
        {
            var ccm = new CcmBlockCipher(new AesEngine());
            ccm.Init(true, new AeadParameters(new KeyParameter(key), TagSize * 8,
                Nonce(fromNode, packetId), Aad(fromNode, toNode)));
            var output = new byte[ccm.GetOutputSize(plaintext.Length)];
            int len = ccm.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
            ccm.DoFinal(output, len);
            return output;
        }
        catch { return null; }
    }
}
