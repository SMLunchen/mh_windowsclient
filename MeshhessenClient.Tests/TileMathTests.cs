using System.IO;
using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

public class TileMathTests
{
    [Fact]
    public void LatLonToTile_Zoom0_AlwaysTileZeroZero()
    {
        Assert.Equal((0, 0), TileDownloaderService.LatLonToTile(52.5, 13.4, 0));
        Assert.Equal((0, 0), TileDownloaderService.LatLonToTile(-33.9, 151.2, 0));
    }

    [Fact]
    public void LatLonToTile_Zoom1_NullIsland_IsTileOneOne()
    {
        // At zoom 1 the world is a 2x2 grid; lon/lat 0,0 is the corner of all four -> (1,1)
        Assert.Equal((1, 1), TileDownloaderService.LatLonToTile(0.0, 0.0, 1));
    }

    [Fact]
    public void LatLonToTile_KnownValue_Frankfurt()
    {
        // Frankfurt ~ 50.11N, 8.68E at zoom 10 -> well-known slippy tile 536/346
        Assert.Equal((536, 346), TileDownloaderService.LatLonToTile(50.11, 8.68, 10));
    }

    [Fact]
    public void EstimateTileCount_Zoom0_IsOne()
    {
        Assert.Equal(1, TileDownloaderService.EstimateTileCount(0.1, 0.0, 0.1, 0.0, 0, 0));
    }

    [Fact]
    public void EstimateTileCount_GrowsWithZoomRange()
    {
        var small = TileDownloaderService.EstimateTileCount(51.7, 49.4, 10.3, 7.8, 1, 8);
        var large = TileDownloaderService.EstimateTileCount(51.7, 49.4, 10.3, 7.8, 1, 12);
        Assert.True(large > small);
    }

    [Theory]
    [InlineData("https://tile.openstreetmap.org/{z}/{x}/{y}.png", true)]
    [InlineData("https://a.tile.opentopomap.org/{z}/{x}/{y}.png", true)]
    [InlineData("https://tile.meshhessenclient.de/osm/{z}/{x}/{y}.png", false)]
    public void IsPublicTileServer_DetectsOsmAndOpenTopo(string url, bool expected)
    {
        Assert.Equal(expected, TileDownloaderService.IsPublicTileServer(url));
    }

    [Theory]
    [InlineData(MapSource.OSM, "osm")]
    [InlineData(MapSource.OSMTopo, "osmtopo")]
    [InlineData(MapSource.OSMDark, "osmdark")]
    public void GetSourceFolderName_MapsEnumToFolder(MapSource src, string expected)
    {
        Assert.Equal(expected, TileDownloaderService.GetSourceFolderName(src));
    }
}

public class VectorCachePathTests
{
    private const string Base = @"C:\cache\vectortiles";

    [Fact]
    public void ExtensionlessTileLeaf_GetsBinSuffix()
    {
        var uri = new Uri("https://vectortile.meshhessenclient.de/basemap/8/133/86");
        var path = VectorTileCacheService.MapUrlToCachePath(Base, uri);

        var expected = Path.Combine(Base, "vectortile.meshhessenclient.de", "basemap", "8", "133", "86.bin");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void StyleJson_KeepsItsExtension()
    {
        var uri = new Uri("https://vectortile.meshhessenclient.de/styles/osm.json");
        var path = VectorTileCacheService.MapUrlToCachePath(Base, uri);

        var expected = Path.Combine(Base, "vectortile.meshhessenclient.de", "styles", "osm.json");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void DifferentHosts_MapToSeparateFolders()
    {
        var a = VectorTileCacheService.MapUrlToCachePath(Base, new Uri("https://server-a.local/basemap/1/2/3"));
        var b = VectorTileCacheService.MapUrlToCachePath(Base, new Uri("https://server-b.local/basemap/1/2/3"));

        Assert.Contains("server-a.local", a);
        Assert.Contains("server-b.local", b);
        Assert.NotEqual(a, b);
    }
}
