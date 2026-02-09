namespace MeshhessenClient.Models;

public class MessageItem
{
    public string Time { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty; // Channel Index (legacy)
    public string ChannelName { get; set; } = string.Empty; // Channel Name for display
    public uint FromId { get; set; }
    public uint ToId { get; set; }
    public bool IsEncrypted { get; set; } = false;
    public bool IsViaMqtt { get; set; } = false;
    public string SenderColorHex { get; set; } = string.Empty;
    public string SenderNote { get; set; } = string.Empty;
}
