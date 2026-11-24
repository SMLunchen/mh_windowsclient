namespace MeshhessenClient.Services.Firmware;

/// <summary>
/// ESP32 "Unified OTA" loader protocol (meshtastic/firmware-ota): line-oriented text over BLE or TCP.
/// Host → <c>OTA &lt;size&gt; &lt;sha256hex&gt;\n</c>; loader → <c>ERASING</c> (0..n) then <c>OK</c>, or <c>ERR …</c>;
/// then raw firmware bytes; the loader answers <c>ACK</c> per receive-buffer drain (BLE) and a terminal
/// <c>OK</c> once the whole image arrived and its hash verified, or <c>ERR Hash Mismatch</c>.
/// </summary>
public abstract record OtaResponse
{
    public sealed record Ok(string? HwVersion = null, string? FwVersion = null, int? RebootCount = null, string? GitHash = null) : OtaResponse;
    public sealed record Erasing : OtaResponse;
    public sealed record Ack : OtaResponse;
    public sealed record Error(string Message) : OtaResponse;

    public static OtaResponse Parse(string response)
    {
        var t = response.Trim();
        if (t == "OK") return new Ok();
        if (t.StartsWith("OK ", StringComparison.Ordinal))
        {
            var parts = t[3..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 4
                ? new Ok(parts[0], parts[1], int.TryParse(parts[2], out var rc) ? rc : null, parts[3])
                : new Ok();
        }
        if (t == "ERASING") return new Erasing();
        if (t == "ACK") return new Ack();
        if (t.StartsWith("ERR ", StringComparison.Ordinal)) return new Error(t[4..]);
        if (t == "ERR") return new Error("Unknown error");
        return new Error($"Unknown response: {t}");
    }

    public static string StartCommand(long sizeBytes, string sha256Hex) => $"OTA {sizeBytes} {sha256Hex}\n";
}

public enum OtaErrorKind { ConnectionFailed, Timeout, HashRejected, CommandFailed, TransferFailed, VerificationFailed }

public sealed class OtaException : Exception
{
    public OtaErrorKind Kind { get; }
    public OtaException(OtaErrorKind kind, string message, Exception? inner = null) : base(message, inner) => Kind = kind;
}

/// <summary>Transport-agnostic view of one OTA session (BLE GATT or TCP).</summary>
public interface IUnifiedOtaTransport : IAsyncDisposable
{
    /// <summary>Connect to the loader (after the device rebooted into OTA mode).</summary>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>Send the start command and wait for the loader's OK (ERASING reports go to <paramref name="onErasing"/>).</summary>
    Task StartAsync(long sizeBytes, string sha256Hex, Action? onErasing, CancellationToken ct);

    /// <summary>Stream the image; <paramref name="onProgress"/> gets 0..1. Returns after the terminal OK.</summary>
    Task StreamAsync(byte[] firmware, IProgress<double>? onProgress, CancellationToken ct);
}
