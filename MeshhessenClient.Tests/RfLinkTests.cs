using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

// Direct-RF link evidence store (rf_links): upsert semantics, direction mapping and
// the day window used by the telemetry window's neighbor table.
public class RfLinkTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rflink_test_{Guid.NewGuid():N}.db");
    private readonly TelemetryDatabaseService _db;
    private static readonly Dictionary<uint, string> Names = new() { [1] = "Alpha", [2] = "Bravo" };

    public RfLinkTests() => _db = new TelemetryDatabaseService(_dbPath);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void Upsert_updates_existing_row_instead_of_duplicating()
    {
        _db.UpsertRfLink(1, 2, 5.0f, "rx", DateTime.UtcNow.AddMinutes(-10));
        _db.UpsertRfLink(1, 2, 7.5f, "rx", DateTime.UtcNow);

        var rows = _db.GetRfNeighbors(1, 0, Names);
        var partner = Assert.Single(rows);
        Assert.Equal(2u, partner.NeighborId);
        Assert.Equal(7.5f, partner.SnrIn);
        Assert.Single(partner.Sources);
    }

    [Fact]
    public void Upsert_null_snr_keeps_previous_value()
    {
        _db.UpsertRfLink(1, 2, 5.0f, "rx", DateTime.UtcNow.AddMinutes(-10));
        _db.UpsertRfLink(1, 2, null, "rx", DateTime.UtcNow);

        var partner = Assert.Single(_db.GetRfNeighbors(1, 0, Names));
        Assert.Equal(5.0f, partner.SnrIn);
    }

    [Fact]
    public void Direction_maps_to_SnrIn_and_SnrOut()
    {
        _db.UpsertRfLink(1, 2, 3.0f, "rx", DateTime.UtcNow);           // 1 hears 2
        _db.UpsertRfLink(2, 1, -6.5f, "neighborinfo", DateTime.UtcNow); // 2 hears 1

        var fromOnes = Assert.Single(_db.GetRfNeighbors(1, 0, Names));
        Assert.Equal(3.0f, fromOnes.SnrIn);   // selected node (1) hears partner
        Assert.Equal(-6.5f, fromOnes.SnrOut); // partner hears selected node
        Assert.Equal(2, fromOnes.Sources.Count);

        // Same link viewed from node 2: directions swap
        var fromTwos = Assert.Single(_db.GetRfNeighbors(2, 0, Names));
        Assert.Equal(-6.5f, fromTwos.SnrIn);
        Assert.Equal(3.0f, fromTwos.SnrOut);
    }

    [Fact]
    public void Day_window_filters_stale_links()
    {
        _db.UpsertRfLink(1, 2, 5.0f, "rx", DateTime.UtcNow.AddDays(-10));
        _db.UpsertRfLink(1, 3, 2.0f, "traceroute", DateTime.UtcNow);

        var recent = _db.GetRfNeighbors(1, 7, Names);
        Assert.Single(recent);
        Assert.Equal(3u, recent[0].NeighborId);

        Assert.Equal(2, _db.GetRfNeighbors(1, 0, Names).Count); // 0 = all
    }

    [Fact]
    public void Collection_start_marker_is_set_once_and_kept()
    {
        Assert.Null(_db.GetRfCollectionStart());
        var first = DateTime.UtcNow.AddDays(-9);
        _db.UpsertRfLink(1, 2, 5.0f, "rx", first);
        _db.UpsertRfLink(1, 3, 2.0f, "rx", DateTime.UtcNow); // must NOT move the marker

        var start = _db.GetRfCollectionStart();
        Assert.NotNull(start);
        // FromUnix returns local time — compare in UTC
        Assert.True(Math.Abs((start!.Value.ToUniversalTime() - first).TotalSeconds) < 2);
    }

    [Fact]
    public void Self_links_and_zero_ids_are_ignored()
    {
        _db.UpsertRfLink(1, 1, 5.0f, "rx", DateTime.UtcNow);
        _db.UpsertRfLink(0, 2, 5.0f, "rx", DateTime.UtcNow);
        _db.UpsertRfLink(1, 0, 5.0f, "rx", DateTime.UtcNow);

        Assert.Empty(_db.GetRfNeighbors(1, 0, Names));
    }
}
