using System.IO;
using System.Windows;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class VectorTileDownloaderWindow : Window
{
    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    private CancellationTokenSource? _cts;
    private static readonly string CacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vectortiles");

    public VectorTileDownloaderWindow(AppSettings settings)
    {
        InitializeComponent();

        BundeslandComboBox.SelectedIndex = 0;  // (Manuelle Eingabe)

        // Topo extras default: on when the topo style is the active map source
        TopoExtrasCheck.IsChecked = settings.MapSource == "osmtopo";

        // One checkbox per registered overlay; preselect the currently active ones
        var active = MapOverlayRegistry.ParseActive(settings.MapOverlays);
        foreach (var overlay in MapOverlayRegistry.All)
        {
            var text = new System.Windows.Controls.TextBlock { FontSize = 12 };
            text.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, overlay.NameResourceKey);
            var cb = new System.Windows.Controls.CheckBox
            {
                Tag = overlay.Key,
                Margin = new Thickness(0, 0, 0, 4),
                Content = text,
                IsChecked = active.Contains(overlay.Key)
            };
            cb.SetResourceReference(FrameworkElement.ToolTipProperty, overlay.NameResourceKey + "Tooltip");
            cb.Checked += ParamsChanged;
            cb.Unchecked += ParamsChanged;
            OverlaysPanel.Children.Add(cb);
        }

        UpdateEstimate();
    }

    private void ParamsChanged(object sender, RoutedEventArgs e) => UpdateEstimate();
    private void ParamsChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateEstimate();

    private void BundeslandComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (BundeslandComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
            return;

        var tag = item.Tag as string;
        if (string.IsNullOrEmpty(tag))
            return;

        var parts = tag.Split(',');
        if (parts.Length == 4)
        {
            NorthBox.Text = parts[0];
            SouthBox.Text = parts[1];
            EastBox.Text = parts[2];
            WestBox.Text = parts[3];
            UpdateEstimate();
        }
    }

    private List<MapOverlayDef> SelectedOverlays() =>
        OverlaysPanel == null
            ? new List<MapOverlayDef>()
            : OverlaysPanel.Children.OfType<System.Windows.Controls.CheckBox>()
                .Where(cb => cb.IsChecked == true && cb.Tag is string)
                .Select(cb => MapOverlayRegistry.All.First(o => o.Key == (string)cb.Tag))
                .ToList();

    private bool TryGetParams(out double north, out double south, out double east, out double west, out int maxZoom)
    {
        north = south = east = west = 0;
        maxZoom = 0;
        if (NorthBox == null || SouthBox == null || EastBox == null || WestBox == null || MaxZoomBox == null)
            return false;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return double.TryParse(NorthBox.Text, System.Globalization.NumberStyles.Float, ci, out north)
            && double.TryParse(SouthBox.Text, System.Globalization.NumberStyles.Float, ci, out south)
            && double.TryParse(EastBox.Text, System.Globalization.NumberStyles.Float, ci, out east)
            && double.TryParse(WestBox.Text, System.Globalization.NumberStyles.Float, ci, out west)
            && int.TryParse(MaxZoomBox.Text, out maxZoom)
            && maxZoom >= 12 && maxZoom <= 17;
    }

    private void UpdateEstimate()
    {
        if (EstimateText == null) return;
        try
        {
            if (!TryGetParams(out var n, out var s, out var e, out var w, out var maxZ))
            {
                EstimateText.Text = Loc("StrTdEstimateNone");
                return;
            }
            var plans = VectorPackageDownloaderService.BuildPlans(maxZ, TopoExtrasCheck?.IsChecked == true, SelectedOverlays());
            var count = VectorPackageDownloaderService.EstimateTileCount(plans, n, s, e, w);
            EstimateText.Text = string.Format(Loc("StrVdEstimate"), count);
        }
        catch
        {
            EstimateText.Text = Loc("StrTdEstimateNone");
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetParams(out var n, out var s, out var east, out var w, out var maxZ))
        {
            MessageBox.Show(Loc("StrVdInvalidParams"), Loc("StrVdTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var plans = VectorPackageDownloaderService.BuildPlans(maxZ, TopoExtrasCheck.IsChecked == true, SelectedOverlays());
        var total = VectorPackageDownloaderService.EstimateTileCount(plans, n, s, east, w);

        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        _cts = new CancellationTokenSource();
        Services.Logger.WriteLine(
            $"Vector package download: N={n:F4} S={s:F4} E={east:F4} W={w:F4} maxZ={maxZ} sources=[{string.Join(",", plans.Select(p => p.TileSource))}] ~{total} tiles");

        DownloadProgress.Maximum = total;

        var progress = new Progress<(int done, int total, string status)>(p =>
        {
            DownloadProgress.Maximum = p.total;
            DownloadProgress.Value = p.done;
            StatusText.Text = $"{p.done} / {p.total} — {p.status}";
        });

        try
        {
            var (downloaded, skipped, errors) = await VectorPackageDownloaderService.DownloadAsync(
                n, s, east, w, plans, CacheDir, progress, _cts.Token);

            var msg = _cts.Token.IsCancellationRequested
                ? Loc("StrVdCancelled")
                : string.Format(Loc("StrVdDone"), downloaded, skipped, errors);
            StatusText.Text = msg;
            Services.Logger.WriteLine($"Vector package download: {msg}");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Loc("StrVdCancelled");
            Services.Logger.WriteLine("Vector package download cancelled.");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Vector package download ERROR: {ex.Message}");
            MessageBox.Show(ex.Message, Loc("StrVdTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
}
