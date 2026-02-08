namespace MeshtasticClient.Models;

public class MessageItem
{
    public string Time { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public uint FromId { get; set; }
    public uint ToId { get; set; }
    public bool IsEncrypted { get; set; } = false;
}
