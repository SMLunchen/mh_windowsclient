using System.IO;
using Meshtastic.Protobufs;
using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

public class EnvironmentMetricInfoTests
{
    [Fact]
    public void Registry_HasCoreAndExtendedMetrics()
    {
        Assert.NotNull(EnvironmentMetricInfo.ByKey("temperature"));
        Assert.NotNull(EnvironmentMetricInfo.ByKey("iaq"));
        Assert.NotNull(EnvironmentMetricInfo.ByKey("gas"));          // extended sensor set
        Assert.NotNull(EnvironmentMetricInfo.ByKey("wind_speed"));
        Assert.Null(EnvironmentMetricInfo.ByKey("does_not_exist"));
    }

    [Fact]
    public void Columns_MatchRegistryOneToOne()
    {
        Assert.Equal(EnvironmentMetricInfo.All.Count, EnvironmentMetricInfo.Columns.Count);
        Assert.Contains("gas_resistance", EnvironmentMetricInfo.Columns);
        Assert.True(EnvironmentMetricInfo.IsKnownColumn("relative_humidity"));
        Assert.False(EnvironmentMetricInfo.IsKnownColumn("node_id"));
    }

    [Fact]
    public void Format_AppendsUnit()
    {
        Assert.StartsWith("21", EnvironmentMetricInfo.Format("temperature", 21.4));
        Assert.EndsWith("°C", EnvironmentMetricInfo.Format("temperature", 21.4));
        Assert.DoesNotContain("°", EnvironmentMetricInfo.Format("iaq", 42));   // iaq is unitless
    }

    [Fact]
    public void Bands_CoverRangeAtStep()
    {
        var temp = EnvironmentMetricInfo.ByKey("temperature")!;   // -10..40 step 5
        var bands = EnvironmentMetricInfo.Bands(temp);
        Assert.Equal(10, bands.Count);                            // (40 - -10)/5
    }

    [Fact]
    public void RgbAt_EndsMatchGradient()
    {
        Assert.Equal(EnvironmentMetricInfo.Gradient[0].rgb,  EnvironmentMetricInfo.RgbAt(0.0));
        Assert.Equal(EnvironmentMetricInfo.Gradient[^1].rgb, EnvironmentMetricInfo.RgbAt(1.0));
    }

    [Fact]
    public void Extract_TakesPresentFields_SkipsZeros()
    {
        var em = new EnvironmentMetrics { Temperature = 21.5f, RelativeHumidity = 60f, Iaq = 42, GasResistance = 12.3f };
        var d = EnvironmentMetricInfo.Extract(em);

        Assert.Equal(21.5, d["temperature"], 3);
        Assert.Equal(60.0, d["humidity"], 3);
        Assert.Equal(42.0, d["iaq"], 3);
        Assert.Equal(12.3, d["gas"], 3);
        Assert.False(d.ContainsKey("pressure"));   // 0 = not reported
    }
}

public class EnvironmentHeatmapBuilderTests
{
    [Fact]
    public void NoSamples_ReturnsNull()
    {
        Assert.Null(EnvironmentHeatmapBuilder.Representative(new List<(double, double)>()));
    }

    [Fact]
    public void FreshReading_HasFullRecency_AndRealValue()
    {
        var r = EnvironmentHeatmapBuilder.Representative(new[] { (0.0, 25.0) });
        Assert.NotNull(r);
        Assert.Equal(25.0, r!.Value.Value, 3);       // value is NOT normalised
        Assert.True(r.Value.Recency > 0.99, $"recency was {r.Value.Recency}");
    }

    [Fact]
    public void RecentReadingsDominateValue()
    {
        // recent 30 (age 0), old 10 (age 24h): weighted value pulls toward the recent one,
        // i.e. above the plain average of 20.
        var r = EnvironmentHeatmapBuilder.Representative(new[] { (0.0, 30.0), (24.0, 10.0) });
        Assert.NotNull(r);
        Assert.True(r!.Value.Value > 20.0, $"value was {r.Value.Value}");
    }

    [Fact]
    public void StaleNode_LosesRecency()
    {
        var fresh = EnvironmentHeatmapBuilder.Representative(new[] { (0.0, 25.0) })!.Value.Recency;
        var stale = EnvironmentHeatmapBuilder.Representative(new[] { (48.0, 25.0) })!.Value.Recency;
        Assert.True(stale < fresh);
        Assert.True(stale < 0.5, $"stale recency was {stale}");
    }
}

public class EnvironmentZonesTests
{
    [Fact]
    public void SingleBand_AllCellsGetZoneAverage()
    {
        var g = new EnvironmentField.Grid
        {
            Nx = 4, Ny = 2, Lon0 = 8, Lat0 = 50, DLon = 0.1, DLat = 0.1,
            Values = Enumerable.Repeat(12.0, 8).ToArray(),   // all in band [10,15)
            Alpha = Enumerable.Repeat(1.0, 8).ToArray(),
        };
        var avg = EnvironmentZones.ZoneAverages(g, -10, 5, 10);
        Assert.All(avg, a => Assert.Equal(12.0, a, 3));
    }

    [Fact]
    public void TwoBands_EachCellGetsItsZoneAverage()
    {
        var vals = new double[8];
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 4; i++)
                vals[j * 4 + i] = i < 2 ? 12.0 : 22.0;   // left band 12, right band 22
        var g = new EnvironmentField.Grid
        {
            Nx = 4, Ny = 2, Lon0 = 8, Lat0 = 50, DLon = 0.1, DLat = 0.1,
            Values = vals, Alpha = Enumerable.Repeat(1.0, 8).ToArray(),
        };
        var avg = EnvironmentZones.ZoneAverages(g, -10, 5, 10);
        Assert.Equal(12.0, avg[0], 3);   // left cell
        Assert.Equal(22.0, avg[3], 3);   // right cell
    }
}

public class EnvironmentFieldTests
{
    private static EnvironmentField.Grid.Cell Nearest(EnvironmentField.Grid g, double lon, double lat)
    {
        double best = double.MaxValue; int bi = 0, bj = 0;
        for (int j = 0; j < g.Ny; j++)
            for (int i = 0; i < g.Nx; i++)
            {
                double cl = g.Lon0 + (i + 0.5) * g.DLon, ca = g.Lat0 + (j + 0.5) * g.DLat;
                double d = (cl - lon) * (cl - lon) + (ca - lat) * (ca - lat);
                if (d < best) { best = d; bi = i; bj = j; }
            }
        int idx = bj * g.Nx + bi;
        return new EnvironmentField.Grid.Cell(g.Values[idx], g.Alpha[idx]);
    }

    [Fact]
    public void NoSensors_ReturnsNull()
    {
        Assert.Null(EnvironmentField.Build(new List<EnvironmentField.Sensor>(), 30));
    }

    [Fact]
    public void InterpolatedValues_StayWithinSensorRange()
    {
        var sensors = new[]
        {
            new EnvironmentField.Sensor(8.0, 50.0, 30.0, 1.0),   // hot
            new EnvironmentField.Sensor(8.3, 50.0, 10.0, 1.0),   // cold, ~21 km east
        };
        var g = EnvironmentField.Build(sensors, 30);
        Assert.NotNull(g);

        foreach (var v in g!.Values)
            if (!double.IsNaN(v))
                Assert.True(v >= 10.0 - 1e-6 && v <= 30.0 + 1e-6, $"value out of range: {v}");   // IDW weighted average
    }

    [Fact]
    public void Contours_EmitSegmentWhereLevelCrossed()
    {
        // 2x2 lattice, only the NE corner above the level → exactly one contour segment.
        var g = new EnvironmentField.Grid
        {
            Nx = 2, Ny = 2, Lon0 = 8.0, Lat0 = 50.0, DLon = 0.1, DLat = 0.1,
            Values = new double[] { 0, 0, 0, 20 },   // row-major j*Nx+i; index 3 = NE
            Alpha  = new double[] { 1, 1, 1, 1 },
        };
        var segs = EnvironmentContours.Build(g, new[] { 10.0 });
        Assert.Single(segs);
        Assert.Equal(4, segs[0].Length);   // [lon1,lat1,lon2,lat2]
    }

    [Fact]
    public void CellAtSensor_TakesThatSensorsValue()
    {
        var sensors = new[]
        {
            new EnvironmentField.Sensor(8.0, 50.0, 30.0, 1.0),
            new EnvironmentField.Sensor(8.3, 50.0, 10.0, 1.0),
        };
        var g = EnvironmentField.Build(sensors, 30)!;
        var hot = Nearest(g, 8.0, 50.0);
        Assert.True(hot.Value > 29.0, $"value at hot sensor was {hot.Value}");
        Assert.True(hot.Alpha > 0.0);
    }
}

public class EnvironmentDbRoundTripTests
{
    [Fact]
    public void InsertAndReadBack_FullSensorSet()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mh_envtest_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new TelemetryDatabaseService(dbPath))
            {
                db.InsertEnvironmentTelemetry(123u, DateTime.UtcNow, new Dictionary<string, double>
                {
                    ["temperature"] = 21.5,
                    ["humidity"]    = 55,
                    ["gas"]         = 12.3,   // extended column added by migration
                    ["wind_speed"]  = 3.2,
                });

                var latest = db.GetLatestEnvironmentPerNode();
                var e = Assert.Single(latest);
                Assert.Equal(123u, e.NodeId);
                Assert.Equal(21.5, e.Values["temperature"], 3);
                Assert.Equal(12.3, e.Values["gas"], 3);
                Assert.True(e.Values.ContainsKey("wind_speed"));
                Assert.False(e.Values.ContainsKey("pressure"));    // never inserted

                var series = db.GetTimeSeries(new[] { 123u }, "gas", 1);
                Assert.Single(series);
                Assert.Equal(12.3, series[0].Value, 3);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort cleanup */ }
        }
    }
}
