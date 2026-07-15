using System.IO;
using System.Net;
using System.Net.Http;

namespace MeshhessenClient.Services;

/// <summary>
/// Downloads a vector offline package for a bounding box into the same
/// vectortiles/ cache the live map uses (see VectorTileCacheService):
///   - basemap MVT tiles (one download serves all three map styles)
///   - optionally contours + hillshade (OpenTopo extras)
///   - optionally overlay sources from MapOverlayRegistry (e.g. emergency)
///   - always: the three styles, TileJSON, Noto Sans glyphs, sprites
/// Already-cached tiles (including known-empty ".missing" markers) are skipped,
/// so re-running a download only fetches what is new.
/// </summary>
public static class VectorPackageDownloaderService
{
    public const int BasemapMaxZoom = 14;   // server-side planetiler max; higher zooms overzoom client-side

    private static readonly string[] FontStacks = { "Noto Sans Regular", "Noto Sans Bold", "Noto Sans Italic" };
    private const int FontRangeCount = 8;   // 0-255 … 1792-2047 covers Latin incl. umlauts; rest via lazy cache

    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"MeshhessenClient/{version} (+https://meshhessenclient.de; contact: admin@meshhessenclient.de)");
        return client;
    }

    public record SourcePlan(string TileSource, int MinZoom, int MaxZoom);

    /// <summary>Tile source plans for the chosen options (bounded by maxDetailZoom).</summary>
    public static List<SourcePlan> BuildPlans(int maxDetailZoom, bool includeTopo, IEnumerable<MapOverlayDef> overlays)
    {
        var plans = new List<SourcePlan>
        {
            new("basemap", 1, Math.Min(maxDetailZoom, BasemapMaxZoom))
        };
        if (includeTopo)
        {
            plans.Add(new SourcePlan("contours", 11, Math.Min(maxDetailZoom, 17)));
            plans.Add(new SourcePlan("hillshade", 1, Math.Min(maxDetailZoom, BasemapMaxZoom)));
        }
        foreach (var o in overlays)
            plans.Add(new SourcePlan(o.TileSource, o.MinZoom, Math.Min(Math.Max(maxDetailZoom, o.MinZoom), o.MaxZoom)));
        return plans;
    }

    public static int EstimateTileCount(List<SourcePlan> plans, double north, double south, double east, double west)
        => plans.Sum(p => TileDownloaderService.EstimateTileCount(north, south, east, west, p.MinZoom, p.MaxZoom));

    public static Task<(int downloaded, int skipped, int errors)> DownloadAsync(
        double north, double south, double east, double west,
        List<SourcePlan> plans,
        string cacheBaseDir,
        IProgress<(int done, int total, string status)> progress,
        CancellationToken ct)
        // Everything (incl. building the URL list, which can be large) runs off the UI thread
        => Task.Run(() => DownloadCoreAsync(north, south, east, west, plans, cacheBaseDir, progress, ct), CancellationToken.None);

    private static async Task<(int downloaded, int skipped, int errors)> DownloadCoreAsync(
        double north, double south, double east, double west,
        List<SourcePlan> plans,
        string cacheBaseDir,
        IProgress<(int done, int total, string status)> progress,
        CancellationToken ct)
    {
        var baseUrl = $"https://{VectorTileCacheService.DefaultVectorHost}";
        var urls = new List<string>();

        // Static resources: styles, TileJSON, glyphs, sprites
        urls.Add($"{baseUrl}/styles/osm.json");
        urls.Add($"{baseUrl}/styles/opentopo.json");
        urls.Add($"{baseUrl}/styles/dark.json");
        foreach (var p in plans)
            urls.Add($"{baseUrl}/{p.TileSource}");
        foreach (var stack in FontStacks)
            for (int r = 0; r < FontRangeCount; r++)
                urls.Add($"{baseUrl}/fonts/{Uri.EscapeDataString(stack)}/{r * 256}-{r * 256 + 255}.pbf");
        foreach (var variant in new[] { "bright", "dark" })
            foreach (var file in new[] { "sprite.json", "sprite.png", "sprite@2x.json", "sprite@2x.png" })
                urls.Add($"{baseUrl}/sprites/{variant}/{file}");

        // Tiles per source
        foreach (var p in plans)
        {
            for (int z = p.MinZoom; z <= p.MaxZoom; z++)
            {
                var (x1, y1) = TileDownloaderService.LatLonToTile(north, west, z);
                var (x2, y2) = TileDownloaderService.LatLonToTile(south, east, z);
                for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
                    for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
                        urls.Add($"{baseUrl}/{p.TileSource}/{z}/{x}/{y}");
            }
        }

        int total = urls.Count, done = 0, downloaded = 0, skipped = 0, errors = 0;

        // Fixed worker pool instead of one task per URL: cancelling aborts at most
        // the in-flight requests instead of storming the UI thread with tens of
        // thousands of cancellation callbacks (that froze the client).
        var queue = new System.Collections.Concurrent.ConcurrentQueue<string>(urls);

        async Task WorkerAsync()
        {
            while (!ct.IsCancellationRequested && queue.TryDequeue(out var url))
            {
                try
                {
                    var result = await FetchOneAsync(url, cacheBaseDir, ct);
                    switch (result)
                    {
                        case FetchResult.Downloaded: Interlocked.Increment(ref downloaded); break;
                        case FetchResult.Skipped: Interlocked.Increment(ref skipped); break;
                        default: Interlocked.Increment(ref errors); break;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch { Interlocked.Increment(ref errors); }

                var d = Interlocked.Increment(ref done);
                if (d % 25 == 0 || d == total)
                    progress.Report((d, total, url.Substring(baseUrl.Length + 1)));
            }
        }

        var workers = Enumerable.Range(0, 6).Select(_ => Task.Run(WorkerAsync, CancellationToken.None)).ToList();
        await Task.WhenAll(workers);

        return (downloaded, skipped, errors);
    }

    private enum FetchResult { Downloaded, Skipped, Error }

    private static async Task<FetchResult> FetchOneAsync(string url, string cacheBaseDir, CancellationToken ct)
    {
        var uri = new Uri(url);
        var cachePath = VectorTileCacheService.MapUrlToCachePath(cacheBaseDir, uri);
        var missingMarker = cachePath + ".missing";

        // Styles/TileJSON always refreshed (small, may change); tiles/fonts/sprites skip existing
        var isStyleOrTileJson = uri.AbsolutePath.StartsWith("/styles/", StringComparison.OrdinalIgnoreCase)
                             || uri.Segments.Length <= 2;
        if (!isStyleOrTileJson && (File.Exists(cachePath) || File.Exists(missingMarker)))
            return FetchResult.Skipped;

        using var response = await _http.GetAsync(uri, ct);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllBytesAsync(missingMarker, Array.Empty<byte>(), ct);
            return FetchResult.Downloaded;
        }

        if (!response.IsSuccessStatusCode)
        {
            Logger.WriteLine($"[VectorDl] HTTP {(int)response.StatusCode} for {url}");
            return FetchResult.Error;
        }

        var data = await response.Content.ReadAsByteArrayAsync(ct);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllBytesAsync(cachePath, data, ct);
        if (File.Exists(missingMarker)) File.Delete(missingMarker);
        return FetchResult.Downloaded;
    }
}
