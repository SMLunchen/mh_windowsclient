using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MeshhessenClient.Models;

public class MessageItem : INotifyPropertyChanged
{
    public string Time { get; set; } = string.Empty;

    // Sortable timestamp for chronological ordering of the chat (live = arrival
    // time, DB-restored = the stored time). Defaults to construction time so a
    // message without an explicit value still sorts sensibly.
    public DateTime SortTime { get; set; } = DateTime.Now;

    /// <summary>Insert <paramref name="msg"/> into a time-ordered list at the right
    /// position (ascending SortTime). Scans from the end, so appending the newest
    /// message is O(1).</summary>
    public static void InsertByTime(System.Collections.Generic.IList<MessageItem> list, MessageItem msg)
    {
        int i = list.Count;
        while (i > 0 && list[i - 1].SortTime > msg.SortTime) i--;
        list.Insert(i, msg);
    }

    // Observable: an "Unknown" sender is updated to the real name once the node's
    // NodeInfo arrives (retroactive resolution in the chat).
    private string _from = string.Empty;
    public string From
    {
        get => _from;
        set { _from = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(From))); }
    }

    private string _message = string.Empty;
    // Observable: a PKI DM shown as an encrypted placeholder is updated in place
    // once the sender's key arrives (retroactive decryption).
    public string Message
    {
        get => _message;
        set { _message = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message))); }
    }

    public string Channel { get; set; } = string.Empty; // Channel Index (legacy, display string)
    public uint ChannelIndex { get; set; }              // raw channel index the packet arrived on

    // Observable: resolved from the current channel list, so a message shown before the
    // channels arrived (e.g. DB backlog on connect) updates from "Kanal N" to the real name.
    private string _channelName = string.Empty;
    public string ChannelName
    {
        get => _channelName;
        set { _channelName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChannelName))); }
    }
    public uint FromId { get; set; }
    public uint ToId { get; set; }
    public uint Id { get; set; } // Packet ID (for reactions)

    private bool _isEncrypted = false;
    public bool IsEncrypted
    {
        get => _isEncrypted;
        set
        {
            _isEncrypted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEncrypted)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetryDecrypt)));
        }
    }

    // Ciphertext of an undecryptable PKI DM, kept so the message can be decrypted
    // later — this session, or after a restart (persisted alongside the message) —
    // once the sender's public key arrives. Null for normal/decrypted messages.
    private byte[]? _pkiCipher;
    public byte[]? PkiCipher
    {
        get => _pkiCipher;
        set
        {
            _pkiCipher = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetryDecrypt)));
        }
    }

    /// <summary>True only when this is an encrypted DM we actually hold ciphertext
    /// for — i.e. the "request key / decrypt" action can do something.</summary>
    public bool CanRetryDecrypt => IsEncrypted && _pkiCipher is { Length: > 0 };
    public bool IsViaMqtt { get; set; } = false;

    // Observable so a message from an "Unknown" sender updates in place once the
    // node's NodeInfo arrives.
    private string _senderShortName = string.Empty;
    public string SenderShortName
    {
        get => _senderShortName;
        set { _senderShortName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SenderShortName))); }
    }
    private string _senderColorHex = string.Empty;
    public string SenderColorHex
    {
        get => _senderColorHex;
        set { _senderColorHex = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SenderColorHex))); }
    }
    private string _senderNote = string.Empty;
    public string SenderNote
    {
        get => _senderNote;
        set { _senderNote = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SenderNote))); }
    }
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

    // Signal info — only set when we received the packet directly (0 hops, no MQTT)
    public float? RxSnr  { get; set; }
    public int?   RxRssi { get; set; }
    public bool HasSignalInfo  => RxSnr.HasValue || RxRssi.HasValue;
    public string SnrDisplay   => RxSnr.HasValue  ? $"SNR {RxSnr.Value:F1}" : string.Empty;
    public string RssiDisplay  => RxRssi.HasValue ? $"RSSI {RxRssi.Value}"  : string.Empty;
    public string SnrColorHex  => RxSnr.HasValue  ? NodeInfo.SnrToColor(RxSnr.Value)   : "#9E9E9E";
    public string RssiColorHex => RxRssi.HasValue ? NodeInfo.RssiToColor(RxRssi.Value) : "#9E9E9E";

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

    // Tooltip listing who reacted with what, e.g. "👍  Anna, Max\n☁️  Sebastian".
    private string _reactionsTooltip = string.Empty;
    public string ReactionsTooltip
    {
        get => _reactionsTooltip;
        set { _reactionsTooltip = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReactionsTooltip))); }
    }

    // Internal reaction storage: emoji -> list of sender node IDs
    public Dictionary<string, List<uint>> ReactionsByEmoji { get; } = new();
    // Sender node id -> display name, for the reaction tooltip.
    private readonly Dictionary<uint, string> _reactionSenderNames = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddReaction(string emoji, uint senderNodeId, string senderName = "")
    {
        if (!ReactionsByEmoji.TryGetValue(emoji, out var senders))
        {
            senders = new List<uint>();
            ReactionsByEmoji[emoji] = senders;
        }
        if (!senders.Contains(senderNodeId))
            senders.Add(senderNodeId);
        if (!string.IsNullOrEmpty(senderName))
            _reactionSenderNames[senderNodeId] = senderName;

        var ordered = ReactionsByEmoji.OrderByDescending(kv => kv.Value.Count).ToList();

        // Rebuild display string (most reactions first)
        ReactionsDisplay = string.Join("  ", ordered
            .Select(kv => kv.Value.Count > 1 ? $"{kv.Key} ×{kv.Value.Count}" : kv.Key));

        // Rebuild tooltip: one line per emoji with the sender names
        ReactionsTooltip = string.Join("\n", ordered
            .Select(kv => $"{kv.Key}  " + string.Join(", ", kv.Value.Select(id =>
                _reactionSenderNames.TryGetValue(id, out var n) && !string.IsNullOrEmpty(n) ? n : $"!{id:x8}"))));
    }
}
