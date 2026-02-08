using System.IO;
using System.Text;

namespace MeshhessenClient.Services;

public static class MessageLogger
{
    private static readonly string LogDirectory;
    private static readonly object _lock = new object();

    static MessageLogger()
    {
        // Verwende das Verzeichnis der EXE
        var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        LogDirectory = Path.Combine(exeDirectory, "logs");

        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR creating message log directory: {ex.Message}");
        }
    }

    public static void LogChannelMessage(int channelIndex, string channelName, string from, string message)
    {
        var fileName = SanitizeFileName($"Channel_{channelIndex}_{channelName}.log");
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var logEntry = $"[{timestamp}] {from}: {message}";

        WriteToLog(fileName, logEntry);
    }

    public static void LogDirectMessage(uint nodeId, string nodeName, string from, string message)
    {
        var fileName = SanitizeFileName($"DM_{nodeId:X8}_{nodeName}.log");
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var logEntry = $"[{timestamp}] {from}: {message}";

        WriteToLog(fileName, logEntry);
    }

    private static void WriteToLog(string fileName, string logEntry)
    {
        try
        {
            var filePath = Path.Combine(LogDirectory, fileName);

            lock (_lock)
            {
                File.AppendAllText(filePath, logEntry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR writing to message log '{fileName}': {ex.Message}");
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(fileName);

        foreach (var c in invalidChars)
        {
            sanitized.Replace(c, '_');
        }

        // Ersetze auch andere problematische Zeichen
        sanitized.Replace(':', '_');
        sanitized.Replace('*', '_');
        sanitized.Replace('?', '_');
        sanitized.Replace('"', '_');
        sanitized.Replace('<', '_');
        sanitized.Replace('>', '_');
        sanitized.Replace('|', '_');

        return sanitized.ToString();
    }

    public static string GetLogDirectory()
    {
        return LogDirectory;
    }
}
