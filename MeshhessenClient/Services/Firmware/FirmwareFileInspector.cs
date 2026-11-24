using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MeshhessenClient.Services.Firmware.Dfu;

namespace MeshhessenClient.Services.Firmware;

public enum FileCheckLevel { Info, Warning, Error }

/// <summary>One line of the pre-flight file check; <see cref="Key"/> is an i18n key, <see cref="Args"/> its format args.</summary>
public sealed record FileCheckItem(FileCheckLevel Level, string Key, params object[] Args);

public sealed record FileCheckResult(IReadOnlyList<FileCheckItem> Items, string? EmbeddedVersion = null)
{
    public bool HasErrors => Items.Any(i => i.Level == FileCheckLevel.Error);
}

/// <summary>
/// Pure plausibility checks on firmware files before anything is sent to a device: filename ↔ target,
/// container magic, ESP image chip id ↔ architecture, UF2 family id ↔ architecture, DFU manifest sanity.
/// None of these can prove a file is right, but each catches the classic "wrong board / wrong kind of image" mistake.
/// </summary>
public static class FirmwareFileInspector
{
    // ── Filename ───────────────────────────────────────────────────────────

    /// <summary>Port of the Android check: the name must carry the exact target followed by a dotted version,
    /// the literal "firmware" or the extension itself, so sibling variants (tlora-v2 vs tlora-v2-1-1_6) never match.</summary>
    public static bool IsValidFirmwareFileName(string filename, string target, string fileExtension)
    {
        var baseName = filename.Replace('\\', '/');
        var leaf = baseName[(baseName.LastIndexOf('/') + 1)..];
        if (leaf.StartsWith("littlefs-", StringComparison.OrdinalIgnoreCase) ||
            leaf.StartsWith("bleota", StringComparison.OrdinalIgnoreCase) ||
            leaf.StartsWith("mt-", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains(".factory.", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(target) ||
            !baseName.EndsWith(fileExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        var targetToken = Regex.Escape(target);
        var extToken = Regex.Escape(fileExtension);
        const string versionOrFirmware = @"([\-_]v?\d+\.\d+|[\-_.]firmware)";
        var pattern = new Regex($@"^(^|.*[\-_]){targetToken}({versionOrFirmware}.*{extToken}$|{extToken}$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return pattern.IsMatch(baseName);
    }

    /// <summary>Version embedded in a release filename ("firmware-heltec-v3-2.7.26.54e0d8d.bin" → "2.7.26.54e0d8d").</summary>
    public static string? VersionFromFileName(string fileName)
    {
        var leaf = Path.GetFileName(fileName);
        var m = Regex.Match(leaf, @"(?:^|[-_])v?(\d+(?:\.[0-9A-Za-z]+)+)(?=(?:[-_][A-Za-z]+)?\.[^.]+$)");
        return m.Success ? m.Groups[1].Value : null;
    }

    // ── UF2 ────────────────────────────────────────────────────────────────

    public const uint Uf2MagicStart0 = 0x0A324655;
    public const uint Uf2MagicStart1 = 0x9E5D5157;
    public const uint Uf2FlagFamilyId = 0x2000;
    public const uint Uf2FamilyNrf52840 = 0xADA52840;
    public const uint Uf2FamilyRp2040 = 0xE48BFF56;
    public const uint Uf2FamilyRp2350ArmS = 0xE48BFF59;
    public const uint Uf2FamilyRp2350RiscV = 0xE48BFF5A;
    public const uint Uf2FamilyRp2350ArmNs = 0xE48BFF5B;
    public const uint Uf2FamilyRp2Absolute = 0xE48BFF57;

    public static bool IsUf2(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 512 &&
        BitConverter.ToUInt32(bytes[..4]) == Uf2MagicStart0 &&
        BitConverter.ToUInt32(bytes.Slice(4, 4)) == Uf2MagicStart1;

    /// <summary>Family id declared by the first block, or null when the file is not UF2 or declares none.</summary>
    public static uint? Uf2FamilyId(ReadOnlySpan<byte> bytes)
    {
        if (!IsUf2(bytes)) return null;
        var flags = BitConverter.ToUInt32(bytes.Slice(8, 4));
        if ((flags & Uf2FlagFamilyId) == 0) return null;
        return BitConverter.ToUInt32(bytes.Slice(28, 4));
    }

    public static uint? Uf2FirstTargetAddress(ReadOnlySpan<byte> bytes) =>
        IsUf2(bytes) ? BitConverter.ToUInt32(bytes.Slice(12, 4)) : null;

    /// <summary>UF2 families a catalog architecture may legitimately carry.</summary>
    public static IReadOnlyCollection<uint> ExpectedUf2Families(string architecture)
    {
        var a = architecture.ToLowerInvariant();
        if (a.StartsWith("nrf52")) return new[] { Uf2FamilyNrf52840 };
        if (a.StartsWith("rp2350")) return new[] { Uf2FamilyRp2350ArmS, Uf2FamilyRp2350RiscV, Uf2FamilyRp2350ArmNs, Uf2FamilyRp2Absolute };
        if (a.StartsWith("rp2040")) return new[] { Uf2FamilyRp2040, Uf2FamilyRp2Absolute };
        return Array.Empty<uint>();
    }

    public static string Uf2FamilyName(uint id) => id switch
    {
        Uf2FamilyNrf52840 => "nRF52840",
        Uf2FamilyRp2040 => "RP2040",
        Uf2FamilyRp2350ArmS => "RP2350 (ARM-S)",
        Uf2FamilyRp2350RiscV => "RP2350 (RISC-V)",
        Uf2FamilyRp2350ArmNs => "RP2350 (ARM-NS)",
        Uf2FamilyRp2Absolute => "RP2 absolute",
        _ => $"0x{id:X8}"
    };

    // ── ESP32 application image ────────────────────────────────────────────

    public const byte EspImageMagic = 0xE9;
    public const uint EspAppDescMagic = 0xABCD5432;

    /// <summary>esp_image_header_t.chip_id (uint16 LE at offset 12), or null if the file is no ESP image.</summary>
    public static int? EspImageChipId(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24 || bytes[0] != EspImageMagic) return null;
        return bytes[12] | (bytes[13] << 8);
    }

    /// <summary>True when an esp_app_desc_t follows the first segment header — i.e. this is an application
    /// image, not a bootloader/merged/factory image (which also starts with 0xE9 but has no app descriptor).</summary>
    public static bool HasEspAppDescriptor(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 0x20 + 4 && BitConverter.ToUInt32(bytes.Slice(0x20, 4)) == EspAppDescMagic;

    /// <summary>esp_app_desc_t.version (32-byte C string at 0x20+16), or null.</summary>
    public static string? EspAppVersion(ReadOnlySpan<byte> bytes) => HasEspAppDescriptor(bytes) ? CString(bytes, 0x20 + 16, 32) : null;

    /// <summary>esp_app_desc_t.project_name (32-byte C string at 0x20+48), or null.</summary>
    public static string? EspAppProjectName(ReadOnlySpan<byte> bytes) => HasEspAppDescriptor(bytes) ? CString(bytes, 0x20 + 48, 32) : null;

    private static string? CString(ReadOnlySpan<byte> bytes, int offset, int len)
    {
        if (bytes.Length < offset + len) return null;
        var s = bytes.Slice(offset, len);
        var end = s.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? s : s[..end]);
    }

    /// <summary>esp_chip_id_t for a catalog architecture ("esp32-s3" → 9), or null when unknown.</summary>
    public static int? ExpectedEspChipId(string architecture) => architecture.ToLowerInvariant() switch
    {
        "esp32" => 0x0000,
        "esp32-s2" or "esp32s2" => 0x0002,
        "esp32-c3" or "esp32c3" => 0x0005,
        "esp32-s3" or "esp32s3" => 0x0009,
        "esp32-c2" or "esp32c2" => 0x000C,
        "esp32-c6" or "esp32c6" => 0x000D,
        "esp32-h2" or "esp32h2" => 0x0010,
        _ => null
    };

    public static string EspChipName(int id) => id switch
    {
        0x0000 => "ESP32",
        0x0002 => "ESP32-S2",
        0x0005 => "ESP32-C3",
        0x0009 => "ESP32-S3",
        0x000C => "ESP32-C2",
        0x000D => "ESP32-C6",
        0x0010 => "ESP32-H2",
        _ => $"0x{id:X4}"
    };

    // ── Combined check ─────────────────────────────────────────────────────

    /// <summary>Required file extension for a method (lower-case, with dot).</summary>
    public static string ExpectedExtension(FirmwareUpdateMethod method) => method switch
    {
        FirmwareUpdateMethod.Esp32Wifi or FirmwareUpdateMethod.Esp32Ble => ".bin",
        FirmwareUpdateMethod.Nrf52BleDfu => ".zip",
        FirmwareUpdateMethod.Uf2Usb => ".uf2",
        _ => ""
    };

    /// <summary>Inspects a local file for a given device + method. Reads at most the first 4 KB for header
    /// checks; DFU zips are parsed fully (manifest sanity).</summary>
    public static FileCheckResult Inspect(string path, DeviceHardware hw, FirmwareUpdateMethod method)
    {
        var items = new List<FileCheckItem>();
        string? embeddedVersion = null;
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var expectedExt = ExpectedExtension(method);

        if (expectedExt.Length > 0 && ext != expectedExt)
        {
            items.Add(new FileCheckItem(FileCheckLevel.Error, "StrFwCheckWrongExt", expectedExt));
            return new FileCheckResult(items);
        }

        if (name.Contains(".factory.", StringComparison.OrdinalIgnoreCase))
            items.Add(new FileCheckItem(FileCheckLevel.Error, "StrFwCheckFactoryImage"));

        var target = hw.EffectiveTarget;
        bool nameMismatch = false;
        if (string.IsNullOrEmpty(target))
            items.Add(new FileCheckItem(FileCheckLevel.Warning, "StrFwCheckNoTarget"));
        else if (!IsValidFirmwareFileName(name, target, method == FirmwareUpdateMethod.Nrf52BleDfu ? "-ota.zip" : expectedExt))
        {
            nameMismatch = true;
            items.Add(new FileCheckItem(FileCheckLevel.Warning, "StrFwCheckNameMismatch", target));
        }
        else
            items.Add(new FileCheckItem(FileCheckLevel.Info, "StrFwCheckNameOk", target));

        byte[] head;
        try
        {
            using var fs = File.OpenRead(path);
            head = new byte[(int)Math.Min(4096, fs.Length)];
            _ = fs.Read(head, 0, head.Length);
        }
        catch (Exception ex)
        {
            items.Add(new FileCheckItem(FileCheckLevel.Error, "StrFwCheckUnreadable", ex.Message));
            return new FileCheckResult(items);
        }

        switch (method)
        {
            case FirmwareUpdateMethod.Esp32Wifi:
            case FirmwareUpdateMethod.Esp32Ble:
            {
                var chip = EspImageChipId(head);
                if (chip == null)
                    items.Add(new FileCheckItem(FileCheckLevel.Error, "StrFwCheckNotEspImage"));
                else
                {
                    if (!HasEspAppDescriptor(head))
                        items.Add(new FileCheckItem(FileCheckLevel.Error, "StrFwCheckNotAppImage"));
                    var expected = ExpectedEspChipId(hw.Architecture);
                    if (expected == null)
                        items.Add(new FileCheckItem(FileCheckLevel.Info, "StrFwCheckChipUnverified", EspChipName(chip.Value)));
                    else if (expected != chip)
                        items.Add(new FileCheckItem(FileCheckLevel.Error, "StrFwCheckChipMismatch", EspChipName(chip.Value), EspChipName(expected.Value)));
                    else
                        items.Add(new FileCheckItem(FileCheckLevel.Info, "StrFwCheckChipOk", EspChipName(chip.Value)));
                    embeddedVersion = EspAppVersion(head);
                    var project = EspAppProjectName(head);
                    if (embeddedVersion != null)
                        items.Add(new FileCheckItem(FileCheckLevel.Info, "StrFwCheckEmbeddedVersion", embeddedVersion, project ?? "?"));
                }
                break;
            }
            case FirmwareUpdateMethod.Uf2Usb:
            {
                if (!IsUf2(head))
                    items.Add(new FileCheckItem(FileCheckLevel.Error, "StrFwCheckNotUf2"));
                else
                {
                    var fam = Uf2FamilyId(head);
                    var expected = ExpectedUf2Families(hw.Architecture);
                    if (fam == null)
                        items.Add(new FileCheckItem(FileCheckLevel.Warning, "StrFwCheckUf2NoFamily"));
                    else if (expected.Count == 0)
                        items.Add(new FileCheckItem(FileCheckLevel.Info, "StrFwCheckChipUnverified", Uf2FamilyName(fam.Value)));
                    else if (!expected.Contains(fam.Value))
                        items.Add(new FileCheckItem(FileCheckLevel.Error, "StrFwCheckChipMismatch", Uf2FamilyName(fam.Value), Uf2FamilyName(expected.First())));
                    else
                        items.Add(new FileCheckItem(FileCheckLevel.Info, "StrFwCheckChipOk", Uf2FamilyName(fam.Value)));
                }
                break;
            }
            case FirmwareUpdateMethod.Nrf52BleDfu:
            {
                try
                {
                    var pkg = DfuPackage.FromFile(path);
                    items.Add(new FileCheckItem(FileCheckLevel.Info, "StrFwCheckDfuOk", pkg.BinFile, pkg.Firmware.Length));
                    if (pkg.InitPacket.Length > 256)
                        items.Add(new FileCheckItem(FileCheckLevel.Info, "StrFwCheckDfuSecureInit"));
                }
                catch (Exception ex)
                {
                    items.Add(new FileCheckItem(FileCheckLevel.Error, "StrFwCheckDfuInvalid", ex.Message));
                }
                break;
            }
        }

        // A self-built image (PlatformIO writes plain "firmware.bin"/"firmware.uf2") never matches the
        // release naming scheme; when the container itself checks out, say so instead of leaving the
        // name warning as the last word.
        if (nameMismatch && !items.Any(i => i.Level == FileCheckLevel.Error))
            items.Add(new FileCheckItem(FileCheckLevel.Info, "StrFwCheckCustomBuildHint"));

        return new FileCheckResult(items, embeddedVersion);
    }
}
