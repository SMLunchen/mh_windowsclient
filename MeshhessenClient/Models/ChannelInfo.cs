namespace MeshhessenClient.Models;

public class ChannelInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Psk { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool Downlink { get; set; }
    public bool Uplink { get; set; }
}
