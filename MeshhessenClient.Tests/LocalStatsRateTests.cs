using MeshhessenClient.Services;
using Meshtastic.Protobufs;

namespace MeshhessenClient.Tests;

// Delta-rate derivation from cumulative LocalStats counters (packets/h between polls).
public class LocalStatsRateTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lsrate_test_{Guid.NewGuid():N}.db");
    private readonly TelemetryDatabaseService _db;

    public LocalStatsRateTests() => _db = new TelemetryDatabaseService(_dbPath);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private void Insert(DateTime ts, uint rx) =>
        _db.InsertLocalStats(1, ts, new LocalStats { NumPacketsRx = rx });

    [Fact]
    public void Rate_is_delta_per_hour_between_consecutive_polls()
    {
        var t0 = DateTime.UtcNow.AddHours(-3);
        Insert(t0,              1000);
        Insert(t0.AddHours(1),  1060);   // +60 in 1 h → 60/h
        Insert(t0.AddHours(3),  1100);   // +40 in 2 h → 20/h

        var pts = _db.GetTimeSeries(new[] { 1u }, "rx_pph", 0);
        Assert.Equal(2, pts.Count);
        Assert.Equal(60, pts[0].Value, precision: 3);
        Assert.Equal(20, pts[1].Value, precision: 3);
    }

    [Fact]
    public void Counter_reset_after_reboot_is_skipped()
    {
        var t0 = DateTime.UtcNow.AddHours(-2);
        Insert(t0,              5000);
        Insert(t0.AddHours(1),  12);     // reboot: counter restarted → no negative rate
        Insert(t0.AddHours(2),  72);     // +60 in 1 h → 60/h

        var pts = _db.GetTimeSeries(new[] { 1u }, "rx_pph", 0);
        var p = Assert.Single(pts);
        Assert.Equal(60, p.Value, precision: 3);
    }

    [Fact]
    public void Degenerate_intervals_are_skipped()
    {
        var t0 = DateTime.UtcNow.AddHours(-30);
        Insert(t0,                  100);
        Insert(t0.AddSeconds(20),   101);  // < 1 min apart → skipped
        Insert(t0.AddHours(28),     200);  // > 24 h gap → skipped

        Assert.Empty(_db.GetTimeSeries(new[] { 1u }, "rx_pph", 0));
    }
}
