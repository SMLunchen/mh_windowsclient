using MeshhessenClient.Models;
using Xunit;

namespace MeshhessenClient.Tests;

public class DashboardAlarmTests
{
    private const double NaN = double.NaN;

    [Theory]
    // High side: warn below alarm
    [InlineData(10, NaN, 25, NaN, 50, AlarmZone.Ok)]
    [InlineData(25, NaN, 25, NaN, 50, AlarmZone.WarnHigh)]   // inclusive bound
    [InlineData(49, NaN, 25, NaN, 50, AlarmZone.WarnHigh)]
    [InlineData(50, NaN, 25, NaN, 50, AlarmZone.AlarmHigh)]  // alarm wins over warn
    [InlineData(99, NaN, 25, NaN, 50, AlarmZone.AlarmHigh)]
    // Low side (e.g. battery): warn above alarm
    [InlineData(80, 30, NaN, 15, NaN, AlarmZone.Ok)]
    [InlineData(30, 30, NaN, 15, NaN, AlarmZone.WarnLow)]
    [InlineData(16, 30, NaN, 15, NaN, AlarmZone.WarnLow)]
    [InlineData(15, 30, NaN, 15, NaN, AlarmZone.AlarmLow)]
    [InlineData(2,  30, NaN, 15, NaN, AlarmZone.AlarmLow)]
    // Band config (both sides), e.g. voltage 3.2–4.3 ok
    [InlineData(3.7, 3.4, 4.3, 3.2, 4.5, AlarmZone.Ok)]
    [InlineData(3.3, 3.4, 4.3, 3.2, 4.5, AlarmZone.WarnLow)]
    [InlineData(3.1, 3.4, 4.3, 3.2, 4.5, AlarmZone.AlarmLow)]
    [InlineData(4.4, 3.4, 4.3, 3.2, 4.5, AlarmZone.WarnHigh)]
    [InlineData(4.6, 3.4, 4.3, 3.2, 4.5, AlarmZone.AlarmHigh)]
    // Nothing configured / NaN value → Ok
    [InlineData(42, NaN, NaN, NaN, NaN, AlarmZone.Ok)]
    [InlineData(NaN, 10, 20, 5, 25, AlarmZone.Ok)]
    public void Classify_assigns_expected_zone(double value, double warnLow, double warnHigh,
        double alarmLow, double alarmHigh, AlarmZone expected)
    {
        Assert.Equal(expected, DashboardAlarms.Classify(value, warnLow, warnHigh, alarmLow, alarmHigh));
    }

    [Fact]
    public void Migrate_moves_legacy_threshold_to_alarm_high()
    {
        var w = new DashboardWidget("id1", "line", "snr", new List<uint> { 1 }, 7, "t",
            Threshold: 12.5);
        var m = DashboardAlarms.Migrate(w);
        Assert.Equal(12.5, m.AlarmHigh);
        Assert.True(double.IsNaN(m.Threshold));
    }

    [Fact]
    public void Migrate_keeps_existing_zones_untouched()
    {
        var w = new DashboardWidget("id1", "line", "snr", new List<uint> { 1 }, 7, "t",
            Threshold: 12.5, AlarmHigh: 30);
        var m = DashboardAlarms.Migrate(w);
        Assert.Equal(30, m.AlarmHigh);         // explicit zone wins
        Assert.Equal(12.5, m.Threshold);       // untouched when zones already exist
    }

    [Fact]
    public void Severity_helpers_match_zones()
    {
        Assert.True(DashboardAlarms.IsAlarm(AlarmZone.AlarmLow));
        Assert.True(DashboardAlarms.IsAlarm(AlarmZone.AlarmHigh));
        Assert.True(DashboardAlarms.IsWarn(AlarmZone.WarnLow));
        Assert.True(DashboardAlarms.IsWarn(AlarmZone.WarnHigh));
        Assert.False(DashboardAlarms.IsAlarm(AlarmZone.Ok));
        Assert.False(DashboardAlarms.IsWarn(AlarmZone.Ok));
    }

    [Fact]
    public void HasAlarmLevels_detects_any_configured_bound()
    {
        var none = new DashboardWidget("a", "line", "snr", new List<uint>(), 7, "t");
        Assert.False(none.HasAlarmLevels);
        Assert.True((none with { WarnHigh  = 1 }).HasAlarmLevels);
        Assert.True((none with { AlarmLow  = 1 }).HasAlarmLevels);
    }
}
