using System.Globalization;
using System.IO;
using MeshhessenClient.Models;

namespace MeshhessenClient.Services;

public static class LocationLogger
{
    private static string LogDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locationlogs");

    public static string GetLogPath(uint nodeId) =>
        Path.Combine(LogDir, $"NODE_{nodeId:X8}.csv");

    public static void Log(NodeInfo node)
    {
        if (!node.Latitude.HasValue || !node.Longitude.HasValue) return;

        try
        {
            Directory.CreateDirectory(LogDir);
            var path = GetLogPath(node.NodeId);
            var ci = CultureInfo.InvariantCulture;
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", ci);
            var lat = node.Latitude.Value.ToString("F7", ci);
            var lon = node.Longitude.Value.ToString("F7", ci);
            var alt = node.Altitude?.ToString(ci) ?? "";
            var name = node.ShortName ?? node.Id;

            // Write header if file is new
            bool isNew = !File.Exists(path);
            using var sw = new StreamWriter(path, append: true);
            if (isNew)
                sw.WriteLine("Timestamp;NodeId;Name;Latitude;Longitude;Altitude");
            sw.WriteLine($"{timestamp};{node.NodeId:X8};{name};{lat};{lon};{alt}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"LocationLogger error: {ex.Message}");
        }
    }

    public record LocationEntry(DateTime Timestamp, string Name, double Latitude, double Longitude, double? Altitude);

    public static List<LocationEntry> ReadLog(uint nodeId)
    {
        var entries = new List<LocationEntry>();
        var path = GetLogPath(nodeId);
        if (!File.Exists(path)) return entries;

        try
        {
            var ci = CultureInfo.InvariantCulture;
            foreach (var line in File.ReadLines(path).Skip(1)) // skip header
            {
                var parts = line.Split(';');
                if (parts.Length < 5) continue;
                if (!DateTime.TryParse(parts[0], ci, System.Globalization.DateTimeStyles.None, out var ts)) continue;
                if (!double.TryParse(parts[3], NumberStyles.Float, ci, out var lat)) continue;
                if (!double.TryParse(parts[4], NumberStyles.Float, ci, out var lon)) continue;
                double? alt = parts.Length > 5 && double.TryParse(parts[5], NumberStyles.Float, ci, out var a) ? a : null;
                entries.Add(new LocationEntry(ts, parts[2], lat, lon, alt));
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"LocationLogger read error: {ex.Message}");
        }
        return entries;
    }

    public static bool HasLog(uint nodeId) => File.Exists(GetLogPath(nodeId));
}
