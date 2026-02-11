using System.IO;

namespace MeshhessenClient.Services;

public static class TileMigrationService
{
    /// <summary>
    /// Prüft ob Migration notwendig ist (alte flat-Struktur existiert, neue nicht)
    /// </summary>
    public static bool IsMigrationNeeded(string tileDir)
    {
        var newOsmDir = Path.Combine(tileDir, "osm");

        // Migration nicht nötig wenn neue Struktur bereits existiert
        if (Directory.Exists(newOsmDir))
            return false;

        // Migration nötig wenn alte Struktur existiert
        if (!Directory.Exists(tileDir))
            return false;

        // Prüfe ob alte Zoom-Level Ordner existieren (1-19)
        for (int z = 1; z <= 19; z++)
        {
            var zoomDir = Path.Combine(tileDir, z.ToString());
            if (Directory.Exists(zoomDir))
            {
                // Prüfe ob PNG-Dateien existieren
                if (Directory.GetFiles(zoomDir, "*.png", SearchOption.AllDirectories).Length > 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Zählt alle zu migrierenden Tiles
    /// </summary>
    public static int CountTilesToMigrate(string tileDir)
    {
        int count = 0;
        for (int z = 1; z <= 19; z++)
        {
            var zoomDir = Path.Combine(tileDir, z.ToString());
            if (Directory.Exists(zoomDir))
            {
                count += Directory.GetFiles(zoomDir, "*.png", SearchOption.AllDirectories).Length;
            }
        }
        return count;
    }

    /// <summary>
    /// Migriert Tiles von maptiles/{z}/{x}/{y}.png nach maptiles/osm/{z}/{x}/{y}.png
    /// </summary>
    public static async Task MigrateTilesAsync(
        string tileDir,
        IProgress<(int done, int total, string status)> progress,
        CancellationToken ct)
    {
        var newOsmDir = Path.Combine(tileDir, "osm");
        Directory.CreateDirectory(newOsmDir);

        int total = CountTilesToMigrate(tileDir);
        int done = 0;

        Logger.WriteLine($"[Migration] Starting tile migration: {total} files");

        for (int z = 1; z <= 19 && !ct.IsCancellationRequested; z++)
        {
            var oldZoomDir = Path.Combine(tileDir, z.ToString());
            if (!Directory.Exists(oldZoomDir))
                continue;

            var newZoomDir = Path.Combine(newOsmDir, z.ToString());

            // Alle PNG-Dateien in diesem Zoom-Level finden
            var files = Directory.GetFiles(oldZoomDir, "*.png", SearchOption.AllDirectories);

            foreach (var oldFilePath in files)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    // Relativen Pfad extrahieren: {x}/{y}.png
                    var relativePath = Path.GetRelativePath(oldZoomDir, oldFilePath);
                    var newFilePath = Path.Combine(newZoomDir, relativePath);

                    // Zielverzeichnis erstellen
                    Directory.CreateDirectory(Path.GetDirectoryName(newFilePath)!);

                    // Datei verschieben (schneller als kopieren)
                    File.Move(oldFilePath, newFilePath, overwrite: false);

                    done++;
                    if (done % 100 == 0 || done == total)
                    {
                        progress.Report((done, total, $"Z{z} ({done}/{total})"));
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"[Migration] Failed to migrate {oldFilePath}: {ex.Message}");
                }
            }

            // Leere alte Ordner aufräumen
            try
            {
                if (Directory.Exists(oldZoomDir) && Directory.GetFiles(oldZoomDir, "*", SearchOption.AllDirectories).Length == 0)
                {
                    Directory.Delete(oldZoomDir, recursive: true);
                }
            }
            catch
            {
                // Ignorieren wenn Löschen fehlschlägt
            }
        }

        Logger.WriteLine($"[Migration] Migration completed: {done} files migrated");
        progress.Report((done, total, "Abgeschlossen"));
    }
}
