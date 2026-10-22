using System.Collections.ObjectModel;

namespace MeshhessenClient.Models;

public class DirectMessageConversation
{
    public uint NodeId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public ObservableCollection<MessageItem> Messages { get; set; } = new();
    public bool HasUnread { get; set; } = false;
    /// <summary>Number of unread incoming messages in this conversation.</summary>
    public int UnreadCount { get; set; }
    public string ColorHex { get; set; } = string.Empty;
}
