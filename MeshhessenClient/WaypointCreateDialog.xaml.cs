using System.Windows;

namespace MeshhessenClient;

public partial class WaypointCreateDialog : Window
{
    public string WaypointName        { get; private set; } = string.Empty;
    public string WaypointDescription { get; private set; } = string.Empty;
    public uint   WaypointIcon        { get; private set; }  // Unicode codepoint, 0 = default
    public uint   ExpireHours         { get; private set; }  // 0 = never
    public bool   SendToMesh          { get; private set; }  // User opted in to broadcast

    public WaypointCreateDialog(double lat, double lon)
    {
        InitializeComponent();
        CoordText.Text = $"Position: {lat:F6}°N, {lon:F6}°E";
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Bitte einen Namen eingeben.", "Waypoint", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WaypointName        = NameBox.Text.Trim();
        WaypointDescription = DescBox.Text.Trim();

        // Parse icon (first rune of the icon text)
        string iconText = IconBox.Text.Trim();
        if (!string.IsNullOrEmpty(iconText))
        {
            var runes = System.Text.Rune.GetRuneAt(iconText, 0);
            WaypointIcon = (uint)runes.Value;
        }

        if (uint.TryParse(ExpiryBox.Text, out var h) && h > 0)
            ExpireHours = h;

        SendToMesh = SendToMeshCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
