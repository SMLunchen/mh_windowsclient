using System.IO;
using System.Globalization;

namespace MeshhessenClient.Services;

public enum PskMismatchAction { Warn = 0, Overwrite = 1, Ask = 2 }

public record AppSettings(
    bool DarkMode,
    string StationName,
    bool ShowEncryptedMessages,
    double MyLatitude,
    double MyLongitude,
    string LastComPort,
    string LastTcpHost,                      // Last TCP/WiFi hostname or IP
    int LastTcpPort,                         // Last TCP/WiFi port
    string MapSource,                        // Map tile source: "osm", "osmtopo", "osmdark"
    string OSMTileUrl,                       // Tile URL for OSM (including http:// or https://)
    string OSMTopoTileUrl,                   // Tile URL for OpenTopoMap (including http:// or https://)
    string OSMDarkTileUrl,                   // Tile URL for OSM Dark (including http:// or https://)
    Dictionary<uint, string> NodeColors,     // NodeId -> Color (hex)
    Dictionary<uint, string> NodeNotes,      // NodeId -> Note text
    bool DebugMessages,                      // Enable message debug logging
    bool DebugSerial,                        // Enable serial data hex dump
    bool DebugDevice,                        // Enable device serial debug output logging
    bool DebugBluetooth,                     // Enable BLE debug logging
    bool AlertBellSound,                     // Play sound on alert bell character
    string Language,                         // UI language: "de" or "en"
    bool EnableLocationLogging,              // Log GPS positions to locationlogs/
    Dictionary<uint, bool> PinnedNodes,      // NodeId -> pinned
    int TelemetryRetentionDays,              // 0=unlimited, 30/90/365
    PskMismatchAction NodeKeyMismatchAction, // Warn / Overwrite / Ask
    int SignalWeatherWindowHours,            // Short analysis window for weather detection (default 6h)
    int SignalAntennaWindowDays,             // Long analysis window for antenna trend (default 7d)
    int PositionHistoryHours,                // Hours of position history to show on map (0=unlimited, default 24)
    bool AutoTimeSyncOnConnect,              // Send time sync packet after connection init
    int TimeSyncDriftThresholdSeconds,       // Trigger time sync if rx_time drifts more than N seconds (default 300)
    string MapMode);                         // Tile fetch mode: "offline", "online-own", "online-osm"

public static class SettingsService
{
    private static string IniFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meshhessen-client.ini");

    public static AppSettings Load()
    {
        var defaults = new AppSettings(
            false,
            string.Empty,
            false,
            50.9,
            9.5,
            string.Empty,
            "192.168.1.1",
            4403,
            "osm",
            "https://tile.meshhessenclient.de/osm/{z}/{x}/{y}.png",        // OSM
            "https://tile.meshhessenclient.de/opentopo/{z}/{x}/{y}.png",   // OSM Topo
            "https://tile.meshhessenclient.de/dark/{z}/{x}/{y}.png",       // OSM Dark
            new Dictionary<uint, string>(),
            new Dictionary<uint, string>(),
            false,
            false,
            false,
            false,
            true,   // AlertBellSound default enabled
            "de",   // Language default German
            false,  // EnableLocationLogging default off
            new Dictionary<uint, bool>(),   // PinnedNodes
            90,                             // TelemetryRetentionDays default 90
            PskMismatchAction.Overwrite,    // NodeKeyMismatchAction default Overwrite
            6,                              // SignalWeatherWindowHours default 6h
            7,                              // SignalAntennaWindowDays default 7d
            24,                             // PositionHistoryHours default 24h
            true,                           // AutoTimeSyncOnConnect default on
            300,                            // TimeSyncDriftThresholdSeconds default 5min
            "offline");                     // MapMode default offline

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

            // Load pinned nodes
            var pinnedNodes = new Dictionary<uint, bool>();
            foreach (var key in values.Keys)
            {
                if (key.StartsWith("PinnedNode_", StringComparison.OrdinalIgnoreCase))
                {
                    var nodeIdHex = key.Substring(11);
                    if (uint.TryParse(nodeIdHex, NumberStyles.HexNumber, null, out uint nodeId))
                    {
                        pinnedNodes[nodeId] = true;
                    }
                }
            }

            // Migration: Convert old TileServerUrl to new format
            string osmUrl = defaults.OSMTileUrl;
            string osmTopoUrl = defaults.OSMTopoTileUrl;
            string osmDarkUrl = defaults.OSMDarkTileUrl;

            if (values.TryGetValue("TileServerUrl", out var oldTileServerUrl) && !string.IsNullOrWhiteSpace(oldTileServerUrl))
            {
                // Old format: just hostname without protocol
                // Convert to new format with https://
                osmUrl = $"https://{oldTileServerUrl}/osm/{{z}}/{{x}}/{{y}}.png";
                osmTopoUrl = $"https://{oldTileServerUrl}/opentopo/{{z}}/{{x}}/{{y}}.png";
                osmDarkUrl = $"https://{oldTileServerUrl}/dark/{{z}}/{{x}}/{{y}}.png";
            }

            // Load new individual URLs (override migration if present)
            if (values.TryGetValue("OSMTileUrl", out var osmUrlValue) && !string.IsNullOrWhiteSpace(osmUrlValue))
                osmUrl = osmUrlValue;
            if (values.TryGetValue("OSMTopoTileUrl", out var osmTopoUrlValue) && !string.IsNullOrWhiteSpace(osmTopoUrlValue))
                osmTopoUrl = osmTopoUrlValue;
            if (values.TryGetValue("OSMDarkTileUrl", out var osmDarkUrlValue) && !string.IsNullOrWhiteSpace(osmDarkUrlValue))
                osmDarkUrl = osmDarkUrlValue;

            return new AppSettings(
                DarkMode: values.TryGetValue("DarkMode", out var dm) && bool.TryParse(dm, out var dmBool) ? dmBool : defaults.DarkMode,
                StationName: values.TryGetValue("StationName", out var sn) ? sn : defaults.StationName,
                ShowEncryptedMessages: values.TryGetValue("ShowEncryptedMessages", out var se) && bool.TryParse(se, out var seBool) && seBool,
                MyLatitude: values.TryGetValue("MyLatitude", out var lat) && double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latVal) ? latVal : defaults.MyLatitude,
                MyLongitude: values.TryGetValue("MyLongitude", out var lon) && double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lonVal) ? lonVal : defaults.MyLongitude,
                LastComPort: lastComPort,
                LastTcpHost: values.TryGetValue("LastTcpHost", out var tcpHost) ? tcpHost : defaults.LastTcpHost,
                LastTcpPort: values.TryGetValue("LastTcpPort", out var tcpPort) && int.TryParse(tcpPort, out var tcpPortInt) ? tcpPortInt : defaults.LastTcpPort,
                MapSource: values.TryGetValue("MapSource", out var mapSrc) ? mapSrc : defaults.MapSource,
                OSMTileUrl: osmUrl,
                OSMTopoTileUrl: osmTopoUrl,
                OSMDarkTileUrl: osmDarkUrl,
                NodeColors: nodeColors,
                NodeNotes: nodeNotes,
                DebugMessages: values.TryGetValue("DebugMessages", out var dbg) && bool.TryParse(dbg, out var dbgBool) && dbgBool,
                DebugSerial: values.TryGetValue("DebugSerial", out var dbs) && bool.TryParse(dbs, out var dbsBool) && dbsBool,
                DebugDevice: values.TryGetValue("DebugDevice", out var dbd) && bool.TryParse(dbd, out var dbdBool) && dbdBool,
                DebugBluetooth: values.TryGetValue("DebugBluetooth", out var dbb) && bool.TryParse(dbb, out var dbbBool) && dbbBool,
                AlertBellSound: !values.TryGetValue("AlertBellSound", out var abs) || !bool.TryParse(abs, out var absBool) || absBool,
                Language: values.TryGetValue("Language", out var lang) && !string.IsNullOrEmpty(lang) ? lang : defaults.Language,
                EnableLocationLogging: values.TryGetValue("EnableLocationLogging", out var ell) && bool.TryParse(ell, out var ellBool) && ellBool,
                PinnedNodes: pinnedNodes,
                TelemetryRetentionDays: values.TryGetValue("TelemetryRetentionDays", out var trd) && int.TryParse(trd, out var trdInt) ? trdInt : defaults.TelemetryRetentionDays,
                NodeKeyMismatchAction: values.TryGetValue("NodeKeyMismatchAction", out var pkm) && Enum.TryParse(pkm, out PskMismatchAction pkmVal) ? pkmVal : defaults.NodeKeyMismatchAction,
                SignalWeatherWindowHours: values.TryGetValue("SignalWeatherWindowHours", out var swh) && int.TryParse(swh, out var swhInt) ? swhInt : defaults.SignalWeatherWindowHours,
                SignalAntennaWindowDays: values.TryGetValue("SignalAntennaWindowDays", out var sad) && int.TryParse(sad, out var sadInt) ? sadInt : defaults.SignalAntennaWindowDays,
                PositionHistoryHours: values.TryGetValue("PositionHistoryHours", out var phh) && int.TryParse(phh, out var phhInt) ? phhInt : defaults.PositionHistoryHours,
                AutoTimeSyncOnConnect: !values.TryGetValue("AutoTimeSyncOnConnect", out var ats) || !bool.TryParse(ats, out var atsBool) || atsBool,
                TimeSyncDriftThresholdSeconds: values.TryGetValue("TimeSyncDriftThresholdSeconds", out var tsd) && int.TryParse(tsd, out var tsdInt) ? tsdInt : defaults.TimeSyncDriftThresholdSeconds,
                MapMode: values.TryGetValue("MapMode", out var mapMode) && !string.IsNullOrEmpty(mapMode) ? mapMode : defaults.MapMode
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
                $"MapSource={settings.MapSource}",
                $"OSMTileUrl={settings.OSMTileUrl}",
                $"OSMTopoTileUrl={settings.OSMTopoTileUrl}",
                $"OSMDarkTileUrl={settings.OSMDarkTileUrl}",
                $"DebugMessages={settings.DebugMessages}",
                $"DebugSerial={settings.DebugSerial}",
                $"DebugDevice={settings.DebugDevice}",
                $"DebugBluetooth={settings.DebugBluetooth}",
                $"AlertBellSound={settings.AlertBellSound}",
                $"Language={settings.Language}",
                $"EnableLocationLogging={settings.EnableLocationLogging}",
                $"TelemetryRetentionDays={settings.TelemetryRetentionDays}",
                $"NodeKeyMismatchAction={(int)settings.NodeKeyMismatchAction}",
                $"SignalWeatherWindowHours={settings.SignalWeatherWindowHours}",
                $"SignalAntennaWindowDays={settings.SignalAntennaWindowDays}",
                $"PositionHistoryHours={settings.PositionHistoryHours}",
                $"AutoTimeSyncOnConnect={settings.AutoTimeSyncOnConnect}",
                $"TimeSyncDriftThresholdSeconds={settings.TimeSyncDriftThresholdSeconds}",
                $"MapMode={settings.MapMode}"
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

            // Save pinned nodes
            foreach (var kvp in settings.PinnedNodes.Where(p => p.Value))
            {
                lines.Add($"PinnedNode_{kvp.Key:X8}=true");
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
