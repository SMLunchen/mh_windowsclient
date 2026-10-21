using MeshhessenClient.Models;
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
    public void Newest_source_wins_the_snr_snapshot()
    {
        // Two evidence sources for the same direction: the value of the NEWEST row must
        // win deterministically (used to depend on arbitrary table order).
        _db.UpsertRfLink(1, 2, 9.0f, "neighborinfo", DateTime.UtcNow.AddHours(-2));
        _db.UpsertRfLink(1, 2, 3.0f, "traceroute", DateTime.UtcNow);

        var partner = Assert.Single(_db.GetRfNeighbors(1, 0, Names));
        Assert.Equal(3.0f, partner.SnrIn);
    }

    [Fact]
    public void Snr_history_accumulates_per_observation()
    {
        // traceroute/neighborinfo upserts overwrite the snapshot but append to history
        _db.UpsertRfLink(1, 2, 4.0f, "traceroute", DateTime.UtcNow.AddHours(-3));
        _db.UpsertRfLink(1, 2, 6.0f, "traceroute", DateTime.UtcNow.AddHours(-2));
        _db.UpsertRfLink(1, 2, 8.0f, "neighborinfo", DateTime.UtcNow);
        _db.UpsertRfLink(2, 1, -1.0f, "traceroute", DateTime.UtcNow); // other direction
        _db.UpsertRfLink(1, 2, null, "traceroute", DateTime.UtcNow);  // no SNR → no history row

        var samples = _db.GetRfLinkSnrSamples(1, 2, myNodeId: 99, days: 0);
        Assert.Equal(3, samples.Count);
        Assert.Equal(6.0, TriangulationService.TrimmedMedian(samples)!.Value, precision: 6);
        Assert.Single(_db.GetRfLinkSnrSamples(2, 1, myNodeId: 99, days: 0));
    }

    [Fact]
    public void Own_receptions_feed_the_sample_series_only_direct_ones()
    {
        const uint me = 42, remote = 7;
        _db.InsertPacketRx(remote, 100, DateTime.UtcNow.AddMinutes(-30), 5.0f, -90, 0, false);
        _db.InsertPacketRx(remote, 101, DateTime.UtcNow.AddMinutes(-20), 7.0f, -80, null, false); // old firmware = direct
        _db.InsertPacketRx(remote, 102, DateTime.UtcNow.AddMinutes(-10), 1.0f, -120, 2, false);   // relayed → excluded

        // "me heard remote": packet_rx counts only when the observer IS our node
        Assert.Equal(2, _db.GetRfLinkSnrSamples(me, remote, myNodeId: me, days: 0).Count);
        Assert.Empty(_db.GetRfLinkSnrSamples(remote, me, myNodeId: me, days: 0));

        var rssi = _db.GetDirectRssiSamples(remote, 0);
        Assert.Equal(2, rssi.Count);
        Assert.DoesNotContain(-120.0, rssi);
    }

    [Fact]
    public void Snr_zero_means_no_measurement()
    {
        // Raw 0 in traceroute/NeighborInfo evidence = rx_snr of a non-RF reception
        // (MQTT downlink / API): the link row is kept, the bogus value is not.
        _db.UpsertRfLink(1, 2, 0f, "neighborinfo", DateTime.UtcNow);
        var partner = Assert.Single(_db.GetRfNeighbors(1, 0, Names));
        Assert.Null(partner.SnrIn);
        Assert.Empty(_db.GetRfLinkSnrSamples(1, 2, myNodeId: 99, days: 0));

        // A later real measurement fills the value; a 0 afterwards must not wipe it
        _db.UpsertRfLink(1, 2, 4.5f, "neighborinfo", DateTime.UtcNow);
        _db.UpsertRfLink(1, 2, 0f, "neighborinfo", DateTime.UtcNow);
        Assert.Equal(4.5f, Assert.Single(_db.GetRfNeighbors(1, 0, Names)).SnrIn);
    }

    [Fact]
    public void Non_rf_snr_detection_covers_sentinel_and_zero()
    {
        Assert.True(TracerouteResult.IsNonRfSnr(-128)); // firmware unknown-sentinel
        Assert.True(TracerouteResult.IsNonRfSnr(0));    // rx_snr of a non-RF reception
        Assert.False(TracerouteResult.IsNonRfSnr(1));   // 0.25 dB — smallest real reading
        Assert.False(TracerouteResult.IsNonRfSnr(-127)); // clamped real reading
        Assert.False(TracerouteResult.IsNonRfSnr(-26)); // −6.5 dB
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
