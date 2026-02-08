namespace MeshhessenClient.Models;

public class ChannelInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Psk { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public uint Downlink { get; set; }
    public uint Uplink { get; set; }
}
