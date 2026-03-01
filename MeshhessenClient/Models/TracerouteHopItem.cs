namespace MeshhessenClient.Models;

/// <summary>
/// Represents one hop in a traceroute result, displayed in TracerouteWindow.
/// </summary>
public class TracerouteHopItem
{
    public int HopIndex { get; set; }       // 0 = us, last = destination
    public string NodeId { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string Distance { get; set; } = "?";
    public string SnrDisplay { get; set; } = "-";   // e.g. "12.5 dB" or "MQTT"
    public string RssiDisplay { get; set; } = "-";
    public bool IsViaMqtt { get; set; } = false;
    public bool HasPosition { get; set; } = false;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsSource { get; set; } = false;     // First hop (us)
    public bool IsDestination { get; set; } = false;
    public bool IsPositionKnown => HasPosition;

    // Icon for each hop row
    public string HopIcon
    {
        get
        {
            if (IsSource) return "📍";
            if (IsDestination) return "🎯";
            return "🔗";
        }
    }

    // Status badge text
    public string StatusBadge
    {
        get
        {
            if (IsViaMqtt) return "MQTT";
            if (!HasPosition) return "?";
            return string.Empty;
        }
    }
}
