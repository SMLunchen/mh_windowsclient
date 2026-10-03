using System.ComponentModel;

namespace MeshhessenClient.Models;

/// <summary>
/// A direct-message conversation entry in the inline (WhatsApp-style) sidebar. Its messages are not
/// stored here — they live in the main message list and are filtered by <see cref="PartnerId"/> —
/// so it only carries the display name, node colour and an unread badge.
/// </summary>
public class DmSidebarItem : INotifyPropertyChanged
{
    public uint PartnerId { get; }

    public DmSidebarItem(uint partnerId, string name)
    {
        PartnerId = partnerId;
        _name = name;
    }

    private string _name;
    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
    }

    public string ColorHex { get; set; } = string.Empty;

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
