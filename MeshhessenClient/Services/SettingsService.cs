using System.IO;
using System.Globalization;

namespace MeshhessenClient.Services;

public enum PskMismatchAction { Warn = 0, Overwrite = 1, Ask = 2 }

// Init-only properties with defaults instead of a 44-parameter positional record:
// construction via object initializer (named, order-independent), `with` still works,
// and the defaults live here once – no duplicated default block at the call sites.
public record AppSettings
{
    public bool DarkMode { get; init; } = false;
    public string StationName { get; init; } = string.Empty;
    public bool ShowEncryptedMessages { get; init; } = false;
    public double MyLatitude { get; init; } = 50.9;
    public double MyLongitude { get; init; } = 9.5;
    public string LastComPort { get; init; } = string.Empty;
    public string LastTcpHost { get; init; } = "192.168.1.1";   // Last TCP/WiFi hostname or IP
    public int LastTcpPort { get; init; } = 4403;               // Last TCP/WiFi port
    public string MapSource { get; init; } = "osm";             // "osm", "osmtopo", "osmdark"
    public string OSMTileUrl { get; init; } = "https://tile.meshhessenclient.de/osm/{z}/{x}/{y}.png";
    public string OSMTopoTileUrl { get; init; } = "https://tile.meshhessenclient.de/opentopo/{z}/{x}/{y}.png";
    public string OSMDarkTileUrl { get; init; } = "https://tile.meshhessenclient.de/dark/{z}/{x}/{y}.png";
    public Dictionary<uint, string> NodeColors { get; init; } = new();   // NodeId -> Color (hex)
    public Dictionary<uint, string> NodeNotes { get; init; } = new();    // NodeId -> Note text
    public bool DebugMessages { get; init; } = false;
    public bool DebugSerial { get; init; } = false;
    public bool DebugDevice { get; init; } = false;
    public bool DebugBluetooth { get; init; } = false;
    public bool AlertBellSound { get; init; } = true;           // Play sound on alert bell character
    public string Language { get; init; } = "de";              // UI language: "de" or "en"
    public bool EnableLocationLogging { get; init; } = false;   // Log GPS positions to locationlogs/
    public Dictionary<uint, bool> PinnedNodes { get; init; } = new();     // NodeId -> pinned
    public Dictionary<uint, bool> FavoriteNodes { get; init; } = new();   // NodeId -> favorite (synced with device)
    public int TelemetryRetentionDays { get; init; } = 90;      // 0=unlimited, 30/90/365
    public PskMismatchAction NodeKeyMismatchAction { get; init; } = PskMismatchAction.Overwrite;
    public int SignalWeatherWindowHours { get; init; } = 6;     // Short analysis window for weather detection
    public int SignalAntennaWindowDays { get; init; } = 7;      // Long analysis window for antenna trend
    public int PositionHistoryHours { get; init; } = 24;        // Position history on map (0=unlimited)
    public bool AutoTimeSyncOnConnect { get; init; } = true;    // Send time sync packet after connection init
    public int TimeSyncDriftThresholdSeconds { get; init; } = 300;  // Trigger time sync above N seconds drift
    public string MapMode { get; init; } = "offline";          // "offline", "online-own", "online-custom", "online-osm"
    public bool EnableMessageDb { get; init; } = true;         // Persist messages in SQLite DB
    public int MessageDbRetentionDays { get; init; } = 90;     // 0=unlimited, 30/90/365
    public string LastConnectionType { get; init; } = "Serial"; // "Serial", "Bluetooth", "Tcp"
    public string LastBtDevice { get; init; } = string.Empty;  // Last used Bluetooth device name
    public int RemoteAdminTimeoutSeconds { get; init; } = 30;  // Remote admin request timeout (seconds)
    public bool VirtualNodeEnabled { get; init; } = false;     // Enable Virtual Node TCP proxy server
    public int VirtualNodePort { get; init; } = 4404;          // TCP port for Virtual Node
    public bool VirtualNodeBlockAdmin { get; init; } = false;  // Block admin commands from Virtual Node clients
    public Dictionary<uint, string> NodeStationNames { get; init; } = new();  // NodeId -> per-node station name
    public bool FancyNodeList { get; init; } = false;          // Tile view instead of table in Nodes tab
    public bool FancyNodeListColorful { get; init; } = true;   // Color tiles by signal quality
    public bool KioskModeEnabled { get; init; } = false;       // Kiosk/training mode: lockable UI
    public string KioskPasswordHash { get; init; } = string.Empty;  // PBKDF2 "salt:hash" (base64); empty = none
    public string KioskLockedFeatures { get; init; } = string.Empty; // CSV of feature keys hidden while locked
    public string MapRenderMode { get; init; } = "raster";     // "raster" (Mapsui) or "vector" (MapLibre/WebView2)
    public string VectorStyleOsmUrl { get; init; } = "https://vectortile.meshhessenclient.de/styles/osm.json";
    public string VectorStyleTopoUrl { get; init; } = "https://vectortile.meshhessenclient.de/styles/opentopo.json";
    public string VectorStyleDarkUrl { get; init; } = "https://vectortile.meshhessenclient.de/styles/dark.json";
    public string MapOverlays { get; init; } = string.Empty;   // CSV of active vector overlay keys (MapOverlayRegistry)

    // ── Environment data on the map (opt-in) ──────────────────────────────────
    public bool ShowEnvironmentData { get; init; } = false;    // master switch (Settings); unlocks the map controls
    public string EnvBoxMode { get; init; } = "always";        // value boxes: "off" | "always" | "hover"
    public bool EnvShowHeatmap { get; init; } = false;         // heatmap overlay (vector map only)
    public string EnvMetric { get; init; } = "temperature";    // metric for the heatmap (EnvironmentMetricInfo key)
    public string EnvDisabledNodes { get; init; } = string.Empty; // CSV of node ids excluded from boxes+heatmap
}

public static class SettingsService
{
    private static string IniFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meshhessen-client.ini");

    public static AppSettings Load()
    {
        var defaults = new AppSettings();  // all defaults defined on the record

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

            // Load favorite nodes
            var favoriteNodes = new Dictionary<uint, bool>();
            foreach (var key in values.Keys)
            {
                if (key.StartsWith("FavoriteNode_", StringComparison.OrdinalIgnoreCase))
                {
                    var nodeIdHex = key.Substring(13);
                    if (uint.TryParse(nodeIdHex, NumberStyles.HexNumber, null, out uint nodeId))
                    {
                        favoriteNodes[nodeId] = true;
                    }
                }
            }

            // Load per-node station names
            var nodeStationNames = new Dictionary<uint, string>();
            foreach (var key in values.Keys)
            {
                if (key.StartsWith("NodeStationName_", StringComparison.OrdinalIgnoreCase))
                {
                    var nodeIdHex = key.Substring(16);
                    if (uint.TryParse(nodeIdHex, NumberStyles.HexNumber, null, out uint nodeId))
                        nodeStationNames[nodeId] = values[key];
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

            return new AppSettings
            {
                DarkMode = values.TryGetValue("DarkMode", out var dm) && bool.TryParse(dm, out var dmBool) ? dmBool : defaults.DarkMode,
                StationName = values.TryGetValue("StationName", out var sn) ? sn : defaults.StationName,
                ShowEncryptedMessages = values.TryGetValue("ShowEncryptedMessages", out var se) && bool.TryParse(se, out var seBool) && seBool,
                MyLatitude = values.TryGetValue("MyLatitude", out var lat) && double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latVal) ? latVal : defaults.MyLatitude,
                MyLongitude = values.TryGetValue("MyLongitude", out var lon) && double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lonVal) ? lonVal : defaults.MyLongitude,
                LastComPort = lastComPort,
                LastTcpHost = values.TryGetValue("LastTcpHost", out var tcpHost) ? tcpHost : defaults.LastTcpHost,
                LastTcpPort = values.TryGetValue("LastTcpPort", out var tcpPort) && int.TryParse(tcpPort, out var tcpPortInt) ? tcpPortInt : defaults.LastTcpPort,
                MapSource = values.TryGetValue("MapSource", out var mapSrc) ? mapSrc : defaults.MapSource,
                OSMTileUrl = osmUrl,
                OSMTopoTileUrl = osmTopoUrl,
                OSMDarkTileUrl = osmDarkUrl,
                NodeColors = nodeColors,
                NodeNotes = nodeNotes,
                DebugMessages = values.TryGetValue("DebugMessages", out var dbg) && bool.TryParse(dbg, out var dbgBool) && dbgBool,
                DebugSerial = values.TryGetValue("DebugSerial", out var dbs) && bool.TryParse(dbs, out var dbsBool) && dbsBool,
                DebugDevice = values.TryGetValue("DebugDevice", out var dbd) && bool.TryParse(dbd, out var dbdBool) && dbdBool,
                DebugBluetooth = values.TryGetValue("DebugBluetooth", out var dbb) && bool.TryParse(dbb, out var dbbBool) && dbbBool,
                AlertBellSound = !values.TryGetValue("AlertBellSound", out var abs) || !bool.TryParse(abs, out var absBool) || absBool,
                Language = values.TryGetValue("Language", out var lang) && !string.IsNullOrEmpty(lang) ? lang : defaults.Language,
                EnableLocationLogging = values.TryGetValue("EnableLocationLogging", out var ell) && bool.TryParse(ell, out var ellBool) && ellBool,
                PinnedNodes = pinnedNodes,
                FavoriteNodes = favoriteNodes,
                TelemetryRetentionDays = values.TryGetValue("TelemetryRetentionDays", out var trd) && int.TryParse(trd, out var trdInt) ? trdInt : defaults.TelemetryRetentionDays,
                NodeKeyMismatchAction = values.TryGetValue("NodeKeyMismatchAction", out var pkm) && Enum.TryParse(pkm, out PskMismatchAction pkmVal) ? pkmVal : defaults.NodeKeyMismatchAction,
                SignalWeatherWindowHours = values.TryGetValue("SignalWeatherWindowHours", out var swh) && int.TryParse(swh, out var swhInt) ? swhInt : defaults.SignalWeatherWindowHours,
                SignalAntennaWindowDays = values.TryGetValue("SignalAntennaWindowDays", out var sad) && int.TryParse(sad, out var sadInt) ? sadInt : defaults.SignalAntennaWindowDays,
                PositionHistoryHours = values.TryGetValue("PositionHistoryHours", out var phh) && int.TryParse(phh, out var phhInt) ? phhInt : defaults.PositionHistoryHours,
                AutoTimeSyncOnConnect = !values.TryGetValue("AutoTimeSyncOnConnect", out var ats) || !bool.TryParse(ats, out var atsBool) || atsBool,
                TimeSyncDriftThresholdSeconds = values.TryGetValue("TimeSyncDriftThresholdSeconds", out var tsd) && int.TryParse(tsd, out var tsdInt) ? tsdInt : defaults.TimeSyncDriftThresholdSeconds,
                MapMode = values.TryGetValue("MapMode", out var mapMode) && !string.IsNullOrEmpty(mapMode) ? mapMode : defaults.MapMode,
                EnableMessageDb = values.TryGetValue("EnableMessageDb", out var emdb) && bool.TryParse(emdb, out var emdbBool) ? emdbBool : defaults.EnableMessageDb,
                MessageDbRetentionDays = values.TryGetValue("MessageDbRetentionDays", out var mdr) && int.TryParse(mdr, out var mdrInt) ? mdrInt : defaults.MessageDbRetentionDays,
                LastConnectionType = values.TryGetValue("LastConnectionType", out var lct) && !string.IsNullOrEmpty(lct) ? lct : defaults.LastConnectionType,
                LastBtDevice = values.TryGetValue("LastBtDevice", out var lbd) ? lbd : defaults.LastBtDevice,
                RemoteAdminTimeoutSeconds = values.TryGetValue("RemoteAdminTimeoutSeconds", out var rats) && int.TryParse(rats, out var ratsInt) ? ratsInt : defaults.RemoteAdminTimeoutSeconds,
                VirtualNodeEnabled = values.TryGetValue("VirtualNodeEnabled", out var vne) && bool.TryParse(vne, out var vneBool) && vneBool,
                VirtualNodePort = values.TryGetValue("VirtualNodePort", out var vnp) && int.TryParse(vnp, out var vnpInt) ? vnpInt : defaults.VirtualNodePort,
                VirtualNodeBlockAdmin = values.TryGetValue("VirtualNodeBlockAdmin", out var vnba) && bool.TryParse(vnba, out var vnbaBool) && vnbaBool,
                NodeStationNames = nodeStationNames,
                FancyNodeList = values.TryGetValue("FancyNodeList", out var fnl) && bool.TryParse(fnl, out var fnlBool) && fnlBool,
                FancyNodeListColorful = !values.TryGetValue("FancyNodeListColorful", out var fnc) || !bool.TryParse(fnc, out var fncBool) || fncBool,
                KioskModeEnabled = values.TryGetValue("KioskModeEnabled", out var kme) && bool.TryParse(kme, out var kmeBool) && kmeBool,
                KioskPasswordHash = values.TryGetValue("KioskPasswordHash", out var kph) ? kph : string.Empty,
                KioskLockedFeatures = values.TryGetValue("KioskLockedFeatures", out var klf) ? klf : string.Empty,
                MapRenderMode = values.TryGetValue("MapRenderMode", out var mrm) && !string.IsNullOrEmpty(mrm) ? mrm : defaults.MapRenderMode,
                VectorStyleOsmUrl = values.TryGetValue("VectorStyleOsmUrl", out var vso) && !string.IsNullOrWhiteSpace(vso) ? vso : defaults.VectorStyleOsmUrl,
                VectorStyleTopoUrl = values.TryGetValue("VectorStyleTopoUrl", out var vst) && !string.IsNullOrWhiteSpace(vst) ? vst : defaults.VectorStyleTopoUrl,
                VectorStyleDarkUrl = values.TryGetValue("VectorStyleDarkUrl", out var vsd) && !string.IsNullOrWhiteSpace(vsd) ? vsd : defaults.VectorStyleDarkUrl,
                MapOverlays = values.TryGetValue("MapOverlays", out var mov) ? mov : defaults.MapOverlays,
                ShowEnvironmentData = values.TryGetValue("ShowEnvironmentData", out var sed) && bool.TryParse(sed, out var sedBool) && sedBool,
                EnvBoxMode = values.TryGetValue("EnvBoxMode", out var ebm) && !string.IsNullOrEmpty(ebm) ? ebm : defaults.EnvBoxMode,
                EnvShowHeatmap = values.TryGetValue("EnvShowHeatmap", out var esh) && bool.TryParse(esh, out var eshBool) && eshBool,
                EnvMetric = values.TryGetValue("EnvMetric", out var emk) && !string.IsNullOrEmpty(emk) ? emk : defaults.EnvMetric,
                EnvDisabledNodes = values.TryGetValue("EnvDisabledNodes", out var edn) ? edn : defaults.EnvDisabledNodes
            };
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
                $"MapMode={settings.MapMode}",
                $"EnableMessageDb={settings.EnableMessageDb}",
                $"MessageDbRetentionDays={settings.MessageDbRetentionDays}",
                $"LastConnectionType={settings.LastConnectionType}",
                $"LastBtDevice={settings.LastBtDevice}",
                $"RemoteAdminTimeoutSeconds={settings.RemoteAdminTimeoutSeconds}",
                $"VirtualNodeEnabled={settings.VirtualNodeEnabled}",
                $"VirtualNodePort={settings.VirtualNodePort}",
                $"VirtualNodeBlockAdmin={settings.VirtualNodeBlockAdmin}",
                $"FancyNodeList={settings.FancyNodeList}",
                $"FancyNodeListColorful={settings.FancyNodeListColorful}",
                $"KioskModeEnabled={settings.KioskModeEnabled}",
                $"KioskPasswordHash={settings.KioskPasswordHash}",
                $"KioskLockedFeatures={settings.KioskLockedFeatures}",
                $"MapRenderMode={settings.MapRenderMode}",
                $"VectorStyleOsmUrl={settings.VectorStyleOsmUrl}",
                $"VectorStyleTopoUrl={settings.VectorStyleTopoUrl}",
                $"VectorStyleDarkUrl={settings.VectorStyleDarkUrl}",
                $"MapOverlays={settings.MapOverlays}",
                $"ShowEnvironmentData={settings.ShowEnvironmentData}",
                $"EnvBoxMode={settings.EnvBoxMode}",
                $"EnvShowHeatmap={settings.EnvShowHeatmap}",
                $"EnvMetric={settings.EnvMetric}",
                $"EnvDisabledNodes={settings.EnvDisabledNodes}"
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

            // Save favorite nodes
            foreach (var kvp in settings.FavoriteNodes.Where(p => p.Value))
            {
                lines.Add($"FavoriteNode_{kvp.Key:X8}=true");
            }

            // Save per-node station names
            foreach (var kvp in settings.NodeStationNames.Where(p => !string.IsNullOrEmpty(p.Value)))
                lines.Add($"NodeStationName_{kvp.Key:X8}={kvp.Value}");

            File.WriteAllLines(IniFilePath, lines);
            Logger.WriteLine($"Settings saved to {IniFilePath}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR saving settings: {ex.Message}");
        }
    }
}
