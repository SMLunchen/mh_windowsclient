namespace MeshhessenClient.Models;

public class NodeInfo
{
    public string Name { get; set; } = "Unknown";
    public string ShortName { get; set; } = string.Empty;
    public string LongName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Distance { get; set; } = "-";
    public string Snr { get; set; } = "-";
    public string Rssi { get; set; } = "-";
    public string Battery { get; set; } = "-";
    public string LastSeen { get; set; } = "-";
    public uint NodeId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? Altitude { get; set; }
    public float? GroundSpeed { get; set; }  // m/s
    public float? GroundTrack { get; set; }  // degrees 0–360 (0=North, clockwise)
    public string ColorHex { get; set; } = string.Empty;  // Empty = no color, otherwise #RRGGBB
    public string Note { get; set; } = string.Empty;      // User note
    public bool IsPinned { get; set; }                    // Pinned to top of node list
    public string HardwareModel { get; set; } = string.Empty;
    public bool PkiKeyKnown { get; set; }
    public string PkiKeyIcon => PkiKeyKnown ? "🔑" : "";
    public string SignalTrendColor { get; set; } = string.Empty;  // "" | "#4CAF50" | "#FFC107" | "#F44336"

    // Live values for 3-dot node-list indicators (set when packets/telemetry arrive)
    public float? SnrValue     { get; set; }  // most recent rx_snr from this node
    public float? BatteryValue { get; set; }  // most recent battery_level (%) from this node

    public string SignalQualityColor => SnrValue switch
    {
        null   => string.Empty,
        >= 5f  => "#4CAF50",
        >= -5f => "#FFC107",
        _      => "#F44336"
    };

    public string BatteryStatusColor => BatteryValue switch
    {
        null    => string.Empty,
        >= 50f  => "#4CAF50",
        >= 20f  => "#FFC107",
        _       => "#F44336"
    };
}
