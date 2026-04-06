using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MeshhessenClient.Models;

public class MessageItem : INotifyPropertyChanged
{
    public string Time { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty; // Channel Index (legacy)
    public string ChannelName { get; set; } = string.Empty; // Channel Name for display
    public uint FromId { get; set; }
    public uint ToId { get; set; }
    public uint Id { get; set; } // Packet ID (for reactions)
    public bool IsEncrypted { get; set; } = false;
    public bool IsViaMqtt { get; set; } = false;
    public string SenderShortName { get; set; } = string.Empty;
    public string SenderColorHex { get; set; } = string.Empty;
    public string SenderNote { get; set; } = string.Empty;
    public bool HasAlertBell { get; set; } = false;

    private bool _isOwnMessage = false;
    public bool IsOwnMessage
    {
        get => _isOwnMessage;
        set { _isOwnMessage = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOwnMessage))); }
    }

    // Hop count: number of relay hops the packet traversed. -1 = unknown (e.g. from DB without hop info)
    public int HopCount { get; set; } = -1;
    public bool HasHopCount => HopCount >= 0;
    public string HopCountDisplay => HopCount >= 0 ? $"↪ {HopCount}" : string.Empty;

    // Protocol-level reply (Meshtastic Data.reply_id field 7)
    public uint ReplyId { get; set; }
    public string ReplyFromName { get; set; } = string.Empty;
    public string ReplyPreview { get; set; } = string.Empty;
    public bool HasReply => ReplyId != 0;
    public string ReplyQuoteText => $"↳ {ReplyFromName}: {ReplyPreview}";

    private string _reactionsDisplay = string.Empty;
    public string ReactionsDisplay
    {
        get => _reactionsDisplay;
        set { _reactionsDisplay = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReactionsDisplay))); }
    }

    // Internal reaction storage: emoji -> list of sender node IDs
    public Dictionary<string, List<uint>> ReactionsByEmoji { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddReaction(string emoji, uint senderNodeId)
    {
        if (!ReactionsByEmoji.TryGetValue(emoji, out var senders))
        {
            senders = new List<uint>();
            ReactionsByEmoji[emoji] = senders;
        }
        if (!senders.Contains(senderNodeId))
            senders.Add(senderNodeId);

        // Rebuild display string (most reactions first)
        ReactionsDisplay = string.Join("  ", ReactionsByEmoji
            .OrderByDescending(kv => kv.Value.Count)
            .Select(kv => kv.Value.Count > 1 ? $"{kv.Key} ×{kv.Value.Count}" : kv.Key));
    }
}
