using System.ComponentModel;

namespace MeshhessenClient.Models;

public class ChannelInfo : INotifyPropertyChanged
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Psk { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool Downlink { get; set; }
    public bool Uplink { get; set; }
    public uint PositionPrecision { get; set; }

    // Unread counter for the conversation sidebar (WhatsApp-style badge).
    private int _unread;
    public int Unread
    {
        get => _unread;
        set
        {
            if (_unread == value) return;
            _unread = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Unread)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnreadText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUnread)));
        }
    }
    public string UnreadText => _unread > 99 ? "99+" : _unread.ToString();
    public bool HasUnread => _unread > 0;

    public event PropertyChangedEventHandler? PropertyChanged;
}
