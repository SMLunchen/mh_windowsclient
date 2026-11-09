using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace MeshhessenClient.Services.Firmware;

/// <summary>Result of loading the hardware catalog + release list, from the network or the local cache.</summary>
public sealed record FirmwareCatalog(
    IReadOnlyList<DeviceHardware> Hardware,
    IReadOnlyList<FirmwareRelease> Releases,
    bool FromCache,
    DateTime? CacheTimestampUtc,
    string? Error)
{
    public bool IsEmpty => Hardware.Count == 0 && Releases.Count == 0;
    public IEnumerable<FirmwareRelease> Stable => Releases.Where(r => r.Channel == FirmwareChannel.Stable);
    public IEnumerable<FirmwareRelease> Alpha => Releases.Where(r => r.Channel == FirmwareChannel.Alpha);
}

/// <summary>
/// Fetches the Meshtastic public API documents the firmware wizard needs (device-hardware catalog and the
/// stable/alpha release list) and mirrors them into <c>firmware/</c> next to the exe so the wizard keeps
/// working offline. The documents are small; the cache is refreshed on every successful online load.
/// </summary>
public sealed class FirmwareCatalogService
{
    public const string ApiBaseUrl = "https://api.meshtastic.org/";
    public const string ReleaseBaseUrl = "https://release.meshtastic.org";
    public const string WebFlasherUrl = "https://flasher.meshtastic.org";

    private const string HardwareFile = "deviceHardware.json";
    private const string ReleasesFile = "releases.json";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    /// <summary>Cache + download directory (exe-relative like the other app folders).</summary>
    public string CacheDir { get; }

    public FirmwareCatalogService(HttpClient http, string? cacheDir = null)
    {
        _http = http;
        CacheDir = cacheDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firmware");
    }

    /// <summary>Online first (bounded by <paramref name="onlineTimeout"/>), cache as fallback. Never throws;
    /// an empty catalog with <see cref="FirmwareCatalog.Error"/> set means neither source was available.</summary>
    public async Task<FirmwareCatalog> LoadAsync(TimeSpan onlineTimeout, CancellationToken ct)
    {
        string? error = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(onlineTimeout);
            var hwTask = _http.GetStringAsync(ApiBaseUrl + "resource/deviceHardware", cts.Token);
            var relTask = _http.GetStringAsync(ApiBaseUrl + "github/firmware/list", cts.Token);
            await Task.WhenAll(hwTask, relTask);

            var hardware = ParseDeviceHardware(hwTask.Result);
            var releases = ParseReleases(relTask.Result);
            if (hardware.Count == 0 || releases.Count == 0)
                throw new InvalidDataException("API returned an empty document");

            try
            {
                Directory.CreateDirectory(CacheDir);
                await File.WriteAllTextAsync(Path.Combine(CacheDir, HardwareFile), hwTask.Result, ct);
                await File.WriteAllTextAsync(Path.Combine(CacheDir, ReleasesFile), relTask.Result, ct);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"[FW] Catalog cache write failed: {ex.Message}");
            }

            Logger.WriteLine($"[FW] Catalog online: {hardware.Count} devices, {releases.Count} releases");
            return new FirmwareCatalog(hardware, releases, false, DateTime.UtcNow, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Logger.WriteLine($"[FW] Catalog online fetch failed: {ex.Message} — trying cache");
        }

        return LoadFromCache(error);
    }

    /// <summary>Cache only (no network). Used when the user explicitly works offline.</summary>
    public FirmwareCatalog LoadFromCache(string? onlineError = null)
    {
        try
        {
            var hwPath = Path.Combine(CacheDir, HardwareFile);
            var relPath = Path.Combine(CacheDir, ReleasesFile);
            if (!File.Exists(hwPath) && !File.Exists(relPath))
                return new FirmwareCatalog(Array.Empty<DeviceHardware>(), Array.Empty<FirmwareRelease>(), true, null,
                    onlineError ?? "no cache");

            var hardware = File.Exists(hwPath) ? ParseDeviceHardware(File.ReadAllText(hwPath)) : Array.Empty<DeviceHardware>();
            var releases = File.Exists(relPath) ? ParseReleases(File.ReadAllText(relPath)) : Array.Empty<FirmwareRelease>();
            var stamp = File.Exists(hwPath) ? File.GetLastWriteTimeUtc(hwPath) : File.GetLastWriteTimeUtc(relPath);
            Logger.WriteLine($"[FW] Catalog from cache ({stamp:u}): {hardware.Count} devices, {releases.Count} releases");
            return new FirmwareCatalog(hardware, releases, true, stamp, onlineError);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[FW] Catalog cache read failed: {ex.Message}");
            return new FirmwareCatalog(Array.Empty<DeviceHardware>(), Array.Empty<FirmwareRelease>(), true, null,
                onlineError ?? ex.Message);
        }
    }

    // ── Parsing (pure, unit-tested) ────────────────────────────────────────

    public static IReadOnlyList<DeviceHardware> ParseDeviceHardware(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<DeviceHardware>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            list.Add(new DeviceHardware(
                HwModel: GetInt(e, "hwModel") ?? 0,
                HwModelSlug: GetString(e, "hwModelSlug") ?? "",
                PlatformioTarget: GetString(e, "platformioTarget") ?? "",
                Architecture: GetString(e, "architecture") ?? "",
                ActivelySupported: GetBool(e, "activelySupported") ?? false,
                DisplayName: GetString(e, "displayName") ?? "",
                RequiresDfu: GetBool(e, "requiresDfu"),
                RequiresBootloaderUpgradeForOta: GetBool(e, "requiresBootloaderUpgradeForOta"),
                SupportLevel: GetInt(e, "supportLevel")));
        }
        return list;
    }

    public static IReadOnlyList<FirmwareRelease> ParseReleases(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<FirmwareRelease>();
        if (!doc.RootElement.TryGetProperty("releases", out var releases)) return list;
        AddChannel(releases, "stable", FirmwareChannel.Stable, list);
        AddChannel(releases, "alpha", FirmwareChannel.Alpha, list);
        return list;

        static void AddChannel(JsonElement releases, string name, FirmwareChannel channel, List<FirmwareRelease> into)
        {
            if (!releases.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var e in arr.EnumerateArray())
            {
                var id = GetString(e, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                into.Add(new FirmwareRelease(
                    id, GetString(e, "title") ?? id, GetString(e, "page_url") ?? "",
                    GetString(e, "zip_url") ?? "", GetString(e, "release_notes") ?? "", channel));
            }
        }
    }

    /// <summary>
    /// Picks the catalog entry for a device. Mirrors the Android lookup: all entries with the device's
    /// hardware-model number, disambiguated by the PlatformIO environment the device reports (exact, then
    /// case-insensitive, then the first entry); when the hwModel is unknown the pioEnv alone is tried. The
    /// reported pioEnv wins over the catalog target because it names the build actually running on the board
    /// (e.g. a RAK4631 variant).
    /// </summary>
    public static DeviceHardware? ResolveHardware(IReadOnlyList<DeviceHardware> catalog, int hwModel, string? pioEnv)
    {
        var target = string.IsNullOrWhiteSpace(pioEnv) ? null : pioEnv.Trim();
        var candidates = catalog.Where(h => h.HwModel == hwModel).ToList();
        if (candidates.Count == 0 && target != null)
            candidates = catalog.Where(h => string.Equals(h.PlatformioTarget, target, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0) return null;

        DeviceHardware match = candidates[0];
        if (target != null)
        {
            match = candidates.FirstOrDefault(h => h.PlatformioTarget == target)
                ?? candidates.FirstOrDefault(h => string.Equals(h.PlatformioTarget, target, StringComparison.OrdinalIgnoreCase))
                ?? candidates[0];
            if (!string.Equals(match.PlatformioTarget, target, StringComparison.Ordinal))
                match = match with { PlatformioTarget = target };
        }
        return match;
    }

    /// <summary>Best-effort architecture from a HardwareModel enum name, for devices missing from the catalog
    /// (or when the catalog is unavailable). Returns "esp32", "nrf52840", "rp2040" or null.</summary>
    public static string? GuessArchitecture(string hwModelName)
    {
        var n = hwModelName.ToUpperInvariant();
        if (n.Length == 0) return null;
        string[] nrf = { "RAK4631", "RAK2560", "RAK3", "T_ECHO", "TECHO", "T114", "NANO_G2", "WIO_WM1110", "WIO_TRACKER", "TRACKER_T1000", "CANARYONE", "NRF52", "PPR", "XIAO_NRF52", "MESHLINK", "THINKNODE_M1", "T1000", "ME25LS01", "MS24SF1", "SEEED_SOLAR", "HELTEC_MESH_NODE", "T_IMPULSE", "MUZI", "R1_NEO", "FEATHER_DIY", "RAK_WISMESH" };
        string[] rp = { "RPI_PICO", "PICO", "RP2040", "RP2350", "SENSELORA_RP2040", "RAK11310", "XIAO_RP2040" };
        string[] esp = { "TLORA", "TBEAM", "HELTEC", "T_DECK", "TDECK", "T_WATCH", "TWATCH", "STATION_G1", "STATION_G2", "DIY_V1", "NANO_G1", "M5STACK", "ESP32", "PICOMPUTER", "T_ETH", "TRACKER_T1000_E_ESP", "UNPHONE", "CHATTER", "WIPHONE", "SENSECAP", "SEEED_XIAO_S3", "RAK11200", "T_LORA_PAGER", "TLORA_PAGER", "RADIOMASTER", "MESH_TAB", "THINKNODE_M2", "T_ETH_ELITE", "CDEBYTE", "BETAFPV", "LILYGO" };
        if (nrf.Any(n.Contains)) return "nrf52840";
        if (rp.Any(n.Contains)) return "rp2040";
        if (esp.Any(n.Contains)) return "esp32";
        return null;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static bool? GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : null;
}
