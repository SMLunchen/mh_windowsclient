using System.Diagnostics;
using System.IO;

namespace MeshhessenClient.Services;

public static class Logger
{
    private static readonly object _lock = new();
    private static string? _logFilePath;
    private static StreamWriter? _logWriter;

    // Event für UI-Updates
    public static event EventHandler<string>? LogMessageReceived;

    static Logger()
    {
        try
        {
            // Log-Datei im Anwendungsverzeichnis erstellen
            string appPath = AppDomain.CurrentDomain.BaseDirectory;
            _logFilePath = Path.Combine(appPath, "meshhessen-client.log");

            // Alte Log-Datei löschen wenn größer als 5MB
            if (File.Exists(_logFilePath))
            {
                var fileInfo = new FileInfo(_logFilePath);
                if (fileInfo.Length > 5 * 1024 * 1024) // 5MB
                {
                    File.Delete(_logFilePath);
                }
            }

            // StreamWriter für Log-Datei öffnen
            _logWriter = new StreamWriter(_logFilePath, append: true)
            {
                AutoFlush = true
            };

            WriteLine("=== Meshhessen Client gestartet ===");
            WriteLine($"Log-Datei: {_logFilePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Fehler beim Initialisieren des Loggers: {ex.Message}");
        }
    }

    public static void WriteLine(string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string logMessage = $"[{timestamp}] {message}";

        lock (_lock)
        {
            try
            {
                // In Debug-Ausgabe schreiben (für Visual Studio / DebugView)
                Debug.WriteLine(logMessage);

                // In Log-Datei schreiben
                _logWriter?.WriteLine(logMessage);

                // Event für UI-Update auslösen
                LogMessageReceived?.Invoke(null, logMessage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Schreiben ins Log: {ex.Message}");
            }
        }
    }

    public static string? GetLogFilePath()
    {
        return _logFilePath;
    }

    public static void Close()
    {
        lock (_lock)
        {
            try
            {
                WriteLine("=== Meshhessen Client beendet ===");
                _logWriter?.Flush();
                _logWriter?.Close();
                _logWriter?.Dispose();
            }
            catch
            {
                // Ignorieren
            }
        }
    }
}
