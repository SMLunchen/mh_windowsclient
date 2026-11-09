namespace MeshhessenClient.Services.Firmware.Dfu;

/// <summary>Upload-time surface shared by the Secure (FE59) and Legacy (1530) bootloader transports.</summary>
public interface IDfuTransport : IAsyncDisposable
{
    /// <summary>True once connected if the bootloader only takes 20-byte packets (no MTU negotiated) — slow path.</summary>
    bool IsLowSpeedTransfer { get; }

    /// <summary>Address of the bootloader device (may be the app MAC + 1).</summary>
    ulong Address { get; }

    /// <summary>Connects to the bootloader and prepares the control point. Throws <see cref="DfuConnectionException"/>.</summary>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>Uploads init packet + firmware. <paramref name="onPhase"/> receives i18n keys for silent waits;
    /// <paramref name="onProgress"/> 0..1. Returns once the bootloader accepted/activated the image.</summary>
    Task TransferAsync(DfuPackage package, Action<string>? onPhase, IProgress<double>? onProgress, CancellationToken ct);

    /// <summary>Best-effort ABORT/RESET so a fresh session can start without a power cycle. Never throws.</summary>
    Task AbortAsync();
}
