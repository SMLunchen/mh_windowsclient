using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Parameters;

namespace MeshhessenClient.Services;

/// <summary>
/// Implements Meshtastic PKI (Curve25519) packet decryption.
/// Algorithm: X25519(ourPrivate, senderPublic) → SHA256 → AES-256-CTR
/// Nonce: [packetId 8 bytes LE] + [fromNode 4 bytes LE] + [0x00 × 4]
/// </summary>
public class PkiDecryptionService
{
    private byte[]? _ourPrivateKey;  // 32 bytes, in memory only — never written to disk

    public bool HasPrivateKey => _ourPrivateKey != null;

    /// <summary>Store the device's private key in RAM. Call on connect, clear on disconnect.</summary>
    public void SetPrivateKey(byte[] privateKey)
    {
        if (privateKey.Length != 32)
        {
            Logger.WriteLine($"PkiDecrypt: invalid private key length {privateKey.Length}, expected 32");
            return;
        }
        _ourPrivateKey = (byte[])privateKey.Clone();
        Logger.WriteLine("PkiDecrypt: private key loaded into memory");
    }

    /// <summary>Remove the private key from memory (call on disconnect or node switch).</summary>
    public void ClearPrivateKey()
    {
        if (_ourPrivateKey != null)
        {
            CryptographicOperations.ZeroMemory(_ourPrivateKey);
            _ourPrivateKey = null;
        }
        Logger.WriteLine("PkiDecrypt: private key cleared from memory");
    }

    /// <summary>
    /// Try to decrypt a PKI-encrypted MeshPacket payload.
    /// Returns null if the key is unavailable or decryption fails.
    /// </summary>
    public byte[]? TryDecrypt(
        byte[] ciphertext,
        byte[] senderPublicKey,
        uint fromNode,
        uint packetId)
    {
        if (_ourPrivateKey == null)
            return null;

        if (senderPublicKey.Length != 32)
            return null;

        try
        {
            // Step 1: X25519 ECDH shared secret
            var ourPrivParam  = new X25519PrivateKeyParameters(_ourPrivateKey, 0);
            var theirPubParam = new X25519PublicKeyParameters(senderPublicKey, 0);

            var agreement = new X25519Agreement();
            agreement.Init(ourPrivParam);
            var sharedSecret = new byte[32];
            agreement.CalculateAgreement(theirPubParam, sharedSecret, 0);

            // Step 2: SHA256(sharedSecret) → AES key
            var aesKey = SHA256.HashData(sharedSecret);
            CryptographicOperations.ZeroMemory(sharedSecret);

            // Step 3: Build nonce (16 bytes)
            // [packetId 8 bytes LE] + [fromNode 4 bytes LE] + [0x00 × 4]
            var nonce = new byte[16];
            var packetId64 = (ulong)packetId;
            nonce[0] = (byte)(packetId64);
            nonce[1] = (byte)(packetId64 >> 8);
            nonce[2] = (byte)(packetId64 >> 16);
            nonce[3] = (byte)(packetId64 >> 24);
            nonce[4] = (byte)(packetId64 >> 32);
            nonce[5] = (byte)(packetId64 >> 40);
            nonce[6] = (byte)(packetId64 >> 48);
            nonce[7] = (byte)(packetId64 >> 56);
            nonce[8]  = (byte)(fromNode);
            nonce[9]  = (byte)(fromNode >> 8);
            nonce[10] = (byte)(fromNode >> 16);
            nonce[11] = (byte)(fromNode >> 24);
            // bytes 12–15 stay 0

            // Step 4: AES-256-CTR decrypt
            var plaintext = AesCtr(aesKey, nonce, ciphertext);
            return plaintext;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"PkiDecrypt: decryption failed: {ex.Message}");
            return null;
        }
    }

    // ── AES-256-CTR (no padding) ──────────────────────────────────────────

    private static byte[] AesCtr(byte[] key, byte[] nonce, byte[] input)
    {
        var output = new byte[input.Length];
        var counter = (byte[])nonce.Clone();  // 16-byte counter block
        var keystream = new byte[16];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        int offset = 0;
        while (offset < input.Length)
        {
            // Encrypt the counter to produce keystream block
            using var enc = aes.CreateEncryptor();
            enc.TransformBlock(counter, 0, 16, keystream, 0);

            // XOR keystream with ciphertext
            var blockLen = Math.Min(16, input.Length - offset);
            for (int i = 0; i < blockLen; i++)
                output[offset + i] = (byte)(input[offset + i] ^ keystream[i]);

            offset += blockLen;

            // Increment counter (little-endian, matching Meshtastic firmware)
            for (int i = 0; i < 16; i++)
            {
                if (++counter[i] != 0) break;
            }
        }

        CryptographicOperations.ZeroMemory(keystream);
        return output;
    }
}
