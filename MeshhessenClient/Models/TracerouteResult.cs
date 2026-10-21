namespace MeshhessenClient.Models;

/// <summary>
/// Contains the full result of a traceroute response from the mesh.
/// </summary>
public class TracerouteResult
{
    public uint RequestId { get; set; }
    public uint DestinationNodeId { get; set; }
    public uint SourceNodeId { get; set; }
    public bool IsViaMqtt { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    // Outbound route: list of node IDs from us to destination (excluding us, including dest)
    public List<uint> RouteForward { get; set; } = new();
    // SNR values for each forward hop (scaled by 4, so divide by 4 to get dB)
    public List<int> SnrTowards { get; set; } = new();

    // Return route: node IDs from destination back to us
    public List<uint> RouteBack { get; set; } = new();
    // SNR values for each return hop
    public List<int> SnrBack { get; set; } = new();

    /// <summary>True when a raw q4-scaled traceroute SNR means "no RF measurement":
    /// −128 is the firmware's explicit unknown-sentinel (inserted for hops that never
    /// recorded themselves), and 0 is what a relay appends when the packet reached it
    /// WITHOUT radio — rx_snr of an MQTT-downlinked or API-injected packet is 0, and
    /// the firmware passes it unfiltered (TraceRouteModule: appendMyIDandSNR(r, p.rx_snr)).
    /// A genuine 0.0 dB reading (|SNR| &lt; 0.125 dB before q4 rounding) is sacrificed —
    /// far rarer and far cheaper than treating an MQTT bridge as a real RF link.</summary>
    public static bool IsNonRfSnr(int snrScaled) => snrScaled <= -128 || snrScaled == 0;
}
