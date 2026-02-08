namespace MeshtasticClient.Models;

public class DeviceInfo
{
    public uint NodeId { get; set; }
    public string NodeIdHex => $"!{NodeId:x8}";
    public string ShortName { get; set; } = "";
    public string LongName { get; set; } = "";
    public string HardwareModel { get; set; } = "";
    public string FirmwareVersion { get; set; } = "";
}
