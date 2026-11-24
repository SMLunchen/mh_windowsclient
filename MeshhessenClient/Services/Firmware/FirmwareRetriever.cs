using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MeshhessenClient.Services.Firmware;

/// <summary>One <c>files[]</c> entry of a per-target <c>.mt.json</c> manifest (published since 2.7.17).</summary>
public sealed record ManifestFile(string Name, string PartName, string Md5, long Bytes);

/// <summary>
/// Resolves and downloads firmware artifacts from release.meshtastic.org into the exe-relative
/// <c>firmware/&lt;version&gt;/</c> cache. Mirrors the Android retriever's lookup order so both clients
/// flash the same bytes:
/// ESP32 OTA image: <c>.mt.json</c> → <c>app0</c> entry (md5/size verified) → <c>-update.bin</c> → <c>.bin</c> → release zip.
/// nRF52 BLE: <c>-ota.zip</c> (Nordic DFU package). USB: <c>.uf2</c>.
/// </summary>
public sealed class FirmwareRetriever
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;

    public FirmwareRetriever(HttpClient http, string cacheDir)
    {
        _http = http;
        _cacheDir = cacheDir;
    }

    public static string ArtifactBaseUrl(FirmwareRelease release) =>
        $"{FirmwareCatalogService.ReleaseBaseUrl}/{release.Version}";

    public static string FileName(string target, string version, string suffix) =>
        $"firmware-{target}-{version}{suffix}";

    /// <summary>Directory where artifacts of this release are cached.</summary>
    public string ReleaseDir(FirmwareRelease release) => Path.Combine(_cacheDir, release.Version);

    /// <summary>Candidate cache filenames for a method, most specific first (mirrors the download order).</summary>
    public static IEnumerable<string> CandidateFileNames(string target, string version, FirmwareUpdateMethod method) => method switch
    {
        FirmwareUpdateMethod.Esp32Wifi or FirmwareUpdateMethod.Esp32Ble =>
            new[] { FileName(target, version, ".bin"), FileName(target, version, "-update.bin") },
        FirmwareUpdateMethod.Nrf52BleDfu => new[] { FileName(target, version, "-ota.zip") },
        FirmwareUpdateMethod.Uf2Usb => new[] { FileName(target, version, ".uf2") },
        _ => Array.Empty<string>()
    };

    /// <summary>Path of an already downloaded (prefetched) artifact for this release/device/method, or null.
    /// Lets the wizard mark releases as usable offline before any download is attempted.</summary>
    public string? FindCachedArtifact(FirmwareRelease release, DeviceHardware hw, FirmwareUpdateMethod method)
    {
        var dir = ReleaseDir(release);
        if (!Directory.Exists(dir)) return null;
        foreach (var name in CandidateFileNames(hw.EffectiveTarget, release.Version, method))
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p) && new FileInfo(p).Length > 0) return p;
        }
        return null;
    }

    // ── ESP32 ───────────────────────────────────────────────────────────────

    public async Task<FirmwareArtifact?> GetEsp32OtaImageAsync(FirmwareRelease release, DeviceHardware hw,
        IProgress<double>? progress, CancellationToken ct)
    {
        var target = hw.EffectiveTarget;
        var version = release.Version;

        // 1. Manifest (authoritative since 2.7.17): the app0 image, verified against md5 + size.
        var manifestJson = await TryGetStringAsync($"{ArtifactBaseUrl(release)}/{FileName(target, version, ".mt.json")}", ct);
        if (manifestJson != null)
        {
            var app0 = FindOtaEntry(manifestJson);
            if (app0 != null)
            {
                var art = await DownloadAsync(release, app0.Name, progress, ct, app0);
                if (art != null) return art;
                Logger.WriteLine("[FW] Manifest app0 download/verification failed — falling back to filename heuristics");
            }
            else
            {
                Logger.WriteLine("[FW] Manifest has no app0 entry — falling back to filename heuristics");
            }
        }

        // 2. Bare OTA app image; preferred over the plain .bin for pre-2.7.17 releases where .bin is a merged image.
        var art2 = await DownloadAsync(release, FileName(target, version, "-update.bin"), progress, ct);
        if (art2 != null) return art2;

        // 3. Plain .bin (the app image on 2.7.17+).
        var art3 = await DownloadAsync(release, FileName(target, version, ".bin"), progress, ct);
        if (art3 != null) return art3;

        // 4. Legacy release zip (only old releases still point zip_url at an actual zip).
        return await ExtractFromReleaseZipAsync(release, hw, ".bin", progress, ct);
    }

    // ── nRF52 / RP2040 ─────────────────────────────────────────────────────

    public async Task<FirmwareArtifact?> GetNrf52DfuPackageAsync(FirmwareRelease release, DeviceHardware hw,
        IProgress<double>? progress, CancellationToken ct)
    {
        var art = await DownloadAsync(release, FileName(hw.EffectiveTarget, release.Version, "-ota.zip"), progress, ct);
        return art ?? await ExtractFromReleaseZipAsync(release, hw, "-ota.zip", progress, ct);
    }

    public async Task<FirmwareArtifact?> GetUf2Async(FirmwareRelease release, DeviceHardware hw,
        IProgress<double>? progress, CancellationToken ct)
    {
        var art = await DownloadAsync(release, FileName(hw.EffectiveTarget, release.Version, ".uf2"), progress, ct);
        return art ?? await ExtractFromReleaseZipAsync(release, hw, ".uf2", progress, ct);
    }

    // ── Release target list ────────────────────────────────────────────────

    /// <summary>
    /// Boards a release was built for. Newer releases publish <c>zip_url</c> as a JSON target manifest
    /// (<c>{"version":…, "targets":[{"board":…,"platform":…}]}</c>); for a real zip or on error this returns
    /// null, which the caller treats as "unknown" rather than "unsupported".
    /// </summary>
    public async Task<IReadOnlySet<string>?> GetReleaseTargetsAsync(FirmwareRelease release, CancellationToken ct)
    {
        if (!release.ZipUrl.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
        var json = await TryGetStringAsync(release.ZipUrl, ct);
        return json == null ? null : ParseReleaseTargets(json);
    }

    public static IReadOnlySet<string>? ParseReleaseTargets(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Array)
                return null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in targets.EnumerateArray())
                if (t.TryGetProperty("board", out var b) && b.ValueKind == JsonValueKind.String)
                    set.Add(b.GetString()!);
            return set;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Manifest helpers (pure) ────────────────────────────────────────────

    /// <summary>The <c>app0</c> (main application) entry of a <c>.mt.json</c>, or null.</summary>
    public static ManifestFile? FindOtaEntry(string manifestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var f in files.EnumerateArray())
            {
                var part = f.TryGetProperty("part_name", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : "";
                if (part != "app0") continue;
                var name = f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.Length == 0) return null;
                var md5 = f.TryGetProperty("md5", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                var bytes = f.TryGetProperty("bytes", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetInt64() : 0;
                return new ManifestFile(name, part!, md5, bytes);
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The <c>mcu</c> field of a <c>.mt.json</c> (e.g. "esp32s3"), used to cross-check the image header.</summary>
    public static string? ManifestMcu(string manifestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            return doc.RootElement.TryGetProperty("mcu", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Download / cache ───────────────────────────────────────────────────

    private async Task<FirmwareArtifact?> DownloadAsync(FirmwareRelease release, string fileName,
        IProgress<double>? progress, CancellationToken ct, ManifestFile? expected = null)
    {
        var dir = ReleaseDir(release);
        var path = Path.Combine(dir, fileName);

        if (File.Exists(path) && new FileInfo(path).Length > 0 && await MatchesManifestAsync(path, expected, ct))
        {
            Logger.WriteLine($"[FW] Using cached {fileName}");
            progress?.Report(1.0);
            return await DescribeAsync(path, ct);
        }

        var url = $"{ArtifactBaseUrl(release)}/{fileName}";
        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.WriteLine($"[FW] {fileName}: HTTP {(int)resp.StatusCode}");
                return null;
            }
            Directory.CreateDirectory(dir);
            var tmp = path + ".part";
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tmp))
            {
                var total = resp.Content.Headers.ContentLength ?? expected?.Bytes ?? 0;
                var buf = new byte[64 * 1024];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
                }
            }
            if (!await MatchesManifestAsync(tmp, expected, ct))
            {
                Logger.WriteLine($"[FW] {fileName}: manifest integrity check failed (size/md5)");
                File.Delete(tmp);
                return null;
            }
            File.Move(tmp, path, overwrite: true);
            progress?.Report(1.0);
            Logger.WriteLine($"[FW] Downloaded {fileName} ({new FileInfo(path).Length} bytes)");
            return await DescribeAsync(path, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[FW] Download {fileName} failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> MatchesManifestAsync(string path, ManifestFile? expected, CancellationToken ct)
    {
        if (expected == null) return true;
        if (expected.Bytes > 0 && new FileInfo(path).Length != expected.Bytes) return false;
        if (string.IsNullOrEmpty(expected.Md5)) return true;
        await using var fs = File.OpenRead(path);
        var hash = await MD5.HashDataAsync(fs, ct);
        return string.Equals(Convert.ToHexString(hash), expected.Md5, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<FirmwareArtifact?> ExtractFromReleaseZipAsync(FirmwareRelease release, DeviceHardware hw,
        string suffix, IProgress<double>? progress, CancellationToken ct)
    {
        if (!release.ZipUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        var zipUrl = ResolveZipUrl(release.ZipUrl, hw.Architecture);
        var dir = ReleaseDir(release);
        var zipPath = Path.Combine(dir, Path.GetFileName(new Uri(zipUrl).AbsolutePath));
        try
        {
            if (!File.Exists(zipPath))
            {
                using var resp = await _http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode) return null;
                Directory.CreateDirectory(dir);
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(zipPath);
                await src.CopyToAsync(dst, ct);
            }
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e =>
                FirmwareFileInspector.IsValidFirmwareFileName(e.FullName, hw.EffectiveTarget, suffix));
            if (entry == null) return null;
            var outPath = Path.Combine(dir, Path.GetFileName(entry.FullName));
            entry.ExtractToFile(outPath, overwrite: true);
            progress?.Report(1.0);
            return await DescribeAsync(outPath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[FW] Release zip fallback failed: {ex.Message}");
            return null;
        }
    }

    private static readonly string[] KnownArchs = { "esp32-s3", "esp32-c3", "esp32-c6", "nrf52840", "rp2040", "stm32", "esp32" };

    public static string ResolveZipUrl(string url, string targetArch)
    {
        foreach (var arch in KnownArchs)
            if (url.Contains(arch, StringComparison.OrdinalIgnoreCase))
                return url.Replace(arch, targetArch.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
        return url;
    }

    public static async Task<FirmwareArtifact> DescribeAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var sha = await SHA256.HashDataAsync(fs, ct);
        return new FirmwareArtifact(path, Path.GetFileName(path), fs.Length, Convert.ToHexString(sha).ToLowerInvariant());
    }

    private async Task<string?> TryGetStringAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(ct) : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[FW] GET {url} failed: {ex.Message}");
            return null;
        }
    }
}
