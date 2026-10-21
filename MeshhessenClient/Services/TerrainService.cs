using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeshhessenClient.Services;

/// <summary>
/// Decoded terrain-RGB height grid in Web-Mercator pixel space at one zoom level
/// (a contiguous tile block). Pure data + math — fetching lives in TerrainService.
/// NaN = no data (outside DEM coverage; the server answers 204 there).
/// </summary>
public sealed class TerrainGrid
{
    private readonly float[] _heights;   // row-major
    private readonly int _w, _h;         // pixels
    private readonly double _originX, _originY; // global mercator px of grid (0,0)
    private readonly double _worldPx;    // 2^zoom * tileSize

    public TerrainGrid(float[] heights, int widthPx, int heightPx,
        double originPxX, double originPxY, int zoom, int tileSize)
    {
        _heights = heights; _w = widthPx; _h = heightPx;
        _originX = originPxX; _originY = originPxY;
        _worldPx = (double)tileSize * (1 << zoom);
    }

    /// <summary>Mapbox terrain-RGB: h = −10000 + (R·65536 + G·256 + B) · 0.1 m.</summary>
    public static float DecodePixel(byte r, byte g, byte b) =>
        (float)(-10000.0 + (r * 65536 + g * 256 + b) * 0.1);

    /// <summary>Global Web-Mercator pixel coordinates of a lat/lon at this grid's zoom.</summary>
    public (double X, double Y) ToGlobalPx(double lat, double lon)
    {
        lat = Math.Clamp(lat, -85.05112878, 85.05112878);
        double x = (lon + 180.0) / 360.0 * _worldPx;
        double sinLat = Math.Sin(lat * Math.PI / 180.0);
        double y = (0.5 - Math.Log((1 + sinLat) / (1 - sinLat)) / (4 * Math.PI)) * _worldPx;
        return (x, y);
    }

    /// <summary>Bilinear elevation (m a.s.l.), null outside the grid or where the DEM
    /// has no data. NaN-aware: a NaN corner falls back to the nearest valid corner.</summary>
    public double? ElevationAt(double lat, double lon)
    {
        var (gx, gy) = ToGlobalPx(lat, lon);
        double fx = gx - _originX, fy = gy - _originY;
        if (fx < 0 || fy < 0 || fx > _w - 1 || fy > _h - 1) return null;

        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, _w - 1), y1 = Math.Min(y0 + 1, _h - 1);
        double tx = fx - x0, ty = fy - y0;

        float h00 = _heights[y0 * _w + x0], h10 = _heights[y0 * _w + x1];
        float h01 = _heights[y1 * _w + x0], h11 = _heights[y1 * _w + x1];

        if (float.IsNaN(h00) && float.IsNaN(h10) && float.IsNaN(h01) && float.IsNaN(h11))
            return null;
        // Replace missing corners by the nearest valid one (coverage edge)
        float any = !float.IsNaN(h00) ? h00 : !float.IsNaN(h10) ? h10 : !float.IsNaN(h01) ? h01 : h11;
        if (float.IsNaN(h00)) h00 = any;
        if (float.IsNaN(h10)) h10 = any;
        if (float.IsNaN(h01)) h01 = any;
        if (float.IsNaN(h11)) h11 = any;

        double top = h00 + (h10 - h00) * tx;
        double bot = h01 + (h11 - h01) * tx;
        return top + (bot - top) * ty;
    }
}

/// <summary>
/// Elevation model from the Meshhessen terrain-RGB tiles (Mapbox encoding, z5–10,
/// SRTM 90 m, served by Martin at /terrain/{z}/{x}/{y}). Loads all tiles covering a
/// bounding box once, decodes them into one contiguous in-memory grid and answers
/// point queries bilinearly — a triangulation grid scan does >10⁶ lookups, so
/// per-lookup tile decoding is not an option. Tiles go through the regular
/// VectorTileCacheService (disk cache, 204 missing markers, offline mode, UA/ACL),
/// so terrain works offline for any area already fetched once.
/// </summary>
public class TerrainService
{
    public static TerrainService Instance { get; } = new();

    public const int MaxZoom = 10;       // source-sharp: SRTM 90 m ≈ z10 pixel size
    public const int MinZoom = 5;
    private const int MaxTiles = 80;     // ~20 MB decoded; larger bboxes drop zoom

    private readonly VectorTileCacheService _cache = new(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vectortiles"));

    /// <summary>Mirrors the app's offline map mode: no network fetches when true.</summary>
    public bool OfflineMode
    {
        get => _cache.OfflineMode;
        set => _cache.OfflineMode = value;
    }

    /// <summary>Loads the height grid covering the bbox (padded slightly). Returns null
    /// when nothing is available (offline without cache, or fully outside coverage).</summary>
    public async Task<TerrainGrid?> LoadGridAsync(double minLat, double minLon, double maxLat, double maxLon)
    {
        // Pad a little so profile sampling near the edge stays inside the grid
        double padLat = Math.Max((maxLat - minLat) * 0.05, 0.01);
        double padLon = Math.Max((maxLon - minLon) * 0.05, 0.01);
        minLat -= padLat; maxLat += padLat; minLon -= padLon; maxLon += padLon;

        int zoom = MaxZoom;
        int tileSize = 256; // actual size read from the first decoded tile
        (int X0, int X1, int Y0, int Y1) range = default;
        for (; zoom >= MinZoom; zoom--)
        {
            range = TileRange(minLat, minLon, maxLat, maxLon, zoom);
            long count = (long)(range.X1 - range.X0 + 1) * (range.Y1 - range.Y0 + 1);
            if (count <= MaxTiles) break;
            if (zoom == MinZoom) return null; // absurd bbox — give up instead of OOM
        }

        int tilesX = range.X1 - range.X0 + 1, tilesY = range.Y1 - range.Y0 + 1;
        float[]? heights = null;
        int w = 0, h = 0;
        int loaded = 0;

        for (int ty = range.Y0; ty <= range.Y1; ty++)
        for (int tx = range.X0; tx <= range.X1; tx++)
        {
            var resp = await _cache.GetResponseAsync(
                $"https://{VectorTileCacheService.DefaultVectorHost}/terrain/{zoom}/{tx}/{ty}");
            if (resp.StatusCode != 200 || resp.Body == null) continue; // 204 = outside coverage → stays NaN

            try
            {
                var decoder = new PngBitmapDecoder(new MemoryStream(resp.Body),
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                if (heights == null)
                {
                    tileSize = frame.PixelWidth;
                    w = tilesX * tileSize; h = tilesY * tileSize;
                    heights = new float[(long)w * h <= int.MaxValue ? w * h : 0];
                    if (heights.Length == 0) return null;
                    Array.Fill(heights, float.NaN);
                }
                if (frame.PixelWidth != tileSize || frame.PixelHeight != tileSize) continue;

                var conv = new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
                var px = new byte[tileSize * tileSize * 3];
                conv.CopyPixels(px, tileSize * 3, 0);

                int ox = (tx - range.X0) * tileSize, oy = (ty - range.Y0) * tileSize;
                for (int row = 0; row < tileSize; row++)
                {
                    int dst = (oy + row) * w + ox;
                    int src = row * tileSize * 3;
                    for (int col = 0; col < tileSize; col++, src += 3)
                        heights[dst + col] = TerrainGrid.DecodePixel(px[src + 2], px[src + 1], px[src]);
                }
                loaded++;
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"[Terrain] tile {zoom}/{tx}/{ty} decode failed: {ex.Message}");
            }
        }

        if (heights == null || loaded == 0)
        {
            Logger.WriteLine($"[Terrain] no terrain data for bbox ({minLat:F2},{minLon:F2})–({maxLat:F2},{maxLon:F2}) z{zoom}");
            return null;
        }

        Logger.WriteLine($"[Terrain] grid z{zoom}: {tilesX}×{tilesY} tiles ({loaded} with data), {w}×{h} px");
        return new TerrainGrid(heights, w, h,
            (double)range.X0 * tileSize, (double)range.Y0 * tileSize, zoom, tileSize);
    }

    private static (int X0, int X1, int Y0, int Y1) TileRange(
        double minLat, double minLon, double maxLat, double maxLon, int zoom)
    {
        int n = 1 << zoom;
        (int X, int Y) Tile(double lat, double lon)
        {
            lat = Math.Clamp(lat, -85.05112878, 85.05112878);
            int x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
            double sinLat = Math.Sin(lat * Math.PI / 180.0);
            int y = (int)Math.Floor((0.5 - Math.Log((1 + sinLat) / (1 - sinLat)) / (4 * Math.PI)) * n);
            return (Math.Clamp(x, 0, n - 1), Math.Clamp(y, 0, n - 1));
        }
        var a = Tile(maxLat, minLon); // top-left
        var b = Tile(minLat, maxLon); // bottom-right
        return (a.X, b.X, a.Y, b.Y);
    }
}
