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
    double HeatMax);

public static class EnvironmentMetricInfo
{
    // Order = display order in the metric selector. temperature first (default heatmap metric).
    public static readonly IReadOnlyList<EnvMetric> All = new[]
    {
        new EnvMetric("temperature",      "temperature",         "StrEnvTemperature",     "°C",  1,  -10,   40),
        new EnvMetric("humidity",         "relative_humidity",   "StrEnvHumidity",        "%",   0,    0,  100),
        new EnvMetric("pressure",         "barometric_pressure", "StrEnvPressure",        "hPa", 0,  970, 1040),
        new EnvMetric("iaq",              "iaq",                 "StrEnvIaq",             "",    0,    0,  500),
        new EnvMetric("gas",              "gas_resistance",      "StrEnvGas",             "MΩ",  2,    0,   50),
        new EnvMetric("lux",              "lux",                 "StrEnvLux",             "lx",  0,    0, 50000),
        new EnvMetric("white_lux",        "white_lux",           "StrEnvWhiteLux",        "lx",  0,    0, 50000),
        new EnvMetric("uv_lux",           "uv_lux",              "StrEnvUvLux",           "lx",  0,    0, 10000),
        new EnvMetric("wind_speed",       "wind_speed",          "StrEnvWindSpeed",       "m/s", 1,    0,   30),
        new EnvMetric("wind_direction",   "wind_direction",      "StrEnvWindDir",         "°",   0,    0,  360),
        new EnvMetric("wind_gust",        "wind_gust",           "StrEnvWindGust",        "m/s", 1,    0,   40),
        new EnvMetric("rain_1h",          "rainfall_1h",         "StrEnvRain1h",          "mm",  1,    0,   20),
        new EnvMetric("rain_24h",         "rainfall_24h",        "StrEnvRain24h",         "mm",  1,    0,   80),
        new EnvMetric("soil_moisture",    "soil_moisture",       "StrEnvSoilMoisture",    "%",   0,    0,  100),
        new EnvMetric("soil_temperature", "soil_temperature",    "StrEnvSoilTemp",        "°C",  1,  -10,   40),
        new EnvMetric("radiation",        "radiation",           "StrEnvRadiation",       "µR/h",1,    0,  100),
        new EnvMetric("distance",         "distance",            "StrEnvDistance",        "mm",  0,    0, 5000),
        new EnvMetric("weight",           "weight",              "StrEnvWeight",          "kg",  2,    0,  200),
    };

    private static readonly Dictionary<string, EnvMetric> _byKey =
        All.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>All DB columns for the metrics, in registry order (for schema/insert/select).</summary>
    public static IReadOnlyList<string> Columns { get; } = All.Select(m => m.Column).ToList();

    public static EnvMetric? ByKey(string? key) =>
        key != null && _byKey.TryGetValue(key, out var m) ? m : null;

    /// <summary>Calibrated colour gradient (RdYlBu reversed – the meteorological diverging
    /// palette). Normalised position t=0..1 → "r,g,b". Mapped onto each metric's
    /// [HeatMin,HeatMax] so the colour encodes the real value, weather-service style.</summary>
    public static readonly (double t, string rgb)[] Gradient =
    {
        (0.00, "49,54,149"),   (0.10, "69,117,180"),  (0.25, "116,173,209"),
        (0.40, "171,217,233"), (0.50, "224,243,248"), (0.60, "255,255,191"),
        (0.72, "254,224,144"), (0.83, "253,174,97"),  (0.92, "244,109,67"),
        (1.00, "215,48,39"),
    };

    /// <summary>Colour stops as (absolute value, "r,g,b") for the given metric — feeds both
    /// the MapLibre value→colour interpolation and the on-map legend.</summary>
    public static List<object> ColorStops(EnvMetric m)
    {
        var span = m.HeatMax - m.HeatMin;
        return Gradient.Select(g => (object)new { val = Math.Round(m.HeatMin + g.t * span, 3), rgb = g.rgb }).ToList();
    }

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
