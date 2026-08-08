using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// Round-trip tests for the INI persistence. Save/Load use the process base
/// directory, so these run sequentially within one class (xUnit default) and
/// no other test class touches the file.
/// </summary>
public class SettingsServiceTests
{
    [Fact]
    public void RoundTrip_ScalarsSurviveSaveAndLoad()
    {
        var original = new AppSettings
        {
            DarkMode = true,
            StationName = "TestStation",
            ShowEncryptedMessages = true,
            MyLatitude = 51.1234567,
            MyLongitude = 8.7654321,
            LastTcpHost = "10.0.0.5",
            LastTcpPort = 5555,
            MapSource = "osmtopo",
            Language = "en",
            TelemetryRetentionDays = 365,
            NodeKeyMismatchAction = PskMismatchAction.Ask,
            SignalWeatherWindowHours = 12,
            PositionHistoryHours = 48,
            MapMode = "online-own",
            EnableMessageDb = false,                 // guards the "save during load" regression
            MapRenderMode = "vector",
            MapOverlays = "emergency",
            VirtualNodeEnabled = true,
            VirtualNodePort = 4405,
        };

        SettingsService.Save(original);
        var loaded = SettingsService.Load();

        Assert.Equal(original.DarkMode, loaded.DarkMode);
        Assert.Equal(original.StationName, loaded.StationName);
        Assert.Equal(original.ShowEncryptedMessages, loaded.ShowEncryptedMessages);
        Assert.Equal(original.MyLatitude, loaded.MyLatitude, 6);
        Assert.Equal(original.MyLongitude, loaded.MyLongitude, 6);
        Assert.Equal(original.LastTcpHost, loaded.LastTcpHost);
        Assert.Equal(original.LastTcpPort, loaded.LastTcpPort);
        Assert.Equal(original.MapSource, loaded.MapSource);
        Assert.Equal(original.Language, loaded.Language);
        Assert.Equal(original.TelemetryRetentionDays, loaded.TelemetryRetentionDays);
        Assert.Equal(original.NodeKeyMismatchAction, loaded.NodeKeyMismatchAction);
        Assert.Equal(original.SignalWeatherWindowHours, loaded.SignalWeatherWindowHours);
        Assert.Equal(original.PositionHistoryHours, loaded.PositionHistoryHours);
        Assert.Equal(original.MapMode, loaded.MapMode);
        Assert.Equal(original.EnableMessageDb, loaded.EnableMessageDb);
        Assert.Equal(original.MapRenderMode, loaded.MapRenderMode);
        Assert.Equal(original.MapOverlays, loaded.MapOverlays);
        Assert.Equal(original.VirtualNodeEnabled, loaded.VirtualNodeEnabled);
        Assert.Equal(original.VirtualNodePort, loaded.VirtualNodePort);
    }

    [Fact]
    public void RoundTrip_EnableMessageDbFalse_StaysFalse()
    {
        // Explicit regression guard: a stored false must not flip to the record default (true).
        SettingsService.Save(new AppSettings { EnableMessageDb = false });
        Assert.False(SettingsService.Load().EnableMessageDb);

        SettingsService.Save(new AppSettings { EnableMessageDb = true });
        Assert.True(SettingsService.Load().EnableMessageDb);
    }

    [Fact]
    public void RoundTrip_DictionariesSurvive()
    {
        var original = new AppSettings
        {
            NodeColors = new() { [0xDEADBEEF] = "#FF8800" },
            NodeNotes = new() { [0x12345678] = "hello world" },
            PinnedNodes = new() { [0xAABBCCDD] = true },
            FavoriteNodes = new() { [0x11223344] = true },
            NodeStationNames = new() { [0x55667788] = "Node A" },
        };

        SettingsService.Save(original);
        var loaded = SettingsService.Load();

        Assert.Equal("#FF8800", loaded.NodeColors[0xDEADBEEF]);
        Assert.Equal("hello world", loaded.NodeNotes[0x12345678]);
        Assert.True(loaded.PinnedNodes[0xAABBCCDD]);
        Assert.True(loaded.FavoriteNodes[0x11223344]);
        Assert.Equal("Node A", loaded.NodeStationNames[0x55667788]);
    }
}
