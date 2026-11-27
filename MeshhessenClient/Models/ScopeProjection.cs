namespace MeshhessenClient.Models;

/// <summary>
/// Fixed-scale situation map ("Lagebild"): our position in the centre, a circle of <see cref="RadiusKm"/>
/// around it, no zoom. Web-Mercator world pixels are the common frame for raster tiles and node markers;
/// the tile zoom is chosen so tiles are drawn close to 1:1 (at most slightly upscaled), which gives the
/// coarse look on purpose. Pure math, unit-tested.
/// </summary>
public sealed class ScopeProjection
{
    public const double EarthCircumferenceKm = 40075.016686;
    private const double MaxLat = 85.05112878;

    public double CenterLat { get; }
    public double CenterLon { get; }
    public double RadiusKm  { get; }
    public double RadiusPx  { get; }
    public double CenterX   { get; }
    public double CenterY   { get; }
    public int    Zoom      { get; }
    /// <summary>Scale from tile pixels (world px at <see cref="Zoom"/>) to scope pixels.</summary>
    public double TileScale { get; }
    public double PxPerKm => RadiusPx / RadiusKm;

    private readonly double _worldCx, _worldCy;   // centre in world px at Zoom

    public ScopeProjection(double centerLat, double centerLon, double radiusKm, double radiusPx,
        double centerX, double centerY, int minZoom = 6, int maxZoom = 11, double maxUpscale = 1.5)
    {
        CenterLat = centerLat;
        CenterLon = centerLon;
        RadiusKm  = radiusKm;
        RadiusPx  = radiusPx;
        CenterX   = centerX;
        CenterY   = centerY;
        Zoom      = ChooseZoom(centerLat, radiusKm, radiusPx, minZoom, maxZoom, maxUpscale);
        TileScale = PxPerKm * KmPerWorldPixel(centerLat, Zoom);
        (_worldCx, _worldCy) = WorldPixel(centerLat, centerLon, Zoom);
    }

    /// <summary>Ground size of one world pixel at <paramref name="zoom"/> on the given latitude.</summary>
    public static double KmPerWorldPixel(double lat, int zoom) =>
        EarthCircumferenceKm * Math.Cos(lat * Math.PI / 180) / (256.0 * (1 << zoom));

    /// <summary>Smallest zoom whose tiles need at most <paramref name="maxUpscale"/>× enlargement
    /// to fill the scope — coarse tiles, drawn big, is the intended look.</summary>
    public static int ChooseZoom(double lat, double radiusKm, double radiusPx, int minZoom, int maxZoom, double maxUpscale)
    {
        double pxPerKm = radiusPx / radiusKm;
        for (int z = minZoom; z <= maxZoom; z++)
            if (pxPerKm * KmPerWorldPixel(lat, z) <= maxUpscale) return z;
        return maxZoom;
    }

    /// <summary>Web-Mercator world pixel of a geographic point at <paramref name="zoom"/> (256 px tiles).</summary>
    public static (double X, double Y) WorldPixel(double lat, double lon, int zoom)
    {
        double n = 256.0 * (1 << zoom);
        double x = (lon + 180.0) / 360.0 * n;
        double latRad = Math.Clamp(lat, -MaxLat, MaxLat) * Math.PI / 180;
        double y = (1 - Math.Log(Math.Tan(latRad) + 1 / Math.Cos(latRad)) / Math.PI) / 2 * n;
        return (x, y);
    }

    /// <summary>Scope pixel of a geographic point (may lie outside the circle).</summary>
    public (double X, double Y) ToScope(double lat, double lon)
    {
        var (wx, wy) = WorldPixel(lat, lon, Zoom);
        return (CenterX + (wx - _worldCx) * TileScale, CenterY + (wy - _worldCy) * TileScale);
    }

    /// <summary>Top-left scope pixel of an OSM tile (x/y at <see cref="Zoom"/>) and its drawn size.</summary>
    public (double X, double Y, double Size) TileRect(int tileX, int tileY)
    {
        double wx = tileX * 256.0, wy = tileY * 256.0;
        return (CenterX + (wx - _worldCx) * TileScale, CenterY + (wy - _worldCy) * TileScale, 256.0 * TileScale);
    }

    /// <summary>OSM tile range (inclusive) covering the scope circle at <see cref="Zoom"/>.</summary>
    public (int X0, int X1, int Y0, int Y1) TileRange()
    {
        double half = RadiusPx / TileScale;   // scope radius in world px
        int max = (1 << Zoom) - 1;
        int x0 = (int)Math.Floor((_worldCx - half) / 256.0), x1 = (int)Math.Floor((_worldCx + half) / 256.0);
        int y0 = (int)Math.Floor((_worldCy - half) / 256.0), y1 = (int)Math.Floor((_worldCy + half) / 256.0);
        return (Math.Clamp(x0, 0, max), Math.Clamp(x1, 0, max), Math.Clamp(y0, 0, max), Math.Clamp(y1, 0, max));
    }

    /// <summary>Great-circle distance (haversine).</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Keep a scope point inside the circle: points beyond the rim are moved onto it
    /// (minus <paramref name="margin"/>) so distant stations still show at their bearing.</summary>
    public (double X, double Y, bool Outside) ClampToRim(double x, double y, double margin = 0)
    {
        double dx = x - CenterX, dy = y - CenterY;
        double d = Math.Sqrt(dx * dx + dy * dy);
        double r = Math.Max(1, RadiusPx - margin);
        if (d <= r) return (x, y, false);
        return (CenterX + dx / d * r, CenterY + dy / d * r, true);
    }
}
