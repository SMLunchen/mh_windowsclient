namespace MeshhessenClient.Models;

/// <summary>
/// Persisted snapshot of a traceroute result including all node positions and names
/// known at the time of recording.
/// </summary>
public class TracerouteSaveData
{
    public TracerouteResult Result { get; set; } = new();
    public string DestinationName { get; set; } = string.Empty;

    /// <summary>All nodes involved in the route with their positions and names.</summary>
    public List<NodeEntry> Nodes { get; set; } = new();

    public class NodeEntry
    {
        public uint NodeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public double? Lat { get; set; }
        public double? Lon { get; set; }
    }

    public Dictionary<uint, (double Lat, double Lon)> GetPositionsDict()
        => Nodes
            .Where(e => e.Lat.HasValue && e.Lon.HasValue)
            .ToDictionary(e => e.NodeId, e => (e.Lat!.Value, e.Lon!.Value));

    public Dictionary<uint, string> GetNamesDict()
        => Nodes.ToDictionary(e => e.NodeId, e => e.Name);
}
