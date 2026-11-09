namespace MeshhessenClient.Services.Firmware;

/// <summary>Release channel of a firmware build. Nightly is deliberately not offered.</summary>
public enum FirmwareChannel
{
    Stable,
    Alpha,
    /// <summary>A file the user picked locally — no release metadata available.</summary>
    Local
}

/// <summary>One entry of api.meshtastic.org <c>github/firmware/list</c>.</summary>
public sealed record FirmwareRelease(
    string Id,
    string Title,
    string PageUrl,
    string ZipUrl,
    string ReleaseNotes,
    FirmwareChannel Channel)
{
    /// <summary>Version as used in artifact paths/filenames ("2.7.26.54e0d8d" for id "v2.7.26.54e0d8d").</summary>
    public string Version => Id.StartsWith('v') ? Id[1..] : Id;

    /// <summary>Numeric major.minor.patch prefix (hash suffix dropped), or null if the id is malformed.</summary>
    public Version? NumericVersion => FirmwareCaps.Parse(Version);

    public static FirmwareRelease LocalFile(string fileName) =>
        new("local", fileName, "", "", "", FirmwareChannel.Local);
}

/// <summary>One entry of api.meshtastic.org <c>resource/deviceHardware</c>.</summary>
public sealed record DeviceHardware(
    int HwModel,
    string HwModelSlug,
    string PlatformioTarget,
    string Architecture,
    bool ActivelySupported,
    string DisplayName,
    bool? RequiresDfu,
    bool? RequiresBootloaderUpgradeForOta,
    int? SupportLevel)
{
    public bool IsEsp32 => Architecture.StartsWith("esp32", StringComparison.OrdinalIgnoreCase);
    public bool IsNrf52 => Architecture.StartsWith("nrf52", StringComparison.OrdinalIgnoreCase);
    public bool IsRp2040 => Architecture.StartsWith("rp2", StringComparison.OrdinalIgnoreCase);

    /// <summary>Target used in artifact filenames; falls back to the slug when the catalog has no PlatformIO target.</summary>
    public string EffectiveTarget => string.IsNullOrEmpty(PlatformioTarget) ? HwModelSlug : PlatformioTarget;

    /// <summary>A placeholder for devices the catalog does not know — the pioEnv the device reports is the only
    /// handle we have; architecture is guessed from the hardware-model name so the wizard can still offer a path.</summary>
    public static DeviceHardware Unknown(int hwModel, string hwModelName, string? pioEnv, string architectureGuess) =>
        new(hwModel, hwModelName, pioEnv ?? "", architectureGuess, false, hwModelName, null, null, null);
}

/// <summary>How the firmware reaches the device.</summary>
public enum FirmwareUpdateMethod
{
    None,
    /// <summary>ESP32 Unified OTA over TCP port 3232 (device must have Wi-Fi configured).</summary>
    Esp32Wifi,
    /// <summary>ESP32 Unified OTA over the loader's BLE service.</summary>
    Esp32Ble,
    /// <summary>nRF52 Nordic DFU (Legacy or Secure) over BLE via the Adafruit bootloader.</summary>
    Nrf52BleDfu,
    /// <summary>UF2 copy onto the USB mass-storage bootloader drive (nRF52 / RP2040).</summary>
    Uf2Usb
}

/// <summary>Progress report of a running update. <see cref="Key"/> is an i18n resource key;
/// <see cref="Args"/> are its format arguments. <see cref="Fraction"/> is 0..1 or null for indeterminate.</summary>
public sealed record FirmwareProgress(string Key, double? Fraction = null, string? Detail = null, object[]? Args = null);

/// <summary>A downloaded or user-supplied firmware file.</summary>
public sealed record FirmwareArtifact(string Path, string FileName, long Size, string Sha256Hex);

/// <summary>Why an update could not run; mapped to i18n keys by the UI.</summary>
public sealed class FirmwareUpdateException : Exception
{
    public string Key { get; }
    public object[] Args { get; }

    public FirmwareUpdateException(string key, params object[] args) : base(key)
    {
        Key = key;
        Args = args;
    }

    public FirmwareUpdateException(string key, Exception inner, params object[] args) : base(key, inner)
    {
        Key = key;
        Args = args;
    }
}
