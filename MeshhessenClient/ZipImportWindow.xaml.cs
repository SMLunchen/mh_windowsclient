using System.IO;
using System.IO.Compression;
using System.Windows;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class ZipImportWindow : Window
{
    public ZipImportWindow()
    {
        InitializeComponent();
    }

    public async Task<bool> ImportFromZipAsync(string zipPath, string tileDir)
    {
        FileNameText.Text = $"Importiere: {Path.GetFileName(zipPath)}";

        var cts = new CancellationTokenSource();

        try
        {
            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && e.Name.EndsWith(".png")).ToList();

                if (entries.Count == 0)
                {
                    Dispatcher.Invoke(() => MessageBox.Show("Keine PNG-Tiles im Zip gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                // Detect source folder from first entry
                var firstEntry = entries[0];
                var firstPath = firstEntry.FullName.Replace('\\', '/');
                string? detectedSource = null;

                if (firstPath.StartsWith("osm/")) detectedSource = "osm";
                else if (firstPath.StartsWith("osmtopo/")) detectedSource = "osmtopo";
                else if (firstPath.StartsWith("osmdark/")) detectedSource = "osmdark";

                if (detectedSource == null)
                {
                    Dispatcher.Invoke(() => MessageBox.Show(
                        "Konnte Kartenquelle nicht erkennen. Zip muss Ordner 'osm/', 'osmtopo/' oder 'osmdark/' enthalten.",
                        "Fehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
                    return;
                }

                var targetDir = Path.Combine(tileDir, detectedSource);
                Logger.WriteLine($"[Zip Import] Detected source: {detectedSource}, target: {targetDir}");

                var total = entries.Count;
                var done = 0;

                foreach (var entry in entries)
                {
                    if (cts.Token.IsCancellationRequested)
                        break;

                    try
                    {
                        // Remove source prefix from path
                        var relativePath = entry.FullName.Replace('\\', '/');
                        if (relativePath.StartsWith(detectedSource + "/"))
                        {
                            relativePath = relativePath.Substring(detectedSource.Length + 1);
                        }

                        var targetPath = Path.Combine(targetDir, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                        entry.ExtractToFile(targetPath, overwrite: true);

                        done++;
                        if (done % 50 == 0 || done == total)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                ImportProgress.Maximum = total;
                                ImportProgress.Value = done;
                                StatusText.Text = $"{done} von {total} Tiles extrahiert";
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine($"[Zip Import] Failed to extract {entry.FullName}: {ex.Message}");
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    ImportProgress.Value = total;
                    StatusText.Text = $"Import abgeschlossen! {done} Tiles importiert.";
                });

                Logger.WriteLine($"[Zip Import] Completed: {done} tiles imported to {detectedSource}");
            }, cts.Token);

            await Task.Delay(1500);  // Kurz anzeigen
            Close();
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[Zip Import] ERROR: {ex.Message}");
            MessageBox.Show($"Fehler beim Import: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return false;
        }
    }
}
