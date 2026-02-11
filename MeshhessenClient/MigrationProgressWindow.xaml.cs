using System.IO;
using System.Windows;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class MigrationProgressWindow : Window
{
    public MigrationProgressWindow()
    {
        InitializeComponent();
    }

    public async Task RunMigrationAsync(string tileDir)
    {
        var cts = new CancellationTokenSource();

        var progress = new Progress<(int done, int total, string status)>(p =>
        {
            MigrationProgress.Maximum = p.total;
            MigrationProgress.Value = p.done;
            StatusText.Text = p.total > 0
                ? $"{p.done} von {p.total} Tiles migriert — {p.status}"
                : "Migration läuft...";
        });

        try
        {
            await TileMigrationService.MigrateTilesAsync(tileDir, progress, cts.Token);
            StatusText.Text = "Migration abgeschlossen!";
            await Task.Delay(1000);  // Kurz anzeigen
            Close();
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[Migration] ERROR: {ex.Message}");
            MessageBox.Show($"Fehler bei der Migration: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }
}
