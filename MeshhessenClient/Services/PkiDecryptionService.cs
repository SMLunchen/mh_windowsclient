using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace MeshhessenClient.Services;

/// <summary>
/// Implements Meshtastic PKC (public-key crypto) packet decryption, matching the
/// firmware's <c>CryptoEngine::decryptCurve25519</c> exactly:
///   key   = SHA256(X25519(ourPrivate, remotePublic))
///   cipher= AES-256-CCM, L=2, tag M=8 bytes, no AAD
///   blob  = [ciphertext][auth tag (8 bytes)][extra nonce (4 bytes LE)]
///   nonce = 13-byte CCM nonce from a 16-byte block:
///           [packetId low32 (4 LE)][extraNonce (4 LE)][fromNode (4 LE)][0]
/// (The earlier implementation used plain AES-CTR with a zero extra-nonce and never
///  stripped the tag — it could not decrypt real firmware PKC packets.)
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

    /// <summary>Derive our Curve25519 public key from the loaded private key
    /// (fallback when the device's SecurityConfig doesn't include it), so we can
    /// advertise it in our NodeInfo.</summary>
    public byte[]? GetOwnPublicKey()
    {
        if (_ourPrivateKey == null) return null;
        try { return new X25519PrivateKeyParameters(_ourPrivateKey, 0).GeneratePublicKey().GetEncoded(); }
        catch { return null; }
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
    /// Try to decrypt a PKC MeshPacket payload (the full on-wire blob:
    /// ciphertext + 8-byte tag + 4-byte extra nonce). Returns null if the key is
    /// unavailable, the blob is too short, or the authentication tag is invalid
    /// (wrong key / not actually a PKC packet). A non-null result is
    /// tag-verified — i.e. guaranteed-correct plaintext, never garbage.
    /// </summary>
    public byte[]? TryDecrypt(byte[] blob, byte[] senderPublicKey, uint fromNode, uint packetId)
    {
        if (_ourPrivateKey == null || senderPublicKey.Length != 32)
            return null;
        // Must hold at least the 8-byte tag + 4-byte extra nonce, plus payload.
        if (blob.Length <= 12)
            return null;

        try
        {
            uint extraNonce = BitConverter.ToUInt32(blob, blob.Length - 4);
            var key   = DeriveKey(senderPublicKey);
            var nonce = BuildNonce(packetId, fromNode, extraNonce);

            // BouncyCastle CCM expects [ciphertext || tag]; that's the blob minus
            // the trailing 4-byte extra nonce.
            int ctLen = blob.Length - 4;
            var ccm = new CcmBlockCipher(new AesEngine());
            ccm.Init(false, new AeadParameters(new KeyParameter(key), 64, nonce));
            var outBuf = new byte[ccm.GetOutputSize(ctLen)];
            int len = ccm.ProcessBytes(blob, 0, ctLen, outBuf, 0);
            len += ccm.DoFinal(outBuf, len);   // throws InvalidCipherTextException on bad tag
            CryptographicOperations.ZeroMemory(key);
            return len == outBuf.Length ? outBuf : outBuf[..len];
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"PkiDecrypt: decryption failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Produce a PKC blob (ciphertext + 8-byte tag + 4-byte extra nonce) for the
    /// given plaintext addressed to <paramref name="recipientPublicKey"/>. Mirrors
    /// the firmware's encryptCurve25519; used for round-trip tests (the device
    /// itself handles real send-side encryption).
    /// </summary>
    public byte[]? Encrypt(byte[] plaintext, byte[] recipientPublicKey, uint fromNode, uint packetId, uint extraNonce)
    {
        if (_ourPrivateKey == null || recipientPublicKey.Length != 32)
            return null;
        try
        {
            var key   = DeriveKey(recipientPublicKey);
            var nonce = BuildNonce(packetId, fromNode, extraNonce);

            var ccm = new CcmBlockCipher(new AesEngine());
            ccm.Init(true, new AeadParameters(new KeyParameter(key), 64, nonce));
            var outBuf = new byte[ccm.GetOutputSize(plaintext.Length)]; // plaintext + 8-byte tag
            int len = ccm.ProcessBytes(plaintext, 0, plaintext.Length, outBuf, 0);
            len += ccm.DoFinal(outBuf, len);
            CryptographicOperations.ZeroMemory(key);

            var result = new byte[len + 4];
            Array.Copy(outBuf, result, len);
            BitConverter.GetBytes(extraNonce).CopyTo(result, len); // append extra nonce (LE)
            return result;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"PkiDecrypt: encryption failed: {ex.Message}");
            return null;
        }
    }

    // key = SHA256(X25519(ourPrivate, otherPublic))
    private byte[] DeriveKey(byte[] otherPublicKey)
    {
        var ourPriv  = new X25519PrivateKeyParameters(_ourPrivateKey!, 0);
        var theirPub = new X25519PublicKeyParameters(otherPublicKey, 0);
        var agreement = new X25519Agreement();
        agreement.Init(ourPriv);
        var shared = new byte[32];
        agreement.CalculateAgreement(theirPub, shared, 0);
        var key = SHA256.HashData(shared);
        CryptographicOperations.ZeroMemory(shared);
        return key;
    }

    // Meshtastic initNonce: 16-byte block, CCM uses the first 13 (L=2).
    // [packetId low32 (4 LE)][extraNonce (4 LE)][fromNode (4 LE)][0]
    private static byte[] BuildNonce(uint packetId, uint fromNode, uint extraNonce)
    {
        var n = new byte[16];
        BitConverter.GetBytes((ulong)packetId).CopyTo(n, 0); // bytes 0-7 (4-7 are zero for a 32-bit id)
        BitConverter.GetBytes(fromNode).CopyTo(n, 8);        // bytes 8-11
        BitConverter.GetBytes(extraNonce).CopyTo(n, 4);      // bytes 4-7 (overwrites the id's zero high bytes)
        return n[..13];
    }
}
