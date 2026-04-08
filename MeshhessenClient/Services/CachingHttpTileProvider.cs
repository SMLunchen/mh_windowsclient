using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using BruTile;

namespace MeshhessenClient.Services;

/// <summary>
/// Online tile provider: fetches tiles on demand and caches them locally.
///
/// Own-server mode (useHttpCacheHeaders=false):
///   Stores tiles permanently – tile on disk is returned immediately without re-checking
///   the server. Effectively a lazy download that fills the same directory as the
///   offline tile set, so switching back to offline "just works".
///
/// OSM mode (useHttpCacheHeaders=true):
///   Respects HTTP cache semantics (Cache-Control max-age / ETag / If-None-Match).
///   Minimum cache TTL is 7 days as required by the OSM Tile Usage Policy.
///   Stale-on-error: serves cached tile when the network is unavailable.
/// </summary>
public class CachingHttpTileProvider : ITileProvider
{
    private readonly string _cacheBaseDir;
    private readonly string _sourceSubDir;
    private readonly string _urlTemplate;
    private readonly bool _useHttpCacheHeaders;

    // Separate clients: OSM policy recommends low-latency; own server allows longer timeout
    private static readonly HttpClient _httpClientOsm      = CreateHttpClient(TimeSpan.FromSeconds(20));
    private static readonly HttpClient _httpClientOwnServer = CreateHttpClient(TimeSpan.FromSeconds(30));

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly()
                          .GetName().Version?.ToString(3) ?? "1.0.0";

        // HTTP/3 preferred; automatic fall-back to HTTP/2 then HTTP/1.1
        var client = new HttpClient
        {
            DefaultRequestVersion = HttpVersion.Version30,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Timeout = timeout
        };

        // OSM policy: app name + contact URL/email required
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"MeshhessenClient/{version} (+https://meshhessenclient.de; contact: admin@meshhessenclient.de)");

        return client;
    }

    private HttpClient HttpClient => _useHttpCacheHeaders ? _httpClientOsm : _httpClientOwnServer;

    /// <param name="cacheBaseDir">Root of the tile cache, e.g. the "maptiles" folder.</param>
    /// <param name="sourceSubDir">Sub-folder for this source, e.g. "osm", "osm_online".</param>
    /// <param name="urlTemplate">URL with {z}/{x}/{y} placeholders.</param>
    /// <param name="useHttpCacheHeaders">
    ///   true  = OSM mode: honour HTTP cache headers, 7-day minimum TTL.<br/>
    ///   false = own-server mode: permanent cache, no expiry check.
    /// </param>
    public CachingHttpTileProvider(
        string cacheBaseDir,
        string sourceSubDir,
        string urlTemplate,
        bool useHttpCacheHeaders)
    {
        _cacheBaseDir = cacheBaseDir;
        _sourceSubDir = sourceSubDir;
        _urlTemplate = urlTemplate;
        _useHttpCacheHeaders = useHttpCacheHeaders;
    }

    public Task<byte[]?> GetTileAsync(TileInfo tileInfo)
    {
        var z = tileInfo.Index.Level;
        var x = tileInfo.Index.Col;
        // BruTile TMS: row 0 = south. OSM: Y=0 = north. Convert.
        var yOsm = (1 << z) - 1 - tileInfo.Index.Row;

        var tilePath = Path.Combine(
            _cacheBaseDir, _sourceSubDir,
            z.ToString(), x.ToString(), $"{yOsm}.png");
        var metaPath = tilePath + ".meta";

        return _useHttpCacheHeaders
            ? FetchOsmTile(tilePath, metaPath, z, x, yOsm)
            : FetchOwnServerTile(tilePath, z, x, yOsm);
    }

    // ── Own-server mode ──────────────────────────────────────────────────────

    private async Task<byte[]?> FetchOwnServerTile(string tilePath, int z, int x, int y)
    {
        if (File.Exists(tilePath))
        {
            try { return await File.ReadAllBytesAsync(tilePath); } catch { }
        }

        var url = BuildUrl(z, x, y);
        try
        {
            var data = await HttpClient.GetByteArrayAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(tilePath)!);
            await File.WriteAllBytesAsync(tilePath, data);
            return data;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[CachingTile] Fetch error {url}: {ex.Message}");
            return null;
        }
    }

    // ── OSM mode ─────────────────────────────────────────────────────────────

    private async Task<byte[]?> FetchOsmTile(string tilePath, string metaPath, int z, int x, int y)
    {
        TileMeta? meta = null;

        if (File.Exists(tilePath))
        {
            meta = LoadMeta(metaPath);
            if (meta != null && meta.Expires > DateTime.UtcNow)
            {
                try { return await File.ReadAllBytesAsync(tilePath); } catch { }
            }
        }

        var url = BuildUrl(z, x, y);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Conditional request when we have a cached tile with metadata
            if (meta != null && File.Exists(tilePath))
            {
                if (!string.IsNullOrEmpty(meta.ETag))
                    request.Headers.IfNoneMatch.ParseAdd(meta.ETag);
                else if (meta.LastModified != default)
                    request.Headers.IfModifiedSince = new DateTimeOffset(meta.LastModified, TimeSpan.Zero);
            }

            using var response = await HttpClient.SendAsync(
                request, HttpCompletionOption.ResponseContentRead);

            if (response.StatusCode == HttpStatusCode.NotModified && File.Exists(tilePath))
            {
                // Tile unchanged on server – refresh expiry and return cached copy
                SaveMeta(metaPath, new TileMeta
                {
                    ETag = meta?.ETag,
                    LastModified = meta?.LastModified ?? default,
                    Expires = ComputeExpiry(response.Headers.CacheControl)
                });
                try { return await File.ReadAllBytesAsync(tilePath); } catch { }
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.WriteLine($"[CachingTile] HTTP {(int)response.StatusCode} for {url}");
                if (File.Exists(tilePath))
                    try { return await File.ReadAllBytesAsync(tilePath); } catch { }
                return null;
            }

            var data = await response.Content.ReadAsByteArrayAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(tilePath)!);
            await File.WriteAllBytesAsync(tilePath, data);

            SaveMeta(metaPath, new TileMeta
            {
                ETag = response.Headers.ETag?.ToString(),
                LastModified = response.Content.Headers.LastModified?.UtcDateTime ?? default,
                Expires = ComputeExpiry(response.Headers.CacheControl)
            });

            return data;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[CachingTile] Error fetching {url}: {ex.Message}");
            // Serve stale cache rather than a blank tile on network error
            if (File.Exists(tilePath))
                try { return await File.ReadAllBytesAsync(tilePath); } catch { }
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string BuildUrl(int z, int x, int y) =>
        _urlTemplate
            .Replace("{z}", z.ToString())
            .Replace("{x}", x.ToString())
            .Replace("{y}", y.ToString());

    private static DateTime ComputeExpiry(CacheControlHeaderValue? cc)
    {
        var minExpiry = DateTime.UtcNow.AddDays(7); // OSM policy: minimum 7 days
        if (cc?.MaxAge is TimeSpan maxAge)
        {
            var fromMaxAge = DateTime.UtcNow.Add(maxAge);
            return fromMaxAge > minExpiry ? fromMaxAge : minExpiry;
        }
        return minExpiry;
    }

    private static TileMeta? LoadMeta(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<TileMeta>(File.ReadAllText(path)); }
        catch { return null; }
    }

    private static void SaveMeta(string path, TileMeta meta)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(meta)); }
        catch { }
    }

    private sealed class TileMeta
    {
        public string? ETag { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime Expires { get; set; }
    }
}
