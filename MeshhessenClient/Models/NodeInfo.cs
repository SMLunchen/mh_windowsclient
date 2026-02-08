namespace MeshhessenClient.Models;

public class NodeInfo
{
    public string Name { get; set; } = "Unknown";
    public string Id { get; set; } = string.Empty;
    public string Distance { get; set; } = "-";
    public string Snr { get; set; } = "-";
    public string Battery { get; set; } = "-";
    public string LastSeen { get; set; } = "-";
    public uint NodeId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? Altitude { get; set; }
}
