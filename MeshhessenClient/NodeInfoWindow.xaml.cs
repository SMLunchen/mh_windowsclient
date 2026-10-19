using System.Windows;
using System.Windows.Media;
using MeshhessenClient.Models;

namespace MeshhessenClient;

public partial class NodeInfoWindow : Window
{
    public NodeInfoWindow(NodeInfo node)
    {
        InitializeComponent();

        // ── Header ────────────────────────────────────────────────────────
        try
        {
            AvatarBorder.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(node.AvatarColor));
        }
        catch { AvatarBorder.Background = new SolidColorBrush(Color.FromRgb(0x54, 0x6E, 0x7A)); }
        AvatarText.Text = node.AvatarLabel;

        LongNameText.Text = string.IsNullOrEmpty(node.LongName) ? node.Name : node.LongName;
        NodeIdText.Text   = node.Id;

        if (string.IsNullOrEmpty(node.RoleDisplay))
            RoleBadge.Visibility = Visibility.Collapsed;
        else
            RoleBadgeText.Text = node.RoleDisplay;

        // ── Device card ───────────────────────────────────────────────────
        HardwareModelText.Text = string.IsNullOrEmpty(node.HardwareModel) ? "–" : node.HardwareModel;
        RoleText.Text          = string.IsNullOrEmpty(node.RoleDisplay)   ? "–" : node.RoleDisplay;
        PkiKeyText.Text = node.PkiKeyKnown
            ? (TryFindResource("StrPkiKeyAvailable")   as string ?? "🔑 PKI decryption available")
            : (TryFindResource("StrPkiKeyUnavailable") as string ?? "PKI key unknown");

        // ── Status card ───────────────────────────────────────────────────
        BatteryText.Text = node.IsPowered ? "🔌"
                         : node.HasBattery ? $"{(int)node.BatteryValue!.Value} %"
                         : "–";
        SignalText.Text  = BuildSignalText(node);
        LastSeenText.Text = string.IsNullOrEmpty(node.LastSeen) || node.LastSeen == "-"
            ? "–" : node.LastSeen;

        // ── Position card ─────────────────────────────────────────────────
        if (node.Latitude.HasValue && node.Longitude.HasValue)
        {
            CoordinatesText.Text = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:F6}, {1:F6}", node.Latitude.Value, node.Longitude.Value);
            AltText.Text = node.HasAltitude ? $"{node.Altitude} m" : "–";
        }
        else
        {
            CoordinatesText.Text = TryFindResource("StrNiNoPosition") as string ?? "–";
            AltText.Text = "–";
        }
    }

    private static string BuildSignalText(NodeInfo node)
    {
        // SnrDisplay already folds in MQTT / hop semantics ("7.5", "2 Hops", "MQTT", …)
        var snr = node.SnrDisplay;
        if (string.IsNullOrEmpty(snr) || snr == "-") return "–";
        var rssi = node.RssiDisplay;
        return string.IsNullOrEmpty(rssi) ? snr : $"{snr} dB · {rssi} dBm";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
