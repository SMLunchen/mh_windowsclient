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
}
