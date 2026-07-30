using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

public class MapOverlayRegistryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseActive_EmptyOrNull_ReturnsEmpty(string? csv)
    {
        Assert.Empty(MapOverlayRegistry.ParseActive(csv));
    }

    [Fact]
    public void ParseActive_TrimsAndSplits()
    {
        var set = MapOverlayRegistry.ParseActive("emergency, thw ,hospitals");
        Assert.Equal(3, set.Count);
        Assert.Contains("emergency", set);
        Assert.Contains("thw", set);
        Assert.Contains("hospitals", set);
    }

    [Fact]
    public void ParseActive_IsCaseInsensitive()
    {
        var set = MapOverlayRegistry.ParseActive("emergency");
        Assert.Contains("EMERGENCY", set);   // HashSet uses OrdinalIgnoreCase
    }

    [Fact]
    public void Registry_ContainsEmergencyOverlayWithExpectedShape()
    {
        var em = Assert.Single(MapOverlayRegistry.All, o => o.Key == "emergency");
        Assert.Equal("em-", em.LayerPrefix);
        Assert.Equal("emergency", em.TileSource);
        Assert.Equal(13, em.MinZoom);
        Assert.Equal(17, em.MaxZoom);
    }
}
