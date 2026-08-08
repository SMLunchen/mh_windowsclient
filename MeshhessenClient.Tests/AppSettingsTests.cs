using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_MatchExpectedValues()
    {
        var s = new AppSettings();

        Assert.False(s.DarkMode);
        Assert.Equal(string.Empty, s.StationName);
        Assert.Equal("de", s.Language);
        Assert.Equal("offline", s.MapMode);
        Assert.Equal("raster", s.MapRenderMode);
        Assert.True(s.AlertBellSound);
        Assert.True(s.EnableMessageDb);          // record default is ON
        Assert.True(s.FancyNodeListColorful);
        Assert.Equal(90, s.TelemetryRetentionDays);
        Assert.Equal(4403, s.LastTcpPort);
        Assert.Equal(4404, s.VirtualNodePort);
        Assert.Equal(PskMismatchAction.Overwrite, s.NodeKeyMismatchAction);
        Assert.Equal(string.Empty, s.MapOverlays);
        Assert.NotNull(s.NodeColors);
        Assert.Empty(s.NodeColors);
    }

    [Fact]
    public void With_OverridesOnlyNamedFields_PreservesTheRest()
    {
        var baseline = new AppSettings();

        var modified = baseline with { DarkMode = true, EnableMessageDb = false };

        Assert.True(modified.DarkMode);
        Assert.False(modified.EnableMessageDb);
        // untouched fields carry over unchanged
        Assert.Equal(baseline.Language, modified.Language);
        Assert.Equal(baseline.MapRenderMode, modified.MapRenderMode);
        Assert.Equal(baseline.VirtualNodePort, modified.VirtualNodePort);
        // original is not mutated (records are immutable via init)
        Assert.False(baseline.DarkMode);
        Assert.True(baseline.EnableMessageDb);
    }
}
