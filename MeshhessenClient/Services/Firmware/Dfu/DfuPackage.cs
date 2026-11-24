using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace MeshhessenClient.Services.Firmware.Dfu;

/// <summary>Contents of a Nordic DFU <c>.zip</c> package: the signed init packet (.dat) and the application image (.bin).</summary>
public sealed record DfuPackage(byte[] InitPacket, byte[] Firmware, string BinFile, string DatFile, int ImageCount)
{
    private const long MaxEntryBytes = 8 * 1024 * 1024;

    public static DfuPackage FromFile(string zipPath)
    {
        using var fs = File.OpenRead(zipPath);
        return FromStream(fs);
    }

    public static DfuPackage FromBytes(byte[] zip)
    {
        using var ms = new MemoryStream(zip);
        return FromStream(ms);
    }

    public static DfuPackage FromStream(Stream zipStream)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var e in zip.Entries)
        {
            if (e.Length == 0 || e.Length > MaxEntryBytes) continue;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            entries[e.FullName] = ms.ToArray();
        }
        return FromEntries(entries);
    }

    /// <summary>Resolves <c>manifest.json</c> → application entry → .dat/.bin. Refuses packages that carry
    /// no application image (SoftDevice/bootloader-only packages must never be flashed as an application).</summary>
    public static DfuPackage FromEntries(IReadOnlyDictionary<string, byte[]> entries)
    {
        if (!entries.TryGetValue("manifest.json", out var manifestBytes))
            throw new DfuException("manifest.json not found in DFU zip");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(manifestBytes); }
        catch (JsonException ex) { throw new DfuException($"Failed to parse manifest.json: {ex.Message}"); }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("manifest", out var manifest))
                throw new DfuException("manifest.json has no 'manifest' object");

            int imageCount = 0;
            foreach (var key in new[] { "application", "softdevice_bootloader", "bootloader", "softdevice" })
                if (manifest.TryGetProperty(key, out var _)) imageCount++;

            if (!manifest.TryGetProperty("application", out var app))
            {
                var found = new[] { "softdevice_bootloader", "bootloader", "softdevice" }
                    .FirstOrDefault(k => manifest.TryGetProperty(k, out _));
                throw new DfuException(found != null
                    ? $"manifest.json declares no 'application' image (found '{found}'). SoftDevice/bootloader packages must not be flashed as an application."
                    : "No firmware entry found in manifest.json");
            }

            var bin = app.TryGetProperty("bin_file", out var b) ? b.GetString() : null;
            var dat = app.TryGetProperty("dat_file", out var d) ? d.GetString() : null;
            if (string.IsNullOrEmpty(bin) || string.IsNullOrEmpty(dat))
                throw new DfuException("manifest.json application entry lacks bin_file/dat_file");
            if (!entries.TryGetValue(dat, out var initPacket))
                throw new DfuException($"Init packet '{dat}' not found in zip");
            if (!entries.TryGetValue(bin, out var firmware))
                throw new DfuException($"Firmware '{bin}' not found in zip");

            if (imageCount > 1)
                Logger.WriteLine($"[DFU] Package declares {imageCount} images; only the application ({bin}) is flashed");
            return new DfuPackage(initPacket, firmware, bin, dat, imageCount);
        }
    }
}
