namespace MeshhessenClient.Services.Firmware.Dfu;

// Nordic DFU wire facts shared by the Secure and Legacy transports. Reference: Nordic SDK 11/15 DFU
// specs and the Meshtastic Android port (feature/firmware/ota/dfu). Byte values are the protocol's own.

/// <summary>Nordic Secure DFU (SDK 12+) UUIDs — service FE59.</summary>
public static class SecureDfuUuids
{
    public static readonly Guid Service = Guid.Parse("0000FE59-0000-1000-8000-00805F9B34FB");
    /// <summary>Control point: write opcodes WITH response, receive notifications.</summary>
    public static readonly Guid ControlPoint = Guid.Parse("8EC90001-F315-4F60-9FB8-838830DAEA50");
    /// <summary>Packet: init data / firmware bytes WITHOUT response.</summary>
    public static readonly Guid Packet = Guid.Parse("8EC90002-F315-4F60-9FB8-838830DAEA50");
    /// <summary>Buttonless DFU without bonds (indicate + write 0x01) exposed by app firmware built with BLE_DFU_SECURE.</summary>
    public static readonly Guid ButtonlessNoBonds = Guid.Parse("8EC90003-F315-4F60-9FB8-838830DAEA50");
}

/// <summary>Nordic Legacy DFU (SDK 11 / Adafruit BLEDfu) UUIDs — service 1530. This is what the stock
/// Meshtastic nRF52 app firmware exposes (buttonless trigger) and what most Adafruit bootloaders speak.</summary>
public static class LegacyDfuUuids
{
    public static readonly Guid Service = Guid.Parse("00001530-1212-EFDE-1523-785FEABCD123");
    /// <summary>Control point: NOTIFY + WRITE. Notifications must be enabled before writing.</summary>
    public static readonly Guid ControlPoint = Guid.Parse("00001531-1212-EFDE-1523-785FEABCD123");
    /// <summary>Packet: WRITE_NO_RESPONSE for image sizes, init packet and firmware bytes.</summary>
    public static readonly Guid Packet = Guid.Parse("00001532-1212-EFDE-1523-785FEABCD123");
    /// <summary>Optional uint16 LE DFU version; ≥ 5 required for the extended init-packet flow.</summary>
    public static readonly Guid Version = Guid.Parse("00001534-1212-EFDE-1523-785FEABCD123");
    public static readonly Guid DeviceInformationService = Guid.Parse("0000180A-0000-1000-8000-00805F9B34FB");
    public static readonly Guid FirmwareRevision = Guid.Parse("00002A26-0000-1000-8000-00805F9B34FB");
}

public static class SecureDfuOpcode
{
    public const byte Create = 0x01;
    public const byte SetPrn = 0x02;
    public const byte CalculateChecksum = 0x03;
    public const byte Execute = 0x04;
    public const byte Select = 0x06;
    public const byte Abort = 0x0C;
    public const byte ResponseCode = 0x60;
}

public static class SecureDfuObjectType
{
    public const byte Command = 0x01; // init packet (.dat)
    public const byte Data = 0x02;    // firmware (.bin)
}

public static class SecureDfuResult
{
    public const byte Success = 0x01;
    public const byte OpCodeNotSupported = 0x02;
    public const byte InvalidParameter = 0x03;
    public const byte InsufficientResources = 0x04;
    public const byte InvalidObject = 0x05;
    public const byte UnsupportedType = 0x07;
    public const byte OperationNotPermitted = 0x08;
    public const byte OperationFailed = 0x0A;
    public const byte ExtError = 0x0B;

    public static string Describe(byte code) => code switch
    {
        Success => "SUCCESS",
        OpCodeNotSupported => "OP_CODE_NOT_SUPPORTED",
        InvalidParameter => "INVALID_PARAMETER",
        InsufficientResources => "INSUFFICIENT_RESOURCES",
        InvalidObject => "INVALID_OBJECT",
        UnsupportedType => "UNSUPPORTED_TYPE",
        OperationNotPermitted => "OPERATION_NOT_PERMITTED",
        OperationFailed => "OPERATION_FAILED",
        ExtError => "EXT_ERROR",
        _ => $"0x{code:X2}"
    };

    public static string DescribeExtended(byte code) => code switch
    {
        0x02 => "Wrong command format",
        0x03 => "Unknown command",
        0x04 => "Init command invalid",
        0x05 => "FW version failure",
        0x06 => "HW version failure",
        0x07 => "SD version failure",
        0x08 => "Signature missing",
        0x09 => "Wrong hash type",
        0x0A => "Hash failed",
        0x0B => "Wrong signature type",
        0x0C => "Verification failed",
        0x0D => "Insufficient space",
        _ => $"Unknown extended error 0x{code:X2}"
    };
}

/// <summary>Parsed Secure DFU control-point notification.</summary>
public abstract record SecureDfuResponse
{
    public sealed record Success(byte Opcode) : SecureDfuResponse;
    public sealed record SelectResult(int MaxSize, int Offset, uint Crc32) : SecureDfuResponse;
    public sealed record ChecksumResult(int Offset, uint Crc32) : SecureDfuResponse;
    public sealed record Failure(byte Opcode, byte ResultCode, byte? ExtendedError) : SecureDfuResponse
    {
        public override string ToString() =>
            $"opcode=0x{Opcode:X2} result={SecureDfuResult.Describe(ResultCode)}" +
            (ExtendedError is { } e ? $" ext={SecureDfuResult.DescribeExtended(e)}" : "");
    }
    public sealed record Unknown(byte[] Raw) : SecureDfuResponse;

    public static SecureDfuResponse Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3 || data[0] != SecureDfuOpcode.ResponseCode) return new Unknown(data.ToArray());
        var opcode = data[1];
        var result = data[2];
        if (result != SecureDfuResult.Success)
        {
            byte? ext = result == SecureDfuResult.ExtError && data.Length >= 4 ? data[3] : null;
            return new Failure(opcode, result, ext);
        }
        switch (opcode)
        {
            case SecureDfuOpcode.Select:
                if (data.Length < 15) return new Failure(opcode, SecureDfuResult.InvalidParameter, null);
                return new SelectResult(ReadInt32Le(data, 3), ReadInt32Le(data, 7), ReadUInt32Le(data, 11));
            case SecureDfuOpcode.CalculateChecksum:
                if (data.Length < 11) return new Failure(opcode, SecureDfuResult.InvalidParameter, null);
                return new ChecksumResult(ReadInt32Le(data, 3), ReadUInt32Le(data, 7));
            default:
                return new Success(opcode);
        }
    }

    public static int ReadInt32Le(ReadOnlySpan<byte> d, int o) => (int)ReadUInt32Le(d, o);
    public static uint ReadUInt32Le(ReadOnlySpan<byte> d, int o) =>
        (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
}

public static class LegacyDfuOpcode
{
    public const byte StartDfu = 0x01;
    public const byte InitDfuParams = 0x02;
    public const byte ReceiveFirmwareImage = 0x03;
    public const byte Validate = 0x04;
    public const byte ActivateAndReset = 0x05;
    public const byte Reset = 0x06;
    public const byte PacketReceiptNotifReq = 0x08;
    public const byte ResponseCode = 0x10;
    public const byte PacketReceipt = 0x11;
    public const byte InitParamsStart = 0x00;
    public const byte InitParamsComplete = 0x01;
    /// <summary>START_DFU image-type bit: application only (Meshtastic never ships SD/BL over OTA).</summary>
    public const byte ImageTypeApplication = 0x04;
}

public static class LegacyDfuStatus
{
    public const byte Success = 0x01;
    public const byte InvalidState = 0x02;
    public const byte NotSupported = 0x03;
    public const byte DataSizeExceedsLimit = 0x04;
    public const byte CrcError = 0x05;
    public const byte OperationFailed = 0x06;

    public static string Describe(byte s) => s switch
    {
        Success => "SUCCESS",
        InvalidState => "INVALID_STATE",
        NotSupported => "NOT_SUPPORTED",
        DataSizeExceedsLimit => "DATA_SIZE_EXCEEDS_LIMIT",
        CrcError => "CRC_ERROR",
        OperationFailed => "OPERATION_FAILED",
        _ => $"UNKNOWN(0x{s:X2})"
    };
}

/// <summary>Parsed Legacy DFU control-point notification.</summary>
public abstract record LegacyDfuResponse
{
    public sealed record Success(byte RequestOpcode) : LegacyDfuResponse;
    public sealed record Failure(byte RequestOpcode, byte Status) : LegacyDfuResponse
    {
        public override string ToString() => $"opcode=0x{RequestOpcode:X2} status={LegacyDfuStatus.Describe(Status)}";
    }
    public sealed record PacketReceipt(uint BytesReceived) : LegacyDfuResponse;
    public sealed record Unknown(byte[] Raw) : LegacyDfuResponse;

    public static LegacyDfuResponse Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return new Unknown(data.ToArray());
        switch (data[0])
        {
            case LegacyDfuOpcode.ResponseCode:
                if (data.Length < 3) return new Unknown(data.ToArray());
                return data[2] == LegacyDfuStatus.Success ? new Success(data[1]) : new Failure(data[1], data[2]);
            case LegacyDfuOpcode.PacketReceipt:
                if (data.Length < 5) return new Unknown(data.ToArray());
                return new PacketReceipt(SecureDfuResponse.ReadUInt32Le(data, 1));
            default:
                return new Unknown(data.ToArray());
        }
    }
}

public static class LegacyDfuPayloads
{
    /// <summary>12-byte image sizes written to the packet characteristic right after START_DFU:
    /// [softdevice u32][bootloader u32][application u32], little-endian.</summary>
    public static byte[] ImageSizes(int appSize, int softDeviceSize = 0, int bootloaderSize = 0)
    {
        var b = new byte[12];
        BitConverter.TryWriteBytes(b.AsSpan(0, 4), softDeviceSize);
        BitConverter.TryWriteBytes(b.AsSpan(4, 4), bootloaderSize);
        BitConverter.TryWriteBytes(b.AsSpan(8, 4), appSize);
        return b;
    }

    /// <summary>PACKET_RECEIPT_NOTIF_REQ with a uint16 LE packet interval.</summary>
    public static byte[] PrnRequest(int packets) =>
        new[] { LegacyDfuOpcode.PacketReceiptNotifReq, (byte)(packets & 0xFF), (byte)((packets >> 8) & 0xFF) };
}

/// <summary>CRC-32 (IEEE 802.3) as used by Secure DFU CALCULATE_CHECKSUM.</summary>
public static class DfuCrc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return ~crc;
    }
}

/// <summary>Errors specific to the Nordic DFU session; the wizard maps them to i18n text.</summary>
public class DfuException : Exception
{
    public DfuException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class DfuConnectionException : DfuException
{
    public DfuConnectionException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class DfuProtocolException : DfuException
{
    public byte Opcode { get; }
    public byte ResultCode { get; }
    public DfuProtocolException(byte opcode, byte resultCode, string detail) : base($"DFU protocol error: {detail}")
    {
        Opcode = opcode;
        ResultCode = resultCode;
    }
}

public sealed class DfuChecksumException : DfuException
{
    public DfuChecksumException(uint expected, uint actual) : base($"CRC-32 mismatch: expected 0x{expected:X8} got 0x{actual:X8}") { }
}

public sealed class DfuTimeoutException : DfuException
{
    public DfuTimeoutException(string message) : base(message) { }
}

/// <summary>Legacy START_DFU was refused with INVALID_STATE — a half-finished earlier session is wedged in
/// the bootloader; the caller resets it over a fresh connection and retries.</summary>
public sealed class DfuStaleSessionException : DfuException
{
    public DfuStaleSessionException() : base("Bootloader rejected START with INVALID_STATE (stale session)") { }
}

/// <summary>Link dropped while firmware bytes were in flight.</summary>
public sealed class DfuMidStreamDisconnectException : DfuException
{
    public int BytesSent { get; }
    public int TotalBytes { get; }
    public DfuMidStreamDisconnectException(int sent, int total, Exception? inner = null)
        : base($"BLE link dropped mid-upload at {sent}/{total} bytes", inner)
    {
        BytesSent = sent;
        TotalBytes = total;
    }
}

/// <summary>The bootloader never accepted START inside the pre-start budget; it will boot the old app on its own.</summary>
public sealed class DfuStartTimedOutException : DfuException
{
    public DfuStartTimedOutException(TimeSpan budget) : base($"START not accepted within {budget.TotalMinutes:0} min of entering DFU mode") { }
}
