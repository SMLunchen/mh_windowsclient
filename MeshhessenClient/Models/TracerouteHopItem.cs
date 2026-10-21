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
    public string SnrDisplay { get; set; } = "-";   // e.g. "12.5 dB", or "—" for a bridged segment
    public string RssiDisplay { get; set; } = "-";
    // True only when the link INTO this hop has no RF SNR (sentinel −128): the segment
    // was bridged (MQTT). NOT the result packet's via_mqtt flag — that one only says how
    // the REPLY reached us and belongs to the window header, not to individual hops.
    public bool IsMqttBridge { get; set; } = false;
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
            if (IsMqttBridge) return "MQTT";
            if (!HasPosition) return "?";
            return string.Empty;
        }
    }
}
