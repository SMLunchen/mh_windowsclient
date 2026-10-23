using System.Text.Json.Serialization;

namespace MeshhessenClient.Models;

public record DashboardWidget(
    string Id,
    string Type,        // "line","area","bar","gauge","heatmap","scatter","stat","ranking","multistat","meshhealth","histogram","candlestick","stateline","clock"
    string Metric,      // "snr","rssi","battery","voltage","channel_util","air_tx_util","temperature","humidity","pressure","packet_count"
    List<uint> NodeIds,
    int Days,
    string Title,
    double Width  = 420,
    double Height = 300,
    double Threshold = double.NaN,   // legacy single threshold — migrated to AlarmHigh on load
    bool ShowMovingAverage = false,  // MA overlay on line/area charts
    // Grafana-style zones (NaN = disabled). Configured in the widget editor; they color
    // gauges/stats/charts and fire Windows toasts on zone transitions.
    double WarnLow   = double.NaN,
    double WarnHigh  = double.NaN,
    double AlarmLow  = double.NaN,
    double AlarmHigh = double.NaN
)
{
    [JsonIgnore] public bool HasAlarmLevels =>
        !double.IsNaN(WarnLow) || !double.IsNaN(WarnHigh) ||
        !double.IsNaN(AlarmLow) || !double.IsNaN(AlarmHigh);
}

/// <summary>Zone of a value relative to a widget's warn/alarm levels. Order doubles as
/// severity: higher = worse (Ok &lt; Warn &lt; Alarm).</summary>
public enum AlarmZone { Ok = 0, WarnLow = 1, WarnHigh = 2, AlarmLow = 3, AlarmHigh = 4 }

public static class DashboardAlarms
{
    /// <summary>Classify a value: alarm bounds win over warn bounds; bounds are inclusive.</summary>
    public static AlarmZone Classify(double value, double warnLow, double warnHigh,
        double alarmLow, double alarmHigh)
    {
        if (double.IsNaN(value)) return AlarmZone.Ok;
        if (!double.IsNaN(alarmLow)  && value <= alarmLow)  return AlarmZone.AlarmLow;
        if (!double.IsNaN(alarmHigh) && value >= alarmHigh) return AlarmZone.AlarmHigh;
        if (!double.IsNaN(warnLow)   && value <= warnLow)   return AlarmZone.WarnLow;
        if (!double.IsNaN(warnHigh)  && value >= warnHigh)  return AlarmZone.WarnHigh;
        return AlarmZone.Ok;
    }

    public static AlarmZone Classify(DashboardWidget w, double value) =>
        Classify(value, w.WarnLow, w.WarnHigh, w.AlarmLow, w.AlarmHigh);

    public static bool IsAlarm(AlarmZone z) => z is AlarmZone.AlarmLow or AlarmZone.AlarmHigh;
    public static bool IsWarn(AlarmZone z)  => z is AlarmZone.WarnLow  or AlarmZone.WarnHigh;

    /// <summary>Migrate the legacy single Threshold (acted as a high alarm) into AlarmHigh.</summary>
    public static DashboardWidget Migrate(DashboardWidget w) =>
        !double.IsNaN(w.Threshold) && !w.HasAlarmLevels
            ? w with { AlarmHigh = w.Threshold, Threshold = double.NaN }
            : w;
}

public record Dashboard(
    string Name,
    List<DashboardWidget> Widgets
);

public class DashboardStore
{
    [JsonPropertyName("dashboards")]
    public List<Dashboard> Dashboards { get; set; } = new();

    /// <summary>Auto-refresh interval in seconds (0 = off). Persisted so the
    /// dashboard reopens with the user's last choice instead of resetting to 30 s.</summary>
    [JsonPropertyName("refreshSeconds")]
    public int RefreshSeconds { get; set; } = 30;

    // Window placement + last active dashboard, restored on reopen (NaN = never saved)
    [JsonPropertyName("windowWidth")]   public double  WindowWidth  { get; set; } = double.NaN;
    [JsonPropertyName("windowHeight")]  public double  WindowHeight { get; set; } = double.NaN;
    [JsonPropertyName("windowLeft")]    public double  WindowLeft   { get; set; } = double.NaN;
    [JsonPropertyName("windowTop")]     public double  WindowTop    { get; set; } = double.NaN;
    [JsonPropertyName("windowMax")]     public bool    WindowMaximized { get; set; }
    [JsonPropertyName("lastDashboard")] public string? LastDashboard   { get; set; }
}
