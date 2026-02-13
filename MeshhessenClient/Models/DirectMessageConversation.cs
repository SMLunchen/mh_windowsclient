using System.Collections.ObjectModel;

namespace MeshhessenClient.Models;

public class DirectMessageConversation
{
    public uint NodeId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public ObservableCollection<MessageItem> Messages { get; set; } = new();
    public bool HasUnread { get; set; } = false;
    public string ColorHex { get; set; } = string.Empty;
}
