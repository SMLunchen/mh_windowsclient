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
";
        cmd.ExecuteNonQuery();
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

    public void InsertEnvironmentTelemetry(uint nodeId, DateTime timestamp,
        float temp, float humidity, float pressure, int iaq)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO environment_telemetry (node_id, timestamp, temperature, relative_humidity, barometric_pressure, iaq)
VALUES ($n, $ts, $t, $h, $p, $q)";
            cmd.Parameters.AddWithValue("$n",  (long)nodeId);
            cmd.Parameters.AddWithValue("$ts", ToUnix(timestamp));
            cmd.Parameters.AddWithValue("$t",  temp != 0 ? temp : DBNull.Value);
            cmd.Parameters.AddWithValue("$h",  humidity > 0 ? humidity : DBNull.Value);
            cmd.Parameters.AddWithValue("$p",  pressure > 0 ? pressure : DBNull.Value);
            cmd.Parameters.AddWithValue("$q",  iaq > 0 ? iaq : DBNull.Value);
            cmd.ExecuteNonQuery();
        }
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
    /// Available metrics: snr, rssi, battery, voltage, channel_util, air_tx_util, hop_count
    /// </summary>
    public List<TimeSeriesPoint> GetTimeSeries(IEnumerable<uint> nodeIds, string metric, int days)
    {
        long since = Since(days);
        var result = new List<TimeSeriesPoint>();

        (string table, string col) = metric.ToLowerInvariant() switch
        {
            "snr"          => ("packet_rx",         "rx_snr"),
            "rssi"         => ("packet_rx",         "rx_rssi"),
            "hop_count"    => ("packet_rx",         "hop_count"),
            "battery"      => ("device_telemetry",  "battery_percent"),
            "voltage"      => ("device_telemetry",  "voltage"),
            "channel_util" => ("device_telemetry",  "channel_utilization"),
            "air_tx_util"  => ("device_telemetry",  "air_util_tx"),
            _              => ("packet_rx",         "rx_snr"),
        };

        using var con = Open();
        foreach (var nodeId in nodeIds)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"SELECT timestamp, {col} FROM {table} WHERE node_id=$n AND timestamp>=$s AND {col} IS NOT NULL ORDER BY timestamp";
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
    private int CountTracerouteRows(uint myNodeId, int days)
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

        // 2. Channel utilization (average across all nodes)
        float channelUtil = 0f;
        using (var con = Open())
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT AVG(channel_utilization) FROM device_telemetry WHERE timestamp>=$s AND channel_utilization IS NOT NULL";
            cmd.Parameters.AddWithValue("$s", since);
            var val = cmd.ExecuteScalar();
            if (val != null && val != DBNull.Value) channelUtil = (float)(double)val;
        }

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
        var summary =
            $"Mesh Health Score: {score:0}%\n" +
            $"Pfad-Kosten (Ø): {avgPathCost:0.00}\n" +
            $"Route-Änderungen: {avgRouteChange:0.00}/h\n" +
            $"Empfangsrate ({dayNight}-Baseline): {expected:0.0} Pkts/h\n" +
            $"Empfangsrate aktuell ({rxWindowHours}h): {currentRxPerHour:0.0} Pkts/h ({rxScore * 100:0}%)\n" +
            $"Kanalauslastung (Ø): {channelUtil:0.1}%";

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
            Summary            = summary,
        };
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        var con = new SqliteConnection(_connectionString);
        con.Open();
        return con;
    }

    private static long ToUnix(DateTime dt)
        => (long)(dt.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;

    private static DateTime FromUnix(long unix)
        => DateTime.UnixEpoch.AddSeconds(unix).ToLocalTime();

    private long Since(int days)
        => days <= 0 ? 0 : ToUnix(DateTime.UtcNow.AddDays(-days));

    private bool IsDay(long unixTs)
        => SunriseSunsetService.IsDay(DateTime.UnixEpoch.AddSeconds(unixTs), Latitude, Longitude);

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SqliteConnection.ClearAllPools();
    }
}
