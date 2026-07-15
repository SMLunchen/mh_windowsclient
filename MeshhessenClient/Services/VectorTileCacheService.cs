using System.IO;
using System.Net;
using System.Net.Http;

namespace MeshhessenClient.Services;

/// <summary>
/// Backend for the WebView2 vector map: every request the map page makes to a
/// vector tile server (style JSON, MVT tiles, glyphs, sprites, hillshade) is
/// intercepted and answered through this service.
///
/// Semantics mirror CachingHttpTileProvider in own-server mode: resources
/// fetched online are stored permanently under vectortiles/&lt;host&gt;/&lt;path&gt;,
/// so areas viewed online remain available offline ("lazy download").
/// Styles and TileJSON are fetched network-first (server-side style updates
/// arrive without a client update), tiles/glyphs/sprites cache-first.
/// In offline mode nothing is fetched – the cache is the only source.
/// </summary>
public class VectorTileCacheService
{
    /// <summary>Attribution required by the OpenMapTiles license (CC-BY).</summary>
    public const string Attribution = "© OpenMapTiles © OpenStreetMap contributors";

    public const string DefaultVectorHost = "vectortile.meshhessenclient.de";

    private readonly string _cacheBaseDir;   // .../vectortiles
    private readonly HashSet<string> _handledHosts = new(StringComparer.OrdinalIgnoreCase) { DefaultVectorHost };

    public bool OfflineMode { get; set; }

    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly()
                          .GetName().Version?.ToString(3) ?? "1.0.0";
        var handler = new HttpClientHandler
        {
            // Martin/nginx deliver MVT gzip-compressed; store and serve decompressed bytes
            AutomaticDecompression = DecompressionMethods.All
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        // Server ACL requires MeshhessenClient/* user agent (same as raster server)
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"MeshhessenClient/{version} (+https://meshhessenclient.de; contact: admin@meshhessenclient.de)");
        return client;
    }

    public VectorTileCacheService(string cacheBaseDir)
    {
        _cacheBaseDir = cacheBaseDir;
    }

    /// <summary>Registers the hosts of the configured style URLs so custom style servers are cached too.</summary>
    public void RegisterStyleUrls(IEnumerable<string> styleUrls)
    {
        foreach (var url in styleUrls)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                _handledHosts.Add(uri.Host);
        }
    }

    public bool IsHandledHost(Uri uri) => _handledHosts.Contains(uri.Host);

    public record VectorResponse(int StatusCode, string ReasonPhrase, string Headers, byte[]? Body);

    public async Task<VectorResponse> GetResponseAsync(string url)
    {
        var uri = new Uri(url);
        var cachePath = GetCachePath(uri);
        var missingMarker = cachePath + ".missing";

        // Styles + TileJSON network-first (small, may change server-side); everything else cache-first
        var networkFirst = uri.AbsolutePath.StartsWith("/styles/", StringComparison.OrdinalIgnoreCase)
                        || (Path.GetFileName(cachePath).EndsWith(".bin", StringComparison.Ordinal) && uri.Segments.Length <= 2);

        if (!networkFirst || OfflineMode)
        {
            var cached = TryReadCache(cachePath, missingMarker);
            if (cached != null) return cached;
            if (OfflineMode)
                return new VectorResponse(404, "Not Found (offline)", CorsHeaders(null), null);
        }

        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseContentRead);

            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            {
                // Empty tile: remember it so offline mode gives the same answer without a request
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                try { await File.WriteAllBytesAsync(missingMarker, Array.Empty<byte>()); } catch { }
                return new VectorResponse(204, "No Content", CorsHeaders(null), null);
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.WriteLine($"[VectorTile] HTTP {(int)response.StatusCode} for {url}");
                var stale = TryReadCache(cachePath, missingMarker);
                return stale ?? new VectorResponse((int)response.StatusCode, response.ReasonPhrase ?? "Error", CorsHeaders(null), null);
            }

            var data = await response.Content.ReadAsByteArrayAsync();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                await File.WriteAllBytesAsync(cachePath, data);
                if (File.Exists(missingMarker)) File.Delete(missingMarker);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"[VectorTile] Cache write failed {cachePath}: {ex.Message}");
            }
            return new VectorResponse(200, "OK", CorsHeaders(SniffContentType(data)), data);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[VectorTile] Fetch error {url}: {ex.Message}");
            // Stale-on-error: a cached copy beats a blank map when the network is down
            var stale = TryReadCache(cachePath, missingMarker);
            return stale ?? new VectorResponse(504, "Gateway Timeout", CorsHeaders(null), null);
        }
    }

    private VectorResponse? TryReadCache(string cachePath, string missingMarker)
    {
        try
        {
            if (File.Exists(missingMarker))
                return new VectorResponse(204, "No Content", CorsHeaders(null), null);
            if (File.Exists(cachePath))
            {
                var data = File.ReadAllBytes(cachePath);
                return new VectorResponse(200, "OK", CorsHeaders(SniffContentType(data)), data);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[VectorTile] Cache read failed {cachePath}: {ex.Message}");
        }
        return null;
    }

    private string GetCachePath(Uri uri) => MapUrlToCachePath(_cacheBaseDir, uri);

    /// <summary>
    /// Maps a URL to vectortiles/&lt;host&gt;/&lt;path&gt;. Extensionless leaves get ".bin"
    /// so a TileJSON endpoint (/basemap) and its tile directory (/basemap/z/x/y)
    /// can coexist on disk. Shared with the offline package downloader.
    /// </summary>
    public static string MapUrlToCachePath(string cacheBaseDir, Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        var safe = segments.Select(s => string.Join("_", s.Split(Path.GetInvalidFileNameChars()))).ToArray();
        if (safe.Length == 0 || safe[^1].Length == 0) safe = new[] { "index" };
        if (!Path.HasExtension(safe[^1])) safe[^1] += ".bin";
        return Path.Combine(new[] { cacheBaseDir, uri.Host }.Concat(safe).ToArray());
    }

    // Content type by signature – avoids sidecar metadata files
    private static string SniffContentType(byte[] data)
    {
        if (data.Length == 0) return "application/octet-stream";
        return data[0] switch
        {
            0x89 => "image/png",
            (byte)'{' or (byte)'[' => "application/json",
            _ => "application/x-protobuf"
        };
    }

    private static string CorsHeaders(string? contentType)
    {
        // map.html lives on its own virtual origin – intercepted responses need CORS headers
        var headers = "Access-Control-Allow-Origin: *";
        if (contentType != null) headers += $"\nContent-Type: {contentType}";
        return headers;
    }

    /// <summary>
    /// Extracts the embedded map page assets (map.html, MapLibre GL JS/CSS) to
    /// &lt;cacheBaseDir&gt;/_assets for the WebView2 virtual host mapping.
    /// Re-extracts when version or asset content changed (stamp includes total size).
    /// </summary>
    public static string ExtractAssets(string cacheBaseDir)
    {
        var assetsDir = Path.Combine(cacheBaseDir, "_assets");
        Directory.CreateDirectory(assetsDir);

        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        const string prefix = "MeshhessenClient.Resources.VectorMap.";
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(n => n).ToList();

        long totalBytes = 0;
        foreach (var name in names)
        {
            using var s = assembly.GetManifestResourceStream(name)!;
            totalBytes += s.Length;
        }

        var version = assembly.GetName().Version?.ToString() ?? "0";
        var stamp = $"{version}:{names.Count}:{totalBytes}";
        var stampFile = Path.Combine(assetsDir, ".version");
        if (File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp && File.Exists(Path.Combine(assetsDir, "map.html")))
            return assetsDir;

        foreach (var name in names)
        {
            var fileName = name.Substring(prefix.Length);
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var file = File.Create(Path.Combine(assetsDir, fileName));
            stream.CopyTo(file);
        }
        File.WriteAllText(stampFile, stamp);
        Logger.WriteLine($"[VectorTile] Map assets extracted to {assetsDir}");
        return assetsDir;
    }
}
