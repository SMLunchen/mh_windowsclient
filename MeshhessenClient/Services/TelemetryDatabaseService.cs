using System.IO;
using Microsoft.Data.Sqlite;
using MeshhessenClient.Models;

namespace MeshhessenClient.Services;

/// <summary>
/// SQLite-based telemetry storage. One DB file (telemetry.db) next to the EXE.
/// Thread-safe: all writes are serialized via _lock, reads use pooled connections.
/// </summary>
public class TelemetryDatabaseService : IDisposable
{
    private readonly string _connectionString;
    private readonly object _lock = new();
    private bool _disposed;

    // Position used for day/night classification (updated from MainWindow)
    public double Latitude  { get; set; } = 50.9;
    public double Longitude { get; set; } = 9.5;

    public TelemetryDatabaseService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};";
        InitSchema();
    }

    // ── Schema ────────────────────────────────────────────────────────────────

    private void InitSchema()
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;

CREATE TABLE IF NOT EXISTS packet_rx (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    node_id      INTEGER NOT NULL,
    packet_id    INTEGER NOT NULL DEFAULT 0,
    timestamp    INTEGER NOT NULL,
    rx_snr       REAL,
    rx_rssi      INTEGER,
    hop_count    INTEGER,
    want_ack     INTEGER DEFAULT 0,
    ack_received INTEGER DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_pkt_node_time ON packet_rx(node_id, timestamp);
CREATE INDEX IF NOT EXISTS idx_pkt_id ON packet_rx(packet_id);

CREATE TABLE IF NOT EXISTS device_telemetry (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    node_id             INTEGER NOT NULL,
    timestamp           INTEGER NOT NULL,
    battery_percent     REAL,
    voltage             REAL,
    channel_utilization REAL,
    air_util_tx         REAL,
    uptime_seconds      INTEGER
);
CREATE INDEX IF NOT EXISTS idx_devtel_node_time ON device_telemetry(node_id, timestamp);

CREATE TABLE IF NOT EXISTS environment_telemetry (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    node_id             INTEGER NOT NULL,
    timestamp           INTEGER NOT NULL,
    temperature         REAL,
    relative_humidity   REAL,
    barometric_pressure REAL,
    iaq                 INTEGER
);
CREATE INDEX IF NOT EXISTS idx_envtel_node_time ON environment_telemetry(node_id, timestamp);

CREATE TABLE IF NOT EXISTS traceroute_hops (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id      INTEGER NOT NULL,
    source_node_id  INTEGER NOT NULL,
    dest_node_id    INTEGER NOT NULL,
    timestamp       INTEGER NOT NULL,
    hop_index       INTEGER,
    node_id         INTEGER,
    snr_towards     REAL,
    snr_back        REAL
);
CREATE INDEX IF NOT EXISTS idx_trhop_source ON traceroute_hops(source_node_id, timestamp);
CREATE INDEX IF NOT EXISTS idx_trhop_dest   ON traceroute_hops(dest_node_id, timestamp);

CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT
);

CREATE TABLE IF NOT EXISTS local_stats (
    id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    node_id               INTEGER NOT NULL,
    timestamp             INTEGER NOT NULL,
    noise_floor           INTEGER,
    num_packets_tx        INTEGER,
    num_packets_rx        INTEGER,
    num_packets_rx_bad    INTEGER,
    num_rx_dupe           INTEGER,
    num_tx_relay          INTEGER,
    num_tx_relay_canceled INTEGER,
    num_tx_dropped        INTEGER,
    num_online_nodes      INTEGER,
    num_total_nodes       INTEGER
);
CREATE INDEX IF NOT EXISTS idx_localstats_node_time ON local_stats(node_id, timestamp);

CREATE TABLE IF NOT EXISTS rf_links (
    observer  INTEGER NOT NULL,   -- node that HEARD the transmission
    neighbor  INTEGER NOT NULL,   -- node that was heard (direct RF, never MQTT)
    snr       REAL,               -- SNR in dB as measured by observer, NULL = unknown
    source    TEXT    NOT NULL,   -- 'rx' (0-hop reception) | 'traceroute' | 'neighborinfo'
    last_seen INTEGER NOT NULL,
    PRIMARY KEY (observer, neighbor, source)
);
CREATE INDEX IF NOT EXISTS idx_rflinks_neighbor ON rf_links(neighbor);

CREATE TABLE IF NOT EXISTS node_positions (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    node_id      INTEGER NOT NULL,
    latitude     REAL    NOT NULL,
    longitude    REAL    NOT NULL,
    altitude     REAL,
    ground_speed REAL,
    ground_track REAL,
    timestamp    INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_nodepos_node_ts ON node_positions(node_id, timestamp DESC);

CREATE TABLE IF NOT EXISTS waypoints (
    id           INTEGER PRIMARY KEY,   -- waypoint ID from Meshtastic proto
    name         TEXT    NOT NULL,
    description  TEXT,
    latitude     REAL    NOT NULL,
    longitude    REAL    NOT NULL,
    expire       INTEGER,               -- Unix timestamp, NULL = never
    locked_to    INTEGER,               -- Node ID that owns it, NULL = anyone
    icon         INTEGER,               -- Unicode codepoint, 0 = default
    from_node    INTEGER,               -- Sender node ID
    received_at  INTEGER NOT NULL       -- local Unix timestamp when received
);
";
        cmd.ExecuteNonQuery();

        // Extend environment_telemetry with the full sensor set (migration for existing DBs).
        EnsureEnvironmentColumns(con);
    }

    /// <summary>Adds any environment metric column from <see cref="EnvironmentMetricInfo"/>
    /// that an older DB doesn't have yet. SQLite ADD COLUMN is cheap and non-destructive;
    /// column names come from the fixed registry (never user input).</summary>
    private void EnsureEnvironmentColumns(SqliteConnection con)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragma = con.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(environment_telemetry)";
            using var r = pragma.ExecuteReader();
            while (r.Read()) existing.Add(r.GetString(1)); // col 1 = name
        }
        foreach (var col in EnvironmentMetricInfo.Columns)
        {
            if (existing.Contains(col)) continue;
            using var alter = con.CreateCommand();
            alter.CommandText = $"ALTER TABLE environment_telemetry ADD COLUMN {col} REAL";
            alter.ExecuteNonQuery();
        }
    }

    // ── Write methods ─────────────────────────────────────────────────────────

    public void InsertNodePosition(uint nodeId, double lat, double lon, double? alt,
        float? speed, float? track, long unixTimestamp)
    {
        lock (_lock)
        {
            using var con = Open();

            // Duplicate guard: skip if same node had a position within 60s that is < 5m away
            using (var chk = con.CreateCommand())
            {
                chk.CommandText = @"
SELECT latitude, longitude FROM node_positions
WHERE node_id = $n AND timestamp >= $minTs
ORDER BY timestamp DESC LIMIT 1";
                chk.Parameters.AddWithValue("$n",     (long)nodeId);
                chk.Parameters.AddWithValue("$minTs", unixTimestamp - 60);
                using var rdr = chk.ExecuteReader();
                if (rdr.Read())
                {
                    var prevLat = rdr.GetDouble(0);
                    var prevLon = rdr.GetDouble(1);
                    if (HaversineMeters(lat, lon, prevLat, prevLon) < 5.0)
                        return;
                }
            }

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO node_positions (node_id, latitude, longitude, altitude, ground_speed, ground_track, timestamp)
VALUES ($n, $lat, $lon, $alt, $spd, $trk, $ts)";
            cmd.Parameters.AddWithValue("$n",   (long)nodeId);
            cmd.Parameters.AddWithValue("$lat", lat);
            cmd.Parameters.AddWithValue("$lon", lon);
            cmd.Parameters.AddWithValue("$alt", alt.HasValue  ? alt.Value   : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$spd", speed.HasValue ? speed.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$trk", track.HasValue ? track.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$ts",  unixTimestamp);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteOldNodePositions(int retentionHours)
    {
        if (retentionHours <= 0) return;
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)retentionHours * 3600;
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM node_positions WHERE timestamp < $cut";
            cmd.Parameters.AddWithValue("$cut", cutoff);
            cmd.ExecuteNonQuery();
        }
    }

    public record NodePositionEntry(double Lat, double Lon, double? Alt, float? Track, float? Speed, long Timestamp);

    public List<NodePositionEntry> GetNodePositionHistory(uint nodeId, int hours)
    {
        var result = new List<NodePositionEntry>();
        long since = hours > 0
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)hours * 3600
            : 0;

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
SELECT latitude, longitude, altitude, ground_track, ground_speed, timestamp
FROM node_positions
WHERE node_id = $n AND timestamp >= $since
ORDER BY timestamp ASC";
        cmd.Parameters.AddWithValue("$n",     (long)nodeId);
        cmd.Parameters.AddWithValue("$since", since);

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            result.Add(new NodePositionEntry(
                Lat:       rdr.GetDouble(0),
                Lon:       rdr.GetDouble(1),
                Alt:       rdr.IsDBNull(2) ? null : rdr.GetDouble(2),
                Track:     rdr.IsDBNull(3) ? null : (float)rdr.GetDouble(3),
                Speed:     rdr.IsDBNull(4) ? null : (float)rdr.GetDouble(4),
                Timestamp: rdr.GetInt64(5)
            ));
        }
        return result;
    }

    // ── Write methods ─────────────────────────────────────────────────────────

    public void InsertPacketRx(uint nodeId, uint packetId, DateTime timestamp, float snr, int rssi, int? hopCount, bool wantAck)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO packet_rx (node_id, packet_id, timestamp, rx_snr, rx_rssi, hop_count, want_ack, ack_received)
VALUES ($n, $pid, $ts, $snr, $rssi, $hops, $wa, 0)";
            cmd.Parameters.AddWithValue("$n",    (long)nodeId);
            cmd.Parameters.AddWithValue("$pid",  (long)packetId);
            cmd.Parameters.AddWithValue("$ts",   ToUnix(timestamp));
            cmd.Parameters.AddWithValue("$snr",  snr  != 0f ? snr  : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$rssi", rssi != 0  ? rssi : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$hops", hopCount.HasValue ? hopCount.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$wa",   wantAck ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public void MarkAckReceived(uint packetId)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE packet_rx SET ack_received=1 WHERE packet_id=$pid";
            cmd.Parameters.AddWithValue("$pid", (long)packetId);
            cmd.ExecuteNonQuery();
        }
    }

    public void InsertDeviceTelemetry(uint nodeId, DateTime timestamp,
        float batteryPercent, float voltage, float channelUtil, float airTxUtil, uint uptimeSeconds)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO device_telemetry (node_id, timestamp, battery_percent, voltage, channel_utilization, air_util_tx, uptime_seconds)
VALUES ($n, $ts, $bat, $v, $cu, $au, $up)";
            cmd.Parameters.AddWithValue("$n",  (long)nodeId);
            cmd.Parameters.AddWithValue("$ts", ToUnix(timestamp));
            cmd.Parameters.AddWithValue("$bat", batteryPercent > 0 ? batteryPercent : DBNull.Value);
            cmd.Parameters.AddWithValue("$v",   voltage > 0 ? voltage : DBNull.Value);
            cmd.Parameters.AddWithValue("$cu",  channelUtil >= 0 ? channelUtil : DBNull.Value);
            cmd.Parameters.AddWithValue("$au",  airTxUtil >= 0 ? airTxUtil : DBNull.Value);
            cmd.Parameters.AddWithValue("$up",  uptimeSeconds > 0 ? (long)uptimeSeconds : DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Inserts one environment reading. <paramref name="metrics"/> is keyed by
    /// <see cref="EnvironmentMetricInfo"/> metric Key (only present sensors); unknown keys
    /// are ignored. Columns are resolved from the registry, so this stores the full sensor
    /// set without a fixed signature.</summary>
    public void InsertEnvironmentTelemetry(uint nodeId, DateTime timestamp,
        IReadOnlyDictionary<string, double> metrics)
    {
        if (metrics == null || metrics.Count == 0) return;

        var cols = new List<string>();
        var vals = new List<double>();
        foreach (var kv in metrics)
        {
            var m = EnvironmentMetricInfo.ByKey(kv.Key);
            if (m == null) continue;               // ignore anything not in the registry
            cols.Add(m.Column);
            vals.Add(kv.Value);
        }
        if (cols.Count == 0) return;

        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            var colList = string.Join(", ", cols);
            var parList = string.Join(", ", cols.Select((_, i) => "$v" + i));
            cmd.CommandText =
                $"INSERT INTO environment_telemetry (node_id, timestamp, {colList}) VALUES ($n, $ts, {parList})";
            cmd.Parameters.AddWithValue("$n",  (long)nodeId);
            cmd.Parameters.AddWithValue("$ts", ToUnix(timestamp));
            for (int i = 0; i < vals.Count; i++)
                cmd.Parameters.AddWithValue("$v" + i, vals[i]);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Stores a LocalStats telemetry reading (newer firmware: noise floor, RX/TX
    /// packet counters, relay stats). Counters are cumulative since the node's last boot.
    /// noise_floor 0 = not reported → NULL.</summary>
    public void InsertLocalStats(uint nodeId, DateTime timestamp, Meshtastic.Protobufs.LocalStats ls)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO local_stats (node_id, timestamp, noise_floor, num_packets_tx, num_packets_rx,
    num_packets_rx_bad, num_rx_dupe, num_tx_relay, num_tx_relay_canceled, num_tx_dropped,
    num_online_nodes, num_total_nodes)
VALUES ($n, $ts, $noise, $tx, $rx, $rxbad, $dupe, $relay, $relayc, $drop, $online, $total)";
            cmd.Parameters.AddWithValue("$n",      (long)nodeId);
            cmd.Parameters.AddWithValue("$ts",     ToUnix(timestamp));
            cmd.Parameters.AddWithValue("$noise",  ls.NoiseFloor != 0 ? ls.NoiseFloor : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$tx",     (long)ls.NumPacketsTx);
            cmd.Parameters.AddWithValue("$rx",     (long)ls.NumPacketsRx);
            cmd.Parameters.AddWithValue("$rxbad",  (long)ls.NumPacketsRxBad);
            cmd.Parameters.AddWithValue("$dupe",   (long)ls.NumRxDupe);
            cmd.Parameters.AddWithValue("$relay",  (long)ls.NumTxRelay);
            cmd.Parameters.AddWithValue("$relayc", (long)ls.NumTxRelayCanceled);
            cmd.Parameters.AddWithValue("$drop",   (long)ls.NumTxDropped);
            cmd.Parameters.AddWithValue("$online", (long)ls.NumOnlineNodes);
            cmd.Parameters.AddWithValue("$total",  (long)ls.NumTotalNodes);
            cmd.ExecuteNonQuery();
        }
    }

    public sealed class LocalStatsReading
    {
        public DateTime Timestamp { get; init; }
        public int?  NoiseFloor { get; init; }
        public long PacketsTx { get; init; }
        public long PacketsRx { get; init; }
        public long PacketsRxBad { get; init; }
        public long RxDupe { get; init; }
        public long TxRelay { get; init; }
        public long TxRelayCanceled { get; init; }
        public long TxDropped { get; init; }
        public long OnlineNodes { get; init; }
        public long TotalNodes { get; init; }
    }

    /// <summary>Rate series (per hour) derived from a cumulative local_stats counter:
    /// each point = (counterΔ / hoursΔ) between two consecutive readings, stamped at the
    /// later reading. Negative deltas (counter reset after reboot) and degenerate
    /// intervals (&lt; 1 min or &gt; 24 h apart) are skipped.</summary>
    private List<TimeSeriesPoint> GetLocalStatsRateSeries(
        IEnumerable<uint> nodeIds, string col, int days, string metricName)
    {
        long since = Since(days);
        var result = new List<TimeSeriesPoint>();

        using var con = Open();
        foreach (var nodeId in nodeIds)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"SELECT timestamp, {col} FROM local_stats " +
                              $"WHERE node_id=$n AND timestamp>=$s AND {col} IS NOT NULL ORDER BY timestamp";
            cmd.Parameters.AddWithValue("$n", (long)nodeId);
            cmd.Parameters.AddWithValue("$s", since);

            long prevTs = 0; double prevVal = double.NaN;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long ts = r.GetInt64(0);
                double v = r.GetDouble(1);
                if (!double.IsNaN(prevVal))
                {
                    double dtHours = (ts - prevTs) / 3600.0;
                    double delta = v - prevVal;
                    if (dtHours >= 1.0 / 60 && dtHours <= 24 && delta >= 0)
                        result.Add(new TimeSeriesPoint
                        {
                            NodeId    = nodeId,
                            Timestamp = FromUnix(ts),
                            Value     = delta / dtHours,
                            Metric    = metricName,
                        });
                }
                prevTs = ts; prevVal = v;
            }
        }
        return result;
    }

    /// <summary>Most recent LocalStats reading of a node, or null if none stored.</summary>
    public LocalStatsReading? GetLatestLocalStats(uint nodeId)
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
SELECT timestamp, noise_floor, num_packets_tx, num_packets_rx, num_packets_rx_bad,
       num_rx_dupe, num_tx_relay, num_tx_relay_canceled, num_tx_dropped,
       num_online_nodes, num_total_nodes
FROM local_stats WHERE node_id=$n ORDER BY timestamp DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$n", (long)nodeId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new LocalStatsReading
        {
            Timestamp       = FromUnix(r.GetInt64(0)),
            NoiseFloor      = r.IsDBNull(1) ? null : r.GetInt32(1),
            PacketsTx       = r.GetInt64(2),
            PacketsRx       = r.GetInt64(3),
            PacketsRxBad    = r.GetInt64(4),
            RxDupe          = r.GetInt64(5),
            TxRelay         = r.GetInt64(6),
            TxRelayCanceled = r.GetInt64(7),
            TxDropped       = r.GetInt64(8),
            OnlineNodes     = r.GetInt64(9),
            TotalNodes      = r.GetInt64(10),
        };
    }

    /// <summary>Records an observed direct RF link: <paramref name="observer"/> heard
    /// <paramref name="neighbor"/> at <paramref name="snr"/> dB. One row per
    /// (observer, neighbor, source); repeats update SNR + last_seen. Evidence sources:
    /// 'rx' = our own 0-hop non-MQTT reception, 'traceroute' = adjacent hop pair from a
    /// traceroute reply, 'neighborinfo' = a node's own NEIGHBORINFO_APP broadcast.</summary>
    private bool _rfStartMarkerSet;

    public void UpsertRfLink(uint observer, uint neighbor, float? snr, string source, DateTime timestamp)
    {
        if (observer == 0 || neighbor == 0 || observer == neighbor) return;
        lock (_lock)
        {
            using var con = Open();

            // One-time marker: when RF-link collection began. The neighbor view uses it to
            // decide when sparse data (1–2 partners) is trustworthy rather than just young.
            if (!_rfStartMarkerSet)
            {
                using var meta = con.CreateCommand();
                meta.CommandText = "INSERT OR IGNORE INTO meta (key, value) VALUES ('rf_links_started', $v)";
                meta.Parameters.AddWithValue("$v", ToUnix(timestamp).ToString());
                meta.ExecuteNonQuery();
                _rfStartMarkerSet = true;
            }

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO rf_links (observer, neighbor, snr, source, last_seen)
VALUES ($o, $n, $snr, $src, $ts)
ON CONFLICT(observer, neighbor, source)
DO UPDATE SET snr = COALESCE($snr, snr), last_seen = $ts";
            cmd.Parameters.AddWithValue("$o",   (long)observer);
            cmd.Parameters.AddWithValue("$n",   (long)neighbor);
            cmd.Parameters.AddWithValue("$snr", snr.HasValue ? snr.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$src", source);
            cmd.Parameters.AddWithValue("$ts",  ToUnix(timestamp));
            cmd.ExecuteNonQuery();
        }
    }

    public class RfNeighbor
    {
        public uint      NeighborId   { get; set; }
        public string    NeighborName { get; set; } = "";
        public float?    SnrIn        { get; set; }  // selected node heard the partner at …
        public float?    SnrOut       { get; set; }  // partner heard the selected node at …
        public List<string> Sources   { get; set; } = new();
        public DateTime  LastSeen     { get; set; }
    }

    /// <summary>When RF-link collection started (first ever rf_links insert), or null.</summary>
    public DateTime? GetRfCollectionStart()
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key='rf_links_started'";
        var v = cmd.ExecuteScalar() as string;
        return long.TryParse(v, out var unix) ? FromUnix(unix) : null;
    }

    /// <summary>All direct-RF partners of <paramref name="nodeId"/> from collected link
    /// evidence, newest first. <paramref name="days"/> &gt; 0 limits to links confirmed
    /// within that window.</summary>
    public List<RfNeighbor> GetRfNeighbors(uint nodeId, int days, Dictionary<uint, string> nodeNames)
    {
        long since = Since(days);
        var byPartner = new Dictionary<uint, RfNeighbor>();

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
SELECT observer, neighbor, snr, source, last_seen
FROM rf_links
WHERE (observer = $n OR neighbor = $n) AND last_seen >= $s";
        cmd.Parameters.AddWithValue("$n", (long)nodeId);
        cmd.Parameters.AddWithValue("$s", since);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            uint observer = (uint)r.GetInt64(0);
            uint neighbor = (uint)r.GetInt64(1);
            float? snr    = r.IsDBNull(2) ? null : (float)r.GetDouble(2);
            string source = r.GetString(3);
            var lastSeen  = FromUnix(r.GetInt64(4));

            uint partner = observer == nodeId ? neighbor : observer;
            if (!byPartner.TryGetValue(partner, out var row))
            {
                nodeNames.TryGetValue(partner, out var name);
                row = new RfNeighbor { NeighborId = partner, NeighborName = name ?? $"!{partner:x8}" };
                byPartner[partner] = row;
            }

            // observer == selected node ⇒ the selected node HEARD the partner (SnrIn)
            if (observer == nodeId) { if (snr.HasValue) row.SnrIn = snr; }
            else                    { if (snr.HasValue) row.SnrOut = snr; }
            if (!row.Sources.Contains(source)) row.Sources.Add(source);
            if (lastSeen > row.LastSeen) row.LastSeen = lastSeen;
        }

        return byPartner.Values.OrderByDescending(x => x.LastSeen).ToList();
    }

    public void InsertTracerouteHops(TracerouteResult result)
    {
        lock (_lock)
        {
            using var con = Open();
            long ts = ToUnix(result.ReceivedAt);

            if (result.RouteBack.Count == 0 && result.RouteForward.Count == 0)
            {
                // Direct link (no relay hops) — store a sentinel row so route-change tracking works.
                // hop_index = -1 marks a direct link; node_id = destination.
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
INSERT INTO traceroute_hops (request_id, source_node_id, dest_node_id, timestamp, hop_index, node_id, snr_towards, snr_back)
VALUES ($rid, $src, $dst, $ts, -1, $dst, NULL, NULL)";
                cmd.Parameters.AddWithValue("$rid", (long)result.RequestId);
                cmd.Parameters.AddWithValue("$src", (long)result.SourceNodeId);
                cmd.Parameters.AddWithValue("$dst", (long)result.DestinationNodeId);
                cmd.Parameters.AddWithValue("$ts",  ts);
                cmd.ExecuteNonQuery();
                return;
            }

            // Insert one row per hop in the return path (snr_back is the primary data)
            for (int i = 0; i < result.RouteBack.Count; i++)
            {
                float snrB = i < result.SnrBack.Count    ? result.SnrBack[i] / 4f    : float.NaN;
                float snrT = i < result.SnrTowards.Count ? result.SnrTowards[i] / 4f : float.NaN;
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
INSERT INTO traceroute_hops (request_id, source_node_id, dest_node_id, timestamp, hop_index, node_id, snr_towards, snr_back)
VALUES ($rid, $src, $dst, $ts, $hi, $nid, $st, $sb)";
                cmd.Parameters.AddWithValue("$rid", (long)result.RequestId);
                cmd.Parameters.AddWithValue("$src", (long)result.SourceNodeId);
                cmd.Parameters.AddWithValue("$dst", (long)result.DestinationNodeId);
                cmd.Parameters.AddWithValue("$ts",  ts);
                cmd.Parameters.AddWithValue("$hi",  i);
                cmd.Parameters.AddWithValue("$nid", (long)result.RouteBack[i]);
                cmd.Parameters.AddWithValue("$st",  float.IsNaN(snrT) ? DBNull.Value : snrT);
                cmd.Parameters.AddWithValue("$sb",  float.IsNaN(snrB) ? DBNull.Value : snrB);
                cmd.ExecuteNonQuery();
            }
        }
    }

    public void RunRetentionCleanup(int retentionDays)
    {
        if (retentionDays <= 0) return;
        long cutoff = ToUnix(DateTime.UtcNow.AddDays(-retentionDays));
        lock (_lock)
        {
            using var con = Open();
            foreach (var table in new[] { "packet_rx", "device_telemetry", "environment_telemetry", "traceroute_hops" })
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = $"DELETE FROM {table} WHERE timestamp < $cutoff";
                cmd.Parameters.AddWithValue("$cutoff", cutoff);
                cmd.ExecuteNonQuery();
            }
            using var vacuum = con.CreateCommand();
            vacuum.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            vacuum.ExecuteNonQuery();
        }
        Logger.WriteLine($"TelemetryDB: retention cleanup done (>{retentionDays}d removed)");
    }

    // ── Query: Signal ─────────────────────────────────────────────────────────

    public SignalStats GetSignalStats(uint nodeId, int days)
    {
        long since = Since(days);
        var rows = new List<(long ts, float snr, int? rssi)>();

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT timestamp, rx_snr, rx_rssi FROM packet_rx WHERE node_id=$n AND timestamp>=$s AND rx_snr IS NOT NULL ORDER BY timestamp";
        cmd.Parameters.AddWithValue("$n", (long)nodeId);
        cmd.Parameters.AddWithValue("$s", since);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(((long)r.GetInt64(0), (float)r.GetDouble(1), r.IsDBNull(2) ? null : (int?)r.GetInt32(2)));

        using var cnt = con.CreateCommand();
        cnt.CommandText = "SELECT COUNT(*) FROM packet_rx WHERE node_id=$n AND timestamp>=$s";
        cnt.Parameters.AddWithValue("$n", (long)nodeId);
        cnt.Parameters.AddWithValue("$s", since);
        int total = (int)(long)cnt.ExecuteScalar()!;

        var day   = rows.Where(x => IsDay(x.ts)).ToList();
        var night = rows.Where(x => !IsDay(x.ts)).ToList();
        var withRssi = rows.Where(x => x.rssi.HasValue).ToList();

        return new SignalStats
        {
            Days         = days,
            TotalPackets = total,
            DaySnrMedian    = Median(day.Select(x => x.snr)),
            NightSnrMedian  = Median(night.Select(x => x.snr)),
            SnrMin          = rows.Count > 0 ? rows.Min(x => x.snr) : null,
            SnrMax          = rows.Count > 0 ? rows.Max(x => x.snr) : null,
            SnrVariance     = Variance(rows.Select(x => x.snr)),
            DayRssiMedian   = Median(day.Where(x => x.rssi.HasValue).Select(x => (float)x.rssi!.Value)),
            NightRssiMedian = Median(night.Where(x => x.rssi.HasValue).Select(x => (float)x.rssi!.Value)),
            RssiMin         = withRssi.Count > 0 ? (float?)withRssi.Min(x => x.rssi!.Value) : null,
            RssiMax         = withRssi.Count > 0 ? (float?)withRssi.Max(x => x.rssi!.Value) : null,
        };
    }

    // ── Query: Power ─────────────────────────────────────────────────────────

    public PowerStats GetPowerStats(uint nodeId, int days)
    {
        long since = Since(days);
        var rows = new List<(long ts, float? bat, float? volt, long? uptime)>();

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT timestamp, battery_percent, voltage, uptime_seconds FROM device_telemetry WHERE node_id=$n AND timestamp>=$s ORDER BY timestamp";
        cmd.Parameters.AddWithValue("$n", (long)nodeId);
        cmd.Parameters.AddWithValue("$s", since);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            float? bat  = r.IsDBNull(1) ? null : (float)r.GetDouble(1);
            float? volt = r.IsDBNull(2) ? null : (float)r.GetDouble(2);
            long?  up   = r.IsDBNull(3) ? null : r.GetInt64(3);
            rows.Add(((long)r.GetInt64(0), bat, volt, up));
        }

        var batRows  = rows.Where(x => x.bat.HasValue).ToList();
        var voltRows = rows.Where(x => x.volt.HasValue).ToList();

        var dayBat   = batRows.Where(x => IsDay(x.ts)).Select(x => x.bat!.Value).ToList();
        var nightBat = batRows.Where(x => !IsDay(x.ts)).Select(x => x.bat!.Value).ToList();
        var dayVolt  = voltRows.Where(x => IsDay(x.ts)).Select(x => x.volt!.Value).ToList();
        var nightVolt = voltRows.Where(x => !IsDay(x.ts)).Select(x => x.volt!.Value).ToList();

        float? dayBatAvg   = dayBat.Count   > 0 ? dayBat.Average()   : null;
        float? nightBatAvg = nightBat.Count > 0 ? nightBat.Average() : null;
        float? dayVoltAvg  = dayVolt.Count  > 0 ? dayVolt.Average()  : null;
        float? nightVoltAvg = nightVolt.Count > 0 ? nightVolt.Average() : null;

        return new PowerStats
        {
            Days             = days,
            TotalReadings    = rows.Count,
            DayBatteryAvg    = dayBatAvg,
            NightBatteryAvg  = nightBatAvg,
            NightBatteryDrop = dayBatAvg.HasValue && nightBatAvg.HasValue ? nightBatAvg - dayBatAvg : null,
            DayVoltageAvg    = dayVoltAvg,
            NightVoltageAvg  = nightVoltAvg,
            NightVoltageDrop = dayVoltAvg.HasValue && nightVoltAvg.HasValue ? nightVoltAvg - dayVoltAvg : null,
            VoltageMin       = voltRows.Count > 0 ? voltRows.Min(x => x.volt) : null,
            VoltageMax       = voltRows.Count > 0 ? voltRows.Max(x => x.volt) : null,
            LastUptimeSeconds = rows.LastOrDefault(x => x.uptime.HasValue).uptime,
        };
    }

    // ── Query: Airtime ────────────────────────────────────────────────────────

    public AirtimeStats GetAirtimeStats(uint nodeId, int days)
    {
        long since = Since(days);
        var rows = new List<(long ts, float? cu, float? au)>();

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT timestamp, channel_utilization, air_util_tx FROM device_telemetry WHERE node_id=$n AND timestamp>=$s ORDER BY timestamp";
        cmd.Parameters.AddWithValue("$n", (long)nodeId);
        cmd.Parameters.AddWithValue("$s", since);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            float? cu = r.IsDBNull(1) ? null : (float)r.GetDouble(1);
            float? au = r.IsDBNull(2) ? null : (float)r.GetDouble(2);
            rows.Add(((long)r.GetInt64(0), cu, au));
        }

        var cuRows = rows.Where(x => x.cu.HasValue).ToList();
        var auRows = rows.Where(x => x.au.HasValue).ToList();

        return new AirtimeStats
        {
            Days               = days,
            DayChannelUtilAvg  = Average(cuRows.Where(x => IsDay(x.ts)).Select(x => x.cu!.Value)),
            NightChannelUtilAvg= Average(cuRows.Where(x => !IsDay(x.ts)).Select(x => x.cu!.Value)),
            ChannelUtilMax     = cuRows.Count > 0 ? cuRows.Max(x => x.cu) : null,
            DayAirTxUtilAvg    = Average(auRows.Where(x => IsDay(x.ts)).Select(x => x.au!.Value)),
            NightAirTxUtilAvg  = Average(auRows.Where(x => !IsDay(x.ts)).Select(x => x.au!.Value)),
            AirTxUtilMax       = auRows.Count > 0 ? auRows.Max(x => x.au) : null,
        };
    }

    // ── Query: Routing ────────────────────────────────────────────────────────

    public RoutingStats GetRoutingStats(uint nodeId, int days)
    {
        long since = Since(days);

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT hop_count, want_ack, ack_received FROM packet_rx WHERE node_id=$n AND timestamp>=$s";
        cmd.Parameters.AddWithValue("$n", (long)nodeId);
        cmd.Parameters.AddWithValue("$s", since);

        var hops = new List<int>();
        int total = 0, ackReq = 0, ackRcv = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            total++;
            if (!r.IsDBNull(0)) hops.Add(r.GetInt32(0));
            if (r.GetInt32(1) == 1) { ackReq++; if (r.GetInt32(2) == 1) ackRcv++; }
        }

        using var uniq = con.CreateCommand();
        uniq.CommandText = "SELECT COUNT(DISTINCT node_id) FROM packet_rx WHERE node_id=$n AND timestamp>=$s";
        // Actually "from" node is the sender - but here node_id IS the sender node.
        // We want unique intermediate relays. For now use hope data from traceroutes.
        // Approximate: count distinct "from" nodes seen for packets to us
        uniq.Parameters.AddWithValue("$n", (long)nodeId);
        uniq.Parameters.AddWithValue("$s", since);

        // Get unique from-node variants via traceroute hops
        using var ufrom = con.CreateCommand();
        ufrom.CommandText = "SELECT COUNT(DISTINCT node_id) FROM traceroute_hops WHERE (source_node_id=$n OR dest_node_id=$n) AND timestamp>=$s";
        ufrom.Parameters.AddWithValue("$n", (long)nodeId);
        ufrom.Parameters.AddWithValue("$s", since);
        int uniqueFrom = (int)(long)(ufrom.ExecuteScalar() ?? 0L);

        var hopDist = hops.GroupBy(h => h).ToDictionary(g => g.Key, g => g.Count());

        return new RoutingStats
        {
            Days            = days,
            TotalPackets    = total,
            AckRequested    = ackReq,
            AckReceived     = ackRcv,
            // Only report a rate when at least one ACK has been confirmed via serial API.
            // If ackRcv==0 but ackReq>0, ACKs are handled entirely by firmware and never
            // forwarded to the client — treat as NoData rather than 100% timeout.
            AckSuccessRate  = ackReq > 0 && ackRcv > 0 ? (float)ackRcv / ackReq : null,
            AvgHops         = hops.Count > 0 ? (float?)hops.Average() : null,
            MinHops         = hops.Count > 0 ? hops.Min() : null,
            MaxHops         = hops.Count > 0 ? hops.Max() : null,
            UniqueFromNodes = uniqueFrom,
            HopDistribution = hopDist,
        };
    }

    // ── Query: Neighbors ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns per-neighbor link stats based on packet_rx records.
    /// node_id in packet_rx = the last-hop sender (i.e., the neighbor we heard directly).
    /// </summary>
    public List<NeighborStats> GetNeighborStats(uint myNodeId, int days, Dictionary<uint, string> nodeNames)
    {
        long since = Since(days);
        var result = new List<NeighborStats>();

        using var con = Open();
        // Group by node_id (= neighbor that forwarded to us), compute median RSSI/SNR
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
SELECT node_id,
       COUNT(*) as cnt,
       MAX(timestamp) as last_ts
FROM packet_rx
WHERE timestamp >= $s AND node_id != $me
GROUP BY node_id
ORDER BY cnt DESC
LIMIT 50";
        cmd.Parameters.AddWithValue("$s",  since);
        cmd.Parameters.AddWithValue("$me", (long)myNodeId);

        var groups = new List<(uint nid, int cnt, long lastTs)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            groups.Add(((uint)r.GetInt64(0), r.GetInt32(1), r.GetInt64(2)));

        foreach (var (nid, cnt, lastTs) in groups)
        {
            using var detail = con.CreateCommand();
            detail.CommandText = "SELECT rx_snr, rx_rssi FROM packet_rx WHERE node_id=$n AND timestamp>=$s AND rx_snr IS NOT NULL ORDER BY timestamp";
            detail.Parameters.AddWithValue("$n", (long)nid);
            detail.Parameters.AddWithValue("$s", since);
            var snrs  = new List<float>();
            var rssis = new List<float>();
            using var dr = detail.ExecuteReader();
            while (dr.Read())
            {
                snrs.Add((float)dr.GetDouble(0));
                if (!dr.IsDBNull(1)) rssis.Add(dr.GetInt32(1));
            }

            double spanHours = Math.Max(1, (DateTime.UtcNow - FromUnix(since)).TotalHours);
            nodeNames.TryGetValue(nid, out var name);

            result.Add(new NeighborStats
            {
                NeighborId     = nid,
                NeighborName   = name ?? $"!{nid:x8}",
                MedianSnr      = Median(snrs),
                MedianRssi     = Median(rssis),
                PacketsPerHour = (float)(cnt / spanHours),
                LastSeen       = FromUnix(lastTs),
                TotalPackets   = cnt,
            });
        }

        return result;
    }

    // ── Query: Time series (for PlotWindow) ──────────────────────────────────

    /// <summary>
    /// Available metrics: snr, rssi, battery, voltage, channel_util, air_tx_util, hop_count,
    /// temperature, humidity, pressure, packet_count (aggregated per hour).
    /// </summary>
    public List<TimeSeriesPoint> GetTimeSeries(IEnumerable<uint> nodeIds, string metric, int days)
    {
        // packet_count is an aggregate (packets per hour bucket) — handled separately
        if (string.Equals(metric, "packet_count", StringComparison.OrdinalIgnoreCase))
            return QueryPacketCountSeries(nodeIds, days);

        // Derived rates from cumulative LocalStats counters: delta between consecutive
        // polls ÷ elapsed time → packets per hour. Counter resets (reboot) are skipped.
        string? rateCol = metric.ToLowerInvariant() switch
        {
            "rx_pph"       => "num_packets_rx",
            "tx_pph"       => "num_packets_tx",
            "rx_bad_pph"   => "num_packets_rx_bad",
            "rx_dupe_pph"  => "num_rx_dupe",
            "tx_relay_pph" => "num_tx_relay",
            _              => null,
        };
        if (rateCol != null)
            return GetLocalStatsRateSeries(nodeIds, rateCol, days, metric);

        long since = Since(days);
        var result = new List<TimeSeriesPoint>();

        (string table, string col) = metric.ToLowerInvariant() switch
        {
            "snr"          => ("packet_rx",             "rx_snr"),
            "rssi"         => ("packet_rx",             "rx_rssi"),
            "hop_count"    => ("packet_rx",             "hop_count"),
            "battery"      => ("device_telemetry",      "battery_percent"),
            "voltage"      => ("device_telemetry",      "voltage"),
            "channel_util" => ("device_telemetry",      "channel_utilization"),
            "air_tx_util"  => ("device_telemetry",      "air_util_tx"),
            // LocalStats telemetry (newer firmware)
            "noise_floor"       => ("local_stats", "noise_floor"),
            "rx_bad"            => ("local_stats", "num_packets_rx_bad"),
            "rx_dupe"           => ("local_stats", "num_rx_dupe"),
            "tx_relay"          => ("local_stats", "num_tx_relay"),
            "tx_relay_canceled" => ("local_stats", "num_tx_relay_canceled"),
            "tx_dropped"        => ("local_stats", "num_tx_dropped"),
            _ when EnvironmentMetricInfo.ByKey(metric) is { } envM && !envM.Virtual
                           => ("environment_telemetry", envM.Column),
            _              => ("packet_rx",             "rx_snr"),
        };

        // SNR/RSSI describe OUR link to the node — only direct receptions count (a relayed
        // packet's rx_snr/rssi is the last hop's, not the originator's). NULL hop count
        // (old firmware) is treated as local, like everywhere else in the client.
        string hopFilter = table == "packet_rx" && (col == "rx_snr" || col == "rx_rssi")
            ? " AND (hop_count = 0 OR hop_count IS NULL)"
            : "";

        using var con = Open();
        foreach (var nodeId in nodeIds)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"SELECT timestamp, {col} FROM {table} WHERE node_id=$n AND timestamp>=$s AND {col} IS NOT NULL{hopFilter} ORDER BY timestamp";
            cmd.Parameters.AddWithValue("$n", (long)nodeId);
            cmd.Parameters.AddWithValue("$s", since);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Add(new TimeSeriesPoint
                {
                    NodeId    = nodeId,
                    Timestamp = FromUnix(r.GetInt64(0)),
                    Value     = r.GetDouble(1),
                    Metric    = metric,
                });
            }
        }

        return result;
    }

    // ── Query: Latest environment reading per node (for map info-boxes) ──────

    /// <summary>Latest environment reading of one node: timestamp + every present metric
    /// (keyed by <see cref="EnvironmentMetricInfo"/> Key), NULL columns omitted.</summary>
    public sealed class EnvReading
    {
        public uint NodeId { get; init; }
        public DateTime Timestamp { get; init; }
        public Dictionary<string, double> Values { get; } = new();
    }

    /// <summary>Returns, for every node that ever reported environment data, its most
    /// recent reading row. Used to render the on-map value boxes.</summary>
    public List<EnvReading> GetLatestEnvironmentPerNode()
    {
        var cols = EnvironmentMetricInfo.EnvOnly.ToList();      // fixed order = read order (no virtual metrics)
        var colList = string.Join(", ", cols.Select(m => m.Column));
        var result = new List<EnvReading>();

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $@"
SELECT e.node_id, e.timestamp, {colList}
FROM environment_telemetry e
JOIN (SELECT node_id, MAX(timestamp) AS mt FROM environment_telemetry GROUP BY node_id) m
  ON e.node_id = m.node_id AND e.timestamp = m.mt";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var reading = new EnvReading
            {
                NodeId    = (uint)r.GetInt64(0),
                Timestamp = FromUnix(r.GetInt64(1)),
            };
            for (int i = 0; i < cols.Count; i++)
            {
                int ord = i + 2;                                // after node_id, timestamp
                if (!r.IsDBNull(ord)) reading.Values[cols[i].Key] = r.GetDouble(ord);
            }
            result.Add(reading);
        }
        return result;
    }

    // ── Query: Packet count per time bucket ─────────────────────────────────

    /// <summary>
    /// Returns packets-per-hour time series for each node. Value = count within the hour bucket.
    /// </summary>
    private List<TimeSeriesPoint> QueryPacketCountSeries(IEnumerable<uint> nodeIds, int days)
    {
        long since = Since(days);
        const int bucketSec = 3600;
        var result = new List<TimeSeriesPoint>();

        using var con = Open();
        foreach (var nodeId in nodeIds)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT (timestamp / {bucketSec}) * {bucketSec} AS bucket, COUNT(*) AS cnt
FROM packet_rx
WHERE node_id = $n AND timestamp >= $s
GROUP BY bucket
ORDER BY bucket";
            cmd.Parameters.AddWithValue("$n", (long)nodeId);
            cmd.Parameters.AddWithValue("$s", since);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Add(new TimeSeriesPoint
                {
                    NodeId    = nodeId,
                    Timestamp = FromUnix(r.GetInt64(0)),
                    Value     = r.GetInt64(1),
                    Metric    = "packet_count",
                });
            }
        }
        return result;
    }

    // ── Query: Heatmap (hour-of-day × day) ──────────────────────────────────

    /// <summary>
    /// Returns a 2D array [dayIndex, hourOfDay] with average metric value (or NaN if no data).
    /// dayIndex 0 = oldest day, dayIndex days-1 = most recent day.
    /// </summary>
    public double[,] GetHeatmapData(uint nodeId, string metric, int days)
    {
        int effectiveDays = days > 0 ? days : 30;
        long since = Since(effectiveDays);

        // packet_count: count packets per (day, hour) cell
        if (string.Equals(metric, "packet_count", StringComparison.OrdinalIgnoreCase))
            return BuildPacketCountHeatmap(nodeId, effectiveDays, since);

        (string table, string col) = metric.ToLowerInvariant() switch
        {
            "snr"          => ("packet_rx",             "rx_snr"),
            "rssi"         => ("packet_rx",             "rx_rssi"),
            "battery"      => ("device_telemetry",      "battery_percent"),
            "noise_floor"       => ("local_stats", "noise_floor"),
            "rx_bad"            => ("local_stats", "num_packets_rx_bad"),
            "rx_dupe"           => ("local_stats", "num_rx_dupe"),
            "tx_relay"          => ("local_stats", "num_tx_relay"),
            "tx_relay_canceled" => ("local_stats", "num_tx_relay_canceled"),
            "tx_dropped"        => ("local_stats", "num_tx_dropped"),
            _ when EnvironmentMetricInfo.ByKey(metric) is { } envM && !envM.Virtual
                           => ("environment_telemetry", envM.Column),
            _              => ("packet_rx",             "rx_snr"),
        };

        // Direct receptions only for SNR/RSSI (see GetTimeSeries)
        string hopFilter = table == "packet_rx" && (col == "rx_snr" || col == "rx_rssi")
            ? " AND (hop_count = 0 OR hop_count IS NULL)"
            : "";

        var sums   = new double[effectiveDays, 24];
        var counts = new int[effectiveDays, 24];

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT timestamp, {col} FROM {table} WHERE node_id=$n AND timestamp>=$s AND {col} IS NOT NULL{hopFilter} ORDER BY timestamp";
        cmd.Parameters.AddWithValue("$n", (long)nodeId);
        cmd.Parameters.AddWithValue("$s", since);

        using var r = cmd.ExecuteReader();
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        while (r.Read())
        {
            long ts  = r.GetInt64(0);
            double v = r.GetDouble(1);
            var dt   = DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime();
            int hour = dt.Hour;
            int day  = (int)((nowUnix - ts) / 86400);
            int dayIdx = effectiveDays - 1 - day;
            if (dayIdx < 0 || dayIdx >= effectiveDays) continue;
            sums[dayIdx, hour]   += v;
            counts[dayIdx, hour] += 1;
        }

        var result = new double[effectiveDays, 24];
        for (int d = 0; d < effectiveDays; d++)
            for (int h = 0; h < 24; h++)
                result[d, h] = counts[d, h] > 0 ? sums[d, h] / counts[d, h] : double.NaN;
        return result;
    }

    private double[,] BuildPacketCountHeatmap(uint nodeId, int days, long since)
    {
        var result   = new double[days, 24];
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT timestamp FROM packet_rx WHERE node_id=$n AND timestamp>=$s";
        cmd.Parameters.AddWithValue("$n", (long)nodeId);
        cmd.Parameters.AddWithValue("$s", since);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long ts = r.GetInt64(0);
            var dt  = DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime();
            int hour = dt.Hour;
            int day  = (int)((nowUnix - ts) / 86400);
            int dayIdx = days - 1 - day;
            if (dayIdx >= 0 && dayIdx < days)
                result[dayIdx, hour] += 1;
        }
        return result;
    }

    // ── Query: Candlestick (daily OHLC) ─────────────────────────────────────

    public record CandlePoint(DateTime Time, double Open, double High, double Low, double Close);

    public List<CandlePoint> GetCandlestickData(uint nodeId, string metric, int days)
    {
        var pts = GetTimeSeries(new[] { nodeId }, metric, days)
                  .OrderBy(p => p.Timestamp).ToList();
        if (pts.Count == 0) return new();

        return pts.GroupBy(p => p.Timestamp.Date)
                  .OrderBy(g => g.Key)
                  .Select(g =>
                  {
                      var vals = g.OrderBy(x => x.Timestamp).Select(x => x.Value).ToList();
                      return new CandlePoint(
                          DateTime.SpecifyKind(g.Key, DateTimeKind.Utc),
                          vals.First(), vals.Max(), vals.Min(), vals.Last());
                  })
                  .ToList();
    }

    // ── Query: Node activity grid (for state timeline) ────────────────────────

    /// <summary>
    /// Returns a 2D array [dayIndex, hourOfDay] where 1.0 = node was heard, 0.0 = silent.
    /// dayIndex 0 = oldest, dayIndex days-1 = most recent.
    /// </summary>
    public double[,] GetNodeActivityGrid(uint nodeId, int days)
    {
        int effectiveDays = days > 0 ? days : 14;
        long since    = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)effectiveDays * 86400;
        var  startDt  = DateTimeOffset.FromUnixTimeSeconds(since).UtcDateTime.Date;
        var  result   = new double[effectiveDays, 24];

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT timestamp FROM packet_rx WHERE node_id=$n AND timestamp>=$since";
        cmd.Parameters.AddWithValue("$n",     (long)nodeId);
        cmd.Parameters.AddWithValue("$since", since);

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var ts     = DateTimeOffset.FromUnixTimeSeconds(rdr.GetInt64(0)).UtcDateTime;
            int dayIdx = (int)(ts.Date - startDt).TotalDays;
            int hour   = ts.Hour;
            if (dayIdx >= 0 && dayIdx < effectiveDays)
                result[dayIdx, hour] = 1.0;
        }
        return result;
    }

    // ── Query: Rolling average helper (for PlotWindow) ───────────────────────

    public static List<TimeSeriesPoint> RollingAverage(IEnumerable<TimeSeriesPoint> points, TimeSpan window)
    {
        var list   = points.OrderBy(p => p.Timestamp).ToList();
        var result = new List<TimeSeriesPoint>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var cutoff = list[i].Timestamp - window;
            var vals   = list.Skip(Math.Max(0, i - 500))
                             .TakeWhile(p => p.NodeId == list[i].NodeId)
                             .Where(p => p.Timestamp >= cutoff && p.Timestamp <= list[i].Timestamp)
                             .Select(p => p.Value)
                             .ToList();
            result.Add(new TimeSeriesPoint
            {
                NodeId    = list[i].NodeId,
                Timestamp = list[i].Timestamp,
                Value     = vals.Count > 0 ? vals.Average() : list[i].Value,
                Metric    = list[i].Metric,
            });
        }
        return result;
    }

    // ── Signal Analysis ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns linear regression slope in units/hour for a list of (unix timestamp, value) pairs.
    /// Requires at least 3 data points, returns 0 otherwise.
    /// </summary>
    private static float LinearRegressionSlope(List<(long ts, float val)> pts)
    {
        if (pts.Count < 3) return 0f;
        double n     = pts.Count;
        double meanX = pts.Average(p => (double)p.ts);
        double meanY = pts.Average(p => (double)p.val);
        double num   = pts.Sum(p => (p.ts - meanX) * (p.val - meanY));
        double den   = pts.Sum(p => (p.ts - meanX) * (p.ts - meanX));
        if (Math.Abs(den) < 1e-9) return 0f;
        return (float)(num / den * 3600.0);  // convert from per-second to per-hour
    }

    /// <summary>
    /// Computes short-term and long-term SNR trends for each node seen in packet_rx.
    /// nodeId=0 → all nodes, otherwise only neighbors of that node (node_id != nodeId).
    /// <summary>
    /// Returns the SNR trend for a single specific node as observed in packet_rx.
    /// Returns null if fewer than 5 data points are available.
    /// </summary>
    public NeighborTrend? GetSingleNodeTrend(
        uint targetNodeId, int shortHours, int longDays, Dictionary<uint, string> nodeNames)
    {
        long since      = Since(longDays);
        long shortSince = ToUnix(DateTime.UtcNow.AddHours(-shortHours));

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT timestamp, rx_snr FROM packet_rx WHERE node_id=$n AND timestamp>=$s AND rx_snr IS NOT NULL ORDER BY timestamp";
        cmd.Parameters.AddWithValue("$n", (long)targetNodeId);
        cmd.Parameters.AddWithValue("$s", since);

        var pts = new List<(long ts, float val)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) pts.Add((r.GetInt64(0), (float)r.GetDouble(1)));

        if (pts.Count < 5) return null;

        var shortPts   = pts.Where(p => p.ts >= shortSince).ToList();
        float shortSlope = shortPts.Count >= 3 ? LinearRegressionSlope(shortPts) : 0f;
        float longSlope  = LinearRegressionSlope(pts) * 24f;

        nodeNames.TryGetValue(targetNodeId, out var name);
        return new NeighborTrend
        {
            NeighborId   = targetNodeId,
            NeighborName = name ?? $"!{targetNodeId:x8}",
            ShortSlope   = shortSlope,
            LongSlope    = longSlope,
            PointCount   = pts.Count,
        };
    }

    /// </summary>
    public List<NeighborTrend> GetNeighborSnrTrends(
        uint myNodeId, int shortHours, int longDays, Dictionary<uint, string> nodeNames)
    {
        long since = Since(longDays);
        long shortSince = ToUnix(DateTime.UtcNow.AddHours(-shortHours));

        // Load all SNR readings grouped by node_id
        var rawData = new Dictionary<uint, List<(long ts, float val)>>();

        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = myNodeId == 0
            ? "SELECT node_id, timestamp, rx_snr FROM packet_rx WHERE timestamp>=$s AND rx_snr IS NOT NULL ORDER BY node_id, timestamp"
            : "SELECT node_id, timestamp, rx_snr FROM packet_rx WHERE timestamp>=$s AND node_id!=$me AND rx_snr IS NOT NULL ORDER BY node_id, timestamp";
        cmd.Parameters.AddWithValue("$s", since);
        if (myNodeId != 0) cmd.Parameters.AddWithValue("$me", (long)myNodeId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var nid = (uint)r.GetInt64(0);
            if (!rawData.ContainsKey(nid)) rawData[nid] = new List<(long, float)>();
            rawData[nid].Add((r.GetInt64(1), (float)r.GetDouble(2)));
        }

        var result = new List<NeighborTrend>();
        foreach (var (nid, pts) in rawData)
        {
            if (pts.Count < 5) continue;  // not enough data for meaningful trend

            var shortPts = pts.Where(p => p.ts >= shortSince).ToList();
            float shortSlope = shortPts.Count >= 3 ? LinearRegressionSlope(shortPts) : 0f;
            float longSlope  = LinearRegressionSlope(pts);  // already in per-hour; convert to per-day
            longSlope *= 24f;

            nodeNames.TryGetValue(nid, out var name);
            result.Add(new NeighborTrend
            {
                NeighborId   = nid,
                NeighborName = name ?? $"!{nid:x8}",
                ShortSlope   = shortSlope,
                LongSlope    = longSlope,
                PointCount   = pts.Count,
            });
        }

        return result;
    }

    /// <summary>
    /// Runs signal analysis for a specific node and returns LED states for all 4 indicators.
    /// </summary>
    public SignalAnalysisResult GetSignalAnalysis(
        uint myNodeId, int shortHours, int longDays, Dictionary<uint, string> nodeNames)
    {
        var trends = GetNeighborSnrTrends(myNodeId, shortHours, longDays, nodeNames);
        var result = new SignalAnalysisResult { Trends = trends };

        if (trends.Count == 0)
        {
            result.WeatherLed  = LedState.NoData;
            result.AntennaLed  = LedState.NoData;
            result.NeighborLed = LedState.NoData;
        }
        else
        {
            result.TotalNeighbors = trends.Count;

            // LED 1: Weather — multiple neighbors simultaneously declining short-term
            int decliningShort = trends.Count(t => t.ShortSlope < -0.5f && t.PointCount >= 5);
            result.DecliningNeighbors = decliningShort;
            result.WeatherLed = decliningShort >= 2 && decliningShort >= trends.Count * 0.4
                ? LedState.Alert
                : decliningShort >= 1 && decliningShort >= trends.Count * 0.25
                    ? LedState.Warning
                    : LedState.Good;

            // LED 2: Antenna — all neighbors falling long-term
            var validTrends = trends.Where(t => t.PointCount >= 5).ToList();
            if (validTrends.Count > 0)
            {
                float avgLong = validTrends.Average(t => t.LongSlope);
                result.AvgLongSlope = avgLong;
                result.AntennaLed = avgLong < -0.2f ? LedState.Alert
                                  : avgLong < -0.1f ? LedState.Warning
                                  : LedState.Good;
            }
            else
            {
                result.AntennaLed = LedState.NoData;
            }

            // LED 3: Neighbor problem — one neighbor significantly worse than the median
            if (trends.Count >= 2)
            {
                var sortedShort = trends.OrderBy(t => t.ShortSlope).ToList();
                float medianSlope = sortedShort.Count % 2 == 1
                    ? sortedShort[sortedShort.Count / 2].ShortSlope
                    : (sortedShort[sortedShort.Count / 2 - 1].ShortSlope + sortedShort[sortedShort.Count / 2].ShortSlope) / 2f;

                var problemNodes = trends
                    .Where(t => t.ShortSlope < -1.0f && t.ShortSlope < medianSlope - 0.8f)
                    .OrderBy(t => t.ShortSlope)
                    .ToList();
                var problemNode = problemNodes.FirstOrDefault();
                if (problemNode != null)
                {
                    result.ProblemNeighborId   = problemNode.NeighborId;
                    result.ProblemNeighborName = problemNode.NeighborName;
                    // Scale: Alert only when 2+ problem nodes OR >25% of all neighbors
                    result.NeighborLed = problemNodes.Count >= 2 || problemNodes.Count >= trends.Count * 0.25
                        ? LedState.Alert
                        : LedState.Warning;
                }
                else
                {
                    result.NeighborLed = LedState.Good;
                }
            }
            else
            {
                result.NeighborLed = LedState.NoData;
            }
        }

        // LED 4: Path stability from traceroute data
        var (hopCost, hopSamples) = GetHopCost(myNodeId, longDays);
        float routeChangeRate = GetRouteChangeRate(myNodeId, longDays);
        result.HopCost         = hopCost;
        result.RouteChangeRate = routeChangeRate;

        if (hopSamples == 0)
        {
            // Check if any traceroute rows exist (e.g. direct links have no snr_back)
            int traceRows = CountTracerouteRows(myNodeId, longDays);
            if (traceRows == 0)
            {
                result.PathLed = LedState.NoData;
            }
            else
            {
                // Rows exist but no SNR data — use route-change-rate alone
                result.PathLed = routeChangeRate > 2f   ? LedState.Alert
                               : routeChangeRate > 0.5f ? LedState.Warning
                               :                          LedState.Good;
            }
        }
        else
        {
            float pathScore = hopCost * 0.6f + Math.Min(1f, routeChangeRate / 2f) * 0.4f;
            result.PathLed = pathScore > 0.6f ? LedState.Alert
                           : pathScore > 0.3f ? LedState.Warning
                           : LedState.Good;
        }

        return result;
    }

    /// <summary>
    /// Returns the total number of traceroute rows (including direct-link sentinels) for a node.
    /// Used to distinguish "no data" from "data but no SNR".
    /// </summary>
    public int CountTracerouteRows(uint myNodeId, int days)
    {
        long since = Since(days);
        using var con = Open();
        using var cmd = con.CreateCommand();
        if (myNodeId == 0)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM traceroute_hops WHERE timestamp>=$s";
        }
        else
        {
            cmd.CommandText = @"SELECT COUNT(*) FROM traceroute_hops
                WHERE (source_node_id=$me OR dest_node_id=$me) AND timestamp>=$s";
            cmd.Parameters.AddWithValue("$me", (long)myNodeId);
        }
        cmd.Parameters.AddWithValue("$s", since);
        var val = cmd.ExecuteScalar();
        return val != null && val != DBNull.Value ? (int)(long)val : 0;
    }

    /// <summary>
    /// Computes a path cost metric (0=perfect, 1=worst) from traceroute SNR data.
    /// </summary>
    public (float cost, int sampleCount) GetHopCost(uint myNodeId, int days)
    {
        long since = Since(days);

        using var con = Open();
        using var cmd = con.CreateCommand();
        // Only use snr_back since it represents quality in both directions
        // When myNodeId==0, query all hops (global view)
        if (myNodeId == 0)
        {
            cmd.CommandText = @"SELECT snr_back FROM traceroute_hops
                WHERE timestamp>=$s AND snr_back IS NOT NULL";
        }
        else
        {
            cmd.CommandText = @"SELECT snr_back FROM traceroute_hops
                WHERE (source_node_id=$me OR dest_node_id=$me)
                AND timestamp>=$s AND snr_back IS NOT NULL";
            cmd.Parameters.AddWithValue("$me", (long)myNodeId);
        }
        cmd.Parameters.AddWithValue("$s", since);

        var snrs = new List<float>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            snrs.Add((float)r.GetDouble(0));

        if (snrs.Count == 0) return (0f, 0);

        float? median = Median(snrs);
        float? variance = Variance(snrs);
        if (!median.HasValue) return (0f, 0);

        float cost = 0.5f * Math.Max(0f, (10f - median.Value) / 30f)
                   + 0.5f * Math.Min(1f, (variance ?? 0f) / 50f);
        return (Math.Clamp(cost, 0f, 1f), snrs.Count);
    }

    /// <summary>
    /// Computes how often routes change per hour based on traceroute history.
    /// </summary>
    public float GetRouteChangeRate(uint myNodeId, int days)
    {
        long since = Since(days);

        using var con = Open();
        using var cmd = con.CreateCommand();
        // When myNodeId==0, query all hops (global view)
        if (myNodeId == 0)
        {
            cmd.CommandText = @"SELECT request_id, dest_node_id, hop_index, node_id, timestamp
                FROM traceroute_hops
                WHERE timestamp>=$s
                ORDER BY dest_node_id, request_id, hop_index";
        }
        else
        {
            cmd.CommandText = @"SELECT request_id, dest_node_id, hop_index, node_id, timestamp
                FROM traceroute_hops
                WHERE source_node_id=$me AND timestamp>=$s
                ORDER BY dest_node_id, request_id, hop_index";
            cmd.Parameters.AddWithValue("$me", (long)myNodeId);
        }
        cmd.Parameters.AddWithValue("$s", since);

        // Group routes: (request_id, dest) → ordered list of hop node_ids
        var routes = new Dictionary<(long req, long dest), List<uint>>();
        var timestamps = new Dictionary<long, long>();  // request_id → timestamp

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long reqId  = r.GetInt64(0);
            long dest   = r.GetInt64(1);
            var  nid    = (uint)r.GetInt64(3);
            long ts     = r.GetInt64(4);

            var key = (reqId, dest);
            if (!routes.ContainsKey(key)) routes[key] = new List<uint>();
            routes[key].Add(nid);
            timestamps.TryAdd(reqId, ts);
        }

        if (routes.Count < 2) return 0f;

        // Group by dest, sort by request timestamp, count route changes
        int changes = 0;
        var byDest = routes.GroupBy(kv => kv.Key.dest);
        long minTs = long.MaxValue, maxTs = long.MinValue;

        foreach (var group in byDest)
        {
            var ordered = group
                .OrderBy(kv => timestamps.TryGetValue(kv.Key.req, out var t) ? t : 0)
                .ToList();

            for (int i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1].Value;
                var curr = ordered[i].Value;
                if (!prev.SequenceEqual(curr)) changes++;
            }
        }

        // Time span
        if (timestamps.Count > 0)
        {
            minTs = timestamps.Values.Min();
            maxTs = timestamps.Values.Max();
        }

        double hours = maxTs > minTs ? (maxTs - minTs) / 3600.0 : 1.0;
        return (float)(changes / Math.Max(1.0, hours));
    }

    /// <summary>
    /// Computes day/night baseline reception rates and the current short-window rate.
    /// Uses the NOAA solar algorithm (same as all other day/night splits in this service).
    /// </summary>
    /// <param name="days">Baseline window in days.</param>
    /// <param name="currentWindowHours">How many hours back counts as "current" (1–2 recommended).</param>
    public (float dayRxPerHour, float nightRxPerHour, float currentRxPerHour) GetReceptionRate(
        int days, int currentWindowHours = 2)
    {
        long since        = Since(days);
        long currentSince = ToUnix(DateTime.UtcNow.AddHours(-currentWindowHours));

        // Load all packet timestamps in the baseline window
        var allTimestamps = new List<long>();
        using (var con = Open())
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT timestamp FROM packet_rx WHERE timestamp>=$s ORDER BY timestamp";
            cmd.Parameters.AddWithValue("$s", since);
            using var r = cmd.ExecuteReader();
            while (r.Read()) allTimestamps.Add(r.GetInt64(0));
        }

        if (allTimestamps.Count == 0)
            return (0f, 0f, 0f);

        // Determine whether we are currently in daytime or nighttime
        bool isCurrentlyDay = IsDay(ToUnix(DateTime.UtcNow));

        // Count day/night packets in baseline (exclude current window)
        int dayPkts   = allTimestamps.Count(ts => ts < currentSince &&  IsDay(ts));
        int nightPkts = allTimestamps.Count(ts => ts < currentSince && !IsDay(ts));

        // Current window: only count packets matching the current day/night period
        // so we never compare night packets against a day baseline or vice versa
        int currentPkts = allTimestamps.Count(ts => ts >= currentSince && IsDay(ts) == isCurrentlyDay);

        // Count actual day/night hours in baseline window by sampling each hour
        long endTs = ToUnix(DateTime.UtcNow.AddHours(-currentWindowHours)); // exclude current window from baseline
        double dayHours = 0, nightHours = 0;
        for (long h = since; h < endTs; h += 3600)
        {
            if (IsDay(h)) dayHours++;
            else          nightHours++;
        }

        float dayRxPerHour   = dayHours   > 0 ? dayPkts   / (float)dayHours   : 0f;
        float nightRxPerHour = nightHours > 0 ? nightPkts / (float)nightHours : 0f;
        float currentRxPerHour = currentPkts / (float)Math.Max(1, currentWindowHours);

        return (dayRxPerHour, nightRxPerHour, currentRxPerHour);
    }

    /// <summary>
    /// Computes a global mesh health score (0-100) across all nodes with telemetry data.
    /// </summary>
    public MeshHealthScore GetMeshHealthScore(int days)
    {
        long since = Since(days);

        // 1. Reception rate (day/night aware, replaces unreliable ACK rate)
        //    ACK rate is only computed inside firmware and never forwarded via serial/TCP.
        const int rxWindowHours = 2;
        var (dayRxPerHour, nightRxPerHour, currentRxPerHour) = GetReceptionRate(days, rxWindowHours);
        bool  isDay    = IsDay(ToUnix(DateTime.UtcNow));
        float expected = isDay ? dayRxPerHour : nightRxPerHour;
        // Linear: 0 current vs baseline = full penalty, at-baseline = no penalty
        float rxScore   = expected > 0 ? Math.Min(1f, currentRxPerHour / expected) : 1f;
        float rxPenalty = 1f - rxScore;

        // 2. Channel utilization: most-recent value per node (within window), then average
        float channelUtil = 0f;
        var chanUtilPerNode = new List<(uint nodeId, float cu)>();
        using (var con = Open())
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT node_id, channel_utilization
FROM device_telemetry d1
WHERE channel_utilization IS NOT NULL
  AND timestamp >= $since
  AND timestamp = (
      SELECT MAX(timestamp) FROM device_telemetry d2
      WHERE d2.node_id = d1.node_id
        AND d2.channel_utilization IS NOT NULL
        AND d2.timestamp >= $since
  )
GROUP BY node_id";
            cmd.Parameters.AddWithValue("$since", since);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var nid = (uint)r.GetInt64(0);
                var cu  = Math.Min(100f, (float)r.GetDouble(1)); // sanity cap at 100%
                chanUtilPerNode.Add((nid, cu));
            }
        }
        if (chanUtilPerNode.Count > 0)
            channelUtil = chanUtilPerNode.Average(x => x.cu);

        Logger.WriteLine($"[MeshHealth] channelUtil={channelUtil.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}% from {chanUtilPerNode.Count} nodes: [{string.Join(", ", chanUtilPerNode.OrderByDescending(x => x.cu).Select(x => $"!{x.nodeId:x8}={x.cu.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%"))}]");

        // 3. Path cost and route change rate across all nodes with traceroute data
        float avgPathCost = 0f, avgRouteChange = 0f;
        List<uint> activeNodes;
        using (var con = Open())
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT source_node_id FROM traceroute_hops WHERE timestamp>=$s";
            cmd.Parameters.AddWithValue("$s", since);
            activeNodes = new List<uint>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) activeNodes.Add((uint)r.GetInt64(0));
        }

        if (activeNodes.Count > 0)
        {
            var costs   = activeNodes.Select(n => GetHopCost(n, days).cost).ToList();
            var changes = activeNodes.Select(n => GetRouteChangeRate(n, days)).ToList();
            avgPathCost   = costs.Average();
            avgRouteChange = changes.Average();
        }

        // Score: 100 = perfect
        float score = 100f
            - 0.3f * avgPathCost                        * 100f
            - 0.3f * Math.Min(1f, avgRouteChange / 10f) * 100f
            - 0.2f * rxPenalty                          * 100f
            - 0.2f * Math.Min(1f, channelUtil / 25f)    * 100f;

        score = Math.Clamp(score, 0f, 100f);

        var state = score > 70f ? LedState.Good
                  : score > 40f ? LedState.Warning
                  : LedState.Alert;

        string dayNight = isDay ? "Tag" : "Nacht";
        var top5 = chanUtilPerNode.OrderByDescending(x => x.cu).Take(5).ToList();
        var chanUtilDetail = chanUtilPerNode.Count == 0
            ? "–"
            : string.Join("\n  ", top5.Select(x => $"!{x.nodeId:x8} = {x.cu.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%"))
              + (chanUtilPerNode.Count > 5 ? $"\n  … ({chanUtilPerNode.Count} Nodes gesamt)" : $"\n  ({chanUtilPerNode.Count} Nodes gesamt)");
        var summary =
            $"Mesh Health Score: {score:0}%\n" +
            $"Pfad-Kosten (Ø): {avgPathCost:0.00}\n" +
            $"Route-Änderungen: {avgRouteChange:0.00}/h\n" +
            $"Empfangsrate ({dayNight}-Baseline): {expected:0.0} Pkts/h\n" +
            $"Empfangsrate aktuell ({rxWindowHours}h): {currentRxPerHour:0.0} Pkts/h ({rxScore * 100:0}%)\n" +
            $"Kanalauslastung (Ø): {channelUtil:0.1}% [{chanUtilDetail}]";

        return new MeshHealthScore
        {
            Score              = score,
            State              = state,
            AvgPathCost        = avgPathCost,
            RouteChangeRate    = avgRouteChange,
            DayRxPerHour       = dayRxPerHour,
            NightRxPerHour     = nightRxPerHour,
            CurrentRxPerHour   = currentRxPerHour,
            RxScore            = rxScore,
            ChannelUtilization = channelUtil,
            ChannelUtilDetail  = chanUtilDetail,
            Summary            = summary,
        };
    }

    /// <summary>
    /// Returns the most recent rx_snr per node within the given day window.
    /// Used to pre-populate SignalQualityColor on startup from DB history.
    /// </summary>
    public List<uint> GetKnownNodeIds()
    {
        var result = new HashSet<uint>();
        using var con = Open();
        foreach (var table in new[] { "device_telemetry", "environment_telemetry", "packet_rx" })
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"SELECT DISTINCT node_id FROM {table}";
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add((uint)r.GetInt64(0));
        }
        return result.ToList();
    }

    public Dictionary<uint, float> GetLastSnrPerNode(int days)
    {
        long since = Since(days);
        using var con = Open();
        using var cmd = con.CreateCommand();
        // Direct receptions only (hop_count 0; NULL = unknown is treated as local, like the
        // rest of the client) — a relayed packet's rx_snr is the LAST HOP's SNR and must not
        // be restored as the originator's signal quality (it painted far nodes red/green).
        cmd.CommandText = @"
SELECT node_id, rx_snr FROM packet_rx
WHERE timestamp = (
    SELECT MAX(timestamp) FROM packet_rx p2
    WHERE p2.node_id = packet_rx.node_id AND p2.timestamp >= $s AND p2.rx_snr IS NOT NULL
      AND (p2.hop_count = 0 OR p2.hop_count IS NULL)
)
AND timestamp >= $s AND rx_snr IS NOT NULL AND (hop_count = 0 OR hop_count IS NULL)";
        cmd.Parameters.AddWithValue("$s", since);
        var result = new Dictionary<uint, float>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result[(uint)r.GetInt64(0)] = (float)r.GetDouble(1);
        return result;
    }

    /// <summary>
    /// Returns the most recent battery_percent per node within the given day window.
    /// Used to pre-populate BatteryStatusColor on startup from DB history.
    /// </summary>
    public Dictionary<uint, float> GetLastBatteryPerNode(int days)
    {
        long since = Since(days);
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
SELECT node_id, battery_percent FROM device_telemetry
WHERE timestamp = (
    SELECT MAX(timestamp) FROM device_telemetry d2
    WHERE d2.node_id = device_telemetry.node_id AND d2.timestamp >= $s AND d2.battery_percent IS NOT NULL
)
AND timestamp >= $s AND battery_percent IS NOT NULL";
        cmd.Parameters.AddWithValue("$s", since);
        var result = new Dictionary<uint, float>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result[(uint)r.GetInt64(0)] = (float)r.GetDouble(1);
        return result;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        var con = new SqliteConnection(_connectionString);
        con.Open();
        return con;
    }

    /// Returns the most recent 0-hop (direct HF) contact for a node: timestamp + SNR + RSSI.
    public (DateTime? LastSeen, float? Snr, int? Rssi) GetDirectNeighborContact(uint nodeId)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            // SQLite: non-aggregated columns come from the MAX(timestamp) row
            cmd.CommandText =
                "SELECT MAX(timestamp), rx_snr, rx_rssi FROM packet_rx " +
                "WHERE node_id = $n AND hop_count = 0 GROUP BY node_id";
            cmd.Parameters.AddWithValue("$n", (long)nodeId);
            using var r = cmd.ExecuteReader();
            if (!r.Read() || r.IsDBNull(0)) return (null, null, null);
            var ts   = FromUnix(r.GetInt64(0));
            float? snr  = r.IsDBNull(1) ? null : (float)r.GetDouble(1);
            int?   rssi = r.IsDBNull(2) ? null : (int)r.GetInt64(2);
            return (ts, snr, rssi);
        }
    }

    private static long ToUnix(DateTime dt)
        => (long)(dt.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;

    private static DateTime FromUnix(long unix)
        => DateTime.UnixEpoch.AddSeconds(unix).ToLocalTime();

    private long Since(int days)
        => days <= 0 ? 0 : ToUnix(DateTime.UtcNow.AddDays(-days));

    private bool IsDay(long unixTs)
        => SunriseSunsetService.IsDay(DateTime.UnixEpoch.AddSeconds(unixTs), Latitude, Longitude);

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static float? Median(IEnumerable<float> values)
    {
        var arr = values.OrderBy(x => x).ToArray();
        if (arr.Length == 0) return null;
        int mid = arr.Length / 2;
        return arr.Length % 2 == 1 ? arr[mid] : (arr[mid - 1] + arr[mid]) / 2f;
    }

    private static float? Variance(IEnumerable<float> values)
    {
        var arr = values.ToArray();
        if (arr.Length < 2) return null;
        double mean = arr.Average();
        return (float)(arr.Sum(x => (x - mean) * (x - mean)) / (arr.Length - 1));
    }

    private static float? Average(IEnumerable<float> values)
    {
        var arr = values.ToArray();
        return arr.Length > 0 ? (float?)arr.Average() : null;
    }

    /// <summary>
    /// Returns recent TracerouteResult objects reconstructed from the DB.
    /// days=0 means no cutoff. RouteForward is approximated as reversed RouteBack.
    /// </summary>
    public List<Models.TracerouteResult> GetRecentTracerouteResults(int days)
    {
        long cutoff = days > 0 ? ToUnix(DateTime.UtcNow.AddDays(-days)) : 0;
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT request_id, source_node_id, dest_node_id, MAX(timestamp) AS ts
FROM traceroute_hops
WHERE ($cutoff = 0 OR timestamp >= $cutoff) AND hop_index >= 0
GROUP BY request_id, source_node_id, dest_node_id
ORDER BY ts DESC";
            cmd.Parameters.AddWithValue("$cutoff", cutoff);

            var routes = new List<(long reqId, uint src, uint dst, long ts)>();
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    routes.Add((r.GetInt64(0), (uint)r.GetInt64(1), (uint)r.GetInt64(2), r.GetInt64(3)));

            var results = new List<Models.TracerouteResult>();
            foreach (var (reqId, src, dst, ts) in routes)
            {
                using var hcmd = con.CreateCommand();
                hcmd.CommandText = @"
SELECT node_id, snr_towards, snr_back
FROM traceroute_hops
WHERE request_id = $rid AND hop_index >= 0
ORDER BY hop_index ASC";
                hcmd.Parameters.AddWithValue("$rid", reqId);

                var routeBack   = new List<uint>();
                var snrTowards  = new List<int>();
                var snrBack     = new List<int>();
                using (var hr = hcmd.ExecuteReader())
                {
                    while (hr.Read())
                    {
                        routeBack.Add((uint)hr.GetInt64(0));
                        snrTowards.Add(hr.IsDBNull(1) ? int.MinValue : (int)Math.Round(hr.GetDouble(1) * 4));
                        snrBack.Add(hr.IsDBNull(2) ? int.MinValue : (int)Math.Round(hr.GetDouble(2) * 4));
                    }
                }

                results.Add(new Models.TracerouteResult
                {
                    RequestId         = (uint)reqId,
                    SourceNodeId      = src,
                    DestinationNodeId = dst,
                    ReceivedAt        = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime,
                    RouteBack         = routeBack,
                    RouteForward      = Enumerable.Reverse(routeBack).ToList(),
                    SnrTowards        = snrTowards,
                    SnrBack           = snrBack,
                });
            }
            return results;
        }
    }

    /// <summary>
    /// Returns SNR statistics for a segment between two adjacent nodes across all recorded traceroutes.
    /// fromNode is the transmitting node, toNode is the receiving node (snr_towards value at toNode's hop row).
    /// </summary>
    public record SegmentSnrStats(float Min, float Max, float Avg, int Count);

    public SegmentSnrStats? GetSegmentSnrStats(uint fromNode, uint toNode, int days = 30)
    {
        long cutoff = ToUnix(DateTime.UtcNow.AddDays(-days));
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            // Case 1: first hop — source_node_id=fromNode, hop_index=0, node_id=toNode
            // Case 2: relay hop — prior hop node_id=fromNode, this hop node_id=toNode (same request_id, hop_index matches)
            cmd.CommandText = @"
SELECT MIN(snr), MAX(snr), AVG(snr), COUNT(snr) FROM (
    SELECT snr_towards AS snr
    FROM traceroute_hops
    WHERE source_node_id = $from AND node_id = $to AND hop_index = 0
      AND snr_towards IS NOT NULL AND timestamp >= $cutoff
    UNION ALL
    SELECT h.snr_towards AS snr
    FROM traceroute_hops h
    JOIN traceroute_hops prev ON prev.request_id = h.request_id
                              AND prev.hop_index = h.hop_index - 1
                              AND prev.node_id = $from
    WHERE h.node_id = $to AND h.snr_towards IS NOT NULL AND h.timestamp >= $cutoff
)";
            cmd.Parameters.AddWithValue("$from", (long)fromNode);
            cmd.Parameters.AddWithValue("$to",   (long)toNode);
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
            using var r = cmd.ExecuteReader();
            if (!r.Read() || r.IsDBNull(3)) return null;
            int count = r.GetInt32(3);
            if (count == 0) return null;
            return new SegmentSnrStats((float)r.GetDouble(0), (float)r.GetDouble(1), (float)r.GetDouble(2), count);
        }
    }

    // ── Waypoints ─────────────────────────────────────────────────────────────

    public record WaypointEntry(
        uint Id, string Name, string Description,
        double Latitude, double Longitude,
        uint? Expire, uint? LockedTo, uint Icon,
        uint FromNode, DateTime ReceivedAt);

    public void UpsertWaypoint(WaypointEntry wp)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO waypoints (id, name, description, latitude, longitude, expire, locked_to, icon, from_node, received_at)
VALUES ($id, $name, $desc, $lat, $lon, $exp, $lck, $icon, $from, $recv)
ON CONFLICT(id) DO UPDATE SET
    name=$name, description=$desc, latitude=$lat, longitude=$lon,
    expire=$exp, locked_to=$lck, icon=$icon, from_node=$from, received_at=$recv";
            cmd.Parameters.AddWithValue("$id",   (long)wp.Id);
            cmd.Parameters.AddWithValue("$name", wp.Name);
            cmd.Parameters.AddWithValue("$desc", wp.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$lat",  wp.Latitude);
            cmd.Parameters.AddWithValue("$lon",  wp.Longitude);
            cmd.Parameters.AddWithValue("$exp",  wp.Expire.HasValue ? (long)wp.Expire.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$lck",  wp.LockedTo.HasValue ? (long)wp.LockedTo.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$icon", (long)wp.Icon);
            cmd.Parameters.AddWithValue("$from", (long)wp.FromNode);
            cmd.Parameters.AddWithValue("$recv", ToUnix(wp.ReceivedAt));
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteWaypoint(uint waypointId)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM waypoints WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", (long)waypointId);
            cmd.ExecuteNonQuery();
        }
    }

    public List<WaypointEntry> GetAllWaypoints(bool excludeExpired = true)
    {
        long now = ToUnix(DateTime.UtcNow);
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = excludeExpired
                ? "SELECT id,name,description,latitude,longitude,expire,locked_to,icon,from_node,received_at FROM waypoints WHERE expire IS NULL OR expire=0 OR expire>$now ORDER BY received_at DESC"
                : "SELECT id,name,description,latitude,longitude,expire,locked_to,icon,from_node,received_at FROM waypoints ORDER BY received_at DESC";
            cmd.Parameters.AddWithValue("$now", now);
            var list = new List<WaypointEntry>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new WaypointEntry(
                    Id:          (uint)r.GetInt64(0),
                    Name:        r.GetString(1),
                    Description: r.IsDBNull(2) ? string.Empty : r.GetString(2),
                    Latitude:    r.GetDouble(3),
                    Longitude:   r.GetDouble(4),
                    Expire:      r.IsDBNull(5) ? null : (uint?)r.GetInt64(5),
                    LockedTo:    r.IsDBNull(6) ? null : (uint?)r.GetInt64(6),
                    Icon:        r.IsDBNull(7) ? 0u : (uint)r.GetInt64(7),
                    FromNode:    (uint)r.GetInt64(8),
                    ReceivedAt:  DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(9)).LocalDateTime
                ));
            }
            return list;
        }
    }

    public record SegmentSnrPoint(DateTime Timestamp, float Snr);

    /// <summary>
    /// Returns the SNR time series for a segment (fromNode→toNode) over the last <paramref name="days"/> days.
    /// </summary>
    public List<SegmentSnrPoint> GetSegmentSnrTimeSeries(uint fromNode, uint toNode, int days = 30)
    {
        long cutoff = ToUnix(DateTime.UtcNow.AddDays(-days));
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT ts, snr FROM (
    SELECT timestamp AS ts, snr_towards AS snr
    FROM traceroute_hops
    WHERE source_node_id = $from AND node_id = $to AND hop_index = 0
      AND snr_towards IS NOT NULL AND timestamp >= $cutoff
    UNION ALL
    SELECT h.timestamp AS ts, h.snr_towards AS snr
    FROM traceroute_hops h
    JOIN traceroute_hops prev ON prev.request_id = h.request_id
                              AND prev.hop_index = h.hop_index - 1
                              AND prev.node_id = $from
    WHERE h.node_id = $to AND h.snr_towards IS NOT NULL AND h.timestamp >= $cutoff
) ORDER BY ts ASC";
            cmd.Parameters.AddWithValue("$from", (long)fromNode);
            cmd.Parameters.AddWithValue("$to",   (long)toNode);
            cmd.Parameters.AddWithValue("$cutoff", cutoff);

            var list = new List<SegmentSnrPoint>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)).LocalDateTime;
                list.Add(new SegmentSnrPoint(dt, (float)r.GetDouble(1)));
            }
            return list;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SqliteConnection.ClearAllPools();
    }
}
