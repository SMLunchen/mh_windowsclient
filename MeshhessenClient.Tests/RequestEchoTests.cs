using Google.Protobuf;
using MeshhessenClient.Models;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;

namespace MeshhessenClient.Tests;

// Funk-Echo: classification of directed requests aimed at our node, plus the
// journal's burst aggregation in the telemetry DB.
public class RequestEchoTests : IDisposable
{
    private const uint Me = 0xAABBCCDD;

    private static MeshPacket Packet(uint from, uint to, int port, bool wantResponse,
        uint requestId = 0, byte hopStart = 3, byte hopLimit = 3, bool viaMqtt = false,
        ByteString? payload = null)
        => new()
        {
            From = from,
            To = to,
            Id = 42,
            HopStart = hopStart,
            HopLimit = hopLimit,
            ViaMqtt = viaMqtt,
            Decoded = new Data
            {
                Portnum = (PortNum)port,
                WantResponse = wantResponse,
                RequestId = requestId,
                Payload = payload ?? ByteString.Empty,
            },
        };

    [Theory]
    [InlineData(4,  RequestEchoType.NodeInfo)]
    [InlineData(3,  RequestEchoType.Position)]
    [InlineData(67, RequestEchoType.Telemetry)]
    [InlineData(70, RequestEchoType.Traceroute)]
    public void Directed_want_response_requests_classify(int port, RequestEchoType expected)
    {
        var result = RequestEchoClassifier.Classify(Me, Packet(0x111, Me, port, wantResponse: true));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Admin_packets_count_even_without_want_response()
    {
        Assert.Equal(RequestEchoType.Admin,
            RequestEchoClassifier.Classify(Me, Packet(0x111, Me, 6, wantResponse: false)));
    }

    [Fact]
    public void Broadcasts_and_overheard_packets_are_ignored()
    {
        // Periodic NodeInfo broadcast
        Assert.Null(RequestEchoClassifier.Classify(Me, Packet(0x111, 0xFFFFFFFF, 4, true)));
        // Overheard request addressed to someone else
        Assert.Null(RequestEchoClassifier.Classify(Me, Packet(0x111, 0x222, 4, true)));
    }

    [Fact]
    public void Replies_to_our_own_requests_are_ignored()
    {
        // A reply carries request_id — e.g. the NodeInfo answer to OUR request
        Assert.Null(RequestEchoClassifier.Classify(Me, Packet(0x111, Me, 4, false, requestId: 99)));
        // Even an admin response to our remote-admin session is not an incoming request
        Assert.Null(RequestEchoClassifier.Classify(Me, Packet(0x111, Me, 6, false, requestId: 99)));
    }

    [Fact]
    public void Plain_packets_without_want_response_are_ignored()
    {
        Assert.Null(RequestEchoClassifier.Classify(Me, Packet(0x111, Me, 4, wantResponse: false)));
        Assert.Null(RequestEchoClassifier.Classify(Me, Packet(0x111, Me, 1, wantResponse: true))); // text DM
    }

    [Theory]
    [InlineData("Device")]
    [InlineData("Environment")]
    [InlineData("Power")]
    [InlineData("AirQuality")]
    [InlineData("LocalStats")]
    [InlineData("Host")]
    public void Telemetry_variant_is_extracted_from_request_payload(string variant)
    {
        var t = new Telemetry();
        switch (variant)
        {
            case "Device":      t.DeviceMetrics      = new DeviceMetrics();      break;
            case "Environment": t.EnvironmentMetrics = new EnvironmentMetrics(); break;
            case "Power":       t.PowerMetrics       = new PowerMetrics();       break;
            case "AirQuality":  t.AirQualityMetrics  = new AirQualityMetrics();  break;
            case "LocalStats":  t.LocalStats         = new LocalStats();         break;
            case "Host":        t.HostMetrics        = new HostMetrics();        break;
        }
        var pkt = Packet(0x111, Me, 67, true, payload: t.ToByteString());
        Assert.Equal(RequestEchoType.Telemetry, RequestEchoClassifier.Classify(Me, pkt));
        Assert.Equal(variant, RequestEchoClassifier.ExtractTelemetryVariant(pkt));
    }

    [Fact]
    public void Telemetry_variant_null_for_empty_or_foreign_payload()
    {
        Assert.Null(RequestEchoClassifier.ExtractTelemetryVariant(Packet(0x111, Me, 67, true)));
        Assert.Null(RequestEchoClassifier.ExtractTelemetryVariant(Packet(0x111, Me, 4, true)));
    }

    [Fact]
    public void Traceroute_route_towards_us_is_extracted()
    {
        var disc = new RouteDiscovery();
        disc.Route.Add(0x10u);
        disc.Route.Add(0x20u);
        var pkt = Packet(0x111, Me, 70, true, payload: disc.ToByteString());
        var route = RequestEchoClassifier.ExtractRoute(pkt);
        Assert.NotNull(route);
        Assert.Equal(new List<uint> { 0x10, 0x20 }, route);
    }

    // ── DB burst aggregation ──────────────────────────────────────────────────

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"reqecho_test_{Guid.NewGuid():N}.db");
    private TelemetryDatabaseService? _db;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void Upsert_aggregates_bursts_within_window_and_splits_outside()
    {
        _db = new TelemetryDatabaseService(_dbPath);
        var t0 = DateTime.UtcNow.AddMinutes(-30);

        Assert.Equal(1, _db.UpsertRequestEcho(0x111, "Telemetry", t0, 0, false, null));
        Assert.Equal(2, _db.UpsertRequestEcho(0x111, "Telemetry", t0.AddMinutes(5), 0, false, null));
        // Same node, different type → own row
        Assert.Equal(1, _db.UpsertRequestEcho(0x111, "Position", t0.AddMinutes(5), 0, false, null));
        // Same type but outside the 10-min window → new burst
        Assert.Equal(1, _db.UpsertRequestEcho(0x111, "Telemetry", t0.AddMinutes(20), 0, false, null));

        var journal = _db.GetRequestEchoJournal(t0.AddMinutes(-1));
        Assert.Equal(3, journal.Count);
        Assert.Equal(2, journal.Single(r => r.Type == "Telemetry" && r.Count > 1).Count);

        var stats = _db.GetRequestEchoStats(1);
        Assert.Equal(4, stats.TotalRequests);        // counts, not rows
        Assert.Equal(1, stats.Stations);
        Assert.Equal(3, stats.PerType["Telemetry"]);
        Assert.Equal(1, stats.PerType["Position"]);
        Assert.Equal(0x111u, stats.TopNodes[0].NodeId);
    }

    [Theory]
    // NodeInfo/Traceroute: modules always answer
    [InlineData(RequestEchoType.NodeInfo,   null,          true,  32u, "yes")]
    [InlineData(RequestEchoType.Traceroute, null,          false, 0u,  "yes")]
    // Position: depends on own position + channel precision
    [InlineData(RequestEchoType.Position,   null,          false, 32u, "no-pos")]
    [InlineData(RequestEchoType.Position,   null,          true,  32u, "yes")]
    [InlineData(RequestEchoType.Position,   null,          true,  0u,  "yes")]      // 0 = precision off → full
    [InlineData(RequestEchoType.Position,   null,          true,  13u, "prec:13")]
    // Telemetry: device/local stats always, sensors unknown
    [InlineData(RequestEchoType.Telemetry,  "Device",      true,  32u, "yes")]
    [InlineData(RequestEchoType.Telemetry,  "LocalStats",  true,  32u, "yes")]
    [InlineData(RequestEchoType.Telemetry,  "Environment", true,  32u, "sensor")]
    [InlineData(RequestEchoType.Telemetry,  null,          true,  32u, "sensor")]
    // Admin: authorization-gated
    [InlineData(RequestEchoType.Admin,      null,          true,  32u, "auth")]
    public void Expectation_is_derived_from_local_state(RequestEchoType type, string? detail,
        bool hasPos, uint precision, string expected)
    {
        Assert.Equal(expected, RequestEchoExpectation.Compute(type, detail, hasPos, precision));
    }

    [Fact]
    public void FunkEcho_time_series_buckets_requests_per_hour()
    {
        _db = new TelemetryDatabaseService(_dbPath);
        var hour = new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc);

        _db.UpsertRequestEcho(0x111, "Telemetry", hour.AddMinutes(5), 0, false, null);
        _db.UpsertRequestEcho(0x111, "Telemetry", hour.AddMinutes(8), 0, false, null);   // same burst → count 2
        _db.UpsertRequestEcho(0x111, "Position",  hour.AddMinutes(10), 0, false, null);
        _db.UpsertRequestEcho(0x111, "NodeInfo",  hour.AddHours(2), 0, false, null);

        var series = _db.GetTimeSeries(new[] { 0x111u }, "funk_echo", 0)
                        .OrderBy(p => p.Timestamp).ToList();
        Assert.Equal(2, series.Count);
        Assert.Equal(3, series[0].Value);                                   // 2 + 1 in the first hour
        Assert.Equal(1, series[1].Value);
        Assert.Equal(2, (series[1].Timestamp - series[0].Timestamp).TotalHours);
    }

    [Fact]
    public void Stats_break_telemetry_down_by_detail()
    {
        _db = new TelemetryDatabaseService(_dbPath);
        var t0 = DateTime.UtcNow.AddHours(-1);
        _db.UpsertRequestEcho(0x111, "Telemetry", t0, 0, false, null, "Environment");
        _db.UpsertRequestEcho(0x222, "Telemetry", t0, 0, false, null, "Environment");
        _db.UpsertRequestEcho(0x111, "Telemetry", t0, 0, false, null, "Power");
        _db.UpsertRequestEcho(0x111, "Position",  t0, 0, false, null);

        var stats = _db.GetRequestEchoStats(1);
        Assert.Equal(3, stats.PerType["Telemetry"]);
        Assert.Equal(2, stats.TelemetryDetails["Environment"]);
        Assert.Equal(1, stats.TelemetryDetails["Power"]);
        Assert.False(stats.TelemetryDetails.ContainsKey("Position"));
    }

    [Fact]
    public void Upsert_keeps_different_telemetry_variants_as_separate_bursts()
    {
        _db = new TelemetryDatabaseService(_dbPath);
        var t0 = DateTime.UtcNow.AddMinutes(-5);

        Assert.Equal(1, _db.UpsertRequestEcho(0x111, "Telemetry", t0, 0, false, null, "Environment"));
        Assert.Equal(1, _db.UpsertRequestEcho(0x111, "Telemetry", t0.AddMinutes(1), 0, false, null, "Power"));
        Assert.Equal(2, _db.UpsertRequestEcho(0x111, "Telemetry", t0.AddMinutes(2), 0, false, null, "Environment"));

        var journal = _db.GetRequestEchoJournal(t0.AddMinutes(-1));
        Assert.Equal(2, journal.Count);
        Assert.Equal(2, journal.Single(r => r.Detail == "Environment").Count);
        Assert.Equal(1, journal.Single(r => r.Detail == "Power").Count);
    }
}
