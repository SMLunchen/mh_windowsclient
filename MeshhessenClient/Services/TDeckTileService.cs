using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace MeshhessenClient.Services;

public record TileProgress(int Done, int Total, int Zoom, int X, int Y, bool WasCopied);

public static class TDeckTileService
{
    private static readonly string _version =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    private static readonly HttpClient _http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", $"MeshhessenClient/{_version}" } }
    };

    // Folder names used on SD card (under maps/)
    public static string GetSDFolderName(MapSource source) => source switch
    {
        MapSource.OSM     => "OSM",
        MapSource.OSMTopo => "OpenTopo",
        MapSource.OSMDark => "OSMDark",
        _                 => "OSM"
    };

    // Maps/<SDFolder>/<z>/<x>/<y>.png
    public static string GetSDTilePath(string sdRoot, MapSource source, int z, int x, int y)
    {
        var folder = GetSDFolderName(source);
        return Path.Combine(sdRoot, "maps", folder, z.ToString(), x.ToString(), $"{y}.png");
    }

    // Local maptiles/<sourceFolder>/<z>/<x>/<y>.png
    private static string GetLocalTilePath(string localTileDir, MapSource source, int z, int x, int y)
    {
        var folder = TileDownloaderService.GetSourceFolderName(source);
        return Path.Combine(localTileDir, folder, z.ToString(), x.ToString(), $"{y}.png");
    }

    /// <summary>
    /// Transfers all tiles in bbox/zoom range to SD card.
    /// Runs entirely on a thread-pool thread so the UI stays responsive.
    /// For each tile: skip if on SD, copy if local, else download.
    /// </summary>
    public static Task TransferTilesAsync(
        MapSource source,
        double north, double south, double east, double west,
        int maxZoom,
        string sdRoot,
        string localTileDir,
        IProgress<TileProgress>? progress,
        CancellationToken ct)
    {
        // All file I/O (File.Exists, File.Copy, Directory.CreateDirectory) is
        // synchronous and would freeze the UI thread. Task.Run moves everything
        // to the thread-pool; the inner HttpClient await is fine there too.
        return Task.Run(async () =>
        {
            int total = TileDownloaderService.EstimateTileCount(north, south, east, west, 1, maxZoom);
            int done = 0;

            var urlTemplate = source switch
            {
                MapSource.OSMTopo => TileDownloaderService.OSMTopoTileUrl,
                MapSource.OSMDark => TileDownloaderService.OSMDarkTileUrl,
                _                 => TileDownloaderService.OSMTileUrl
            };

            for (int z = 1; z <= maxZoom; z++)
            {
                ct.ThrowIfCancellationRequested();

                var (x1, y1) = TileDownloaderService.LatLonToTile(north, west, z);
                var (x2, y2) = TileDownloaderService.LatLonToTile(south, east, z);

                int xMin = Math.Min(x1, x2), xMax = Math.Max(x1, x2);
                int yMin = Math.Min(y1, y2), yMax = Math.Max(y1, y2);

                for (int x = xMin; x <= xMax; x++)
                {
                    ct.ThrowIfCancellationRequested();

                    for (int y = yMin; y <= yMax; y++)
                    {
                        ct.ThrowIfCancellationRequested();

                        bool copied = false;
                        var sdPath = GetSDTilePath(sdRoot, source, z, x, y);

                        if (!File.Exists(sdPath))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(sdPath)!);

                            var localPath = GetLocalTilePath(localTileDir, source, z, x, y);
                            if (File.Exists(localPath))
                            {
                                File.Copy(localPath, sdPath, overwrite: true);
                                copied = true;
                            }
                            else
                            {
                                try
                                {
                                    var url = urlTemplate
                                        .Replace("{z}", z.ToString())
                                        .Replace("{x}", x.ToString())
                                        .Replace("{y}", y.ToString());

                                    var data = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                                    await File.WriteAllBytesAsync(sdPath, data, ct).ConfigureAwait(false);

                                    // Also save to local cache
                                    var localDir2 = Path.GetDirectoryName(localPath)!;
                                    Directory.CreateDirectory(localDir2);
                                    await File.WriteAllBytesAsync(localPath, data, ct).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException)
                                {
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    Logger.WriteLine($"[TDeck] Tile download failed Z{z} X{x} Y{y}: {ex.Message}");
                                }
                            }
                        }

                        done++;
                        progress?.Report(new TileProgress(done, total, z, x, y, copied));
                    }
                }
            }
        }, ct);
    }

    /// <summary>
    /// Exports all local tiles for the given source(s) to a ZIP file.
    /// Pass null for source to export all map types.
    /// </summary>
    public static Task ExportToZipAsync(
        string localTileDir,
        string outputZipPath,
        MapSource? sourceFilter,
        IProgress<(int done, int total, string status)>? progress,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var sources = sourceFilter.HasValue
                ? new[] { sourceFilter.Value }
                : new[] { MapSource.OSM, MapSource.OSMTopo, MapSource.OSMDark };

            var allFiles = new List<(string fullPath, string zipEntry)>();
            foreach (var src in sources)
            {
                ct.ThrowIfCancellationRequested();
                var folder = TileDownloaderService.GetSourceFolderName(src);
                var dir = Path.Combine(localTileDir, folder);
                if (!Directory.Exists(dir)) continue;

                foreach (var f in Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(localTileDir, f).Replace('\\', '/');
                    allFiles.Add((f, rel));
                }
            }

            if (allFiles.Count == 0) return;

            int done = 0;
            int total = allFiles.Count;

            using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);
            foreach (var (fullPath, entry) in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                zip.CreateEntryFromFile(fullPath, entry, CompressionLevel.SmallestSize);
                done++;
                progress?.Report((done, total, entry));
            }
        }, ct);
    }

    public static long EstimateSizeBytes(int tileCount, long avgTileBytes = 15_000)
        => (long)tileCount * avgTileBytes;

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F1} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1} MB";
        return $"{bytes / (double)(1L << 10):F0} KB";
    }
}
