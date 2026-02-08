using System.IO;
using System.Windows;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class TileDownloaderWindow : Window
{
    private CancellationTokenSource? _cts;
    private static readonly string TileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");

    public TileDownloaderWindow()
    {
        InitializeComponent();
        UpdateEstimate();
    }

    private void ZoomChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Nur ausführen wenn alle UI-Elemente bereit sind
        if (EstimateText == null) return;
        UpdateEstimate();
    }

    private void UpdateEstimate()
    {
        try
        {
            if (!TryGetParams(out var n, out var s, out var e, out var w, out var minZ, out var maxZ))
            {
                EstimateText.Text = "Geschätzte Tiles: –";
                return;
            }
            var count = TileDownloaderService.EstimateTileCount(n, s, e, w, minZ, maxZ);
            EstimateText.Text = $"Geschätzte Tiles: ~{count:N0}";
        }
        catch
        {
            EstimateText.Text = "Geschätzte Tiles: –";
        }
    }

    private bool TryGetParams(out double north, out double south, out double east, out double west, out int minZoom, out int maxZoom)
    {
        north = south = east = west = 0;
        minZoom = maxZoom = 0;
        // Guard gegen NullRef während InitializeComponent
        if (NorthBox == null || SouthBox == null || EastBox == null || WestBox == null || MinZoomBox == null || MaxZoomBox == null)
            return false;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return double.TryParse(NorthBox.Text, System.Globalization.NumberStyles.Float, ci, out north)
            && double.TryParse(SouthBox.Text, System.Globalization.NumberStyles.Float, ci, out south)
            && double.TryParse(EastBox.Text, System.Globalization.NumberStyles.Float, ci, out east)
            && double.TryParse(WestBox.Text, System.Globalization.NumberStyles.Float, ci, out west)
            && int.TryParse(MinZoomBox.Text, out minZoom)
            && int.TryParse(MaxZoomBox.Text, out maxZoom)
            && minZoom >= 1 && maxZoom <= 19 && minZoom <= maxZoom;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetParams(out var n, out var s, out var east, out var w, out var minZ, out var maxZ))
        {
            MessageBox.Show("Bitte gültige Koordinaten und Zoom-Stufen eingeben.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        _cts = new CancellationTokenSource();
        Services.Logger.WriteLine($"Tile-Download gestartet: N={n:F4} S={s:F4} E={east:F4} W={w:F4} Zoom {minZ}-{maxZ}");

        var total = TileDownloaderService.EstimateTileCount(n, s, east, w, minZ, maxZ);
        DownloadProgress.Maximum = total;

        var progress = new Progress<(int done, int total, string status)>(p =>
        {
            DownloadProgress.Value = p.done;
            StatusText.Text = $"{p.done} / {p.total} — {p.status}";
        });

        try
        {
            await TileDownloaderService.DownloadTilesAsync(n, s, east, w, minZ, maxZ, TileDir, progress, _cts.Token);
            var msg = _cts.Token.IsCancellationRequested ? "Abgebrochen." : "Download abgeschlossen!";
            StatusText.Text = msg;
            Services.Logger.WriteLine($"Tile-Download: {msg}");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Abgebrochen.";
            Services.Logger.WriteLine("Tile-Download: Abgebrochen.");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Tile-Download FEHLER: {ex.Message}");
            MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
}
