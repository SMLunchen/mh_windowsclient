using Meshtastic.Protobufs;

namespace MeshhessenClient.Services;

/// <summary>
/// Single source of truth for the environmental metrics the client understands.
/// Drives: the DB column set (schema + insert), the map info-boxes, the heatmap
/// weighting/normalisation, the metric selector and i18n labels.
///
/// <para><b>Key</b> is the app-wide metric id (settings, JS, queries). <b>Column</b> is
/// the SQLite column in <c>environment_telemetry</c>. <b>LabelKey</b>/<c>+"Unit"</c> are
/// i18n resource keys. <b>HeatMin/HeatMax</b> normalise a value to 0..1 for the heatmap
/// weight (so each metric gets a sensible spread irrespective of its native range).</para>
/// </summary>
public sealed record EnvMetric(
    string Key,
    string Column,
    string LabelKey,
    string Unit,
    int Decimals,
    double HeatMin,
    double HeatMax,
    double Step);   // legend/band step for the discrete weather-style scale

public static class EnvironmentMetricInfo
{
    // Order = display order in the metric selector. temperature first (default heatmap metric).
    public static readonly IReadOnlyList<EnvMetric> All = new[]
    {
        new EnvMetric("temperature",      "temperature",         "StrEnvTemperature",     "°C",  1,  -10,   40,     5),
        new EnvMetric("humidity",         "relative_humidity",   "StrEnvHumidity",        "%",   0,    0,  100,    10),
        new EnvMetric("pressure",         "barometric_pressure", "StrEnvPressure",        "hPa", 0,  970, 1040,    10),
        new EnvMetric("iaq",              "iaq",                 "StrEnvIaq",             "",    0,    0,  500,    50),
        new EnvMetric("gas",              "gas_resistance",      "StrEnvGas",             "MΩ",  2,    0,   50,     5),
        new EnvMetric("lux",              "lux",                 "StrEnvLux",             "lx",  0,    0, 50000, 10000),
        new EnvMetric("white_lux",        "white_lux",           "StrEnvWhiteLux",        "lx",  0,    0, 50000, 10000),
        new EnvMetric("uv_lux",           "uv_lux",              "StrEnvUvLux",           "lx",  0,    0, 10000,  2000),
        new EnvMetric("wind_speed",       "wind_speed",          "StrEnvWindSpeed",       "m/s", 1,    0,   30,     5),
        new EnvMetric("wind_direction",   "wind_direction",      "StrEnvWindDir",         "°",   0,    0,  360,    45),
        new EnvMetric("wind_gust",        "wind_gust",           "StrEnvWindGust",        "m/s", 1,    0,   40,     5),
        new EnvMetric("rain_1h",          "rainfall_1h",         "StrEnvRain1h",          "mm",  1,    0,   20,     4),
        new EnvMetric("rain_24h",         "rainfall_24h",        "StrEnvRain24h",         "mm",  1,    0,   80,    10),
        new EnvMetric("soil_moisture",    "soil_moisture",       "StrEnvSoilMoisture",    "%",   0,    0,  100,    10),
        new EnvMetric("soil_temperature", "soil_temperature",    "StrEnvSoilTemp",        "°C",  1,  -10,   40,     5),
        new EnvMetric("radiation",        "radiation",           "StrEnvRadiation",       "µR/h",1,    0,  100,    20),
        new EnvMetric("distance",         "distance",            "StrEnvDistance",        "mm",  0,    0, 5000,  1000),
        new EnvMetric("weight",           "weight",              "StrEnvWeight",          "kg",  2,    0,  200,    25),
    };

    private static readonly Dictionary<string, EnvMetric> _byKey =
        All.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>All DB columns for the metrics, in registry order (for schema/insert/select).</summary>
    public static IReadOnlyList<string> Columns { get; } = All.Select(m => m.Column).ToList();

    public static EnvMetric? ByKey(string? key) =>
        key != null && _byKey.TryGetValue(key, out var m) ? m : null;

    /// <summary>Calibrated colour gradient, mapped onto each metric's [HeatMin,HeatMax] so the
    /// colour encodes the real value. Saturated across the whole range (blue→cyan→green→yellow→
    /// orange→red) — unlike a diverging RdYlBu it has no near-white centre, so mid values stay
    /// clearly visible over the map. Normalised position t=0..1 → "r,g,b".</summary>
    public static readonly (double t, string rgb)[] Gradient =
    {
        (0.00, "63,90,204"),   (0.14, "58,150,232"),  (0.28, "62,196,220"),
        (0.42, "96,201,128"),  (0.54, "175,214,78"),  (0.66, "244,221,74"),
        (0.78, "245,157,52"),  (0.90, "230,88,42"),   (1.00, "176,30,30"),
    };

    /// <summary>Interpolated gradient colour "r,g,b" at normalised position t (0..1).</summary>
    public static string RgbAt(double t)
    {
        t = Math.Clamp(t, 0, 1);
        for (int i = 1; i < Gradient.Length; i++)
        {
            if (t > Gradient[i].t) continue;
            var (t0, c0) = Gradient[i - 1];
            var (t1, c1) = Gradient[i];
            double f = t1 > t0 ? (t - t0) / (t1 - t0) : 0;
            var a = c0.Split(','); var b = c1.Split(',');
            int Mix(int k) => (int)Math.Round(int.Parse(a[k]) + f * (int.Parse(b[k]) - int.Parse(a[k])));
            return $"{Mix(0)},{Mix(1)},{Mix(2)}";
        }
        return Gradient[^1].rgb;
    }

    public readonly record struct BandInfo(double Lo, double Hi, string Rgb);

    /// <summary>Discrete colour bands [lo,hi) at the metric's Step — the weather-service scale.</summary>
    public static List<BandInfo> BandList(EnvMetric m)
    {
        double span = m.HeatMax - m.HeatMin;
        int n = Math.Max(1, (int)Math.Round(span / m.Step));
        var bands = new List<BandInfo>(n);
        for (int k = 0; k < n; k++)
            bands.Add(new BandInfo(Math.Round(m.HeatMin + k * m.Step, 3),
                                   Math.Round(m.HeatMin + (k + 1) * m.Step, 3),
                                   RgbAt((k + 0.5) / n)));
        return bands;
    }

    /// <summary>Same bands as JSON-friendly objects for the MapLibre fill + legend.</summary>
    public static List<object> Bands(EnvMetric m) =>
        BandList(m).Select(b => (object)new { lo = b.Lo, hi = b.Hi, rgb = b.Rgb }).ToList();

    public static bool IsKnownColumn(string column) =>
        All.Any(m => string.Equals(m.Column, column, StringComparison.OrdinalIgnoreCase));

    /// <summary>Format a raw value for display, e.g. 21.4 → "21.4 °C". Uses invariant-ish
    /// decimals; unit appended with a thin space (none when unitless).</summary>
    public static string Format(string key, double value)
    {
        var m = ByKey(key);
        if (m == null) return value.ToString("0.#");
        var num = value.ToString("F" + m.Decimals, System.Globalization.CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(m.Unit) ? num : $"{num} {m.Unit}";
    }

    /// <summary>Extracts every present (non-zero) environmental metric from a decoded
    /// <see cref="EnvironmentMetrics"/> payload, keyed by metric Key. Zero is treated as
    /// "not reported" (matches the firmware/proto convention of unset numeric fields).</summary>
    public static Dictionary<string, double> Extract(EnvironmentMetrics em)
    {
        var d = new Dictionary<string, double>();
        void Add(string key, double v) { if (v != 0) d[key] = v; }

        Add("temperature",      em.Temperature);
        Add("humidity",         em.RelativeHumidity);
        Add("pressure",         em.BarometricPressure);
        Add("iaq",              em.Iaq);
        Add("gas",              em.GasResistance);
        Add("lux",              em.Lux);
        Add("white_lux",        em.WhiteLux);
        Add("uv_lux",           em.UvLux);
        Add("wind_speed",       em.WindSpeed);
        Add("wind_direction",   em.WindDirection);
        Add("wind_gust",        em.WindGust);
        Add("rain_1h",          em.Rainfall1H);
        Add("rain_24h",         em.Rainfall24H);
        Add("soil_moisture",    em.SoilMoisture);
        Add("soil_temperature", em.SoilTemperature);
        Add("radiation",        em.Radiation);
        Add("distance",         em.Distance);
        Add("weight",           em.Weight);
        return d;
    }
}
