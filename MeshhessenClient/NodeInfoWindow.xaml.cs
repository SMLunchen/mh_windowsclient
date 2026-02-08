using System.Windows;
using MeshhessenClient.Models;

namespace MeshhessenClient;

public partial class NodeInfoWindow : Window
{
    public NodeInfoWindow(NodeInfo node)
    {
        InitializeComponent();
        ShortNameText.Text = node.ShortName;
        LongNameText.Text = node.LongName;
        NodeIdText.Text = node.Id;
        LatText.Text = node.Latitude.HasValue ? node.Latitude.Value.ToString("F6") : "-";
        LonText.Text = node.Longitude.HasValue ? node.Longitude.Value.ToString("F6") : "-";
        AltText.Text = node.Altitude.HasValue ? $"{node.Altitude.Value} m" : "-";
        LastSeenText.Text = node.LastSeen;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
