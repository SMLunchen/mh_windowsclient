using System.IO;
using System.Windows;
using System.Windows.Controls;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class TDeckExportWindow : Window
{
    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    private static readonly string LocalTileDir =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");

    private CancellationTokenSource? _cts;

    public TDeckExportWindow() => InitializeComponent();

    private async void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        // Determine source filter
        MapSource? filter = null;
        if (TypeComboBox.SelectedItem is ComboBoxItem item)
        {
            filter = (item.Tag as string) switch
            {
                "OSM"  => MapSource.OSM,
                "Dark" => MapSource.OSMDark,
                "Topo" => MapSource.OSMTopo,
                _      => null
            };
        }

        // Verify tiles exist
        var sourceFolder = filter.HasValue
            ? TileDownloaderService.GetSourceFolderName(filter.Value)
            : null;

        if (sourceFolder != null)
        {
            var dir = Path.Combine(LocalTileDir, sourceFolder);
            if (!Directory.Exists(dir) ||
                !Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories).Any())
            {
                StatusText.Text = Loc("StrTDeckExportNoTiles");
                return;
            }
        }

        // Save dialog
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title      = Loc("StrTDeckExportTitle"),
            Filter     = "ZIP-Dateien (*.zip)|*.zip",
            DefaultExt = "zip",
            FileName   = filter.HasValue
                ? $"maptiles_{TDeckTileService.GetSDFolderName(filter.Value)}.zip"
                : "maptiles_all.zip"
        };
        if (dialog.ShowDialog() != true) return;

        ExportBtn.IsEnabled = false;
        ExportProgress.Visibility = Visibility.Visible;
        ExportProgress.IsIndeterminate = false;
        StatusText.Text = "";

        _cts = new CancellationTokenSource();
        int totalReported = 1;

        var progress = new Progress<(int done, int total, string status)>(p =>
        {
            totalReported = p.total;
            ExportProgress.Maximum = p.total;
            ExportProgress.Value   = p.done;
            StatusText.Text        = p.status;
        });

        try
        {
            await TDeckTileService.ExportToZipAsync(
                LocalTileDir, dialog.FileName, filter, progress, _cts.Token);

            StatusText.Text = string.Format(Loc("StrTDeckExportDone"),
                Path.GetFileName(dialog.FileName));
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(Loc("StrTDeckExportFailed"), ex.Message);
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
        }
        finally
        {
            ExportBtn.IsEnabled = true;
            ExportProgress.Visibility = Visibility.Collapsed;
        }
    }
}
