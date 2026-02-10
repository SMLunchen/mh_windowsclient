using System.IO;
using System.Globalization;

namespace MeshhessenClient.Services;

public record AppSettings(
    bool DarkMode,
    string StationName,
    bool ShowEncryptedMessages,
    double MyLatitude,
    double MyLongitude,
    string LastComPort,
    string LastTcpHost,                      // Last TCP/WiFi hostname or IP
    int LastTcpPort,                         // Last TCP/WiFi port
    Dictionary<uint, string> NodeColors,     // NodeId -> Color (hex)
    Dictionary<uint, string> NodeNotes,      // NodeId -> Note text
    bool DebugMessages,                      // Enable message debug logging
    bool DebugSerial,                        // Enable serial data hex dump
    bool DebugDevice,                        // Enable device serial debug output logging
    bool DebugBluetooth);                    // Enable BLE debug logging

public static class SettingsService
{
    private static string IniFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meshhessen-client.ini");

    public static AppSettings Load()
    {
        var defaults = new AppSettings(false, string.Empty, true, 50.9, 9.5, string.Empty, "192.168.1.1", 4403, new Dictionary<uint, string>(), new Dictionary<uint, string>(), false, false, false, false);

        try
        {
            if (!File.Exists(IniFilePath))
                return defaults;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(IniFilePath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[") || trimmed.StartsWith(";") || string.IsNullOrEmpty(trimmed))
                    continue;

                var eq = trimmed.IndexOf('=');
                if (eq > 0)
                    values[trimmed[..eq].Trim()] = trimmed[(eq + 1)..].Trim();
            }

            var lastComPort = values.TryGetValue("LastComPort", out var lcp) ? lcp : string.Empty;

            // Load node colors
            var nodeColors = new Dictionary<uint, string>();
            foreach (var key in values.Keys)
            {
                if (key.StartsWith("NodeColor_", StringComparison.OrdinalIgnoreCase))
                {
                    var nodeIdHex = key.Substring(10);
                    if (uint.TryParse(nodeIdHex, NumberStyles.HexNumber, null, out uint nodeId))
                    {
                        nodeColors[nodeId] = values[key];
                    }
                }
            }

            // Load node notes
            var nodeNotes = new Dictionary<uint, string>();
            foreach (var key in values.Keys)
            {
                if (key.StartsWith("NodeNote_", StringComparison.OrdinalIgnoreCase))
                {
                    var nodeIdHex = key.Substring(9);
                    if (uint.TryParse(nodeIdHex, NumberStyles.HexNumber, null, out uint nodeId))
                    {
                        nodeNotes[nodeId] = values[key];
                    }
                }
            }

            return new AppSettings(
                DarkMode: values.TryGetValue("DarkMode", out var dm) && bool.TryParse(dm, out var dmBool) ? dmBool : defaults.DarkMode,
                StationName: values.TryGetValue("StationName", out var sn) ? sn : defaults.StationName,
                ShowEncryptedMessages: !values.TryGetValue("ShowEncryptedMessages", out var se) || !bool.TryParse(se, out var seBool) || seBool,
                MyLatitude: values.TryGetValue("MyLatitude", out var lat) && double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latVal) ? latVal : defaults.MyLatitude,
                MyLongitude: values.TryGetValue("MyLongitude", out var lon) && double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lonVal) ? lonVal : defaults.MyLongitude,
                LastComPort: lastComPort,
                LastTcpHost: values.TryGetValue("LastTcpHost", out var tcpHost) ? tcpHost : defaults.LastTcpHost,
                LastTcpPort: values.TryGetValue("LastTcpPort", out var tcpPort) && int.TryParse(tcpPort, out var tcpPortInt) ? tcpPortInt : defaults.LastTcpPort,
                NodeColors: nodeColors,
                NodeNotes: nodeNotes,
                DebugMessages: values.TryGetValue("DebugMessages", out var dbg) && bool.TryParse(dbg, out var dbgBool) && dbgBool,
                DebugSerial: values.TryGetValue("DebugSerial", out var dbs) && bool.TryParse(dbs, out var dbsBool) && dbsBool,
                DebugDevice: values.TryGetValue("DebugDevice", out var dbd) && bool.TryParse(dbd, out var dbdBool) && dbdBool,
                DebugBluetooth: values.TryGetValue("DebugBluetooth", out var dbb) && bool.TryParse(dbb, out var dbbBool) && dbbBool
            );
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR loading settings: {ex.Message}");
            return defaults;
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var ci = CultureInfo.InvariantCulture;
            var lines = new List<string>
            {
                "[App]",
                $"DarkMode={settings.DarkMode}",
                $"StationName={settings.StationName}",
                $"ShowEncryptedMessages={settings.ShowEncryptedMessages}",
                $"MyLatitude={settings.MyLatitude.ToString("F7", ci)}",
                $"MyLongitude={settings.MyLongitude.ToString("F7", ci)}",
                $"LastComPort={settings.LastComPort}",
                $"LastTcpHost={settings.LastTcpHost}",
                $"LastTcpPort={settings.LastTcpPort}",
                $"DebugMessages={settings.DebugMessages}",
                $"DebugSerial={settings.DebugSerial}",
                $"DebugDevice={settings.DebugDevice}",
                $"DebugBluetooth={settings.DebugBluetooth}"
            };

            // Save node colors
            foreach (var kvp in settings.NodeColors)
            {
                lines.Add($"NodeColor_{kvp.Key:X8}={kvp.Value}");
            }

            // Save node notes
            foreach (var kvp in settings.NodeNotes)
            {
                lines.Add($"NodeNote_{kvp.Key:X8}={kvp.Value}");
            }

            File.WriteAllLines(IniFilePath, lines);
            Logger.WriteLine($"Settings saved to {IniFilePath}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR saving settings: {ex.Message}");
        }
    }
}
