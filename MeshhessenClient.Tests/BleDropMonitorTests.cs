using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// Bug report 2026-11-14/15: a ThinkNode M6 (nRF52840, firmware 2.7.26) lost its Bluetooth link 140 times in
/// one evening — a fixed attic node on a weak radio path (the drops came ~2 s after the last data, mostly mid
/// node-list download). The client now recognises the storm and names the usual causes once; for nRF52 on
/// firmware before 2.8.0 it adds the "serial USB client switches BLE off" case (firmware PR #10349).
/// </summary>
public class BleDropMonitorTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 16, 30, 0, DateTimeKind.Utc);
    private static DateTime Min(double m) => T0.AddMinutes(m);

    [Fact]
    public void Three_drops_within_the_window_report_exactly_once()
    {
        var m = new BleDropMonitor();
        Assert.False(m.RecordDrop(Min(0)));
        Assert.False(m.RecordDrop(Min(3)));
        Assert.True(m.RecordDrop(Min(6)));          // third drop inside 10 min → hint
        Assert.Equal(3, m.DropsInWindow(Min(6)));
        Assert.False(m.RecordDrop(Min(7)));         // storm goes on, but the user was told already
        Assert.False(m.RecordDrop(Min(60)));
    }

    [Fact]
    public void Drops_outside_the_window_do_not_count()
    {
        var m = new BleDropMonitor();
        Assert.False(m.RecordDrop(Min(0)));
        Assert.False(m.RecordDrop(Min(11)));        // first one has expired
        Assert.Equal(1, m.DropsInWindow(Min(11)));
        Assert.False(m.RecordDrop(Min(22)));        // a drop every 11 min is not a storm
    }

    [Fact]
    public void Reset_forgets_history_and_rearms_the_hint()
    {
        var m = new BleDropMonitor();
        for (int i = 0; i < 3; i++) m.RecordDrop(Min(i));
        m.Reset();
        Assert.Equal(0, m.DropsInWindow(Min(3)));
        Assert.False(m.RecordDrop(Min(4)));
        Assert.False(m.RecordDrop(Min(5)));
        Assert.True(m.RecordDrop(Min(6)));          // reported again after a manual reconnect
    }

    [Theory]
    [InlineData("ThinknodeM6", "2.7.26.54e0d8d", true)]     // the reporter's device, as the client prints it
    [InlineData("THINKNODE_M6", "2.7.26.54e0d8d", true)]
    [InlineData("RAK4631", "2.6.11.60ec05e", true)]
    [InlineData("ThinknodeM6", "2.8.0.47db0e3", false)]     // fixed by PR #10349
    [InlineData("RAK4631", "2.8.1.abcdef0", false)]
    [InlineData("HELTEC_V3", "2.7.26.54e0d8d", false)]      // ESP32 keeps BLE off in serial state by design
    [InlineData("RAK4631", "", false)]                      // version unknown → generic hint
    [InlineData("", "2.7.26.54e0d8d", false)]               // hardware unknown → generic hint
    public void Serial_kills_BLE_only_on_nrf52_before_2_8(string hw, string fw, bool expected) =>
        Assert.Equal(expected, BleDropMonitor.IsSerialKillsBleFirmware(hw, fw));
}
