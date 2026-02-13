using System.IO;
using System.Net.Http;

namespace MeshhessenClient.Services;

public enum MapSource
{
    OSM,          // /osm/
    OSMTopo,      // /opentopo/
    OSMDark       // /dark/
}

public static class TileDownloaderService
{
    private static readonly HttpClient _httpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "MeshhessenClient/1.5.1" } }
    };

    // Tile URL templates für jede Kartenquelle (werden von Settings geladen)
    public static string OSMTileUrl { get; set; } = "https://tile.schwarzes-seelenreich.de/osm/{z}/{x}/{y}.png";
    public static string OSMTopoTileUrl { get; set; } = "https://tile.schwarzes-seelenreich.de/opentopo/{z}/{x}/{y}.png";
    public static string OSMDarkTileUrl { get; set; } = "https://tile.schwarzes-seelenreich.de/dark/{z}/{x}/{y}.png";

    // Gibt die Tile-URL-Template für die angegebene Kartenquelle zurück
    private static string GetTileUrlTemplate(MapSource source) => source switch
    {
        MapSource.OSM => OSMTileUrl,
        MapSource.OSMTopo => OSMTopoTileUrl,
        MapSource.OSMDark => OSMDarkTileUrl,
        _ => throw new ArgumentException($"Unknown map source: {source}")
    };

    // Gibt den Ordnernamen für die lokale Tile-Speicherung zurück
    public static string GetSourceFolderName(MapSource source) => source switch
    {
        MapSource.OSM => "osm",
        MapSource.OSMTopo => "osmtopo",
        MapSource.OSMDark => "osmdark",
        _ => throw new ArgumentException($"Unknown map source: {source}")
    };


    // Berechnet Tile-Koordinaten aus Lat/Lon nach Slippy-Map-Schema
    public static (int x, int y) LatLonToTile(double lat, double lon, int zoom)
    {
        var n = Math.Pow(2, zoom);
        var x = (int)((lon + 180.0) / 360.0 * n);
        var latRad = lat * Math.PI / 180.0;
        var y = (int)((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        return (x, y);
    }

    // Schätzt Anzahl der Tiles für ein Bounding-Box + Zoom-Bereich
    public static int EstimateTileCount(double north, double south, double east, double west, int minZoom, int maxZoom)
    {
        int total = 0;
        for (int z = minZoom; z <= maxZoom; z++)
        {
            var (x1, y1) = LatLonToTile(north, west, z);
            var (x2, y2) = LatLonToTile(south, east, z);
            total += (Math.Abs(x2 - x1) + 1) * (Math.Abs(y2 - y1) + 1);
        }
        return total;
    }

    public static async Task DownloadTilesAsync(
        MapSource source,
        double north, double south, double east, double west,
        int minZoom, int maxZoom,
        string tileDir,
        IProgress<(int done, int total, string status)> progress,
        CancellationToken ct)
    {
        int total = EstimateTileCount(north, south, east, west, minZoom, maxZoom);
        int done = 0;

        for (int z = minZoom; z <= maxZoom && !ct.IsCancellationRequested; z++)
        {
            var (x1, y1) = LatLonToTile(north, west, z);
            var (x2, y2) = LatLonToTile(south, east, z);

            int xMin = Math.Min(x1, x2), xMax = Math.Max(x1, x2);
            int yMin = Math.Min(y1, y2), yMax = Math.Max(y1, y2);

            for (int x = xMin; x <= xMax && !ct.IsCancellationRequested; x++)
            {
                for (int y = yMin; y <= yMax && !ct.IsCancellationRequested; y++)
                {
                    // Neuer Pfad: maptiles/{source}/{z}/{x}/{y}.png
                    var sourceFolderName = GetSourceFolderName(source);
                    var filePath = Path.Combine(tileDir, sourceFolderName, z.ToString(), x.ToString(), $"{y}.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                    if (!File.Exists(filePath))
                    {
                        try
                        {
                            // Get URL template and replace placeholders
                            var urlTemplate = GetTileUrlTemplate(source);
                            var url = urlTemplate
                                .Replace("{z}", z.ToString())
                                .Replace("{x}", x.ToString())
                                .Replace("{y}", y.ToString());

                            var data = await _httpClient.GetByteArrayAsync(url, ct);
                            await File.WriteAllBytesAsync(filePath, data, ct);

                            // Rate-Limiting nur für externe Server (nicht für eigene Server)
                            if (!url.Contains("tile.schwarzes-seelenreich.de", StringComparison.OrdinalIgnoreCase) &&
                                !url.Contains("tile.meshhessen.de", StringComparison.OrdinalIgnoreCase))
                            {
                                await Task.Delay(500, ct);
                            }
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            Logger.WriteLine($"Tile download failed [{sourceFolderName}] Z{z} X:{x} Y:{y}: {ex.Message}");
                        }
                    }

                    done++;
                    progress.Report((done, total, $"Z{z} X:{x} Y:{y}"));
                }
            }
        }
    }
}
