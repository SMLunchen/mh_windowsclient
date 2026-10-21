using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

// Terrain-RGB decoding and bilinear sampling of the in-memory height grid.
// The server (Martin) serves Mapbox-encoded terrain tiles built from SRTM 90 m.
public class TerrainGridTests
{
    [Fact]
    public void Mapbox_rgb_decoding_matches_the_formula()
    {
        // h = −10000 + (R·65536 + G·256 + B) · 0.1
        Assert.Equal(0, TerrainGrid.DecodePixel(1, 134, 160), 0.001);       // sea level
        Assert.Equal(871, TerrainGrid.DecodePixel(1, 168, 166), 0.001);     // Großer Feldberg
        Assert.Equal(-10000, TerrainGrid.DecodePixel(0, 0, 0), 0.001);
    }

    private const int Zoom = 10, TileSize = 256;
    private const double World = (double)TileSize * (1 << Zoom);

    private static (double Lat, double Lon) PxToLatLon(double x, double y)
    {
        double lon = x / World * 360.0 - 180.0;
        double lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / World))) * 180.0 / Math.PI;
        return (lat, lon);
    }

    private static TerrainGrid MakeGrid(float[] heights, double originX, double originY, int w, int h) =>
        new(heights, w, h, originX, originY, Zoom, TileSize);

    [Fact]
    public void Bilinear_sampling_interpolates_between_pixels()
    {
        // 2×2 grid at an arbitrary global pixel origin near 50°N 8°E
        var probe = new TerrainGrid(new float[1], 1, 1, 0, 0, Zoom, TileSize);
        var (ox, oy) = probe.ToGlobalPx(50.0, 8.0);
        double originX = Math.Floor(ox), originY = Math.Floor(oy);

        var grid = MakeGrid(new float[] { 100, 200, 300, 400 }, originX, originY, 2, 2);

        var corner = PxToLatLon(originX, originY);
        Assert.Equal(100, grid.ElevationAt(corner.Lat, corner.Lon)!.Value, 0.5);

        var mid = PxToLatLon(originX + 0.5, originY + 0.5);
        Assert.Equal(250, grid.ElevationAt(mid.Lat, mid.Lon)!.Value, 0.5);

        // Outside the grid → null
        var outside = PxToLatLon(originX + 10, originY);
        Assert.Null(grid.ElevationAt(outside.Lat, outside.Lon));
    }

    [Fact]
    public void Missing_corners_fall_back_instead_of_poisoning()
    {
        var probe = new TerrainGrid(new float[1], 1, 1, 0, 0, Zoom, TileSize);
        var (ox, oy) = probe.ToGlobalPx(50.0, 8.0);
        double originX = Math.Floor(ox), originY = Math.Floor(oy);

        // One NaN corner (coverage edge): sampling must still return a value
        var grid = MakeGrid(new float[] { float.NaN, 200, 300, 400 }, originX, originY, 2, 2);
        var mid = PxToLatLon(originX + 0.5, originY + 0.5);
        Assert.NotNull(grid.ElevationAt(mid.Lat, mid.Lon));

        // All NaN → no data
        var dead = MakeGrid(new float[] { float.NaN, float.NaN, float.NaN, float.NaN }, originX, originY, 2, 2);
        Assert.Null(dead.ElevationAt(mid.Lat, mid.Lon));
    }
}
