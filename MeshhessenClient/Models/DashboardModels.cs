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
}
