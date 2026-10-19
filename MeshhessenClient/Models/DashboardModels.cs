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
    double Threshold = double.NaN,   // NaN = disabled; horizontal annotation line on charts
    bool ShowMovingAverage = false   // MA overlay on line/area charts
);

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
