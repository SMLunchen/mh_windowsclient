using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using MeshhessenClient.Models;
using MeshhessenClient.Services;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Projections;
using Mapsui.Tiling.Layers;
using Mapsui.Extensions;
using BruTile;
using BruTile.Predefined;
using NetTopologySuite.Geometries;
using Mapsui.Nts;

namespace MeshhessenClient;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Initializing,
    Ready,
    Disconnecting,
    Error
}

public partial class MainWindow : Window
{
    private IConnectionService? _connectionService;
    private MeshtasticProtocolService _protocolService;
    private Services.ConnectionType _currentConnectionType = Services.ConnectionType.Serial;

    private ObservableCollection<MessageItem> _messages = new();
    private List<MessageItem> _allMessages = new(); // Ungefilterte Liste aller Nachrichten
    private ObservableCollection<Models.NodeInfo> _nodes = new();
    private List<Models.NodeInfo> _allNodes = new(); // Ungefilterte Liste aller Nodes
    private ObservableCollection<ChannelInfo> _channels = new();
    private ObservableCollection<Models.BluetoothDeviceInfo> _bluetoothDevices = new();
    private int _activeChannelIndex = 0;
    private bool _showEncryptedMessages = false;
    private ChannelInfo? _messageChannelFilter = null;
    private DirectMessagesWindow? _dmWindow = null;
    private uint _myNodeId = 0;
    private string _activeStationName = string.Empty;
    private bool _kioskLocked = false;   // kiosk/training mode: true = features hidden
    private bool _settingsDirty = false;         // unsaved changes in the Settings tab
    private bool _suppressDirtyTracking = false;  // true while programmatically loading settings
    private string? _nodeSortColumn = null;

    // Fancy tile view
    private Models.NodeInfo? _tileContextNode;
    private System.Windows.Threading.DispatcherTimer? _tileSortFilterTimer;
    private bool _tileSortFilterPending;
    private int _tileColumnCount = 3;
    private Models.NodeInfo? SelectedNodeForMenu =>
        _tileContextNode ?? (NodesListView?.SelectedItem as Models.NodeInfo);

    // Neighbour lines
    private MemoryLayer? _neighborLinesLayer;
    private readonly List<IFeature> _neighborLineFeatures = new();
    private bool _showNeighborLines        = false;
    private bool _neighborColorByAge       = false;   // false = SNR, true = age
    private bool _neighborPermanent        = false;   // true = ignore 24 h cutoff

    // Node-list advanced filters
    private int  _nodeFilterLastSeenMinutes = 0;
    private bool _nodeFilterHideMqtt        = false;
    private bool _nodeFilterOnlyFavorites   = false;
    private bool _nodeSortAscending = true;
    private LoRaConfig? _currentLoRaConfig;

    // Karte
    private Mapsui.Map? _map;
    // Vektor-Karte (MapLibre GL JS in WebView2)
    private Services.VectorTileCacheService? _vectorTileCache;
    private bool _vectorMapInitStarted = false;  // CoreWebView2 init/navigation kicked off
    private bool _vectorMapReady = false;        // map.html loaded, JS API callable
    private MemoryLayer? _nodeLayer;
    private MemoryLayer? _myPosLayer;
    private readonly List<IFeature> _nodeFeatures = new();
    private readonly List<IFeature> _myPosFeatures = new();
    private readonly Dictionary<uint, MPoint> _nodePinPositions = new();
    private readonly Dictionary<uint, MPoint> _waypointPinPositions = new();
    private readonly Dictionary<uint, MemoryLayer> _pathLayers = new();
    // Placeholder until LoadSettings() runs in the constructor; all record defaults apply
    private AppSettings _currentSettings = new();
    private NodeInfo? _mapContextMenuNode;
    private uint? _alertNodeId;  // Stores the node ID for "Show on Map" button

    // Message DB
    private Services.MessageDbManager? _messageDbManager;
    private long _dbOldestTimestamp = long.MaxValue; // Oldest timestamp currently in memory (for lazy load)

    // Traceroute + Reactions
    private readonly Dictionary<uint, TracerouteWindow> _tracerouteWindows = new();
    // Keyed by layerKey: "live_{destId:x8}" for live routes, filename for loaded routes
    private readonly Dictionary<string, MemoryLayer> _tracerouteLayers = new();
    private readonly Dictionary<string, string> _tracerouteNames = new();  // layerKey ? display name
    private readonly Dictionary<string, Mapsui.Styles.Color> _tracerouteColors = new(); // layerKey ? color
    private int _tracerouteColorIndex = 0;

    // Segment midpoints for click detection (T3)
    private record SegmentHitTarget(MPoint Midpoint, uint FromId, uint ToId, float? CurrentSnr, bool IsMqtt);
    private readonly Dictionary<string, List<SegmentHitTarget>> _tracerouteSegmentHits = new();
    private System.Windows.Point _mapMouseDownPos; // For drag vs click detection

    // Waypoints map layer (W1+W2)
    private MemoryLayer? _waypointLayer;
    private readonly List<TelemetryDatabaseService.WaypointEntry> _waypoints = new();

    // Palette: avoids Blue (my node), Red/custom (other nodes), Orange (common node color)
    private static readonly Mapsui.Styles.Color[] TracerouteColorPalette =
    {
        new(  0, 191, 255, 255), // Deep Sky Blue
        new(174, 234,   0, 255), // Lime
        new(245,   0,  87, 255), // HotPink
        new(170,   0, 255, 255), // Purple
        new(255, 214,   0, 255), // Yellow
        new(  0, 230, 118, 255), // SpringGreen
        new( 29, 233, 182, 255), // Teal
        new(255, 171,  64, 255), // Amber
    };
    // Map from packet-ID ? MessageItem (for attaching reactions)
    private readonly Dictionary<uint, MessageItem> _messageById = new();
    // Currently pending reply (set by context menu "Reply")
    private MessageItem? _replyToMessage;

    // Telemetry DB
    private TelemetryDatabaseService? _db;

    // Signal Analysis Background Timer
    private System.Threading.Timer? _analysisTimer;

    // Time Sync
    private System.Threading.Timer? _timeSyncTimer;

    // Node Public Key CSV
    private NodeKeyService? _nodeKeyService;

    // MQTT Proxy
    private MqttProxyService? _mqttProxyService;

    // Virtual Node
    private Services.VirtualNodeService? _virtualNodeService;

    // Reconnect state
    private ConnectionParameters? _lastConnectionParams;
    private Services.ConnectionType _lastConnectionType;
    private bool _intentionalDisconnect = false;
    private bool _isReconnecting = false;

    // Easter Eggs
    private System.Threading.Timer? _midnightTimer;
    private bool _midnightFiredToday = false;
    private int _logoClickCount = 0;
    private DateTime _lastLogoClick = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        // Set version from assembly
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "";
        Title = $"Meshhessen Client {versionStr}";
        AboutVersionText.Text = versionStr;
        FooterVersionText.Text = versionStr;

        Loaded += (_, _) => _ = CheckForUpdateAsync();
        _midnightTimer = new System.Threading.Timer(_ => CheckMidnight(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // Initialize with Serial connection (default)
        _connectionService = new SerialConnectionService();
        _protocolService = new MeshtasticProtocolService(_connectionService);

        MessageListView.ItemsSource = _messages;
        NodesListView.ItemsSource = _nodes;
        ChannelsListView.ItemsSource = _channels;
        ActiveChannelComboBox.ItemsSource = _channels;
        BluetoothDeviceComboBox.ItemsSource = _bluetoothDevices;

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _protocolService.MessageReceived += OnMessageReceived;
        _protocolService.NodeInfoReceived += OnNodeInfoReceived;
        _protocolService.ChannelInfoReceived += OnChannelInfoReceived;
        _protocolService.LoRaConfigReceived += OnLoRaConfigReceived;
        _protocolService.DeviceInfoReceived += OnDeviceInfoReceived;
        _protocolService.PacketCountChanged += OnPacketCountChanged;
        _protocolService.TracerouteReceived += OnTracerouteReceived;
        _protocolService.ReactionReceived += OnReactionReceived;
        _protocolService.DeviceTelemetryReceived += OnDeviceTelemetryReceived;
        _protocolService.TimeDriftDetected += OnTimeDriftDetected;
        _protocolService.WaypointReceived  += OnWaypointReceived;
        _protocolService.WaypointDeleted   += (s, id) => Dispatcher.Invoke(() => OnWaypointDeleted(id));
        _protocolService.MqttConfigReceived += OnMqttConfigReceived;

        _mqttProxyService = new MqttProxyService(_protocolService);
        _mqttProxyService.StatusChanged += (s, msg) => Dispatcher.Invoke(() => UpdateStatusBar(msg));

        // LoadRegions / LoadModemPresets removed – now displayed as read-only TextBlocks in Settings right column

        // Context menu opening for dynamic pin label
        NodesListView.ContextMenuOpening += NodesListView_ContextMenuOpening;

        // Logger Event abonnieren für Debug-Fenster
        Services.Logger.LogMessageReceived += OnLogMessageReceived;

        // Zeige Log-Datei-Pfad in der Status-Leiste
        var logPath = Services.Logger.GetLogFilePath();
        if (!string.IsNullOrEmpty(logPath))
        {
            UpdateStatusBar($"Log-Datei: {logPath}");
        }

        // Checkbox für verschlüsselte Nachrichten
        ShowEncryptedMessagesCheckBox.Checked += (s, e) => _showEncryptedMessages = true;
        ShowEncryptedMessagesCheckBox.Unchecked += (s, e) => _showEncryptedMessages = false;

        // Overlay-Checkboxen aus der Registry erzeugen (vor LoadSettings, das die Zustände setzt)
        BuildOverlayPanels();

        // Einstellungen laden (VOR RefreshPorts, damit LastComPort bekannt ist)
        LoadSettings();

        // Dirty-Tracking der Einstellungen verdrahten, sobald der Settings-Tab-Content
        // im Visual Tree ist (TabControl realisiert Tab-Inhalte erst bei Auswahl).
        bool dirtyWired = false;
        MainTabs.SelectionChanged += (_, _) =>
        {
            if (!dirtyWired && ReferenceEquals(MainTabs.SelectedItem, TabItemSettings))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    WireSettingsDirtyTracking();
                    SetSettingsDirty(false);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                dirtyWired = true;
            }
        };

        // Telemetry DB initialisieren
        try
        {
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "telemetry.db");
            _db = new TelemetryDatabaseService(dbPath);
            _db.Latitude  = _currentSettings.MyLatitude;
            _db.Longitude = _currentSettings.MyLongitude;
            _db.RunRetentionCleanup(_currentSettings.TelemetryRetentionDays);
            _db.DeleteOldNodePositions(_currentSettings.PositionHistoryHours);
            _protocolService.SetDatabase(_db);
            _protocolService.TimeDriftThresholdSeconds = _currentSettings.TimeSyncDriftThresholdSeconds;
            Services.Logger.WriteLine($"TelemetryDB initialized: {dbPath}");
            // Populate global status LEDs and waypoints from DB at startup
            Dispatcher.BeginInvoke(LoadWaypointsFromDb);
            // Populate global status LEDs from DB history immediately on startup (no connection required)
            Task.Run(RefreshSignalAnalysis);
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"TelemetryDB init failed: {ex.Message}");
        }

        // NodeKey CSV Service
        try
        {
            var csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "node_keys.csv");
            _nodeKeyService = new NodeKeyService(csvPath);
            _nodeKeyService.KeyMismatchDetected += OnNodeKeyMismatch;
            _protocolService.SetNodeKeyService(_nodeKeyService);
            _protocolService.SetPskMismatchAction(_currentSettings.NodeKeyMismatchAction);
            Services.Logger.WriteLine($"NodeKeyService initialized: {csvPath}");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"NodeKeyService init failed: {ex.Message}");
        }

        RefreshPorts();

        // Karte initialisieren
        InitializeMap();

        // Tile-Migration nach dem Laden des Fensters prüfen
        this.Loaded += async (s, e) => await CheckAndRunTileMigration();
    }

    private async Task CheckAndRunTileMigration()
    {
        var tileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");

        if (Services.TileMigrationService.IsMigrationNeeded(tileDir))
        {
            var count = Services.TileMigrationService.CountTilesToMigrate(tileDir);
            Services.Logger.WriteLine($"[Startup] Tile migration needed: {count} files");

            var migrationWin = new MigrationProgressWindow { Owner = this };
            migrationWin.Show();

            await migrationWin.RunMigrationAsync(tileDir);

            // Karte neu laden nach Migration
            Services.Logger.WriteLine("[Startup] Reloading map after migration");
            InitializeMap();
            UpdateMapTileStatus();
        }
    }

    private void LoadSettings()
    {
        _suppressDirtyTracking = true;
        try
        {
            var settings = SettingsService.Load();
            DarkModeCheckBox.IsChecked = settings.DarkMode;
            StationNameTextBox.Text = settings.StationName;
            RefreshActiveStationName();
            ShowEncryptedMessagesCheckBox.IsChecked = settings.ShowEncryptedMessages;
            _showEncryptedMessages = settings.ShowEncryptedMessages;
            DebugMessagesCheckBox.IsChecked = settings.DebugMessages;
            DebugSerialCheckBox.IsChecked = settings.DebugSerial;
            DebugDeviceCheckBox.IsChecked = settings.DebugDevice;
            DebugBluetoothCheckBox.IsChecked = settings.DebugBluetooth;
            AlertBellSoundCheckBox.IsChecked = settings.AlertBellSound;
            EnableLocationLoggingCheckBox.IsChecked = settings.EnableLocationLogging;

            // Language ComboBox
            foreach (System.Windows.Controls.ComboBoxItem item in LanguageComboBox.Items)
            {
                if ((item.Tag as string) == settings.Language)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }
            ApplyLanguage(settings.Language);

            _currentSettings = settings;
            _protocolService.SetDebugSerial(settings.DebugSerial);
            _protocolService.SetDebugDevice(settings.DebugDevice);
            BluetoothConnectionService.SetDebugEnabled(settings.DebugBluetooth);

            // Load TCP settings
            TcpHostTextBox.Text = settings.LastTcpHost;
            TcpPortTextBox.Text = settings.LastTcpPort.ToString();

            // Load Tile Server URLs
            TileDownloaderService.OSMTileUrl = settings.OSMTileUrl;
            TileDownloaderService.OSMTopoTileUrl = settings.OSMTopoTileUrl;
            TileDownloaderService.OSMDarkTileUrl = settings.OSMDarkTileUrl;

            // Display URL for current map source
            TileServerUrlTextBox.Text = settings.MapSource switch
            {
                "osm" => settings.OSMTileUrl,
                "osmtopo" => settings.OSMTopoTileUrl,
                "osmdark" => settings.OSMDarkTileUrl,
                _ => settings.OSMTileUrl
            };

            // Load Map Source
            bool foundMapSource = false;
            foreach (System.Windows.Controls.ComboBoxItem item in MapSourceComboBox.Items)
            {
                if ((item.Tag as string) == settings.MapSource)
                {
                    MapSourceComboBox.SelectedItem = item;
                    foundMapSource = true;
                    break;
                }
            }
            // Fallback to first item (OSM Standard) if not found
            if (!foundMapSource && MapSourceComboBox.Items.Count > 0)
            {
                MapSourceComboBox.SelectedIndex = 0;
            }

            // Load Map Mode
            MapModeOfflineRadio.IsChecked      = settings.MapMode is not ("online-own" or "online-osm" or "online-custom");
            MapModeOnlineOwnRadio.IsChecked    = settings.MapMode == "online-own";
            MapModeOnlineCustomRadio.IsChecked = settings.MapMode == "online-custom";
            MapModeOnlineOsmRadio.IsChecked    = settings.MapMode == "online-osm";

            // Load Map Render Mode (raster/vector)
            MapRenderRasterRadio.IsChecked = settings.MapRenderMode != "vector";
            MapRenderVectorRadio.IsChecked = settings.MapRenderMode == "vector";

            // Load overlay states into both checkbox panels (settings + map popup)
            SyncOverlayCheckboxes();
            VectorStyleUrlTextBox.Text = settings.MapSource switch
            {
                "osmtopo" => settings.VectorStyleTopoUrl,
                "osmdark" => settings.VectorStyleDarkUrl,
                _ => settings.VectorStyleOsmUrl
            };

            ApplyMapModeUi(settings.MapMode);

            if (settings.DarkMode)
                ModernWpf.ThemeManager.Current.ApplicationTheme = ModernWpf.ApplicationTheme.Dark;

            // Retention ComboBox
            foreach (System.Windows.Controls.ComboBoxItem item in TelemetryRetentionComboBox.Items)
            {
                if (item.Tag is string tagStr && int.TryParse(tagStr, out var tagVal) && tagVal == settings.TelemetryRetentionDays)
                {
                    TelemetryRetentionComboBox.SelectedItem = item;
                    break;
                }
            }

            // PSK Mismatch RadioButtons
            PskWarnRadio.IsChecked      = settings.NodeKeyMismatchAction == Services.PskMismatchAction.Warn;
            PskOverwriteRadio.IsChecked = settings.NodeKeyMismatchAction == Services.PskMismatchAction.Overwrite;
            PskAskRadio.IsChecked       = settings.NodeKeyMismatchAction == Services.PskMismatchAction.Ask;

            // Signal Analysis Windows
            WeatherHoursBox.Text = settings.SignalWeatherWindowHours.ToString();
            AntennaDaysBox.Text  = settings.SignalAntennaWindowDays.ToString();

            // Position History
            foreach (System.Windows.Controls.ComboBoxItem item in PositionHistoryComboBox.Items)
            {
                if (item.Tag is string tag && tag == settings.PositionHistoryHours.ToString())
                {
                    PositionHistoryComboBox.SelectedItem = item;
                    break;
                }
            }

            // Time Sync
            if (AutoTimeSyncCheckBox != null) AutoTimeSyncCheckBox.IsChecked = settings.AutoTimeSyncOnConnect;
            if (TimeSyncDriftBox != null) TimeSyncDriftBox.Text = settings.TimeSyncDriftThresholdSeconds.ToString();

            // Remote Admin
            if (RemoteAdminTimeoutTextBox != null) RemoteAdminTimeoutTextBox.Text = settings.RemoteAdminTimeoutSeconds.ToString();

            // Fancy Node List
            if (FancyNodeListCheckBox != null)         FancyNodeListCheckBox.IsChecked         = settings.FancyNodeList;
            Models.NodeInfo.FancyColorful = settings.FancyNodeListColorful;
            ApplyFancyNodeListSetting(settings.FancyNodeList);

            // Kiosk / Training mode
            if (KioskEnableCheckBox != null) KioskEnableCheckBox.IsChecked = settings.KioskModeEnabled;
            ApplyKioskCheckboxes(settings.KioskLockedFeatures);
            // Kiosk stations always start locked (survives restart)
            if (settings.KioskModeEnabled && !string.IsNullOrEmpty(settings.KioskPasswordHash))
                ApplyKioskLock(true);
            else
                UpdateKioskLockButton();

            // Virtual Node
            if (VirtualNodeEnableCheckBox != null) VirtualNodeEnableCheckBox.IsChecked = settings.VirtualNodeEnabled;
            if (VirtualNodePortBox != null) VirtualNodePortBox.Text = settings.VirtualNodePort.ToString();
            if (VirtualNodeBlockAdminCheckBox != null) VirtualNodeBlockAdminCheckBox.IsChecked = settings.VirtualNodeBlockAdmin;
            RefreshVirtualNodeStatus();

            // Message DB
            EnableMessageDbCheckBox.IsChecked = settings.EnableMessageDb;
            foreach (System.Windows.Controls.ComboBoxItem item in MessageDbRetentionComboBox.Items)
            {
                if (item.Tag is string tagStr && int.TryParse(tagStr, out var tagVal) && tagVal == settings.MessageDbRetentionDays)
                {
                    MessageDbRetentionComboBox.SelectedItem = item;
                    break;
                }
            }

            // Restore last connection type
            switch (settings.LastConnectionType)
            {
                case "Bluetooth": BluetoothRadioButton.IsChecked = true; break;
                case "Tcp":       WifiRadioButton.IsChecked = true; break;
                default:          SerialRadioButton.IsChecked = true; break;
            }

            // Initialize message DB manager if enabled
            if (settings.EnableMessageDb)
            {
                var msgDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "messages");
                if (_messageDbManager == null)
                    _messageDbManager = new Services.MessageDbManager(msgDir);
                // Apply retention on startup
                if (settings.MessageDbRetentionDays > 0)
                    Task.Run(() => _messageDbManager.ApplyRetention(settings.MessageDbRetentionDays));
            }
            else
            {
                _messageDbManager?.Dispose();
                _messageDbManager = null;
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR loading settings: {ex.Message}");
            _showEncryptedMessages = false;
        }
        finally
        {
            _suppressDirtyTracking = false;
            SetSettingsDirty(false);
        }
    }

    // ── Settings "unsaved changes" tracking ────────────────────────────────────

    // Controls that persist themselves immediately (language, map mode) must not
    // trigger the "unsaved changes" indicator.
    private static readonly HashSet<string> _dirtyExemptControls = new()
    {
        "LanguageComboBox", "MapModeOfflineRadio", "MapModeOnlineOwnRadio",
        "MapModeOnlineCustomRadio", "MapModeOnlineOsmRadio"
    };

    /// <summary>Attaches change handlers to every input control in the Settings tab.</summary>
    private void WireSettingsDirtyTracking()
    {
        if (TabItemSettings?.Content is not DependencyObject root) return;
        foreach (var ctrl in EnumerateVisualTree(root))
        {
            switch (ctrl)
            {
                case System.Windows.Controls.Primitives.ToggleButton tb   // CheckBox + RadioButton
                    when !_dirtyExemptControls.Contains(tb.Name):
                    tb.Checked   += (_, _) => MarkSettingsDirty();
                    tb.Unchecked += (_, _) => MarkSettingsDirty();
                    break;
                case System.Windows.Controls.TextBox txt:
                    txt.TextChanged += (_, _) => MarkSettingsDirty();
                    break;
                case System.Windows.Controls.PasswordBox pw:
                    pw.PasswordChanged += (_, _) => MarkSettingsDirty();
                    break;
                case System.Windows.Controls.ComboBox cb
                    when !_dirtyExemptControls.Contains(cb.Name):
                    cb.SelectionChanged += (_, _) => MarkSettingsDirty();
                    break;
            }
        }
    }

    private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in EnumerateVisualTree(child))
                yield return descendant;
        }
    }

    private void MarkSettingsDirty()
    {
        if (_suppressDirtyTracking) return;
        SetSettingsDirty(true);
    }

    private void SetSettingsDirty(bool dirty)
    {
        _settingsDirty = dirty;
        if (SettingsUnsavedIndicator != null)
            SettingsUnsavedIndicator.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        if (SaveSettingsButton != null)
            SaveSettingsButton.FontWeight = dirty ? FontWeights.Bold : FontWeights.Normal;
    }


    private void OnLogMessageReceived(object? sender, string logMessage)
    {
        Dispatcher.BeginInvoke(() =>
        {
            DebugLogTextBox.AppendText(logMessage + Environment.NewLine);
            DebugLogTextBox.ScrollToEnd();

            // Begrenze auf maximal 10000 Zeilen
            var lines = DebugLogTextBox.Text.Split('\n');
            if (lines.Length > 10000)
            {
                DebugLogTextBox.Text = string.Join('\n', lines.Skip(lines.Length - 10000));
            }
        });
    }

    private void ActiveChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActiveChannelComboBox.SelectedItem is ChannelInfo channel)
        {
            _activeChannelIndex = channel.Index;
            UpdateStatusBar(string.Format(Loc("StrActiveChannelStatus"), channel.Name));
        }
    }

    private void RefreshPorts()
    {
        var ports = SerialPort.GetPortNames();
        PortComboBox.ItemsSource = ports;
        if (ports.Length > 0)
        {
            // Try to select last used port
            if (!string.IsNullOrEmpty(_currentSettings.LastComPort) && ports.Contains(_currentSettings.LastComPort))
            {
                PortComboBox.SelectedItem = _currentSettings.LastComPort;
            }
            else
            {
                PortComboBox.SelectedIndex = 0;
            }
        }
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e)
    {
        RefreshPorts();
        UpdateStatusBar(Loc("StrPortsRefreshed"));
    }

    private void ConnectionTypeRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton) return;

        // Panels might not be initialized yet during startup
        if (SerialConnectionPanel == null || BluetoothConnectionPanel == null || TcpConnectionPanel == null)
            return;

        // Hide all connection panels
        SerialConnectionPanel.Visibility = Visibility.Collapsed;
        BluetoothConnectionPanel.Visibility = Visibility.Collapsed;
        TcpConnectionPanel.Visibility = Visibility.Collapsed;

        // Show selected panel and update connection type
        switch (radioButton.Tag as string)
        {
            case "Serial":
                SerialConnectionPanel.Visibility = Visibility.Visible;
                _currentConnectionType = Services.ConnectionType.Serial;
                break;
            case "Bluetooth":
                BluetoothConnectionPanel.Visibility = Visibility.Visible;
                _currentConnectionType = Services.ConnectionType.Bluetooth;
                break;
            case "Tcp":
                TcpConnectionPanel.Visibility = Visibility.Visible;
                _currentConnectionType = Services.ConnectionType.Tcp;
                break;
        }
    }

    private async void ScanBluetooth_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScanBluetoothButton.IsEnabled = false;
            UpdateStatusBar("Suche Bluetooth-Geräte...");

            _bluetoothDevices.Clear();

            // Search for both paired and unpaired BLE devices
            Services.Logger.WriteLine("[BLE] Starting device discovery...");

            // First, get paired devices
            Services.Logger.WriteLine("[BLE] Searching for paired devices...");
            var pairedSelector = Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var pairedDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(pairedSelector);
            Services.Logger.WriteLine($"[BLE] Found {pairedDevices.Count} paired devices");

            foreach (var deviceInfo in pairedDevices)
            {
                Services.Logger.WriteLine($"[BLE] Paired device: {deviceInfo.Name} (ID: {deviceInfo.Id})");
                await TryAddBluetoothDevice(deviceInfo);
            }

            // Then, search for nearby unpaired devices
            Services.Logger.WriteLine("[BLE] Searching for unpaired devices...");
            var unpairedSelector = Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(false);
            var unpairedDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(unpairedSelector);
            Services.Logger.WriteLine($"[BLE] Found {unpairedDevices.Count} unpaired devices");

            foreach (var deviceInfo in unpairedDevices)
            {
                Services.Logger.WriteLine($"[BLE] Unpaired device: {deviceInfo.Name} (ID: {deviceInfo.Id})");
                await TryAddBluetoothDevice(deviceInfo);
            }

            Services.Logger.WriteLine($"[BLE] Total devices added to list: {_bluetoothDevices.Count}");
            UpdateStatusBar($"{_bluetoothDevices.Count} Bluetooth-Geräte gefunden");

            // Pre-select last used BT device
            if (!string.IsNullOrEmpty(_currentSettings.LastBtDevice))
            {
                var match = _bluetoothDevices.FirstOrDefault(d => d.Name == _currentSettings.LastBtDevice);
                if (match != null) BluetoothDeviceComboBox.SelectedItem = match;
            }

            if (_bluetoothDevices.Count == 0)
            {
                MessageBox.Show("Keine Bluetooth-Geräte gefunden.\n\nStellen Sie sicher, dass:\n- Bluetooth aktiviert ist\n- Das Gerät eingeschaltet ist\n- Das Gerät im BLE-Modus ist", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[BLE] ERROR scanning Bluetooth: {ex.Message}");
            Services.Logger.WriteLine($"[BLE] Stack trace: {ex.StackTrace}");
            MessageBox.Show(string.Format(Loc("StrBtScanFailed"), ex.Message), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanBluetoothButton.IsEnabled = true;
        }
    }

    private async Task TryAddBluetoothDevice(Windows.Devices.Enumeration.DeviceInformation deviceInfo)
    {
        try
        {
            if (string.IsNullOrEmpty(deviceInfo.Name))
            {
                Services.Logger.WriteLine($"[BLE] Skipping device with empty name (ID: {deviceInfo.Id})");
                return;
            }

            // Try to get the actual BLE device to extract the Bluetooth address
            var bleDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(deviceInfo.Id);
            if (bleDevice != null)
            {
                var address = bleDevice.BluetoothAddress;
                Services.Logger.WriteLine($"[BLE] Device '{deviceInfo.Name}' has address: {address:X}");

                // Check if already in list (avoid duplicates)
                if (!_bluetoothDevices.Any(d => d.Address == address))
                {
                    _bluetoothDevices.Add(new Models.BluetoothDeviceInfo
                    {
                        Name = deviceInfo.Name,
                        Address = address
                    });
                    Services.Logger.WriteLine($"[BLE] Added device '{deviceInfo.Name}' to list");
                }
                else
                {
                    Services.Logger.WriteLine($"[BLE] Device '{deviceInfo.Name}' already in list, skipping");
                }

                bleDevice.Dispose();
            }
            else
            {
                Services.Logger.WriteLine($"[BLE] Could not get BluetoothLEDevice for {deviceInfo.Name}");
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[BLE] Error adding device {deviceInfo.Name}: {ex.Message}");
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionService?.IsConnected == true)
        {
            try
            {
                _intentionalDisconnect = true;
                _isReconnecting = false;
                ConnectButton.IsEnabled = false;
                UpdateStatusBar("Trenne Verbindung...");
                SetConnectionStatus(ConnectionStatus.Disconnecting);

                // Disconnect im Hintergrund, nicht auf UI-Thread blockieren
                await Task.Run(() =>
                {
                    _protocolService.Disconnect();
                    System.Threading.Thread.Sleep(200);
                    _connectionService?.Disconnect();
                });

                ConnectButton.Content = Loc("StrConnect");
                UpdateStatusBar(Loc("StrDisconnectedMsg"));
                SetConnectionStatus(ConnectionStatus.Disconnected);
                _myNodeId = 0;
                StationNameEditButton.Visibility = Visibility.Collapsed;
                RefreshActiveStationName();
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Disconnect error: {ex.Message}");
                UpdateStatusBar(Loc("StrDisconnectError"));
                SetConnectionStatus(ConnectionStatus.Error);
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }
        else
        {
            // Create connection parameters based on selected connection type
            ConnectionParameters? connectionParams = null;
            string displayName = string.Empty;

            switch (_currentConnectionType)
            {
                case Services.ConnectionType.Serial:
                    var selectedPort = PortComboBox.SelectedItem as string;
                    if (string.IsNullOrEmpty(selectedPort))
                    {
                        MessageBox.Show(Loc("StrSelectComPort"), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    connectionParams = new SerialConnectionParameters
                    {
                        PortName = selectedPort,
                        BaudRate = 115200
                    };
                    displayName = selectedPort;
                    break;

                case Services.ConnectionType.Bluetooth:
                    var selectedDevice = BluetoothDeviceComboBox.SelectedItem as Models.BluetoothDeviceInfo;
                    if (selectedDevice == null)
                    {
                        MessageBox.Show(Loc("StrSelectBtDevice"), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    connectionParams = new BluetoothConnectionParameters
                    {
                        DeviceAddress = selectedDevice.Address,
                        DeviceName = selectedDevice.Name
                    };
                    displayName = selectedDevice.Name;
                    break;

                case Services.ConnectionType.Tcp:
                    var host = TcpHostTextBox.Text.Trim();
                    if (string.IsNullOrEmpty(host))
                    {
                        MessageBox.Show(Loc("StrEnterTcpHost"), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!int.TryParse(TcpPortTextBox.Text, out var port) || port <= 0 || port > 65535)
                    {
                        MessageBox.Show(Loc("StrEnterTcpPort"), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    connectionParams = new TcpConnectionParameters
                    {
                        Hostname = host,
                        Port = port
                    };
                    displayName = $"{host}:{port}";
                    break;
            }

            if (connectionParams == null) return;

            try
            {
                _intentionalDisconnect = false;
                _isReconnecting = false;
                _lastConnectionParams = connectionParams;
                _lastConnectionType = _currentConnectionType;

                ConnectButton.IsEnabled = false;
                UpdateStatusBar(string.Format(Loc("StrConnectingTo"), displayName));
                SetConnectionStatus(ConnectionStatus.Connecting);

                // Create new connection service based on type
                _connectionService?.Dispose();
                _connectionService = _currentConnectionType switch
                {
                    Services.ConnectionType.Serial => new SerialConnectionService(),
                    Services.ConnectionType.Bluetooth => new BluetoothConnectionService(),
                    Services.ConnectionType.Tcp => new TcpConnectionService(),
                    _ => throw new InvalidOperationException($"Unknown connection type: {_currentConnectionType}")
                };

                // Create new protocol service with the new connection
                // (Protocol service subscribes to DataReceived in its constructor)
                _protocolService = new MeshtasticProtocolService(_connectionService);
                _protocolService.MessageReceived += OnMessageReceived;
                _protocolService.NodeInfoReceived += OnNodeInfoReceived;
                _protocolService.ChannelInfoReceived += OnChannelInfoReceived;
                _protocolService.LoRaConfigReceived += OnLoRaConfigReceived;
                _protocolService.DeviceInfoReceived += OnDeviceInfoReceived;
                _protocolService.PacketCountChanged += OnPacketCountChanged;
                _protocolService.TracerouteReceived += OnTracerouteReceived;
                _protocolService.ReactionReceived += OnReactionReceived;
                _protocolService.DeviceTelemetryReceived += OnDeviceTelemetryReceived;
                _protocolService.MqttConfigReceived += OnMqttConfigReceived;
                if (_db != null) _protocolService.SetDatabase(_db);
                if (_nodeKeyService != null) _protocolService.SetNodeKeyService(_nodeKeyService);
                _protocolService.SetPskMismatchAction(_currentSettings.NodeKeyMismatchAction);
                // Re-apply debug flags: a fresh service instance defaults them to off,
                // so serial/device debug would otherwise never take effect on the
                // connection that actually matters.
                _protocolService.SetDebugSerial(_currentSettings.DebugSerial);
                _protocolService.SetDebugDevice(_currentSettings.DebugDevice);
                _dmWindow?.UpdateProtocolService(_protocolService);
                PrepareVirtualNode();

                // Recreate proxy with new protocol service
                _mqttProxyService?.Dispose();
                _mqttProxyService = new MqttProxyService(_protocolService);
                _mqttProxyService.StatusChanged += (s, msg) => Dispatcher.Invoke(() => UpdateStatusBar(msg));

                // Wire up connection state changed
                _connectionService.ConnectionStateChanged += OnConnectionStateChanged;

                // Connect
                await _connectionService.ConnectAsync(connectionParams);

                // Save last used connection settings
                _currentSettings = _currentSettings with { LastConnectionType = _currentConnectionType.ToString() };
                if (_currentConnectionType == Services.ConnectionType.Serial)
                {
                    _currentSettings = _currentSettings with { LastComPort = displayName };
                }
                else if (_currentConnectionType == Services.ConnectionType.Tcp)
                {
                    var tcpParams = (TcpConnectionParameters)connectionParams;
                    _currentSettings = _currentSettings with
                    {
                        LastTcpHost = tcpParams.Hostname,
                        LastTcpPort = tcpParams.Port
                    };
                }
                else if (_currentConnectionType == Services.ConnectionType.Bluetooth)
                {
                    _currentSettings = _currentSettings with { LastBtDevice = displayName };
                }
                SettingsService.Save(_currentSettings);

                // GUI sofort als "Verbunden" anzeigen
                ConnectButton.Content = Loc("StrDisconnect");
                ConnectButton.IsEnabled = true;
                UpdateStatusBar(string.Format(Loc("StrConnectedInit"), displayName));
                SetConnectionStatus(ConnectionStatus.Initializing);

                // Initialisierung im Hintergrund starten (nicht blockieren!)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _protocolService.InitializeAsync();
                        if (_currentSettings.AutoTimeSyncOnConnect)
                            await _protocolService.SendTimeSyncAsync();
                        StartTimeSyncTimer();
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (!(_connectionService?.IsConnected == true)) return;
                            UpdateStatusBar(string.Format(Loc("StrConnectedReady"), displayName));
                            SetConnectionStatus(ConnectionStatus.Ready);
                            NodeConfigButton.IsEnabled = true;
                            RemoteAdminButton.IsEnabled = true;
                            StartVirtualNodeIfEnabled();
                        });
                    }
                    catch (Exception initEx)
                    {
                        Services.Logger.WriteLine($"Initialization error: {initEx.Message}");
                        Dispatcher.BeginInvoke(() =>
                        {
                            UpdateStatusBar(string.Format(Loc("StrConnectedInitError"), displayName));
                            SetConnectionStatus(ConnectionStatus.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                // Translate common socket errors to user-friendly localized messages
                var userMsg = ex.Message;
                if (ex is System.Net.Sockets.SocketException se && se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused
                    || ex.InnerException is System.Net.Sockets.SocketException se2 && se2.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused)
                    userMsg = Loc("StrErrConnectionRefused");
                MessageBox.Show(string.Format(Loc("StrConnectFailed"), userMsg), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar(Loc("StrConnectionFailed"));
                SetConnectionStatus(ConnectionStatus.Error);
                ConnectButton.IsEnabled = true;
            }
        }
    }

    private void PortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        await SendMessage();
    }

    private async void AlertBell_Click(object sender, RoutedEventArgs e)
    {
        if (!_connectionService.IsConnected)
        {
            MessageBox.Show("Nicht verbunden. Bitte zuerst mit einem Gerät verbinden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            string.Format(Loc("StrAlertConfirmText"), "\n"),
            Loc("StrAlertConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            AlertBellButton.IsEnabled = false;

            // Send Alert Bell - use EMOJI ?? (as used by other clients)
            string alertMessage;
            var additionalText = MessageTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(additionalText))
            {
                // Bell emoji + user text
                alertMessage = "?? " + additionalText;
                MessageTextBox.Clear();
            }
            else
            {
                // Bell emoji + standard text (compatible with other Meshtastic clients)
                alertMessage = "?? Alert Bell Character!";
            }

            // Debug log with hex dump (only if debug messages enabled)
            if (_currentSettings.DebugMessages)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(alertMessage);
                var hexDump = string.Join(" ", bytes.Select(b => $"{b:X2}"));
                Services.Logger.WriteLine($"[MSG DEBUG] Sending Alert Bell {bytes.Length} bytes: {hexDump}");
                Services.Logger.WriteLine($"[MSG DEBUG] Text: '{alertMessage}'");
            }

            await _protocolService.SendTextMessageAsync(alertMessage, 0xFFFFFFFF, (uint)_activeChannelIndex);

            UpdateStatusBar(Loc("StrAlertSent"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Senden des Notrufs: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AlertBellButton.IsEnabled = true;
        }
    }

    private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        var message = MessageTextBox.Text.Trim();
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (!_connectionService.IsConnected)
        {
            MessageBox.Show("Nicht verbunden. Bitte zuerst mit einem Gerät verbinden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SendButton.IsEnabled = false;

            // Sende Nachricht mit dem aktiven Kanal
            var replyTarget = _replyToMessage;
            uint sentId = await _protocolService.SendTextMessageAsync(message, 0xFFFFFFFF, (uint)_activeChannelIndex, replyTarget?.Id ?? 0);

            // Clear reply state
            _replyToMessage = null;
            ReplyIndicatorPanel.Visibility = Visibility.Collapsed;

            // Zeige gesendete Nachricht in der Liste
            var activeChannel = _channels.FirstOrDefault(c => c.Index == _activeChannelIndex);
            var channelName = activeChannel?.Name ?? $"Kanal {_activeChannelIndex}";

            var sentMessage = new MessageItem
            {
                Id = sentId,
                Time = DateTime.Now.ToString("HH:mm"),
                From = Loc("StrMe"),
                FromId = _myNodeId,
                Message = message,
                Channel = _activeChannelIndex.ToString(),
                ChannelName = channelName,
                IsViaMqtt = false,
                IsOwnMessage = true,
                ReplyId = replyTarget?.Id ?? 0,
                ReplyFromName = replyTarget?.From ?? string.Empty,
                ReplyPreview = replyTarget?.Message?.Length > 60 ? replyTarget.Message[..60] + "…" : replyTarget?.Message ?? string.Empty
            };
            _allMessages.Add(sentMessage);
            if (sentId != 0) _messageById[sentId] = sentMessage;
            _messages.Add(sentMessage);
            MessageListView.ScrollIntoView(sentMessage);

            // Persistiere gesendete Nachricht in DB
            if (_messageDbManager != null)
            {
                var dbChanName = channelName;
                var dbChanIdx  = _activeChannelIndex;
                var dbMsg      = sentMessage;
                Task.Run(() => _messageDbManager.InsertChannelMessage(dbChanIdx, dbChanName, dbMsg));
            }

            // Log die gesendete Nachricht
            Services.MessageLogger.LogChannelMessage(_activeChannelIndex, channelName, Loc("StrMe"), message, false);

            MessageTextBox.Clear();
            UpdateStatusBar(string.Format(Loc("StrMsgSentChannel"), _activeChannelIndex));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Senden: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    private const string MeshHessenPsk = "+uTMEaOR7hkqaXv+DROOEd5BhvAIQY/CZ/Hr4soZcOU=";
    private const string MeshHessenName = "Mesh Hessen";

    private void ChannelContextMenu_CopyPsk_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelsListView.SelectedItem is Models.ChannelInfo channel && !string.IsNullOrEmpty(channel.Psk))
        {
            Clipboard.SetText(channel.Psk);
        }
    }

    private async void AddChannel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddChannelWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var pskBytes = Convert.FromBase64String(dialog.PskBase64);
                int freeIndex = FindFirstFreeChannelIndex();
                if (freeIndex < 0)
                {
                    MessageBox.Show("Kein freier Kanal-Slot verfügbar (max. 8 Kanäle).", "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await _protocolService.SetChannelAsync(freeIndex, dialog.ChannelName, pskBytes, secondary: true);

                await Task.Delay(1000);
                await _protocolService.RefreshChannelAsync(freeIndex);

                UpdateMeshHessenButtonState();
                Services.Logger.WriteLine($"Channel '{dialog.ChannelName}' added at index {freeIndex}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Hinzufügen des Kanals: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void DeleteChannel_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelsListView.SelectedItem is not ChannelInfo selectedChannel)
        {
            MessageBox.Show("Bitte einen Kanal auswählen.", "Hinweis",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selectedChannel.Index == 0)
        {
            MessageBox.Show("Der primäre Kanal (Index 0) kann nicht gelöscht werden.", "Hinweis",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            string.Format(Loc("StrDeleteChannelConfirm"), selectedChannel.Name, selectedChannel.Index),
            Loc("StrDeleteChannel"), MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _protocolService.DeleteChannelAsync(selectedChannel.Index);

            // Refresh all channels from device (indices shifted)
            _channels.Clear();
            await Task.Delay(500);
            await _protocolService.RefreshAllChannelsAsync();

            UpdateMeshHessenButtonState();
            Services.Logger.WriteLine($"Channel '{selectedChannel.Name}' (Index {selectedChannel.Index}) deleted");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Löschen des Kanals: {ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BrowseChannels_Click(object sender, RoutedEventArgs e)
    {
        var browser = new ChannelBrowserWindow { Owner = this };
        if (browser.ShowDialog() == true && browser.SelectedChannel != null)
        {
            try
            {
                var entry = browser.SelectedChannel;
                var pskBytes = Convert.FromBase64String(entry.Psk);

                if (_channels.Any(c => c.Psk == entry.Psk))
                {
                    MessageBox.Show($"Ein Kanal mit diesem PSK existiert bereits.", "Hinweis",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int freeIndex = FindFirstFreeChannelIndex();
                if (freeIndex < 0)
                {
                    MessageBox.Show("Kein freier Kanal-Slot verfügbar (max. 8 Kanäle).", "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool mqttEnabled = entry.MqttEnabled.Equals("true", StringComparison.OrdinalIgnoreCase);

                await _protocolService.SetChannelAsync(freeIndex, entry.Name, pskBytes,
                    secondary: true, uplinkEnabled: mqttEnabled, downlinkEnabled: mqttEnabled);

                await Task.Delay(1000);
                await _protocolService.RefreshChannelAsync(freeIndex);

                UpdateMeshHessenButtonState();
                Services.Logger.WriteLine($"Channel '{entry.Name}' from list added at index {freeIndex}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Hinzufügen des Kanals: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void AddMeshHessenChannel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_channels.Any(c => c.Psk == MeshHessenPsk))
            {
                MessageBox.Show("Mesh-Hessen Kanal ist bereits vorhanden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // LoRa-Check: Mesh-Hessen benötigt SHORT_SLOW, EU_868, Hop 7
            if (_currentLoRaConfig != null)
            {
                bool needsPreset = _currentLoRaConfig.ModemPreset != ModemPreset.ShortSlow;
                bool needsRegion = (int)_currentLoRaConfig.Region == 0; // Unset = enum value 0
                bool needsHop   = _currentLoRaConfig.HopLimit != 7;

                if (needsPreset || needsRegion || needsHop)
                {
                    var changes = new System.Text.StringBuilder();
                    changes.AppendLine("Für Mesh-Hessen werden folgende Einstellungen empfohlen:\n");
                    if (needsPreset) changes.AppendLine($"  – Modem-Preset: {_currentLoRaConfig.ModemPreset} ? SHORT_SLOW");
                    if (needsRegion) changes.AppendLine("  – Region: Unset ? EU_868");
                    if (needsHop)   changes.AppendLine($"  – Hop-Limit: {_currentLoRaConfig.HopLimit} ? 7");
                    changes.AppendLine("\nJetzt ändern?");

                    var result = MessageBox.Show(
                        changes.ToString(),
                        "LoRa-Einstellungen für Mesh-Hessen",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Cancel)
                        return;

                    if (result == MessageBoxResult.Yes)
                    {
                        var newLora = _currentLoRaConfig.Clone();
                        if (needsPreset) { newLora.ModemPreset = ModemPreset.ShortSlow; newLora.UsePreset = true; }
                        if (needsRegion) { newLora.Region = Region.Eu868; }
                        if (needsHop)    { newLora.HopLimit = 7; }
                        await _protocolService.SetLoRaConfigAsync(newLora);
                        Services.Logger.WriteLine("LoRa config updated for Mesh-Hessen (preset/region/hop)");
                    }
                }
            }

            int freeIndex = FindFirstFreeChannelIndex();
            if (freeIndex < 0)
            {
                MessageBox.Show("Kein freier Kanal-Slot verfügbar (max. 8 Kanäle).", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var pskBytes = Convert.FromBase64String(MeshHessenPsk);
            await _protocolService.SetChannelAsync(freeIndex, MeshHessenName, pskBytes,
                secondary: true, uplinkEnabled: true, downlinkEnabled: true);

            await Task.Delay(1000);
            await _protocolService.RefreshChannelAsync(freeIndex);

            UpdateMeshHessenButtonState();
            Services.Logger.WriteLine($"Mesh-Hessen channel added at index {freeIndex}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Hinzufügen des Mesh-Hessen Kanals: {ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private int FindFirstFreeChannelIndex()
    {
        var usedIndices = _channels
            .Where(c => !c.Role.Equals("DISABLED", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Index)
            .ToHashSet();
        for (int i = 1; i < 8; i++)
        {
            if (!usedIndices.Contains(i))
                return i;
        }
        return -1;
    }

    private void UpdateMeshHessenButtonState()
    {
        if (MeshHessenButton != null)
        {
            MeshHessenButton.IsEnabled = !_channels.Any(c => c.Psk == MeshHessenPsk);
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Get current tile URL from TextBox
            var currentTileUrl = string.IsNullOrWhiteSpace(TileServerUrlTextBox.Text)
                ? "https://tile.meshhessenclient.de/osm/{z}/{x}/{y}.png"
                : TileServerUrlTextBox.Text.Trim();

            // Update the appropriate URL based on current map source
            var osmUrl = _currentSettings.OSMTileUrl;
            var osmTopoUrl = _currentSettings.OSMTopoTileUrl;
            var osmDarkUrl = _currentSettings.OSMDarkTileUrl;

            switch (_currentSettings.MapSource)
            {
                case "osm":
                    osmUrl = currentTileUrl;
                    break;
                case "osmtopo":
                    osmTopoUrl = currentTileUrl;
                    break;
                case "osmdark":
                    osmDarkUrl = currentTileUrl;
                    break;
            }

            // Vector style URL for the current map source (custom vector server)
            var vectorOsmUrl = _currentSettings.VectorStyleOsmUrl;
            var vectorTopoUrl = _currentSettings.VectorStyleTopoUrl;
            var vectorDarkUrl = _currentSettings.VectorStyleDarkUrl;
            if (!string.IsNullOrWhiteSpace(VectorStyleUrlTextBox.Text))
            {
                var styleUrl = VectorStyleUrlTextBox.Text.Trim();
                switch (_currentSettings.MapSource)
                {
                    case "osmtopo": vectorTopoUrl = styleUrl; break;
                    case "osmdark": vectorDarkUrl = styleUrl; break;
                    default: vectorOsmUrl = styleUrl; break;
                }
            }

            // `with` copies every field of _currentSettings and only overrides the ones the
            // settings UI edits – new AppSettings fields are preserved automatically instead of
            // silently resetting to default (the failure mode the old full re-list invited).
            var settings = _currentSettings with
            {
                DarkMode = DarkModeCheckBox.IsChecked == true,
                StationName = StationNameTextBox.Text,
                ShowEncryptedMessages = ShowEncryptedMessagesCheckBox.IsChecked == true,
                OSMTileUrl = osmUrl,
                OSMTopoTileUrl = osmTopoUrl,
                OSMDarkTileUrl = osmDarkUrl,
                DebugMessages = DebugMessagesCheckBox.IsChecked == true,
                DebugSerial = DebugSerialCheckBox.IsChecked == true,
                DebugDevice = DebugDeviceCheckBox.IsChecked == true,
                DebugBluetooth = DebugBluetoothCheckBox.IsChecked == true,
                AlertBellSound = AlertBellSoundCheckBox.IsChecked == true,
                Language = (LanguageComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "de",
                EnableLocationLogging = EnableLocationLoggingCheckBox.IsChecked == true,
                TelemetryRetentionDays = int.TryParse((TelemetryRetentionComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string, out var ret) ? ret : 90,
                NodeKeyMismatchAction = PskWarnRadio.IsChecked == true ? Services.PskMismatchAction.Warn
                                      : PskAskRadio.IsChecked  == true ? Services.PskMismatchAction.Ask
                                      : Services.PskMismatchAction.Overwrite,
                SignalWeatherWindowHours = int.TryParse(WeatherHoursBox.Text, out var swh) ? Math.Max(1, swh) : 6,
                SignalAntennaWindowDays = int.TryParse(AntennaDaysBox.Text, out var sad) ? Math.Max(1, sad) : 7,
                PositionHistoryHours = int.TryParse((PositionHistoryComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string, out var phh) ? phh : 24,
                AutoTimeSyncOnConnect = AutoTimeSyncCheckBox?.IsChecked == true,
                TimeSyncDriftThresholdSeconds = int.TryParse(TimeSyncDriftBox?.Text, out var tsd) ? Math.Max(60, tsd) : 300,
                EnableMessageDb = EnableMessageDbCheckBox.IsChecked == true,
                MessageDbRetentionDays = int.TryParse((MessageDbRetentionComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string, out var mdr) ? mdr : 90,
                RemoteAdminTimeoutSeconds = int.TryParse(RemoteAdminTimeoutTextBox.Text, out var rats) ? Math.Clamp(rats, 5, 120) : 30,
                VirtualNodeEnabled = VirtualNodeEnableCheckBox?.IsChecked == true,
                VirtualNodePort = int.TryParse(VirtualNodePortBox?.Text, out var vnp) ? Math.Clamp(vnp, 1, 65535) : 4404,
                VirtualNodeBlockAdmin = VirtualNodeBlockAdminCheckBox?.IsChecked == true,
                FancyNodeList = FancyNodeListCheckBox?.IsChecked == true,
                FancyNodeListColorful = Models.NodeInfo.FancyColorful,
                KioskModeEnabled = KioskEnableCheckBox?.IsChecked == true,
                KioskLockedFeatures = CollectKioskLockedFeatures(),
                VectorStyleOsmUrl = vectorOsmUrl,
                VectorStyleTopoUrl = vectorTopoUrl,
                VectorStyleDarkUrl = vectorDarkUrl
                // unchanged fields (MyLatitude/Longitude, LastComPort, MapSource/Mode, NodeColors,
                // NodeNotes, PinnedNodes, FavoriteNodes, NodeStationNames, LastConnectionType,
                // LastBtDevice, KioskPasswordHash, MapRenderMode, MapOverlays) carry over via `with`.
            };
            _currentSettings = settings;
            SettingsService.Save(settings);
            ApplyVirtualNodeSettings();

            // Enable/disable message DB manager
            if (settings.EnableMessageDb && _messageDbManager == null)
            {
                var msgDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "messages");
                _messageDbManager = new Services.MessageDbManager(msgDir);
                if (settings.MessageDbRetentionDays > 0)
                    Task.Run(() => _messageDbManager.ApplyRetention(settings.MessageDbRetentionDays));
            }
            else if (!settings.EnableMessageDb && _messageDbManager != null)
            {
                _messageDbManager.Dispose();
                _messageDbManager = null;
            }

            // Propagate manager change to open DM window
            _dmWindow?.SetMessageDbManager(_messageDbManager);

            _protocolService.SetPskMismatchAction(settings.NodeKeyMismatchAction);
            RefreshActiveStationName();
            _showEncryptedMessages = settings.ShowEncryptedMessages;
            TileDownloaderService.OSMTileUrl = settings.OSMTileUrl;
            TileDownloaderService.OSMTopoTileUrl = settings.OSMTopoTileUrl;
            TileDownloaderService.OSMDarkTileUrl = settings.OSMDarkTileUrl;
            _protocolService.SetDebugSerial(settings.DebugSerial);
            _protocolService.SetDebugDevice(settings.DebugDevice);
            BluetoothConnectionService.SetDebugEnabled(settings.DebugBluetooth);
            UpdateKioskLockButton();
            SetSettingsDirty(false);
            MessageBox.Show(Loc("StrSettingsSaved"), Loc("StrSettingsSavedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Speichern: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnConnectionStateChanged(object? sender, bool isConnected)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Only handle disconnection here - connection status is managed by Connect_Click
                if (!isConnected)
                {
                    // Stop MQTT proxy on disconnect
                    if (_mqttProxyService != null)
                        _ = _mqttProxyService.StopAsync();

                    // Stop Virtual Node on disconnect
                    StopVirtualNode();

                    // Stop analysis timer on disconnect
                    _analysisTimer?.Dispose();
                    _analysisTimer = null;
                    var gray = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x75, 0x75, 0x75));
                    GlobalWeatherLed.Fill  = gray;
                    GlobalAntennaLed.Fill  = gray;
                    GlobalNeighborLed.Fill = gray;
                    GlobalPathLed.Fill     = gray;
                    GlobalMeshHealthLed.Fill = gray;
                    GlobalMeshHealthText.Text = "–";

                    ActiveChannelComboBox.IsEnabled = false;
                    _messages.Clear();
                    _allMessages.Clear();
                    _messageById.Clear();
                    _dbOldestTimestamp = long.MaxValue;
                    _nodes.Clear();
                    _allNodes.Clear();
                    _channels.Clear();
                    _currentLoRaConfig = null;
                    NodeConfigButton.IsEnabled = false;
                    RemoteAdminButton.IsEnabled = false;
                    UpdateMeshHessenButtonState();
                    PacketCountText.Text = string.Format(Loc("StrPacketCount"), 0);

                    if (!_intentionalDisconnect && !_isReconnecting && _lastConnectionParams != null)
                    {
                        // Unexpected disconnect (e.g. node reboot) – try to reconnect
                        _isReconnecting = true;
                        StatusIndicator.Fill = Brushes.Orange;
                        StatusText.Text = Loc("StrConnectionLost");
                        ConnectButton.Content = Loc("StrDisconnect");
                        _ = TryReconnectAsync();
                    }
                    else if (!_isReconnecting)
                    {
                        StatusIndicator.Fill = Brushes.Gray;
                        StatusText.Text = Loc("StrDisconnected");
                        ConnectButton.Content = Loc("StrConnect");
                        ConnectButton.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Error updating connection state in UI: {ex.Message}");
            }
        });
    }

    private async Task TryReconnectAsync()
    {
        Services.Logger.WriteLine("[RECONNECT] Starting auto-reconnect...");
        // Wait for device to reboot (ESP32 typically takes ~3-5 seconds)
        await Task.Delay(5000);

        int attempt = 0;
        const int maxAttempts = 15;

        while (!_intentionalDisconnect && attempt < maxAttempts)
        {
            attempt++;
            Services.Logger.WriteLine($"[RECONNECT] Attempt {attempt}/{maxAttempts}...");
            Dispatcher.BeginInvoke(() =>
            {
                UpdateStatusBar(string.Format(Loc("StrReconnectingAttempt"), attempt, maxAttempts));
                SetConnectionStatus(ConnectionStatus.Connecting);
            });

            try
            {
                // Create fresh services
                _connectionService?.Dispose();
                _connectionService = _lastConnectionType switch
                {
                    Services.ConnectionType.Serial => new SerialConnectionService(),
                    Services.ConnectionType.Bluetooth => new BluetoothConnectionService(),
                    Services.ConnectionType.Tcp => new TcpConnectionService(),
                    _ => throw new InvalidOperationException()
                };

                _protocolService = new MeshtasticProtocolService(_connectionService);
                _protocolService.MessageReceived += OnMessageReceived;
                _protocolService.NodeInfoReceived += OnNodeInfoReceived;
                _protocolService.ChannelInfoReceived += OnChannelInfoReceived;
                _protocolService.LoRaConfigReceived += OnLoRaConfigReceived;
                _protocolService.DeviceInfoReceived += OnDeviceInfoReceived;
                _protocolService.PacketCountChanged += OnPacketCountChanged;
                _protocolService.TracerouteReceived += OnTracerouteReceived;
                _protocolService.ReactionReceived += OnReactionReceived;
                _protocolService.DeviceTelemetryReceived += OnDeviceTelemetryReceived;
                _protocolService.MqttConfigReceived += OnMqttConfigReceived;
                if (_db != null) _protocolService.SetDatabase(_db);
                if (_nodeKeyService != null) _protocolService.SetNodeKeyService(_nodeKeyService);
                _protocolService.SetPskMismatchAction(_currentSettings.NodeKeyMismatchAction);
                _protocolService.SetDebugSerial(_currentSettings.DebugSerial);
                _protocolService.SetDebugDevice(_currentSettings.DebugDevice);
                PrepareVirtualNode();

                // Recreate proxy with new protocol service
                _mqttProxyService?.Dispose();
                _mqttProxyService = new MqttProxyService(_protocolService);
                _mqttProxyService.StatusChanged += (s, msg) => Dispatcher.Invoke(() => UpdateStatusBar(msg));

                _connectionService.ConnectionStateChanged += OnConnectionStateChanged;

                await _connectionService.ConnectAsync(_lastConnectionParams!);

                // Reconnect successful
                _isReconnecting = false;
                Services.Logger.WriteLine("[RECONNECT] Reconnected successfully.");

                Dispatcher.BeginInvoke(() =>
                {
                    if (!(_connectionService?.IsConnected == true)) return;
                    ConnectButton.Content = Loc("StrDisconnect");
                    ConnectButton.IsEnabled = true;
                    UpdateStatusBar(Loc("StrConnectedInitSimple"));
                    SetConnectionStatus(ConnectionStatus.Initializing);
                    _dmWindow?.UpdateProtocolService(_protocolService);
                });

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _protocolService.InitializeAsync();
                        if (_currentSettings.AutoTimeSyncOnConnect)
                            await _protocolService.SendTimeSyncAsync();
                        StartTimeSyncTimer();
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (!(_connectionService?.IsConnected == true)) return;
                            UpdateStatusBar(Loc("StrConnectedReadySimple"));
                            SetConnectionStatus(ConnectionStatus.Ready);
                            NodeConfigButton.IsEnabled = true;
                            RemoteAdminButton.IsEnabled = true;
                            StartVirtualNodeIfEnabled();
                        });
                    }
                    catch (Exception initEx)
                    {
                        Services.Logger.WriteLine($"[RECONNECT] Init error: {initEx.Message}");
                    }
                });
                return;
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"[RECONNECT] Attempt {attempt} failed: {ex.Message}");
                if (attempt < maxAttempts)
                    await Task.Delay(3000);
            }
        }

        // All attempts exhausted
        _isReconnecting = false;
        Services.Logger.WriteLine("[RECONNECT] All reconnect attempts failed.");
        Dispatcher.BeginInvoke(() =>
        {
            StatusIndicator.Fill = Brushes.Gray;
            StatusText.Text = Loc("StrDisconnected");
            ConnectButton.Content = Loc("StrConnect");
            ConnectButton.IsEnabled = true;
            UpdateStatusBar(Loc("StrConnectionLostMsg"));
            SetConnectionStatus(ConnectionStatus.Disconnected);
        });
    }

    private void OnMessageReceived(object? sender, MessageItem message)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_currentSettings.DebugMessages)
                {
                    var msgPreview = message.Message != null && message.Message.Length > 0
                        ? message.Message.Substring(0, Math.Min(50, message.Message.Length))
                        : "";
                    Services.Logger.WriteLine($"[MSG DEBUG] Received message: From={message.From} (ID=!{message.FromId:x8}), To=!{message.ToId:x8}, Channel={message.Channel}, Encrypted={message.IsEncrypted}, MQTT={message.IsViaMqtt}, Text={msgPreview}...");
                }

                // Check for Alert Bell - both ASCII (0x07) and Emoji (??)
                bool hasAlertBell = !string.IsNullOrEmpty(message.Message) &&
                                   (message.Message.Contains('\u0007') || message.Message.Contains("??"));

                if (hasAlertBell)
                {
                    message.HasAlertBell = true;

                    // Debug log (only if debug messages enabled)
                    if (_currentSettings.DebugMessages)
                    {
                        Services.Logger.WriteLine($"[MSG DEBUG] Detected alert bell from {message.From} (ID: !{message.FromId:x8})");
                        var bytes = System.Text.Encoding.UTF8.GetBytes(message.Message);
                        var hexDump = string.Join(" ", bytes.Take(50).Select(b => $"{b:X2}"));
                        Services.Logger.WriteLine($"[MSG DEBUG] Original raw bytes (first 50): {hexDump}");
                        Services.Logger.WriteLine($"[MSG DEBUG] Original text length: {message.Message.Length}");
                        Services.Logger.WriteLine($"[MSG DEBUG] Original text: '{message.Message}'");
                    }

                    // Remove both ASCII bell character and bell emoji for display
                    message.Message = message.Message.Replace("\u0007", "").Replace("??", "");

                    // Trim whitespace
                    message.Message = message.Message.Trim();

                    if (_currentSettings.DebugMessages)
                    {
                        Services.Logger.WriteLine($"[MSG DEBUG] After removing bell: '{message.Message}' (length: {message.Message.Length})");
                    }

                    if (_currentSettings.AlertBellSound)
                    {
                        PlayAlertSound();
                    }

                    // Show visual alert animation
                    ShowAlertBellAnimation();

                    // Show alert notification with "Show on Map" button
                    ShowAlertNotification(message.From, message.FromId);
                }

                // Prüfe ob es eine Direktnachricht ist (nicht Broadcast)
                bool isDirectMessage = message.ToId != 0xFFFFFFFF && message.ToId != 0;

                if (isDirectMessage)
                {
                    if (_currentSettings.DebugMessages)
                    {
                        Services.Logger.WriteLine($"[MSG DEBUG] Message is DM, routing to DM window");
                    }

                    // Leite an DM-Fenster weiter
                    if (_dmWindow == null)
                    {
                        _dmWindow = new DirectMessagesWindow(_protocolService, _myNodeId);
                        _dmWindow.SetMessageDbManager(_messageDbManager);
                    }
                    _dmWindow.AddOrUpdateMessage(message);

                    // Optional: Zeige DM-Fenster automatisch bei neuer Nachricht
                    if (!_dmWindow.IsVisible)
                    {
                        // Blinke den Button oder zeige Notification
                        OpenDmWindowButton.FontWeight = FontWeights.Bold;
                    }
                    return; // Nicht in Hauptnachrichten anzeigen
                }

                // Filter verschlüsselte Nachrichten wenn Checkbox deaktiviert
                if (message.IsEncrypted && !_showEncryptedMessages)
                {
                    if (_currentSettings.DebugMessages)
                    {
                        Services.Logger.WriteLine($"[MSG DEBUG] Message filtered: Encrypted and ShowEncrypted=false");
                    }
                    return; // Nicht anzeigen
                }

                // Setze ChannelName basierend auf Channel Index
                if (uint.TryParse(message.Channel, out uint channelIndex))
                {
                    var channel = _channels.FirstOrDefault(c => c.Index == channelIndex);
                    message.ChannelName = channel?.Name ?? $"Kanal {channelIndex}";
                }
                else
                {
                    message.ChannelName = message.Channel;
                }

                // Mark own messages for right-aligned bubble
                if (_myNodeId != 0 && message.FromId == _myNodeId)
                    message.IsOwnMessage = true;

                // Load sender color and note from settings
                var senderNode = _allNodes.FirstOrDefault(n => n.NodeId == message.FromId);
                if (senderNode != null)
                {
                    message.SenderShortName = senderNode.ShortName;
                    message.SenderColorHex = senderNode.ColorHex;
                    message.SenderNote = senderNode.Note;
                }

                // Populate reply preview from original message
                if (message.ReplyId != 0 && _messageById.TryGetValue(message.ReplyId, out var origMsg))
                {
                    message.ReplyFromName = origMsg.From;
                    message.ReplyPreview = origMsg.Message?.Length > 60 ? origMsg.Message[..60] + "…" : origMsg.Message ?? string.Empty;
                }

                // Speichere in ungefilterte Liste und ID-Lookup
                _allMessages.Add(message);
                if (message.Id != 0)
                    _messageById[message.Id] = message;

                // Persistiere in Nachrichten-DB
                if (_messageDbManager != null && uint.TryParse(message.Channel, out uint dbChanIdx))
                    Task.Run(() => _messageDbManager.InsertChannelMessage((int)dbChanIdx, message.ChannelName, message));

                // Log die Kanal-Nachricht
                if (uint.TryParse(message.Channel, out uint logChannelIndex))
                {
                    Services.MessageLogger.LogChannelMessage((int)logChannelIndex, message.ChannelName, message.From, message.Message, message.IsViaMqtt, message.SenderNote);
                }

                // Prüfe ob Nachricht den aktuellen Filter passiert
                bool passesFilter = true;
                if (_messageChannelFilter != null && _messageChannelFilter.Index != 999)
                {
                    if (uint.TryParse(message.Channel, out uint msgChannelIndex))
                    {
                        passesFilter = (msgChannelIndex == _messageChannelFilter.Index);
                        if (_currentSettings.DebugMessages && !passesFilter)
                        {
                            Services.Logger.WriteLine($"[MSG DEBUG] Message filtered by channel: msgChannel={msgChannelIndex}, filterChannel={_messageChannelFilter.Index}");
                        }
                    }
                }

                if (_currentSettings.DebugMessages)
                {
                    Services.Logger.WriteLine($"[MSG DEBUG] Message passes filter: {passesFilter}, adding to display");
                }

                // Füge zu sichtbarer Liste hinzu wenn Filter passt
                if (passesFilter)
                {
                    _messages.Add(message);
                    MessageListView.ScrollIntoView(message);
                }
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Error adding message to UI: {ex.Message}");
            }
        });
    }

    private void OnNodeInfoReceived(object? sender, Models.NodeInfo node)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Calculate distance if coordinates are available
                if (node.Latitude.HasValue && node.Longitude.HasValue)
                {
                    var distance = CalculateDistance(
                        _currentSettings.MyLatitude,
                        _currentSettings.MyLongitude,
                        node.Latitude.Value,
                        node.Longitude.Value
                    );
                    node.Distance = FormatDistance(distance);
                }

                // Load color and note from settings
                if (_currentSettings.NodeColors.TryGetValue(node.NodeId, out var color))
                {
                    node.ColorHex = color;
                }
                if (_currentSettings.NodeNotes.TryGetValue(node.NodeId, out var note))
                {
                    node.Note = note;
                }

                // Apply pinned and favorite state (local settings take precedence; proto value also accepted)
                node.IsPinned    = _currentSettings.PinnedNodes.ContainsKey(node.NodeId);
                node.IsFavorite  = _currentSettings.FavoriteNodes.ContainsKey(node.NodeId) || node.IsFavorite;
                // Sync device-reported favorites into local settings cache
                if (node.IsFavorite)
                    _currentSettings.FavoriteNodes[node.NodeId] = true;

                // Log location if enabled
                if (_currentSettings.EnableLocationLogging)
                {
                    LocationLogger.Log(node);
                }

                // Restore direct-neighbour data from telemetry DB (only if no live data yet)
                if (!node.DirectNeighborAt.HasValue && _db != null)
                {
                    var (lastSeen, snr, rssi) = _db.GetDirectNeighborContact(node.NodeId);
                    if (lastSeen.HasValue) node.DirectNeighborAt = lastSeen;
                    if (snr.HasValue)
                    {
                        node.DirectNeighborSnr = snr;
                        node.SnrValue          = snr;
                        node.Snr               = $"{snr.Value:F1}";
                    }
                    if (rssi.HasValue && rssi != 0)
                        node.Rssi = rssi.Value.ToString();
                }

                // Update in _allNodes
                var existingInAll = _allNodes.FirstOrDefault(n => n.Id == node.Id);
                if (existingInAll != null)
                {
                    _allNodes.Remove(existingInAll);
                }
                _allNodes.Add(node);

                // Apply sorting and filtering
                ApplyNodeSortAndFilter();

                // Update map pin
                UpdateNodePin(node);
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Error updating node in UI: {ex.Message}");
            }
        });
    }

    private void OnChannelInfoReceived(object? sender, ChannelInfo channel)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var existing = _channels.FirstOrDefault(c => c.Index == channel.Index);
                if (existing != null)
                    _channels.Remove(existing);

                // Disabled-Kanäle nicht in die Liste aufnehmen
                if (channel.Role.Equals("DISABLED", StringComparison.OrdinalIgnoreCase))
                    return;

                // Sortiert einfügen nach Channel-Index
                int insertAt = 0;
                for (int i = 0; i < _channels.Count; i++)
                {
                    if (_channels[i].Index < channel.Index)
                        insertAt = i + 1;
                    else
                        break;
                }
                _channels.Insert(insertAt, channel);

                // Aktiviere Kanal-Auswahl wenn Kanäle vorhanden sind
                if (_channels.Count > 0 && !ActiveChannelComboBox.IsEnabled)
                {
                    ActiveChannelComboBox.IsEnabled = true;

                    // Wähle ersten PRIMARY Kanal oder ersten Kanal aus
                    var primaryChannel = _channels.FirstOrDefault(c => c.Role == "PRIMARY");
                    if (primaryChannel != null)
                    {
                        ActiveChannelComboBox.SelectedItem = primaryChannel;
                    }
                    else if (ActiveChannelComboBox.SelectedItem == null)
                    {
                        ActiveChannelComboBox.SelectedIndex = 0;
                    }
                }

                // Update Message Filter ComboBox
                UpdateMessageFilterComboBox();

                UpdateStatusBar(string.Format(Loc("StrChannelReceived"), channel.Index, channel.Name));
                UpdateMeshHessenButtonState();
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Error updating channel in UI: {ex.Message}");
            }
        });
    }

    private void OnDeviceInfoReceived(object? sender, Models.DeviceInfo deviceInfo)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Services.Logger.WriteLine($"OnDeviceInfoReceived: NodeId={deviceInfo.NodeIdHex}");

                // Speichere eigene Node-ID für DM-Fenster
                _myNodeId = deviceInfo.NodeId;
                OwnNodeIdText.Text = $"!{_myNodeId:x8}";
                StationNameEditButton.Visibility = Visibility.Visible;

                // Re-evaluate IsOwnMessage for messages already loaded from DB
                // (DB load can race ahead of DeviceInfo, leaving IsOwnMessage=false for own messages)
                // IsOwnMessage raises PropertyChanged so the bubble switches side immediately.
                foreach (var m in _allMessages.Where(m => !m.IsOwnMessage && m.FromId == _myNodeId))
                    m.IsOwnMessage = true;

                // Set hardware model and firmware version
                HardwareModelText.Text = deviceInfo.HardwareModel;
                FirmwareVersionText.Text = deviceInfo.FirmwareVersion;
                Services.Logger.WriteLine($"  Hardware: {deviceInfo.HardwareModel}");
                Services.Logger.WriteLine($"  Firmware: {deviceInfo.FirmwareVersion}");

                // Ensure our own node is present in the list so it can be pinned/favorited.
                // Some firmwares omit self from the NodeDB; create a minimal entry that
                // the real NodeInfo (if it arrives) will replace via Id-based dedup.
                if (!_allNodes.Any(n => n.NodeId == deviceInfo.NodeId))
                {
                    OnNodeInfoReceived(this, new Models.NodeInfo
                    {
                        NodeId        = deviceInfo.NodeId,
                        Id            = deviceInfo.NodeIdHex,
                        ShortName     = deviceInfo.ShortName,
                        LongName      = deviceInfo.LongName,
                        Name          = string.IsNullOrEmpty(deviceInfo.LongName)
                                            ? (string.IsNullOrEmpty(deviceInfo.ShortName) ? deviceInfo.NodeIdHex : deviceInfo.ShortName)
                                            : deviceInfo.LongName,
                        HardwareModel = deviceInfo.HardwareModel,
                        LastSeen      = Loc("StrMe"),
                        LastSeenDateTime = DateTime.Now,
                        HopsToReach   = 0,
                    });
                }

                // Suche die eigene NodeInfo in der Node-Liste
                var myNode = _nodes.FirstOrDefault(n => n.NodeId == deviceInfo.NodeId);
                if (myNode != null)
                {
                    NodeInfoLongNameText.Text = myNode.Name;
                    NodeInfoShortNameText.Text = myNode.ShortName ?? "";
                    Services.Logger.WriteLine($"  Set device name: {myNode.Name}");
                    RefreshActiveStationName();
                }
                else
                {
                    Services.Logger.WriteLine($"  WARNING: Own node not found in node list (have {_nodes.Count} nodes)");
                    // Warte kurz und probiere nochmal (Node könnte noch nicht da sein)
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            var node = _nodes.FirstOrDefault(n => n.NodeId == deviceInfo.NodeId);
                            if (node != null)
                            {
                                NodeInfoLongNameText.Text = node.Name;
                                NodeInfoShortNameText.Text = node.ShortName ?? "";
                                Services.Logger.WriteLine($"  Set device name (delayed): {node.Name}");
                            }
                            RefreshActiveStationName();
                        });
                    });
                }

                // OwnNodeIdText is already set above; no need to overwrite the main status bar
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"ERROR updating DeviceInfo in UI: {ex.Message}");
            }
        });
    }

    private void OnLoRaConfigReceived(object? sender, LoRaConfig loraConfig)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _currentLoRaConfig = loraConfig;
                Services.Logger.WriteLine($"OnLoRaConfigReceived: Region={loraConfig.Region}, Preset={loraConfig.ModemPreset}");

                // Display in Settings right column (read-only)
                var regionName = loraConfig.Region.ToString();
                var presetName = loraConfig.ModemPreset.ToString();
                NodeInfoRegionText.Text = regionName;
                NodeInfoPresetText.Text = presetName;
                UpdateStatusBar($"LoRa Config: {regionName}, {presetName}");
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"ERROR updating LoRa config in UI: {ex.Message}");
            }
        });
    }

    private void OnMqttConfigReceived(object? sender, MQTTConfig mqttConfig)
    {
        if (_mqttProxyService == null) return;
        if (!mqttConfig.Enabled || !mqttConfig.ProxyToClientEnabled) return;

        Services.Logger.WriteLine($"OnMqttConfigReceived: proxy={mqttConfig.ProxyToClientEnabled}, broker={mqttConfig.Address}");
        _ = _mqttProxyService.StartAsync(mqttConfig, _myNodeId);
    }

    // -- Virtual Node ----------------------------------------------------------

    // Phase 1: called right after new MeshtasticProtocolService is created so that
    // RawFrameReceived is wired before InitializeAsync runs – this way the VN cache
    // fills naturally during init and needs no separate populate step.
    private void PrepareVirtualNode()
    {
        if (!_currentSettings.VirtualNodeEnabled) return;

        // Tear down any previous instance
        if (_virtualNodeService != null)
        {
            _virtualNodeService.Stop();
            _virtualNodeService.Dispose();
            _virtualNodeService = null;
        }
        // Unsubscribe in case it was left wired to the old protocol service
        // (safe no-op if already unsubscribed)
        try { _protocolService.RawFrameReceived -= OnVnRawFrame; } catch { }

        _virtualNodeService = new Services.VirtualNodeService(_connectionService!)
        {
            Port = _currentSettings.VirtualNodePort,
            // Kiosk lock enforces admin blocking regardless of the VNode setting
            BlockAdminCommands = _currentSettings.VirtualNodeBlockAdmin || _kioskLocked
        };
        _virtualNodeService.ClientCountChanged += (_, _) => Dispatcher.BeginInvoke(RefreshVirtualNodeStatus);
        _virtualNodeService.LogMessage += (_, msg) => Services.Logger.WriteLine($"[VN] {msg}");
        _virtualNodeService.ClientPacketReceived += OnVnClientPacket;
        _protocolService.RawFrameReceived += OnVnRawFrame;
    }

    // Phase 2: called after Ready – starts the TCP listener
    private void StartVirtualNodeIfEnabled()
    {
        if (!_currentSettings.VirtualNodeEnabled) return;
        if (_connectionService?.IsConnected != true) return;
        if (_virtualNodeService?.IsRunning == true) return;

        // If PrepareVirtualNode was never called (e.g. VN was enabled after connect)
        // create the service now – cache will be partially filled but that is acceptable
        if (_virtualNodeService == null)
            PrepareVirtualNode();

        _ = Task.Run(async () =>
        {
            try
            {
                await _virtualNodeService!.StartAsync();
                Dispatcher.BeginInvoke(RefreshVirtualNodeStatus);
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Virtual Node start failed: {ex.Message}");
                Dispatcher.BeginInvoke(RefreshVirtualNodeStatus);
            }
        });
    }

    private void StopVirtualNode()
    {
        if (_protocolService != null)
            _protocolService.RawFrameReceived -= OnVnRawFrame;
        if (_virtualNodeService != null)
            _virtualNodeService.ClientPacketReceived -= OnVnClientPacket;
        _virtualNodeService?.Stop();
        _virtualNodeService?.Dispose();
        _virtualNodeService = null;
        Dispatcher.BeginInvoke(RefreshVirtualNodeStatus);
    }

    private void OnVnRawFrame(object? sender, byte[] frame)
        => _virtualNodeService?.OnRawFrameFromPhysical(frame);

    private void OnVnClientPacket(object? sender, byte[] fromRadioBytes)
        => _protocolService?.ProcessExternalPacket(fromRadioBytes);

    private void ApplyVirtualNodeSettings()
    {
        if (_virtualNodeService != null)
            _virtualNodeService.BlockAdminCommands = _currentSettings.VirtualNodeBlockAdmin;

        if (_currentSettings.VirtualNodeEnabled && _connectionService?.IsConnected == true)
        {
            // Restart if port changed or not running yet
            bool portChanged = _virtualNodeService?.Port != _currentSettings.VirtualNodePort;
            if (!(_virtualNodeService?.IsRunning == true) || portChanged)
            {
                StopVirtualNode();
                StartVirtualNodeIfEnabled();
            }
        }
        else if (!_currentSettings.VirtualNodeEnabled)
        {
            StopVirtualNode();
        }
        RefreshVirtualNodeStatus();
    }

    private void VirtualNodeSettings_Changed(object sender, RoutedEventArgs e)
    {
        // Immediate apply without full settings save (save happens via SaveSettings_Click)
        var enabled = VirtualNodeEnableCheckBox?.IsChecked == true;
        var blockAdmin = VirtualNodeBlockAdminCheckBox?.IsChecked == true;
        int port = int.TryParse(VirtualNodePortBox?.Text, out var p) ? Math.Clamp(p, 1, 65535) : 4404;

        _currentSettings = _currentSettings with
        {
            VirtualNodeEnabled = enabled,
            VirtualNodePort = port,
            VirtualNodeBlockAdmin = blockAdmin
        };
        SettingsService.Save(_currentSettings);
        ApplyVirtualNodeSettings();
    }

    private void RefreshVirtualNodeStatus()
    {
        if (VirtualNodeStatusText == null) return;
        var running = _virtualNodeService?.IsRunning == true;
        VirtualNodeStatusText.Text = running ? Loc("StrVirtualNodeRunning") : Loc("StrVirtualNodeStopped");
        VirtualNodeStatusText.Foreground = running
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x75, 0x75, 0x75));

        var clients = _virtualNodeService?.GetConnectedClients() ?? [];
        VirtualNodeClientCountText.Text = clients.Count.ToString();
        VirtualNodeClientListText.Text = clients.Count > 0
            ? string.Join("\n", clients.Select(c => $"{c.Id}  {c.Ip}"))
            : Loc("StrVirtualNodeNoClients");
    }

    // -------------------------------------------------------------------------

    private void OnPacketCountChanged(object? sender, int count)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                PacketCountText.Text = string.Format(Loc("StrPacketCount"), count);
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"ERROR updating packet count in UI: {ex.Message}");
            }
        });
    }

    // -- Neighbour lines ----------------------------------------------------------

    private void DrawNeighborLines()
    {
        PushNeighborLinesToVectorMap();
        if (_neighborLinesLayer == null) return;

        _neighborLineFeatures.Clear();

        if (_showNeighborLines && _myNodeId != 0)
        {
            var myNode = _allNodes.FirstOrDefault(n => n.NodeId == _myNodeId);
            double myLat = myNode?.Latitude  ?? _currentSettings.MyLatitude;
            double myLon = myNode?.Longitude ?? _currentSettings.MyLongitude;

            if (myLat != 0 || myLon != 0)
            {
                var myPos  = SphericalMercator.FromLonLat(myLon, myLat);
                var cutoff = _neighborPermanent ? DateTime.MinValue : DateTime.Now.AddHours(-24);

                // Collect segments first so we can draw outlines before colors
                var segments = new List<(NetTopologySuite.Geometries.LineString line, Mapsui.Styles.Color color)>();

                foreach (var node in _allNodes)
                {
                    if (node.NodeId == _myNodeId) continue;
                    if (!node.DirectNeighborAt.HasValue || node.DirectNeighborAt < cutoff) continue;
                    if (!node.Latitude.HasValue || !node.Longitude.HasValue) continue;

                    Mapsui.Styles.Color color;
                    if (_neighborColorByAge)
                    {
                        color = NeighborColorByAge(node.DirectNeighborAt);
                    }
                    else
                    {
                        if (!node.DirectNeighborSnr.HasValue) continue;
                        color = NeighborColorBySnr(node.DirectNeighborSnr);
                    }

                    var nPos = SphericalMercator.FromLonLat(node.Longitude.Value, node.Latitude.Value);
                    var line = new NetTopologySuite.Geometries.LineString(new[]
                    {
                        new NetTopologySuite.Geometries.Coordinate(myPos.x, myPos.y),
                        new NetTopologySuite.Geometries.Coordinate(nPos.x,  nPos.y)
                    });
                    segments.Add((line, color));
                }

                // Pass 1: dark outlines (behind color lines)
                var outline = Mapsui.Styles.Color.FromArgb(200, 20, 20, 20);
                foreach (var (line, _) in segments)
                {
                    var gf = new GeometryFeature { Geometry = line };
                    gf.Styles.Add(new VectorStyle { Line = new Mapsui.Styles.Pen(outline, 4.5) });
                    _neighborLineFeatures.Add(gf);
                }

                // Pass 2: colored lines on top
                foreach (var (line, color) in segments)
                {
                    var gf = new GeometryFeature { Geometry = line };
                    gf.Styles.Add(new VectorStyle { Line = new Mapsui.Styles.Pen(color, 2.5) });
                    _neighborLineFeatures.Add(gf);
                }
            }
        }

        _neighborLinesLayer.DataHasChanged();
        MapControl?.Refresh();
    }

    private static Mapsui.Styles.Color LerpColor(Mapsui.Styles.Color a, Mapsui.Styles.Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Mapsui.Styles.Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    // SNR gradient: -20 dB ? red  –  0 dB ? yellow  –  +10 dB ? green
    private static Mapsui.Styles.Color NeighborColorBySnr(float? snr)
    {
        if (!snr.HasValue) return Mapsui.Styles.Color.FromArgb(160, 128, 128, 128);
        float t = Math.Clamp((snr.Value + 20f) / 30f, 0f, 1f);
        var red    = Mapsui.Styles.Color.FromArgb(255, 244,  67,  54);
        var yellow = Mapsui.Styles.Color.FromArgb(255, 255, 193,   7);
        var green  = Mapsui.Styles.Color.FromArgb(255,  76, 175,  80);
        return t < 0.5f
            ? LerpColor(red, yellow, t * 2f)
            : LerpColor(yellow, green, (t - 0.5f) * 2f);
    }

    // Age gradient: 0 min ? cyan  –  24 h ? gray
    private static Mapsui.Styles.Color NeighborColorByAge(DateTime? seenAt)
    {
        if (!seenAt.HasValue) return Mapsui.Styles.Color.FromArgb(160, 94, 53, 177);
        float t = Math.Clamp((float)(DateTime.Now - seenAt.Value).TotalMinutes / (24f * 60f), 0f, 1f);
        var fresh = Mapsui.Styles.Color.FromArgb(255,   0, 229, 255);  // cyan
        var old   = Mapsui.Styles.Color.FromArgb(255,  94,  53, 177);  // deep purple (distinct from hop-gray)
        return LerpColor(fresh, old, t);
    }

    private void NeighborLines_Changed(object sender, RoutedEventArgs e)
    {
        _showNeighborLines = NeighborLinesCheckBox.IsChecked == true;
        NeighborLinesOptions.Visibility = _showNeighborLines ? Visibility.Visible : Visibility.Collapsed;
        DrawNeighborLines();
    }

    private void NeighborPermanent_Changed(object sender, RoutedEventArgs e)
    {
        if (NeighborPermanentCheckBox == null) return;
        _neighborPermanent = NeighborPermanentCheckBox.IsChecked == true;
        DrawNeighborLines();
    }

    private void NeighborColorMode_Changed(object sender, RoutedEventArgs e)
    {
        if (NeighborColorAgeRadio == null || NeighborSnrLegendPanel == null || NeighborAgeLegendPanel == null)
            return;
        _neighborColorByAge = NeighborColorAgeRadio.IsChecked == true;
        NeighborSnrLegendPanel.Visibility  = _neighborColorByAge ? Visibility.Collapsed : Visibility.Visible;
        NeighborAgeLegendPanel.Visibility  = _neighborColorByAge ? Visibility.Visible   : Visibility.Collapsed;
        DrawNeighborLines();
    }

    // -- Node-list advanced filters --------------------------------------------

    // -- Fancy Node List ---------------------------------------------------

    private void TileViewGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        int cols = Math.Max(1, (int)(e.NewSize.Width / 295));
        if (cols != _tileColumnCount)
        {
            _tileColumnCount = cols;
            if (IsTileViewActive) ApplyNodeSortAndFilterCore();
        }
    }

    private void ApplyFancyNodeListSetting(bool fancy)
    {
        if (TileViewGrid == null || NodesListView == null) return;
        TileViewGrid.Visibility  = fancy ? Visibility.Visible : Visibility.Collapsed;
        NodesListView.Visibility = fancy ? Visibility.Collapsed : Visibility.Visible;
        if (TileSortPanel != null)
            TileSortPanel.Visibility = fancy ? Visibility.Visible : Visibility.Collapsed;
        if (fancy) ApplyNodeSortAndFilterCore();
    }

    private void FancyNodeList_Changed(object sender, RoutedEventArgs e)
    {
        if (FancyNodeListCheckBox == null) return;
        ApplyFancyNodeListSetting(FancyNodeListCheckBox.IsChecked == true);
    }

    private void TileSort_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TileSortComboBox == null) return;
        var tag = (TileSortComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "Name_asc";
        var parts = tag.Split('_');
        _nodeSortColumn    = parts[0] == "None" ? null : parts[0];
        _nodeSortAscending = parts.Length < 2 || parts[1] == "asc";
        ApplyNodeSortAndFilterCore(); // immediate — user action
    }

    private void TileNode_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _tileContextNode = (sender as FrameworkElement)?.DataContext as Models.NodeInfo;

        // Update dynamic Favorite / Pin headers in the tile context menu
        if (_tileContextNode is not Models.NodeInfo n) return;
        var cm = (sender as FrameworkElement)?.ContextMenu;
        if (cm == null) return;
        bool isOwn = n.NodeId == _myNodeId;
        foreach (var item in cm.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            // Time sync only makes sense for our own node
            if (item.Name == "TileTimeSyncMenuItem")
            {
                item.Visibility = isOwn ? Visibility.Visible : Visibility.Collapsed;
                continue;
            }
            if (item.Header is string h)
            {
                if (h == Loc("StrFavorite") || h == Loc("StrUnfavorite"))
                    item.Header = n.IsFavorite ? Loc("StrUnfavorite") : Loc("StrFavorite");
                else if (h == Loc("StrPin") || h == Loc("StrUnpin"))
                    item.Header = n.IsPinned ? Loc("StrUnpin") : Loc("StrPin");
                else if (h == Loc("StrRemoteAdmin"))
                    item.Visibility = IsKioskFeatureLocked("RemoteAdmin") ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private void RequestInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string tag) return;
        if (SelectedNodeForMenu is not NodeInfo node) return;
        SendInfoRequest(node, tag, mi.Header as string);
    }

    private async void SendInfoRequest(NodeInfo node, string tag, string? label)
    {
        if (_connectionService?.IsConnected != true) return;
        if (!Enum.TryParse<Services.MeshtasticProtocolService.InfoRequestType>(tag, out var type)) return;
        try
        {
            await _protocolService.RequestNodeInfoAsync(node.NodeId, type);
            UpdateStatusBar(string.Format(Loc("StrInfoRequestSent"), label ?? tag, node.Name));
        }
        catch
        {
            UpdateStatusBar(Loc("StrInfoRequestFailed"));
        }
    }

    private static readonly (string key, string tag)[] InfoRequestMenuEntries =
    {
        ("StrReqUserInfo",      "UserInfo"),
        ("StrReqPosition",      "Position"),
        ("StrReqDeviceMetrics", "DeviceMetrics"),
        ("StrReqEnvMetrics",    "EnvironmentMetrics"),
        ("StrReqAirQuality",    "AirQualityMetrics"),
        ("StrReqPowerMetrics",  "PowerMetrics"),
        ("StrReqLocalStats",    "LocalStats"),
        ("StrReqHostMetrics",   "HostMetrics"),
        ("StrReqPax",           "PaxCounter"),
    };

    private void TileFavorite_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Models.NodeInfo node) return;
        _tileContextNode = node;
        ToggleFavoriteInternal(node);
        e.Handled = true;
    }

    private void SnrColor_Changed(object sender, RoutedEventArgs e)
    {
        if (SnrColorCheckBox == null || NodesListView == null) return;
        bool on = SnrColorCheckBox.IsChecked == true;
        Models.NodeInfo.ShowSignalColors = on;
        Models.NodeInfo.FancyColorful    = on;  // Kachelfarbe folgt derselben Checkbox
        NodesListView.Items.Refresh();
        if (IsTileViewActive && NodeTileView != null)
            ApplyNodeSortAndFilterCore();
    }

    private void NodeAdvancedFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (NodeLastSeenFilterComboBox == null || HideMqttNodesCheckBox == null || OnlyFavoritesFilterCheckBox == null)
            return;
        _nodeFilterLastSeenMinutes =
            int.TryParse((NodeLastSeenFilterComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                         ?.Tag as string, out var m) ? m : 0;
        _nodeFilterHideMqtt      = HideMqttNodesCheckBox.IsChecked == true;
        _nodeFilterOnlyFavorites = OnlyFavoritesFilterCheckBox.IsChecked == true;
        ApplyNodeSortAndFilterCore();
    }

    // ========== Message Context Menu Handlers ==========

    private NodeInfo? GetNodeFromSelectedMessage()
    {
        if (MessageListView.SelectedItem is not MessageItem msg) return null;
        return _allNodes.FirstOrDefault(n => n.NodeId == msg.FromId);
    }

    private void MessageContextMenu_CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (MessageListView.SelectedItem is MessageItem msg)
        {
            try { System.Windows.Clipboard.SetText(msg.Message ?? string.Empty); }
            catch { }
        }
    }

    private void MessageContextMenu_SendDm_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node == null) { MessageBox.Show("Node nicht gefunden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (node.NodeId == _myNodeId) { MessageBox.Show(Loc("StrNoDmToSelf"), Loc("StrHint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        OpenDmToNode(node);
    }

    private void MessageContextMenu_NodeInfo_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node == null) { MessageBox.Show("Node nicht gefunden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        ShowNodeInfoDialog(node);
    }

    private void MessageContextMenu_ShowOnMap_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node == null || !node.Latitude.HasValue || !node.Longitude.HasValue)
        {
            MessageBox.Show("Position für diesen Node ist nicht bekannt.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MainTabs.SelectedIndex = 3;
        var nodePos = SphericalMercator.FromLonLat(node.Longitude.Value, node.Latitude.Value);
        if (_map != null)
        {
            _map.Navigator.CenterOnAndZoomTo(new MPoint(nodePos.x, nodePos.y), 76.0);
            MapControl.Refresh();
        }
    }

    private void MessageContextMenu_SetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is string color)
        {
            var node = GetNodeFromSelectedMessage();
            if (node != null) SetNodeColorInternal(node, color);
        }
    }

    private void MessageContextMenu_RemoveColor_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node != null) RemoveNodeColorInternal(node);
    }

    private void MessageContextMenu_EditNote_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node != null) EditNodeNoteInternal(node);
    }

    private void MessagesListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Select the right-clicked row (not the previously selected one)
        if (e.OriginalSource is DependencyObject d)
        {
            var container = ItemsControl.ContainerFromElement(MessageListView, d) as ListViewItem;
            if (container?.Content is MessageItem clickedMsg)
                MessageListView.SelectedItem = clickedMsg;
        }

        var node = GetNodeFromSelectedMessage();
        bool hasNode = node != null;

        // Node-specific items: hide when sender is unknown
        PinMsgMenuItem.Visibility = hasNode ? Visibility.Visible : Visibility.Collapsed;

        if (hasNode)
        {
            PinMsgMenuItem.Header = node!.IsPinned ? Loc("StrUnpin") : Loc("StrPin");
            bool pathActive = _pathLayers.ContainsKey(node.NodeId);
            ShowPathMsgMenuItem.Visibility = pathActive ? Visibility.Collapsed : Visibility.Visible;
            HidePathMsgMenuItem.Visibility = pathActive ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ShowPathMsgMenuItem.Visibility = Visibility.Collapsed;
            HidePathMsgMenuItem.Visibility = Visibility.Collapsed;
        }
    }

    private void MessageContextMenu_Pin_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node != null) PinNodeInternal(node);
    }

    private void MessageContextMenu_ShowPath_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node != null) ShowPathForNode(node);
    }

    private void MessageContextMenu_HidePath_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node == null) return;
        HidePathForNode(node);
        HidePathMsgMenuItem.Visibility = Visibility.Collapsed;
    }

    // ========== Language ==========

    private void ApplyLanguage(string lang)
    {
        try
        {
            var source = $"Resources/Strings.{lang}.xaml";
            var dict = new ResourceDictionary
            {
                Source = new Uri(source, UriKind.Relative)
            };

            var dicts = Application.Current.Resources.MergedDictionaries;
            // Find existing Strings.*.xaml and replace it, or insert at 0
            var existing = dicts.FirstOrDefault(d => d.Source?.OriginalString?.Contains("Strings.") == true);
            if (existing != null)
            {
                var idx = dicts.IndexOf(existing);
                dicts[idx] = dict;
            }
            else
            {
                dicts.Insert(0, dict);
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ApplyLanguage error ({lang}): {ex.Message}");
        }
    }

    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var lang = item.Tag as string ?? "de";
        ApplyLanguage(lang);
        // Don't persist while LoadSettings() is populating controls – at that point
        // _currentSettings is still the placeholder and saving it would clobber the INI.
        if (_suppressDirtyTracking) return;
        // Save immediately so language persists without hitting "Speichern"
        _currentSettings = _currentSettings with { Language = lang };
        SettingsService.Save(_currentSettings);
    }

    // ========== Node Config + Remote Admin ==========

    private void RemoteAdmin_Click(object sender, RoutedEventArgs e)
    {
        // Show node selector from favorites list
        var favorites = _allNodes.Where(n => n.IsFavorite).ToList();
        if (favorites.Count == 0)
        {
            MessageBox.Show(Loc("StrRemoteAdminNoFavorites"), Loc("StrRemoteAdminTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Simple selection dialog using a standard ListBox in a window
        var dialog = new Window
        {
            Title           = Loc("StrRemoteAdminSelectNode"),
            Width           = 380,
            Height          = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner           = this,
            ResizeMode      = ResizeMode.NoResize
        };
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text       = Loc("StrRemoteAdminSelectNode"),
            FontWeight = FontWeights.SemiBold,
            Margin     = new Thickness(0, 0, 0, 10)
        });
        var listBox = new ListBox { Height = 180 };
        foreach (var n in favorites)
            listBox.Items.Add(new ListBoxItem { Content = $"{n.Name} ({n.Id})", Tag = n });
        listBox.SelectedIndex = 0;
        stack.Children.Add(listBox);
        var btnRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var okBtn = new System.Windows.Controls.Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true };
        var cancelBtn = new System.Windows.Controls.Button { Content = Loc("StrCancel"), Width = 80, IsCancel = true };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        stack.Children.Add(btnRow);
        dialog.Content = stack;
        bool confirmed = false;
        okBtn.Click    += (_, _) => { confirmed = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();

        if (!confirmed || listBox.SelectedItem is not ListBoxItem { Tag: NodeInfo selected }) return;

        var timeout = _currentSettings.RemoteAdminTimeoutSeconds * 1000;
        var win = new RemoteAdminWindow(_protocolService, selected, timeout, selected.IsFavorite) { Owner = this };
        win.Show();
        win.Activate();
    }

    private void NodeConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var win = new NodeConfigWindow(_protocolService, GetMapCenter, GetMyPosition, BuildTileLayer) { Owner = this };
            win.Show();
            win.Activate();
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR opening NodeConfigWindow: {ex.Message}");
            MessageBox.Show($"Fehler beim Öffnen der Node-Konfiguration: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private (double lat, double lon)? GetMapCenter()
    {
        try
        {
            if (MapControl?.Map == null) return null;
            var vp = MapControl.Map.Navigator.Viewport;
            var (lon, lat) = Mapsui.Projections.SphericalMercator.ToLonLat(vp.CenterX, vp.CenterY);
            return (lat, lon);
        }
        catch { return null; }
    }

    private (double lat, double lon)? GetMyPosition()
    {
        if (_currentSettings.MyLatitude != 0 || _currentSettings.MyLongitude != 0)
            return (_currentSettings.MyLatitude, _currentSettings.MyLongitude);
        return null;
    }

    private Mapsui.Tiling.Layers.TileLayer BuildTileLayer()
    {
        var tileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");
        var sourceFolder = _currentSettings.MapSource;
        var schema = new BruTile.Predefined.GlobalSphericalMercator(BruTile.YAxis.TMS, 0, 18, "OSM");
        BruTile.ITileProvider tileProvider = _currentSettings.MapMode switch
        {
            "online-osm" => new Services.CachingHttpTileProvider(tileDir, "osm_online",
                "https://tile.openstreetmap.org/{z}/{x}/{y}.png", useHttpCacheHeaders: true),
            "online-own" => new Services.CachingHttpTileProvider(tileDir, sourceFolder,
                GetMeshhessenUrlForSource(sourceFolder), useHttpCacheHeaders: false),
            "online-custom" => new Services.CachingHttpTileProvider(tileDir, sourceFolder,
                GetUrlForSource(sourceFolder), useHttpCacheHeaders: false),
            _ => new Services.LocalFileTileProvider(tileDir, sourceFolder)
        };
        return new Mapsui.Tiling.Layers.TileLayer(new BruTile.TileSource(tileProvider, schema)) { Name = "OSM" };
    }

    // ========== Node Pinning ==========

    private void NodesListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _tileContextNode = null; // list always uses its own selection
        if (SelectedNodeForMenu is NodeInfo node)
        {
            PinNodeMenuItem.Header      = node.IsPinned    ? Loc("StrUnpin")       : Loc("StrPin");
            FavoriteNodeMenuItem.Header = node.IsFavorite  ? Loc("StrUnfavorite")  : Loc("StrFavorite");
            bool pathActive = _pathLayers.ContainsKey(node.NodeId);
            ShowPathNodeMenuItem.Visibility = pathActive ? Visibility.Collapsed : Visibility.Visible;
            HidePathMenuItem.Visibility = pathActive ? Visibility.Visible : Visibility.Collapsed;
            // Time sync only for our own node
            TimeSyncNodeMenuItem.Visibility = node.NodeId == _myNodeId ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void PinNodeInternal(NodeInfo node)
    {
        node.IsPinned = !node.IsPinned;

        var existing = _allNodes.FirstOrDefault(n => n.NodeId == node.NodeId);
        if (existing != null) existing.IsPinned = node.IsPinned;

        if (node.IsPinned)
            _currentSettings.PinnedNodes[node.NodeId] = true;
        else
            _currentSettings.PinnedNodes.Remove(node.NodeId);
        SettingsService.Save(_currentSettings);

        ApplyNodeSortAndFilterCore();
        Services.Logger.WriteLine($"Node {node.Name} ({node.Id}) {(node.IsPinned ? "pinned" : "unpinned")}");
    }

    private void ToggleFavoriteInternal(NodeInfo node)
    {
        node.IsFavorite = !node.IsFavorite;

        var existing = _allNodes.FirstOrDefault(n => n.NodeId == node.NodeId);
        if (existing != null) existing.IsFavorite = node.IsFavorite;

        if (node.IsFavorite)
            _currentSettings.FavoriteNodes[node.NodeId] = true;
        else
            _currentSettings.FavoriteNodes.Remove(node.NodeId);
        SettingsService.Save(_currentSettings);

        if (_protocolService != null)
        {
            _ = node.IsFavorite
                ? _protocolService.AddFavoriteNodeAsync(node.NodeId)
                : _protocolService.RemoveFavoriteNodeAsync(node.NodeId);
        }

        ApplyNodeSortAndFilterCore();
        Services.Logger.WriteLine($"Node {node.Name} ({node.Id}) {(node.IsFavorite ? "favorited" : "unfavorited")}");
    }

    private void NodeContextMenu_Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeForMenu is NodeInfo node)
            ToggleFavoriteInternal(node);
    }

    private void NodeContextMenu_Pin_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeForMenu is not NodeInfo node) return;
        PinNodeInternal(node);
    }

    // ========== Path Display ==========

    private void NodeContextMenu_ShowPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeForMenu is not NodeInfo node) return;
        ShowPathForNode(node);
    }

    private void NodeContextMenu_HidePath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeForMenu is not NodeInfo node) return;
        HidePathForNode(node);
        HidePathMenuItem.Visibility = Visibility.Collapsed;
    }

    private void ShowPathForNode(NodeInfo node)
    {
        // Prefer DB; fall back to CSV if DB empty
        var dbEntries = _db?.GetNodePositionHistory(node.NodeId, _currentSettings.PositionHistoryHours)
                        ?? new List<Services.TelemetryDatabaseService.NodePositionEntry>();

        List<(double Lat, double Lon, double? Alt, float? Track, float? Speed, DateTime Time)> points;
        if (dbEntries.Count >= 2)
        {
            points = dbEntries.Select(e => (e.Lat, e.Lon, e.Alt, e.Track, e.Speed,
                DateTimeOffset.FromUnixTimeSeconds(e.Timestamp).LocalDateTime)).ToList();
        }
        else
        {
            // Fall back to CSV
            var csvEntries = LocationLogger.ReadLog(node.NodeId);
            if (csvEntries.Count < 2)
            {
                MessageBox.Show("Nicht genug Einträge für einen Pfad (mindestens 2 Punkte benötigt).\n\nPositions-Logging ist aktiv und speichert bei jeder empfangenen GPS-Position.",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            points = csvEntries.Select(e => (e.Latitude, e.Longitude, e.Altitude, e.GroundTrack, e.GroundSpeed, e.Timestamp)).ToList();
        }

        try
        {
            // Remove existing path layer
            if (_pathLayers.TryGetValue(node.NodeId, out var oldLayer))
            {
                _map?.Layers.Remove(oldLayer);
                _pathLayers.Remove(node.NodeId);
            }

            // Base color from node color or default red
            Mapsui.Styles.Color baseColor = Mapsui.Styles.Color.Red;
            if (!string.IsNullOrEmpty(node.ColorHex))
            {
                try
                {
                    var wpfColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(node.ColorHex);
                    baseColor = new Mapsui.Styles.Color(wpfColor.R, wpfColor.G, wpfColor.B);
                }
                catch { }
            }

            var features = new List<IFeature>();
            int n = points.Count;

            // World coords
            var coords = points
                .Select(p => SphericalMercator.FromLonLat(p.Lon, p.Lat))
                .Select(c => new MPoint(c.x, c.y))
                .ToList();

            // Gradient segments: oldest = low alpha, newest = full alpha
            for (int i = 0; i < n - 1; i++)
            {
                double t = n > 2 ? (double)i / (n - 2) : 1.0;
                int alpha = (int)(80 + t * 175);  // 80 (oldest) ? 255 (newest)
                var segColor = new Mapsui.Styles.Color(baseColor.R, baseColor.G, baseColor.B, alpha);
                var segCoords = new[] { new Coordinate(coords[i].X, coords[i].Y), new Coordinate(coords[i + 1].X, coords[i + 1].Y) };
                var segLine = new GeometryFeature(new NetTopologySuite.Geometries.LineString(segCoords));
                segLine.Styles.Add(new VectorStyle { Line = new Mapsui.Styles.Pen(segColor, 3), Fill = null });
                features.Add(segLine);
            }

            // Arrow markers with direction, max 30 evenly distributed
            const int MaxArrows = 30;
            var arrowIndices = n <= MaxArrows
                ? Enumerable.Range(0, n).ToList()
                : Enumerable.Range(0, MaxArrows).Select(i => i * (n - 1) / (MaxArrows - 1)).Distinct().ToList();

            foreach (int i in arrowIndices)
            {
                var pt = points[i];
                double t = n > 1 ? (double)i / (n - 1) : 1.0;
                int alpha = (int)(80 + t * 175);
                var arrowColor = new Mapsui.Styles.Color(baseColor.R, baseColor.G, baseColor.B, alpha);

                // Bearing: use GroundTrack if available, else calculate from next point
                double rotation = 0;
                if (pt.Track.HasValue)
                {
                    rotation = pt.Track.Value;
                }
                else if (i < n - 1)
                {
                    var next = points[i + 1];
                    rotation = BearingDeg(pt.Lat, pt.Lon, next.Lat, next.Lon);
                }
                else if (i > 0)
                {
                    var prev = points[i - 1];
                    rotation = BearingDeg(prev.Lat, prev.Lon, pt.Lat, pt.Lon);
                }

                string speedStr = pt.Speed.HasValue ? $"\n{pt.Speed.Value * 3.6:F0} km/h" : "";
                string trackStr = pt.Track.HasValue ? $"  {pt.Track.Value:F0}°" : "";
                string tooltip  = $"{pt.Time:dd.MM. HH:mm}{speedStr}{trackStr}";
                if (pt.Alt.HasValue) tooltip += $"\n{pt.Alt.Value:F0} m";

                var arrow = new PointFeature(coords[i]);
                arrow.Styles.Add(new SymbolStyle
                {
                    SymbolType    = SymbolType.Triangle,
                    Fill          = new Mapsui.Styles.Brush(arrowColor),
                    Outline       = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 1),
                    SymbolScale   = 0.3,
                    SymbolRotation = rotation
                });
                arrow["tooltip"] = tooltip;
                features.Add(arrow);
            }

            var pathLayer = new MemoryLayer($"Path_{node.NodeId}") { Features = features, Style = null };
            _pathLayers[node.NodeId] = pathLayer;
            _map?.Layers.Add(pathLayer);

            // Vector map: gradient path segments (oldest transparent -> newest opaque) + direction arrows
            var vecFeatures = new List<object>();
            for (int i = 0; i < n - 1; i++)
            {
                double t = n > 2 ? (double)i / (n - 2) : 1.0;
                int alpha = (int)(80 + t * 175);
                var segColor = new Mapsui.Styles.Color(baseColor.R, baseColor.G, baseColor.B, alpha);
                vecFeatures.Add(LineFeature(points[i].Lon, points[i].Lat, points[i + 1].Lon, points[i + 1].Lat,
                    new { color = CssColor(segColor), width = 3 }));
            }
            foreach (int i in arrowIndices)
            {
                var pt = points[i];
                double t = n > 1 ? (double)i / (n - 1) : 1.0;
                int alpha = (int)(80 + t * 175);
                double rotation = 0;
                if (pt.Track.HasValue) rotation = pt.Track.Value;
                else if (i < n - 1) rotation = BearingDeg(pt.Lat, pt.Lon, points[i + 1].Lat, points[i + 1].Lon);
                else if (i > 0) rotation = BearingDeg(points[i - 1].Lat, points[i - 1].Lon, pt.Lat, pt.Lon);
                vecFeatures.Add(PointFeatureGeo(pt.Lon, pt.Lat, new
                {
                    bearing = Math.Round(rotation, 1),
                    iconSize = 0.6,
                    color = CssColor(new Mapsui.Styles.Color(baseColor.R, baseColor.G, baseColor.B, alpha))
                }));
            }
            PushVectorLines($"path_{node.NodeId:x8}", FeatureCollection(vecFeatures));

            // Zoom to fit
            MainTabs.SelectedIndex = 3;
            var minX = coords.Min(c => c.X); var maxX = coords.Max(c => c.X);
            var minY = coords.Min(c => c.Y); var maxY = coords.Max(c => c.Y);
            var paddedSize = Math.Max(Math.Max(maxX - minX, maxY - minY) * 1.3, 1000.0);
            _map?.Navigator.CenterOnAndZoomTo(new MPoint((minX + maxX) / 2, (minY + maxY) / 2),
                Math.Max(paddedSize / 800.0, 10.0));
            MapControl.Refresh();

            if (UseVectorMap)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double w = points.Min(p => p.Lon), e2 = points.Max(p => p.Lon);
                double s2 = points.Min(p => p.Lat), n2 = points.Max(p => p.Lat);
                ExecVectorScript($"fitBounds({w.ToString(ci)}, {s2.ToString(ci)}, {e2.ToString(ci)}, {n2.ToString(ci)})");
            }

            HidePathMenuItem.Visibility = Visibility.Visible;
            Services.Logger.WriteLine($"Path layer for {node.Name}: {n} points (source: {(dbEntries.Count >= 2 ? "DB" : "CSV")})");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR showing path: {ex.Message}");
            MessageBox.Show($"Fehler beim Anzeigen des Pfads: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var lat1R = lat1 * Math.PI / 180;
        var lat2R = lat2 * Math.PI / 180;
        var y = Math.Sin(dLon) * Math.Cos(lat2R);
        var x = Math.Cos(lat1R) * Math.Sin(lat2R) - Math.Sin(lat1R) * Math.Cos(lat2R) * Math.Cos(dLon);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    /// <summary>
    /// Builds a zigzag (lightning bolt) polyline between two Mercator points.
    /// Used to visualise MQTT hops in traceroutes (Visio Dial-in style).
    /// </summary>
    private static Coordinate[] BuildMqttZigzag(MPoint from, MPoint to, int peaks = 5)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return new[] { new Coordinate(from.X, from.Y), new Coordinate(to.X, to.Y) };

        // Unit vector along segment and perpendicular normal
        double ux = dx / len, uy = dy / len;
        double nx = -uy,      ny =  ux;

        // Amplitude: 3% of segment length
        double amp = len * 0.03;

        // Build zigzag points: from ? peaks alternating sides ? to
        int totalPts = peaks * 2 + 2;
        var coords = new Coordinate[totalPts];
        coords[0] = new Coordinate(from.X, from.Y);

        for (int k = 0; k < peaks * 2; k++)
        {
            double t   = (k + 1.0) / (peaks * 2 + 1);
            double side = (k % 2 == 0) ? amp : -amp;
            double px  = from.X + ux * len * t + nx * side;
            double py  = from.Y + uy * len * t + ny * side;
            coords[k + 1] = new Coordinate(px, py);
        }
        coords[totalPts - 1] = new Coordinate(to.X, to.Y);
        return coords;
    }

    private void HidePathForNode(NodeInfo node)
    {
        RemoveVectorLines($"path_{node.NodeId:x8}");
        if (_pathLayers.TryGetValue(node.NodeId, out var layer))
        {
            _map?.Layers.Remove(layer);
            _pathLayers.Remove(node.NodeId);
            MapControl.Refresh();
            Services.Logger.WriteLine($"Path layer removed for {node.Name}");
        }
    }

    private void ShowAlertNotification(string nodeName, uint nodeId)
    {
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                _alertNodeId = nodeId;

                // Update notification text
                AlertNotificationText.Text = $"?? Notruf von {nodeName}!";

                // Check if we have position for this node
                var node = _nodes.FirstOrDefault(n => n.NodeId == nodeId);
                bool hasPosition = node != null && node.Latitude.HasValue && node.Longitude.HasValue;

                // Show "Show on Map" button only if we have the node's position
                ShowOnMapButton.Visibility = hasPosition ? Visibility.Visible : Visibility.Collapsed;

                // Show notification bar
                AlertNotificationBar.Visibility = Visibility.Visible;

                // Auto-hide after 30 seconds
                Task.Delay(30000).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (AlertNotificationBar.Visibility == Visibility.Visible)
                        {
                            AlertNotificationBar.Visibility = Visibility.Collapsed;
                        }
                    });
                });
            });
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error showing alert notification: {ex.Message}");
        }
    }

    private void CloseAlertNotification_Click(object sender, RoutedEventArgs e)
    {
        AlertNotificationBar.Visibility = Visibility.Collapsed;
    }

    private void ShowOnMap_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_alertNodeId == null)
                return;

            var node = _nodes.FirstOrDefault(n => n.NodeId == _alertNodeId);
            if (node == null || !node.Latitude.HasValue || !node.Longitude.HasValue)
            {
                MessageBox.Show("Position für diesen Node ist nicht bekannt.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Switch to Map tab
            MainTabs.SelectedIndex = 3; // Map is tab index 3 (0=Messages, 1=Nodes, 2=Channels, 3=Map, 4=Settings, ...)

            // Center map on node position with closer zoom
            var nodePos = SphericalMercator.FromLonLat(node.Longitude.Value, node.Latitude.Value);
            if (_map != null)
            {
                // Zoom level 12 (resolution ~76)
                _map.Navigator.CenterOnAndZoomTo(new MPoint(nodePos.x, nodePos.y), 76.0);
                MapControl.Refresh();
            }

            // Close notification
            AlertNotificationBar.Visibility = Visibility.Collapsed;

            Services.Logger.WriteLine($"Jumped to map position of node {node.Name} (Lat: {node.Latitude}, Lon: {node.Longitude})");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error showing node on map: {ex.Message}");
            MessageBox.Show($"Fehler beim Anzeigen der Node-Position: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -----------------------------------------------------------------------
    //  TRACEROUTE
    // -----------------------------------------------------------------------

    private void OpenTracerouteForNode(NodeInfo node)
    {
        if (!_tracerouteWindows.TryGetValue(node.NodeId, out var win) || !win.IsVisible)
        {
            win = new TracerouteWindow(_protocolService, node, _myNodeId) { Owner = this };
            win.PlotOnMapRequested += (_, result) => PlotTracerouteOnMap(result, node);
            win.ClearFromMapRequested += (_, nodeId) => ClearLiveTracerouteFromMap(nodeId);
            win.Closed += (_, _) => _tracerouteWindows.Remove(node.NodeId);
            _tracerouteWindows[node.NodeId] = win;
        }

        // Feed current node positions so window can compute distances
        var positions = new Dictionary<uint, (double Lat, double Lon)>();
        foreach (var n in _allNodes)
        {
            if (n.Latitude.HasValue && n.Longitude.HasValue)
                positions[n.NodeId] = (n.Latitude.Value, n.Longitude.Value);
        }
        (double Lat, double Lon)? myPos = null;
        if (_currentSettings.MyLatitude != 0 || _currentSettings.MyLongitude != 0)
            myPos = (_currentSettings.MyLatitude, _currentSettings.MyLongitude);

        win.SetKnownPositions(positions, myPos);
        win.Show();
        win.Activate();
    }

    private void OnTracerouteReceived(object? sender, TracerouteResult result)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Services.Logger.WriteLine($"Traceroute result for !{result.DestinationNodeId:x8}: {result.RouteForward.Count} forward hops");

            // Auto-save JSON snapshot on every received result so "Traceroute laden" always has data.
            var positions = new Dictionary<uint, (double Lat, double Lon)>();
            foreach (var n in _allNodes)
                if (n.Latitude.HasValue && n.Longitude.HasValue)
                    positions[n.NodeId] = (n.Latitude.Value, n.Longitude.Value);
            if (_currentSettings.MyLatitude != 0 || _currentSettings.MyLongitude != 0)
                positions[_myNodeId] = (_currentSettings.MyLatitude, _currentSettings.MyLongitude);

            var nodeNames = _allNodes.ToDictionary(n => n.NodeId, n => n.LongName);
            nodeNames[_myNodeId] = Loc("StrMe");

            string destName = nodeNames.TryGetValue(result.DestinationNodeId, out var dn)
                ? dn : $"!{result.DestinationNodeId:x8}";

            SaveTracerouteToFile(result, destName, positions, nodeNames);
        });
    }

    private void PlotTracerouteOnMap(TracerouteResult result, NodeInfo targetNode)
    {
        // Build positions from live nodes
        var positions = new Dictionary<uint, (double Lat, double Lon)>();
        foreach (var n in _allNodes)
            if (n.Latitude.HasValue && n.Longitude.HasValue)
                positions[n.NodeId] = (n.Latitude.Value, n.Longitude.Value);

        // My position: live node data or map settings fallback
        (double Lat, double Lon)? myPos = null;
        if (positions.TryGetValue(_myNodeId, out var np)) myPos = np;
        else if (_currentSettings.MyLatitude != 0 || _currentSettings.MyLongitude != 0)
            myPos = (_currentSettings.MyLatitude, _currentSettings.MyLongitude);
        if (myPos.HasValue) positions[_myNodeId] = myPos.Value;

        // Build node names from live data
        var nodeNames = _allNodes.ToDictionary(n => n.NodeId, n => n.LongName);
        nodeNames[_myNodeId] = Loc("StrMe");

        // Live routes get a stable key per destination (new measurement replaces old on map)
        string layerKey = $"live_{result.DestinationNodeId:x8}";
        string displayName = $"{targetNode.LongName} (live)";

        // Assign palette color (reuse existing live-key color so re-plots keep the same color)
        if (!_tracerouteColors.TryGetValue(layerKey, out var color))
        {
            color = TracerouteColorPalette[_tracerouteColorIndex % TracerouteColorPalette.Length];
            _tracerouteColorIndex++;
        }

        // Save snapshot before drawing (positions captured at this moment)
        SaveTracerouteToFile(result, targetNode.LongName, positions, nodeNames);

        DrawTracerouteLayer(result, displayName, positions, nodeNames, color, layerKey, zoomToFit: true);
    }

    /// <summary>
    /// Core drawing: builds and adds a MemoryLayer for a traceroute.
    /// layerKey uniquely identifies the layer (live or loaded-file-based).
    /// </summary>
    private void DrawTracerouteLayer(
        TracerouteResult result,
        string displayName,
        Dictionary<uint, (double Lat, double Lon)> positions,
        Dictionary<uint, string> nodeNames,
        Mapsui.Styles.Color color,
        string layerKey,
        bool zoomToFit = false)
    {
        // Remove existing layer with this exact key (e.g. previous live plot for same dest)
        if (_tracerouteLayers.TryGetValue(layerKey, out var oldLayer))
        {
            _map?.Layers.Remove(oldLayer);
            _tracerouteLayers.Remove(layerKey);
        }
        // Clear old segment hit targets for this layer (rebuilt below)
        _tracerouteSegmentHits[layerKey] = new List<SegmentHitTarget>();

        var orderedIds = new List<uint> { result.SourceNodeId == 0 ? _myNodeId : result.SourceNodeId };
        orderedIds.AddRange(result.RouteForward);
        orderedIds.Add(result.DestinationNodeId);

        var features = new List<IFeature>();
        MPoint? lastKnownPoint = null;

        const int MqttSentinelRaw = -128; // raw SnrTowards = -128 ? -32 dB = MQTT hop

        for (int i = 0; i < orderedIds.Count - 1; i++)
        {
            uint fromId = orderedIds[i];
            uint toId   = orderedIds[i + 1];

            bool hasFrom = positions.TryGetValue(fromId, out var fromPos);
            bool hasTo   = positions.TryGetValue(toId,   out var toPos);
            bool isMqtt  = result.IsViaMqtt
                         || (result.SnrTowards.Count > i && result.SnrTowards[i] == MqttSentinelRaw);

            if (hasFrom && hasTo)
            {
                var ptFrom = SphericalMercator.FromLonLat(fromPos.Lon, fromPos.Lat);
                var ptTo   = SphericalMercator.FromLonLat(toPos.Lon,   toPos.Lat);
                var mFrom  = new MPoint(ptFrom.x, ptFrom.y);
                var mTo    = new MPoint(ptTo.x,   ptTo.y);

                float? segSnr = (!isMqtt && result.SnrTowards.Count > i) ? result.SnrTowards[i] / 4f : null;

                if (isMqtt)
                {
                    // ? Zickzack-Blitzlinie für MQTT-Hops (statt gerader Linie)
                    var zigCoords = BuildMqttZigzag(mFrom, mTo);
                    var zigGeom = new NetTopologySuite.Geometries.LineString(zigCoords);

                    var zigBorder = new GeometryFeature(zigGeom);
                    zigBorder.Styles.Add(new VectorStyle { Line = new Mapsui.Styles.Pen(Mapsui.Styles.Color.Black, 4.5), Fill = null });
                    features.Add(zigBorder);

                    var zigLine = new GeometryFeature(zigGeom);
                    zigLine.Styles.Add(new VectorStyle { Line = new Mapsui.Styles.Pen(new Mapsui.Styles.Color(255, 220, 0, 255), 2.5), Fill = null });
                    features.Add(zigLine);
                    // Track midpoint for MQTT segments too (T3)
                    var midMqtt = new MPoint((ptFrom.x + ptTo.x) / 2, (ptFrom.y + ptTo.y) / 2);
                    _tracerouteSegmentHits[layerKey].Add(new SegmentHitTarget(midMqtt, fromId, toId, null, true));
                }
                else
                {
                    // Normale Linie
                    var segCoords = new[] { new Coordinate(ptFrom.x, ptFrom.y), new Coordinate(ptTo.x, ptTo.y) };
                    var line = new GeometryFeature(new NetTopologySuite.Geometries.LineString(segCoords));
                    line.Styles.Add(new VectorStyle { Line = new Mapsui.Styles.Pen(color, 2.5), Fill = null });
                    features.Add(line);

                    // Richtungspfeil auf Segmentmitte (T2)
                    var midX = (ptFrom.x + ptTo.x) / 2;
                    var midY = (ptFrom.y + ptTo.y) / 2;
                    var midPt = new MPoint(midX, midY);
                    // Atan2 gibt Winkel Ost=0 CCW; Mapsui-Rotation: 0=Norden CW ? konvertieren
                    var bearing    = Math.Atan2(ptTo.y - ptFrom.y, ptTo.x - ptFrom.x) * 180 / Math.PI;
                    var mapRotation = (90 - bearing + 360) % 360;
                    var arrow = new PointFeature(midPt);
                    arrow.Styles.Add(new SymbolStyle
                    {
                        SymbolType     = SymbolType.Triangle,
                        Fill           = new Mapsui.Styles.Brush(color),
                        Outline        = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 1),
                        SymbolScale    = 0.25,
                        SymbolRotation = mapRotation
                    });
                    features.Add(arrow);
                    // Visible click indicator for T3 segment hit (small semi-transparent circle)
                    var clickDot = new PointFeature(midPt);
                    clickDot.Styles.Add(new SymbolStyle
                    {
                        SymbolType  = SymbolType.Ellipse,
                        Fill        = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(color.R, color.G, color.B, 160)),
                        Outline     = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 1.2f),
                        SymbolScale = 0.35,
                    });
                    features.Add(clickDot);
                    // Track midpoint for click detection (T3)
                    _tracerouteSegmentHits[layerKey].Add(new SegmentHitTarget(midPt, fromId, toId, segSnr, false));
                }
                lastKnownPoint = mTo;
            }
            else if (hasFrom && !hasTo)
            {
                var ptFrom = SphericalMercator.FromLonLat(fromPos.Lon, fromPos.Lat);
                var start  = new MPoint(ptFrom.x, ptFrom.y);
                features.AddRange(MakeDashedLine(start, null, "?", color));
                lastKnownPoint = start;
            }
            else if (!hasFrom && hasTo)
            {
                var ptTo = SphericalMercator.FromLonLat(toPos.Lon, toPos.Lat);
                var end  = new MPoint(ptTo.x, ptTo.y);
                features.AddRange(MakeDashedLine(lastKnownPoint, end, "?", color));
                lastKnownPoint = end;
            }
            else
            {
                if (lastKnownPoint != null)
                    features.AddRange(MakeDashedLine(lastKnownPoint, null, "?", color));
            }
        }

        // Dots + labels for each hop with known position
        var dotColor = new Mapsui.Styles.Color(color.R, color.G, color.B, 200);
        var bgColor  = new Mapsui.Styles.Color(color.R, color.G, color.B, 160);
        foreach (var nodeId in orderedIds)
        {
            if (!positions.TryGetValue(nodeId, out var pos)) continue;
            var pt = SphericalMercator.FromLonLat(pos.Lon, pos.Lat);
            var mpt = new MPoint(pt.x, pt.y);

            string label = nodeId == _myNodeId ? Loc("StrMe") : $"!{nodeId:x4}";
            if (nodeNames.TryGetValue(nodeId, out var nm)) label = nm;

            var pin = new PointFeature(mpt);
            pin.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new Mapsui.Styles.Brush(dotColor),
                Outline = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 1),
                SymbolScale = 0.2,
            });
            pin.Styles.Add(new LabelStyle
            {
                Text = label,
                ForeColor = Mapsui.Styles.Color.Black,
                BackColor = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(255, 255, 255, 180)),
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Left,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
                Offset = new Offset(8, 0),
                Font = new Mapsui.Styles.Font { FontFamily = "Segoe UI Emoji", Size = 9 },
            });
            features.Add(pin);
        }

        var traceLayer = new MemoryLayer($"Traceroute_{layerKey}") { Features = features, Style = null };
        _tracerouteLayers[layerKey] = traceLayer;
        _tracerouteNames[layerKey] = displayName;
        _tracerouteColors[layerKey] = color;
        _map?.Layers.Add(traceLayer);
        UpdateTracerouteLegend();

        if (zoomToFit)
        {
            MainTabs.SelectedIndex = 3;
            var allPts = orderedIds
                .Where(id => positions.ContainsKey(id))
                .Select(id => { var p = SphericalMercator.FromLonLat(positions[id].Lon, positions[id].Lat); return new MPoint(p.x, p.y); })
                .ToList();

            if (allPts.Count >= 2)
            {
                double minX = allPts.Min(p => p.X), maxX = allPts.Max(p => p.X);
                double minY = allPts.Min(p => p.Y), maxY = allPts.Max(p => p.Y);
                var center = new MPoint((minX + maxX) / 2, (minY + maxY) / 2);
                double extent = Math.Max(maxX - minX, maxY - minY);
                _map?.Navigator.CenterOnAndZoomTo(center, Math.Max(extent * 1.4 / 800.0, 10.0));
            }
            else if (allPts.Count == 1)
                _map?.Navigator.CenterOnAndZoomTo(allPts[0], 10.0);

            // Vector map: fit to the route's lon/lat bounds
            if (UseVectorMap)
            {
                var geo = orderedIds.Where(id => positions.ContainsKey(id)).Select(id => positions[id]).ToList();
                if (geo.Count >= 1)
                {
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    double w = geo.Min(p => p.Lon), e = geo.Max(p => p.Lon);
                    double s = geo.Min(p => p.Lat), n = geo.Max(p => p.Lat);
                    ExecVectorScript($"fitBounds({w.ToString(ci)}, {s.ToString(ci)}, {e.ToString(ci)}, {n.ToString(ci)})");
                }
            }

            Services.Logger.WriteLine($"Traceroute plotted [{layerKey}] for {displayName}: {allPts.Count} known positions");
        }

        MapControl.Refresh();
        PushTracerouteToVectorMap(result, positions, nodeNames, color, layerKey);
    }

    /// <summary>Mirrors DrawTracerouteLayer() onto the vector map: colored segments with
    /// direction arrows, MQTT zigzag ("Blitz"), dashed unknown-route bridges with "?"
    /// markers, clickable midpoint dots and labeled hop dots.</summary>
    private void PushTracerouteToVectorMap(
        TracerouteResult result,
        Dictionary<uint, (double Lat, double Lon)> positions,
        Dictionary<uint, string> nodeNames,
        Mapsui.Styles.Color color,
        string layerKey)
    {
        try
        {
            var orderedIds = new List<uint> { result.SourceNodeId == 0 ? _myNodeId : result.SourceNodeId };
            orderedIds.AddRange(result.RouteForward);
            orderedIds.Add(result.DestinationNodeId);

            const int MqttSentinelRaw = -128;
            var features = new List<object>();
            var colorCss = CssColor(color);
            (double Lat, double Lon)? lastKnown = null;

            void AddUnknownMarker(double lon, double lat) =>
                features.Add(PointFeatureGeo(lon, lat, new { label = "?", radius = 4, color = "rgba(120,120,120,0.85)" }));

            void AddDashedBridge((double Lat, double Lon) from, (double Lat, double Lon) to)
            {
                features.Add(LineFeature(from.Lon, from.Lat, to.Lon, to.Lat,
                    new { dash = 1, color = colorCss, width = 2.0 }));
                AddUnknownMarker(to.Lon, to.Lat);
            }

            for (int i = 0; i < orderedIds.Count - 1; i++)
            {
                uint fromId = orderedIds[i];
                uint toId = orderedIds[i + 1];
                bool hasFrom = positions.TryGetValue(fromId, out var fromPos);
                bool hasTo = positions.TryGetValue(toId, out var toPos);

                bool isMqtt = result.IsViaMqtt
                            || (result.SnrTowards.Count > i && result.SnrTowards[i] == MqttSentinelRaw);
                float? segSnr = (!isMqtt && result.SnrTowards.Count > i) ? result.SnrTowards[i] / 4f : null;

                if (hasFrom && hasTo)
                {
                    if (isMqtt)
                    {
                        // Zigzag "Blitz" polyline like the raster map (built in Mercator, converted back)
                        var mFrom = SphericalMercator.FromLonLat(fromPos.Lon, fromPos.Lat);
                        var mTo = SphericalMercator.FromLonLat(toPos.Lon, toPos.Lat);
                        var zig = BuildMqttZigzag(new MPoint(mFrom.x, mFrom.y), new MPoint(mTo.x, mTo.y))
                            .Select(c => { var ll = SphericalMercator.ToLonLat(c.X, c.Y); return new[] { ll.lon, ll.lat }; })
                            .ToList();
                        features.Add(LineFeatureCoords(zig, new { outline = 1, color = "rgba(0,0,0,0.85)", width = 4.5 }));
                        features.Add(LineFeatureCoords(zig, new { color = "#ffdc00", width = 2.5 }));
                    }
                    else
                    {
                        features.Add(LineFeature(fromPos.Lon, fromPos.Lat, toPos.Lon, toPos.Lat,
                            new { color = colorCss, width = 2.5 }));
                    }

                    // Clickable midpoint dot -> SNR popup; direction arrow on RF segments
                    var midProps = new Dictionary<string, object?>
                    {
                        ["click"] = 1,
                        ["fromId"] = fromId,
                        ["toId"] = toId,
                        ["mqtt"] = isMqtt ? 1 : 0,
                        ["color"] = isMqtt ? "rgba(255,220,0,0.7)" : CssColor(new Mapsui.Styles.Color(color.R, color.G, color.B, 160)),
                        ["radius"] = 6
                    };
                    if (segSnr.HasValue) midProps["snr"] = segSnr.Value;
                    if (!isMqtt)
                    {
                        midProps["bearing"] = Math.Round(BearingDeg(fromPos.Lat, fromPos.Lon, toPos.Lat, toPos.Lon), 1);
                        midProps["iconSize"] = 0.55;
                    }
                    features.Add(PointFeatureGeo((fromPos.Lon + toPos.Lon) / 2, (fromPos.Lat + toPos.Lat) / 2, midProps));
                    lastKnown = toPos;
                }
                else if (hasFrom && !hasTo)
                {
                    // Destination position unknown: "?" marker at the known end
                    AddUnknownMarker(fromPos.Lon, fromPos.Lat);
                    lastKnown = fromPos;
                }
                else if (!hasFrom && hasTo)
                {
                    // Bridge the unknown gap with a dashed line from the last known position
                    if (lastKnown.HasValue) AddDashedBridge(lastKnown.Value, toPos);
                    else AddUnknownMarker(toPos.Lon, toPos.Lat);
                    lastKnown = toPos;
                }
            }

            // Hop dots + labels
            var dotCss = CssColor(new Mapsui.Styles.Color(color.R, color.G, color.B, 200));
            foreach (var nodeId in orderedIds.Distinct())
            {
                if (!positions.TryGetValue(nodeId, out var pos)) continue;
                string label = nodeId == _myNodeId ? Loc("StrMe") : $"!{nodeId:x4}";
                if (nodeNames.TryGetValue(nodeId, out var nm)) label = nm;
                features.Add(PointFeatureGeo(pos.Lon, pos.Lat, new { color = dotCss, radius = 4, label }));
            }

            if (features.Count == 0) RemoveVectorLines(layerKey);
            else PushVectorLines(layerKey, FeatureCollection(features));
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[VectorMap] traceroute push error: {ex.Message}");
        }
    }

    private void SaveTracerouteToFile(
        TracerouteResult result,
        string destName,
        Dictionary<uint, (double Lat, double Lon)> positions,
        Dictionary<uint, string> nodeNames)
    {
        try
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "traceroutes");
            Directory.CreateDirectory(folder);

            // Collect all node IDs in this route
            var allIds = new HashSet<uint> { result.SourceNodeId == 0 ? _myNodeId : result.SourceNodeId, result.DestinationNodeId };
            foreach (var id in result.RouteForward) allIds.Add(id);
            foreach (var id in result.RouteBack)   allIds.Add(id);

            var saveData = new TracerouteSaveData
            {
                Result = result,
                DestinationName = destName,
                Nodes = allIds.Select(id => new TracerouteSaveData.NodeEntry
                {
                    NodeId = id,
                    Name   = nodeNames.TryGetValue(id, out var n) ? n : $"!{id:x8}",
                    Lat    = positions.TryGetValue(id, out var p) ? p.Lat : null,
                    Lon    = positions.TryGetValue(id, out var p2) ? p2.Lon : null,
                }).ToList(),
            };

            string ts = result.ReceivedAt.ToString("yyyyMMdd_HHmmss");
            string file = Path.Combine(folder, $"traceroute_!{result.DestinationNodeId:x8}_{ts}.json");
            string json = JsonSerializer.Serialize(saveData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
            Services.Logger.WriteLine($"Traceroute saved: {Path.GetFileName(file)}");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error saving traceroute: {ex.Message}");
        }
    }

    private void MapLoadTraceroute_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Traceroute laden",
            Filter = "Traceroute-Dateien (*.json)|*.json",
            InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "traceroutes"),
            Multiselect = true,
        };

        if (dlg.ShowDialog() != true) return;

        foreach (var file in dlg.FileNames)
        {
            try
            {
                string json = File.ReadAllText(file);
                var saveData = JsonSerializer.Deserialize<TracerouteSaveData>(json);
                if (saveData?.Result == null) continue;

                // Use filename (without extension) as the unique layer key so multiple
                // saves for the same destination can coexist on the map.
                string layerKey = Path.GetFileNameWithoutExtension(file);
                string ts = saveData.Result.ReceivedAt.ToString("dd.MM HH:mm");
                string displayName = $"{saveData.DestinationName} ({ts})";

                var positions = saveData.GetPositionsDict();
                var nodeNames = saveData.GetNamesDict();

                // Each loaded file always gets its own fresh color from the palette
                var color = TracerouteColorPalette[_tracerouteColorIndex % TracerouteColorPalette.Length];
                _tracerouteColorIndex++;

                DrawTracerouteLayer(saveData.Result, displayName, positions, nodeNames, color, layerKey, zoomToFit: true);
                Services.Logger.WriteLine($"Traceroute loaded: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Error loading traceroute {Path.GetFileName(file)}: {ex.Message}");
                MessageBox.Show($"Fehler beim Laden:\n{ex.Message}", "Traceroute laden", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void MapLoadTracerouteFromDb_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) { MessageBox.Show("Datenbank nicht verfügbar.", "Traceroute aus DB", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        int days = 7;
        if (TracerouteAgeFilterCombo.SelectedItem is System.Windows.Controls.ComboBoxItem ci && int.TryParse(ci.Tag?.ToString(), out var d))
            days = d;

        var traceroutes = _db.GetRecentTracerouteResults(days);
        if (traceroutes.Count == 0) { MessageBox.Show($"Keine Traceroutes in den letzten {(days == 0 ? "8" : days.ToString())} Tagen.", "Traceroute aus DB", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        // T5: node-pair deduplication (only keep newest per A?B pair)
        bool dedupe = TracerouteDedupeCheckBox?.IsChecked == true;
        if (dedupe)
        {
            var seen = new HashSet<string>();
            var deduped = new List<Models.TracerouteResult>();
            foreach (var tr in traceroutes) // already ordered newest-first
            {
                uint a = Math.Min(tr.SourceNodeId, tr.DestinationNodeId);
                uint b = Math.Max(tr.SourceNodeId, tr.DestinationNodeId);
                string key = $"{a}_{b}";
                if (seen.Add(key)) deduped.Add(tr);
            }
            traceroutes = deduped;
        }

        // Build position + name lookup from known nodes
        var positions = _nodes
            .Where(n => n.Latitude.HasValue && n.Longitude.HasValue)
            .ToDictionary(n => n.NodeId, n => (Lat: n.Latitude!.Value, Lon: n.Longitude!.Value));
        var nodeNames = _nodes.ToDictionary(n => n.NodeId, n => string.IsNullOrEmpty(n.ShortName) ? $"!{n.NodeId:x4}" : n.ShortName);

        int drawn = 0;
        foreach (var tr in traceroutes)
        {
            string layerKey = $"db_{tr.RequestId}_{tr.SourceNodeId:x}_{tr.DestinationNodeId:x}";
            if (_tracerouteLayers.ContainsKey(layerKey)) continue; // already on map

            var color = TracerouteColorPalette[_tracerouteColorIndex % TracerouteColorPalette.Length];
            _tracerouteColorIndex++;
            string ts = tr.ReceivedAt.ToString("dd.MM HH:mm");
            string destName = nodeNames.TryGetValue(tr.DestinationNodeId, out var dn) ? dn : $"!{tr.DestinationNodeId:x4}";
            DrawTracerouteLayer(tr, $"{destName} ({ts})", positions, nodeNames, color, layerKey, zoomToFit: false);
            drawn++;
        }

        if (drawn == 0)
            MapStatusText.Text = "Alle DB-Traceroutes bereits auf der Karte.";
        else
        {
            MainTabs.SelectedIndex = 3;
            MapStatusText.Text = $"{drawn} Traceroute(s) aus DB geladen ({days}d).";
        }
    }

    /// <summary>
    /// Simulates a dashed line between two map points (or a "?" marker if toPoint is null).
    /// Draws alternating short LineString segments using the traceroute's assigned color.
    /// </summary>
    private static List<IFeature> MakeDashedLine(MPoint? fromPoint, MPoint? toPoint, string unknownLabel, Mapsui.Styles.Color? color = null)
    {
        var result = new List<IFeature>();
        var dashColor = color ?? Mapsui.Styles.Color.Cyan;

        if (fromPoint == null)
        {
            if (toPoint != null) result.Add(MakeUnknownMarker(toPoint, unknownLabel));
            return result;
        }

        if (toPoint == null)
        {
            // No destination: just put a "?" marker at the known end
            result.Add(MakeUnknownMarker(fromPoint, unknownLabel));
            return result;
        }

        // Draw dashed line: split into N segments, draw even ones, skip odd ones
        const int steps = 16; // 8 dashes + 8 gaps
        double dx = toPoint.X - fromPoint.X;
        double dy = toPoint.Y - fromPoint.Y;

        for (int s = 0; s < steps; s += 2) // only even = dash, skip odd = gap
        {
            double t0 = (double)s / steps;
            double t1 = (double)(s + 1) / steps;
            var p0 = new Coordinate(fromPoint.X + dx * t0, fromPoint.Y + dy * t0);
            var p1 = new Coordinate(fromPoint.X + dx * t1, fromPoint.Y + dy * t1);
            var seg = new GeometryFeature(new NetTopologySuite.Geometries.LineString(new[] { p0, p1 }));
            seg.Styles.Add(new VectorStyle { Line = new Mapsui.Styles.Pen(dashColor, 2.0), Fill = null });
            result.Add(seg);
        }

        // "?" marker at the destination end
        result.Add(MakeUnknownMarker(toPoint, unknownLabel));
        return result;
    }

    private void ClearTracerouteFromMap(string layerKey)
    {
        RemoveVectorLines(layerKey);
        if (_tracerouteLayers.TryGetValue(layerKey, out var layer))
        {
            _map?.Layers.Remove(layer);
            _tracerouteLayers.Remove(layerKey);
            _tracerouteNames.Remove(layerKey);
            _tracerouteSegmentHits.Remove(layerKey);
            // Keep color in _tracerouteColors so re-plotting reuses the same color
            MapControl.Refresh();
            UpdateTracerouteLegend();
            Services.Logger.WriteLine($"Traceroute layer cleared: {layerKey}");
        }
    }

    /// <summary>Called by TracerouteWindow's ClearFromMapRequested (passes uint destId).</summary>
    private void ClearLiveTracerouteFromMap(uint destinationNodeId)
        => ClearTracerouteFromMap($"live_{destinationNodeId:x8}");

    private void ClearAllTraceroutes_Click(object sender, RoutedEventArgs e)
    {
        foreach (var key in _tracerouteLayers.Keys.ToList())
            ClearTracerouteFromMap(key);
    }

    private void MapLegendClose_Click(object sender, RoutedEventArgs e)
    {
        MapLegendBorder.Visibility = Visibility.Collapsed;
    }

    private void UpdateTracerouteLegend()
    {
        TracerouteLegendItems.Items.Clear();

        if (_tracerouteLayers.Count == 0)
        {
            TracerouteLegend.Visibility = Visibility.Collapsed;
            return;
        }

        TracerouteLegend.Visibility = Visibility.Visible;
        foreach (var kv in _tracerouteLayers)
        {
            string layerKey = kv.Key;
            string name = _tracerouteNames.TryGetValue(layerKey, out var n) ? n : layerKey;
            bool isLive = layerKey.StartsWith("live_");

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            // Colored dot matching the traceroute line color
            var mc = _tracerouteColors.TryGetValue(layerKey, out var tc)
                ? System.Windows.Media.Color.FromArgb((byte)tc.A, (byte)tc.R, (byte)tc.G, (byte)tc.B)
                : Colors.Cyan;
            row.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 10, Height = 10,
                Fill = new SolidColorBrush(mc),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            // Live indicator
            if (isLive)
                row.Children.Add(new TextBlock
                {
                    Text = "??",
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });

            // Display name (includes timestamp for loaded routes)
            row.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 160,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            // Clear button
            var clearBtn = new Button
            {
                Content = "?",
                FontSize = 10,
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 255, 80, 80)),
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand,
                ToolTip = "Traceroute von Karte entfernen",
            };
            string capturedKey = layerKey;
            clearBtn.Click += (_, _) => ClearTracerouteFromMap(capturedKey);
            row.Children.Add(clearBtn);

            TracerouteLegendItems.Items.Add(row);
        }
    }

    private static PointFeature MakeUnknownMarker(MPoint pos, string label)
    {
        var f = new PointFeature(pos);
        f.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Fill = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(150, 150, 150, 180)),
            Outline = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 1),
            SymbolScale = 0.35,
        });
        f.Styles.Add(new LabelStyle
        {
            Text = label,
            ForeColor = Mapsui.Styles.Color.White,
            BackColor = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(100, 100, 100, 200)),
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
            Font = new Mapsui.Styles.Font { FontFamily = "Segoe UI Emoji", Size = 9 },
        });
        return f;
    }

    // -----------------------------------------------------------------------
    //  TELEMETRY
    // -----------------------------------------------------------------------

    private void OnDeviceTelemetryReceived(object? sender, (uint NodeId, float BatteryPercent, float Voltage) e)
    {
        // Already handled in HandleTelemetryPacket via NodeInfoReceived – no extra UI update needed here.
        // This event is available for future consumers (e.g. live tile updates).
    }

    private void OnTimeDriftDetected(object? sender, int driftSeconds)
    {
        Services.Logger.WriteLine($"Auto time sync triggered (drift={driftSeconds}s)");
        _ = _protocolService.SendTimeSyncAsync();
    }

    private void StartTimeSyncTimer()
    {
        _timeSyncTimer?.Dispose();
        _timeSyncTimer = new System.Threading.Timer(async _ =>
        {
            if (_connectionService?.IsConnected == true)
            {
                Services.Logger.WriteLine("Scheduled 12h time sync");
                await _protocolService.SendTimeSyncAsync();
            }
        }, null, TimeSpan.FromHours(12), TimeSpan.FromHours(12));
    }

    private void OnWaypointReceived(object? sender, TelemetryDatabaseService.WaypointEntry wp)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var existing = _waypoints.FirstOrDefault(w => w.Id == wp.Id);
            if (existing != null) _waypoints.Remove(existing);
            _waypoints.Add(wp);
            RefreshWaypointLayer();
        });
    }

    private void OnWaypointDeleted(uint id)
    {
        var existing = _waypoints.FirstOrDefault(w => w.Id == id);
        if (existing == null) return;
        _waypoints.Remove(existing);
        _db?.DeleteWaypoint(id);
        RefreshWaypointLayer();
    }

    private void RefreshWaypointLayer()
    {
        PushWaypointsToVectorMap();
        if (_map == null) return;

        if (_waypointLayer != null)
            _map.Layers.Remove(_waypointLayer);

        var features = new List<IFeature>();
        foreach (var wp in _waypoints)
        {
            var pt = SphericalMercator.FromLonLat(wp.Longitude, wp.Latitude);
            var mpt = new MPoint(pt.x, pt.y);

            // Icon: use emoji char if available, else ??
            string iconText = wp.Icon > 0 ? char.ConvertFromUtf32((int)wp.Icon) : "??";

            var pin = new PointFeature(mpt);
            pin.Styles.Add(new SymbolStyle
            {
                SymbolType  = SymbolType.Ellipse,
                Fill        = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(255, 165, 0, 220)),
                Outline     = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 1.5f),
                SymbolScale = 0.55,
            });
            pin.Styles.Add(new LabelStyle
            {
                Text                = $"{iconText} {wp.Name}",
                ForeColor           = Mapsui.Styles.Color.Black,
                BackColor           = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(255, 255, 255, 190)),
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Left,
                VerticalAlignment   = LabelStyle.VerticalAlignmentEnum.Center,
                Offset              = new Offset(10, 0),
                Font                = new Mapsui.Styles.Font { FontFamily = "Segoe UI Emoji", Size = 11 },
            });
            features.Add(pin);
        }

        _waypointLayer = new MemoryLayer("Waypoints") { Features = features, Style = null };
        _map.Layers.Add(_waypointLayer);

        // Populate waypoint pin positions for hit testing
        _waypointPinPositions.Clear();
        foreach (var wp in _waypoints)
        {
            var pt = SphericalMercator.FromLonLat(wp.Longitude, wp.Latitude);
            _waypointPinPositions[wp.Id] = new MPoint(pt.x, pt.y);
        }

        MapControl.Refresh();
    }

    private void LoadWaypointsFromDb()
    {
        if (_db == null) return;
        var wps = _db.GetAllWaypoints(excludeExpired: true);
        _waypoints.Clear();
        _waypoints.AddRange(wps);
        RefreshWaypointLayer();
        Services.Logger.WriteLine($"Loaded {wps.Count} waypoints from DB.");
    }

    private async void CreateWaypointAt(double lat, double lon)
    {
        var dlg = new WaypointCreateDialog(lat, lon) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        uint expireUnix = dlg.ExpireHours > 0
            ? (uint)DateTimeOffset.UtcNow.AddHours(dlg.ExpireHours).ToUnixTimeSeconds()
            : 0;

        var entry = new TelemetryDatabaseService.WaypointEntry(
            Id:          (uint)Random.Shared.Next(1, int.MaxValue),
            Name:        dlg.WaypointName,
            Description: dlg.WaypointDescription,
            Latitude:    lat,
            Longitude:   lon,
            Expire:      expireUnix > 0 ? expireUnix : null,
            LockedTo:    null,  // 0 = anyone can edit (don't lock to sender)
            Icon:        dlg.WaypointIcon,
            FromNode:    _myNodeId,
            ReceivedAt:  DateTime.Now);

        _db?.UpsertWaypoint(entry);
        _waypoints.Add(entry);
        RefreshWaypointLayer();

        if (dlg.SendToMesh && _connectionService?.IsConnected == true)
            await _protocolService.SendWaypointAsync(entry);

        MapStatusText.Text = dlg.SendToMesh
            ? string.Format(Loc("StrWpCreatedStatus"), entry.Name)
            : $"Waypoint \"{entry.Name}\" erstellt (nur lokal).";
    }

    private async void TimeSyncManual_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionService?.IsConnected != true) return;
        bool ok = await _protocolService.SendTimeSyncAsync();
        MapStatusText.Text = ok ? Loc("StrTimeSyncSent") : Loc("StrTimeSyncFailed");
    }

    private void OnNodeKeyMismatch(object? sender, Services.NodeKeyMismatchEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var dlg = new NodeKeyMismatchDialog(e.NodeId, e.ShortName, e.OldKeyBase64, e.NewKeyBase64)
            {
                Owner = this
            };
            e.Accept = dlg.ShowDialog() == true;
        });
    }

    private void RefreshSignalAnalysis()
    {
        if (_db == null) return;
        try
        {
            var nodeNames = _allNodes.ToDictionary(n => n.NodeId, n => n.Name);
            int shortH = _currentSettings.SignalWeatherWindowHours;
            int longD  = _currentSettings.SignalAntennaWindowDays;

            // Global signal analysis (nodeId=0 ? all nodes)
            var analysis = _db.GetSignalAnalysis(0, shortH, longD, nodeNames);
            // Global mesh health
            var health = _db.GetMeshHealthScore(longD);
            // Last known SNR / battery per node from DB (pre-populates LEDs even before live packets arrive)
            var dbSnr     = _db.GetLastSnrPerNode(longD);
            var dbBattery = _db.GetLastBatteryPerNode(longD);

            Dispatcher.Invoke(() =>
            {
                // Restore SnrValue / BatteryValue from DB for nodes that haven't received live data yet
                foreach (var node in _allNodes)
                {
                    if (node.SnrValue == null && dbSnr.TryGetValue(node.NodeId, out var snr))
                        node.SnrValue = snr;
                    if (node.BatteryValue == null && dbBattery.TryGetValue(node.NodeId, out var bat))
                        node.BatteryValue = bat;
                }

                // Global 4-LED signal status strip
                LedFill(GlobalWeatherLed, analysis.WeatherLed,
                    string.Format(Loc("StrLedWeatherEffect"), analysis.DecliningNeighbors, analysis.TotalNeighbors));
                LedFill(GlobalAntennaLed, analysis.AntennaLed,
                    analysis.AvgLongSlope > 0.2f ? string.Format(Loc("StrLedAntennaUp"), analysis.AvgLongSlope)
                  : analysis.AvgLongSlope < -0.2f ? string.Format(Loc("StrLedAntennaDown"), analysis.AvgLongSlope)
                  : Loc("StrLedAntennaFlat"));
                LedFill(GlobalNeighborLed, analysis.NeighborLed,
                    string.IsNullOrEmpty(analysis.ProblemNeighborName)
                    ? Loc("StrLedAllGoodNeighbor")
                    : string.Format(Loc("StrLedNeighborProblem"), analysis.ProblemNeighborName));
                LedFill(GlobalPathLed, analysis.PathLed,
                    string.Format(Loc("StrLedPathStability"), analysis.RouteChangeRate,
                        $"Hop-Kosten: {analysis.HopCost:0.00}"));

                // Update per-node signal trend colors from analysis trends
                var trendDict = analysis.Trends.ToDictionary(t => t.NeighborId);
                foreach (var node in _allNodes)
                {
                    if (trendDict.TryGetValue(node.NodeId, out var trend))
                    {
                        node.SignalTrendColor = trend.ShortSlope < -1.0f ? "#F44336"
                                             : trend.ShortSlope < -0.3f ? "#FFC107"
                                             : trend.PointCount >= 5    ? "#4CAF50"
                                             : string.Empty;
                    }
                }
                NodesListView.Items.Refresh();

                // -- Individuelle Trend-Pfeile pro Metrik -------------------------------
                SetTopArrow(GlobalWeatherArrow,
                    up:   analysis.TotalNeighbors > 0 && analysis.DecliningNeighbors == 0,
                    down: analysis.TotalNeighbors > 0 && analysis.DecliningNeighbors >= Math.Max(1, analysis.TotalNeighbors * 0.25),
                    noData: analysis.TotalNeighbors == 0,
                    upTip:   Loc("StrArrowWeatherUp"),
                    flatTip: string.Format(Loc("StrArrowWeatherFlat"), analysis.DecliningNeighbors, analysis.TotalNeighbors),
                    downTip: string.Format(Loc("StrArrowWeatherDown"), analysis.DecliningNeighbors, analysis.TotalNeighbors));

                float snrSlope = analysis.AvgLongSlope; // dB/day
                SetTopArrow(GlobalAntennaArrow,
                    up:   snrSlope >  0.2f,
                    down: snrSlope < -0.2f,
                    noData: analysis.TotalNeighbors == 0,
                    upTip:   string.Format(Loc("StrArrowAntennaUp"), snrSlope, analysis.TotalNeighbors),
                    flatTip: string.Format(Loc("StrArrowAntennaFlat"), snrSlope, analysis.TotalNeighbors),
                    downTip: string.Format(Loc("StrArrowAntennaDown"), snrSlope, analysis.TotalNeighbors));

                bool hasNeighborProblem = analysis.ProblemNeighborId.HasValue;
                SetTopArrow(GlobalNeighborArrow,
                    up:   !hasNeighborProblem && analysis.TotalNeighbors > 0,
                    down: hasNeighborProblem,
                    noData: analysis.TotalNeighbors == 0,
                    upTip:   Loc("StrArrowNeighborUp"),
                    flatTip: Loc("StrArrowNeighborFlat"),
                    downTip: string.Format(Loc("StrArrowNeighborDown"), analysis.ProblemNeighborName));

                SetTopArrow(GlobalPathArrow,
                    up:   analysis.HopCost < 0.2f && analysis.RouteChangeRate < 1f,
                    down: analysis.HopCost > 0.5f || analysis.RouteChangeRate > 3f,
                    noData: analysis.HopCost == 0f && analysis.RouteChangeRate == 0f,
                    upTip:   string.Format(Loc("StrArrowPathUp"), analysis.HopCost, analysis.RouteChangeRate),
                    flatTip: string.Format(Loc("StrArrowPathFlat"), analysis.HopCost, analysis.RouteChangeRate),
                    downTip: string.Format(Loc("StrArrowPathDown"), analysis.HopCost, analysis.RouteChangeRate));

                // Mesh Health: Empfangsrate-Pfeil
                bool   rxIsDay = Services.SunriseSunsetService.IsDay(DateTime.UtcNow, _currentSettings.MyLatitude, _currentSettings.MyLongitude);
                float  rxBase  = rxIsDay ? health.DayRxPerHour : health.NightRxPerHour;
                string rxPeriod = rxIsDay ? Loc("StrTelDayLabel") : Loc("StrTelNightLabel");
                SetTopArrow(GlobalRxTrendArrow,
                    up:   health.RxScore >= 0.8f,
                    down: health.RxScore <  0.5f,
                    noData: rxBase == 0f,
                    upTip:   string.Format(Loc("StrArrowRxUp"),   health.CurrentRxPerHour, (int)(health.RxScore * 100), rxPeriod, rxBase),
                    flatTip: string.Format(Loc("StrArrowRxFlat"), health.CurrentRxPerHour, (int)(health.RxScore * 100), rxPeriod, rxBase),
                    downTip: string.Format(Loc("StrArrowRxDown"), health.CurrentRxPerHour, (int)(health.RxScore * 100), rxPeriod, rxBase));

                // Update mesh health in status strip
                // Mesh Health: full detail summary with localized labels
                var meshSummary =
                    $"Mesh Health Score: {health.Score:0}%\n" +
                    string.Format(Loc("StrMeshSumPathCost"), health.AvgPathCost) + "\n" +
                    string.Format(Loc("StrMeshSumRouteChange"), health.RouteChangeRate) + "\n" +
                    string.Format(Loc("StrMeshSumRxBaseline"), health.DayRxPerHour > 0 ? Loc("StrTelDayLabel") : Loc("StrTelNightLabel"),
                        health.DayRxPerHour > 0 ? health.DayRxPerHour : health.NightRxPerHour) + "\n" +
                    string.Format(Loc("StrMeshSumRxCurrent"), health.CurrentRxPerHour, (int)(health.RxScore * 100)) + "\n" +
                    string.Format(Loc("StrMeshSumChanUtil"), health.ChannelUtilization) +
                    (string.IsNullOrEmpty(health.ChannelUtilDetail) ? "" : $"\n  {health.ChannelUtilDetail}");
                LedFill(GlobalMeshHealthLed, health.State, meshSummary);
                GlobalMeshHealthText.Text = health.Score > 0 ? $"{health.Score:0}%" : "–";
            });
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[Analysis] RefreshSignalAnalysis error: {ex.Message}");
        }
    }

    private static void SetTopArrow(TextBlock tb, bool up, bool down, bool noData,
        string upTip, string flatTip, string downTip)
    {
        if (noData) { tb.Text = ""; ToolTipService.SetToolTip(tb, null); return; }
        tb.Text = up ? "?" : down ? "?" : "?";
        tb.ClearValue(TextBlock.ForegroundProperty); // inherit theme color – LED provides the color indicator
        ToolTipService.SetToolTip(tb, up ? upTip : down ? downTip : flatTip);
    }

    private static void LedFill(System.Windows.Shapes.Ellipse led, LedState state, string tooltip)
    {
        led.Fill = state switch
        {
            LedState.Good    => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)),
            LedState.Warning => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xC1, 0x07)),
            LedState.Alert   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36)),
            _                => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xBD, 0xBD, 0xBD)),
        };
        ToolTipService.SetToolTip(led, tooltip);
    }

    private void OpenTelemetryForNode(NodeInfo node)
    {
        if (_db == null) return;
        var nodeNames = _allNodes.ToDictionary(n => n.NodeId, n => n.Name);
        var win = new TelemetryWindow(node, _db, nodeNames, _currentSettings.MyLatitude, _currentSettings.MyLongitude,
            _currentSettings.SignalWeatherWindowHours, _currentSettings.SignalAntennaWindowDays)
        {
            Owner = this
        };
        win.Show();
        win.Activate();
    }

    private void NodeContextMenu_Telemetry_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeForMenu is not NodeInfo node) return;
        OpenTelemetryForNode(node);
    }

    private void OpenDashboardMain_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;
        var nodeNames = _allNodes.ToDictionary(n => n.NodeId, n => n.Name);
        var nodeShortNames = _allNodes
            .Where(n => !string.IsNullOrWhiteSpace(n.ShortName))
            .ToDictionary(n => n.NodeId, n => n.ShortName);
        var dash = new TelemetryDashboardWindow(_db, nodeNames, nodeShortNames);
        dash.Show();
        dash.Activate();
    }

    // Context menu handlers for traceroute
    private void NodeContextMenu_Traceroute_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeForMenu is not NodeInfo node) return;
        OpenTracerouteForNode(node);
    }

    private void MessageContextMenu_Traceroute_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node == null) return;
        OpenTracerouteForNode(node);
    }

    private void MessageContextMenu_Telemetry_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node == null || _db == null) return;
        var nodeNames = _allNodes.ToDictionary(n => n.NodeId, n => n.Name);
        var win = new TelemetryWindow(node, _db, nodeNames, _currentSettings.MyLatitude, _currentSettings.MyLongitude,
            _currentSettings.SignalWeatherWindowHours, _currentSettings.SignalAntennaWindowDays)
        {
            Owner = this
        };
        win.Show();
        win.Activate();
    }

    // -----------------------------------------------------------------------
    //  UPDATE CHECK
    // -----------------------------------------------------------------------

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(6) };

    private async Task CheckForUpdateAsync()
    {
        try
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MeshhessenClient");
            var json = await _httpClient.GetStringAsync(
                "https://api.github.com/repos/SMLunchen/mh_windowsclient/releases");

            var releases = JsonSerializer.Deserialize<JsonElement>(json);
            if (releases.GetArrayLength() == 0) return;

            var latest  = releases[0];
            var tagName = latest.GetProperty("tag_name").GetString();
            var htmlUrl = latest.GetProperty("html_url").GetString();
            if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(htmlUrl)) return;

            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (!Version.TryParse(tagName.TrimStart('v'), out var remote)) return;

            if (remote > current)
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateHintRun.Text = $"?? Update verfügbar: {tagName}";
                    UpdateHintLink.NavigateUri = new Uri(htmlUrl);
                    UpdateBanner.Visibility = Visibility.Visible;
                    Services.Logger.WriteLine($"Update available: {tagName} ? {htmlUrl}");
                });
            }
        }
        catch
        {
            // Offline or API unavailable – silently ignore
        }
    }

    private void UpdateHint_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
        e.Handled = true;
    }

    // -----------------------------------------------------------------------
    //  REACTIONS / TAP-BACKS
    // -----------------------------------------------------------------------

    private void OnReactionReceived(object? sender, (uint ReplyId, string Emoji, uint FromId) reaction)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Try to find the message by ID in _messageById
            if (_messageById.TryGetValue(reaction.ReplyId, out var msg))
            {
                msg.AddReaction(reaction.Emoji, reaction.FromId);
                Services.Logger.WriteLine($"Reaction '{reaction.Emoji}' from !{reaction.FromId:x8} added to msg {reaction.ReplyId}");
            }
            else
            {
                Services.Logger.WriteLine($"Reaction '{reaction.Emoji}' for unknown msg ID {reaction.ReplyId}");
            }
        });
    }

    private void ShowEmojiPickerForMessage(MessageItem message, uint destinationId, uint channel)
    {
        var quickEmojis = new[]
        {
            "??", "??", "??", "??", "??", "??", "??", "??",
            "?", "?", "??", "*??", "1??", "2??", "3??", "4??",
            "5??", "6??", "7??", "??", "??", "??", "??", "??",
            "??", "?", "??", "???", "?", "?", "??", "??",
        };

        var popup = new System.Windows.Controls.Primitives.Popup
        {
            StaysOpen = false,
            AllowsTransparency = true,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };

        var border = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("SystemControlBackgroundChromeMediumLowBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("SystemControlForegroundBaseLowBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.3 },
        };

        var panel = new WrapPanel { MaxWidth = 380 }; // 8 columns – ~47px

        foreach (var emoji in quickEmojis)
        {
            var emojiBlock = new Emoji.Wpf.TextBlock
            {
                Text = emoji,
                FontSize = 24,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var btn = new Button
            {
                Content = emojiBlock,
                Padding = new Thickness(4),
                Margin = new Thickness(2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = emoji,
                MinWidth = 40,
                MinHeight = 40,
            };
            btn.Click += async (_, _) =>
            {
                popup.IsOpen = false;
                try
                {
                    await _protocolService.SendReactionAsync(emoji, message.Id, destinationId, channel);
                    // Show our own reaction immediately
                    message.AddReaction(emoji, _myNodeId);
                    if (message.Id != 0) _messageById[message.Id] = message;
                }
                catch (Exception ex)
                {
                    Services.Logger.WriteLine($"Error sending reaction: {ex.Message}");
                }
            };
            panel.Children.Add(btn);
        }

        border.Child = panel;
        popup.Child = border;
        popup.IsOpen = true;
    }

    private void MessageContextMenu_React_Click(object sender, RoutedEventArgs e)
    {
        if (MessageListView.SelectedItem is not MessageItem msg) return;
        if (msg.Id == 0)
        {
            MessageBox.Show("Auf diese Nachricht kann nicht reagiert werden (keine ID).", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // DM (ToId is a specific node): react back to sender; channel broadcast: react to all on that channel
        bool isDm = msg.ToId != 0 && msg.ToId != 0xFFFFFFFF;
        uint dest = isDm ? msg.FromId : 0xFFFFFFFF;
        uint ch = uint.TryParse(msg.Channel, out var ci) ? ci : 0;
        ShowEmojiPickerForMessage(msg, dest, ch);
    }

    private void DmMessageContextMenu_React_Click(object sender, RoutedEventArgs e)
    {
        // Forwarded from DirectMessagesWindow - handled there via EmojiPickerRequested
    }

    private void MessageContextMenu_Reply_Click(object sender, RoutedEventArgs e)
    {
        if (MessageListView.SelectedItem is not MessageItem msg) return;
        _replyToMessage = msg;
        var preview = msg.Message?.Length > 60 ? msg.Message[..60] + "…" : msg.Message ?? string.Empty;
        ReplyIndicatorText.Text = string.Format(Loc("StrReplyingTo"), msg.From, preview);
        ReplyIndicatorPanel.Visibility = Visibility.Visible;
        MessageTextBox.Focus();
        if (MainTabs.SelectedIndex != 0) MainTabs.SelectedIndex = 0;
    }

    private void CancelReply_Click(object sender, RoutedEventArgs e)
    {
        _replyToMessage = null;
        ReplyIndicatorPanel.Visibility = Visibility.Collapsed;
    }

    // -------------------------------------------
    // Easter Eggs
    // -------------------------------------------

    private void CheckMidnight()
    {
        var now = DateTime.Now;
        if (now.Hour == 0 && now.Minute == 0 && !_midnightFiredToday)
        {
            _midnightFiredToday = true;  // always lock out further checks this midnight
            if (_allNodes.Count > 0 && Random.Shared.Next(3) == 0)  // ~1 in 3 nights
                Dispatcher.BeginInvoke(ShowMidnightMesh);
        }
        else if (now.Hour != 0)
        {
            _midnightFiredToday = false;
        }
    }

    private void ShowMidnightMesh()
    {
        var myShortName = _allNodes.FirstOrDefault(n => n.NodeId == _myNodeId)?.ShortName
                          ?? "MH";

        var lines = new[]
        {
            "- - - - - - - - - - - - - - - - - - - -",
            $"SENDESTELLE {myShortName.ToUpper()} – OSTEREI",
            "AM-SENDELEISTUNG WIRD JETZT REDUZIERT",
            "Ionosphäre aktiv: AM-Signale reichen nachts",
            "hunderte km weit (Skywave-Propagation).",
            "FCC-Nachtpflicht seit 1934 – LoRa: unaffected",
            "- - - - - - - - - - - - - - - - - - - -"
        };
        var msg = new MessageItem
        {
            Time        = "00:00",
            From        = "? SENDESTELLE OSTEREI",
            Message     = string.Join("\n", lines),
            ChannelName = "SYSTEM",
            IsOwnMessage = false
        };
        _allMessages.Add(msg);
        _messages.Add(msg);

        // Scroll to bottom
        if (MessageListView.Items.Count > 0)
            MessageListView.ScrollIntoView(MessageListView.Items[^1]);
    }

    private void Logo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var now = DateTime.Now;
        if ((now - _lastLogoClick).TotalSeconds > 3)
            _logoClickCount = 0;
        _lastLogoClick = now;
        _logoClickCount++;

        if (_logoClickCount >= 5)
        {
            _logoClickCount = 0;
            ShowLogoEasterEgg();
        }
    }

    private void ShowLogoEasterEgg()
    {
        var myShortName = _allNodes.FirstOrDefault(n => n.NodeId == _myNodeId)?.ShortName ?? "MH";
        // Classic ham radio CQ call: CQ CQ CQ DE [CALLSIGN] MESHHESSEN QTH HESSEN 73 SK
        var cwText = $"CQ DE {myShortName.ToUpper()} MESHHESSEN 73 SK";
        Task.Run(() => BeepMorse(cwText));
    }

    private static void BeepMorse(string text)
    {
        const int Freq      = 700;
        const int Dot       = 50;
        const int Dash      = 150;
        const int ElemGap   = 50;
        const int LetterGap = 150;
        const int WordGap   = 350;

        var table = new Dictionary<char, string>
        {
            {'A',".-"},  {'B',"-..."}, {'C',"-.-."}, {'D',"-.."}, {'E',"."},
            {'F',"..-."}, {'G',"--."}, {'H',"...."}, {'I',".."}, {'J',".---"},
            {'K',"-.-"}, {'L',".-.."}, {'M',"--"},  {'N',"-."},  {'O',"---"},
            {'P',".--."}, {'Q',"--.-"}, {'R',".-."}, {'S',"..."}, {'T',"-"},
            {'U',"..-"}, {'V',"...-"}, {'W',".--"}, {'X',"-..-"}, {'Y',"-.--"},
            {'Z',"--.."},
            {'0',"-----"}, {'1',".----"}, {'2',"..---"}, {'3',"...--"},
            {'4',"....-"}, {'5',"....."}, {'6',"-...."}, {'7',"--..."},
            {'8',"---.."}, {'9',"----."}
        };

        bool firstLetter = true;
        foreach (char c in text)
        {
            if (c == ' ') { System.Threading.Thread.Sleep(WordGap); firstLetter = true; continue; }
            if (!table.TryGetValue(c, out var code)) continue;

            if (!firstLetter) System.Threading.Thread.Sleep(LetterGap);
            firstLetter = false;

            bool firstElem = true;
            foreach (char sym in code)
            {
                if (!firstElem) System.Threading.Thread.Sleep(ElemGap);
                firstElem = false;
                PlaySineTone(Freq, sym == '.' ? Dot : Dash);
            }
        }
    }

    private static void PlaySineTone(int freqHz, int durationMs)
    {
        const int SampleRate = 44100;
        const int FadeMs     = 8; // Fade-in/out in ms – eliminiert Knackgeräusche
        int totalSamples = SampleRate * durationMs / 1000;
        int fadeSamples  = SampleRate * FadeMs / 1000;

        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);

        // WAV-Header (PCM, Mono, 16-bit)
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + totalSamples * 2);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(SampleRate); bw.Write(SampleRate * 2);
        bw.Write((short)2); bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(totalSamples * 2);

        for (int i = 0; i < totalSamples; i++)
        {
            // Lineare Hüllkurve: sanftes Ein- und Ausblenden
            double env = 1.0;
            if (i < fadeSamples)                        env = (double)i / fadeSamples;
            else if (i >= totalSamples - fadeSamples)   env = (double)(totalSamples - i) / fadeSamples;

            double sample = Math.Sin(2 * Math.PI * freqHz * i / SampleRate) * env * 0.75;
            bw.Write((short)(sample * 32767));
        }

        ms.Position = 0;
        using var player = new System.Media.SoundPlayer(ms);
        player.PlaySync();
    }
}
