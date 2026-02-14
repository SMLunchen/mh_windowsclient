using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
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
using LoRaConfig = Meshtastic.Protobufs.LoRaConfig;

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
    private bool _showEncryptedMessages = true;
    private ChannelInfo? _messageChannelFilter = null;
    private DirectMessagesWindow? _dmWindow = null;
    private uint _myNodeId = 0;
    private string? _nodeSortColumn = null;
    private bool _nodeSortAscending = true;

    // Karte
    private Mapsui.Map? _map;
    private MemoryLayer? _nodeLayer;
    private MemoryLayer? _myPosLayer;
    private readonly List<IFeature> _nodeFeatures = new();
    private readonly List<IFeature> _myPosFeatures = new();
    private readonly Dictionary<uint, MPoint> _nodePinPositions = new();
    private AppSettings _currentSettings = new(
        false,
        string.Empty,
        true,
        50.9,
        9.5,
        string.Empty,
        "192.168.1.1",
        4403,
        "osm",
        "https://tile.schwarzes-seelenreich.de/osm/{z}/{x}/{y}.png",
        "https://tile.schwarzes-seelenreich.de/opentopo/{z}/{x}/{y}.png",
        "https://tile.schwarzes-seelenreich.de/dark/{z}/{x}/{y}.png",
        new Dictionary<uint, string>(),
        new Dictionary<uint, string>(),
        false,
        false,
        false,
        false,
        true);
    private NodeInfo? _mapContextMenuNode;
    private uint? _alertNodeId;  // Stores the node ID for "Show on Map" button

    public MainWindow()
    {
        InitializeComponent();

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

        LoadRegions();
        LoadModemPresets();

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

        // Einstellungen laden (VOR RefreshPorts, damit LastComPort bekannt ist)
        LoadSettings();

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
        try
        {
            var settings = SettingsService.Load();
            DarkModeCheckBox.IsChecked = settings.DarkMode;
            StationNameTextBox.Text = settings.StationName;
            StationNameLabel.Text = settings.StationName;
            ShowEncryptedMessagesCheckBox.IsChecked = settings.ShowEncryptedMessages;
            _showEncryptedMessages = settings.ShowEncryptedMessages;
            DebugMessagesCheckBox.IsChecked = settings.DebugMessages;
            DebugSerialCheckBox.IsChecked = settings.DebugSerial;
            DebugDeviceCheckBox.IsChecked = settings.DebugDevice;
            DebugBluetoothCheckBox.IsChecked = settings.DebugBluetooth;
            AlertBellSoundCheckBox.IsChecked = settings.AlertBellSound;

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

            if (settings.DarkMode)
                ModernWpf.ThemeManager.Current.ApplicationTheme = ModernWpf.ApplicationTheme.Dark;
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR loading settings: {ex.Message}");
        }
    }

    #region Karte

    private void InitializeMap()
    {
        try
        {
            _map = new Mapsui.Map();

            // Lokale Tile-Layer mit ausgewählter Kartenquelle
            var tileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");
            var sourceFolder = _currentSettings.MapSource;  // "osm", "osmtopo", oder "osmdark"
            var schema = new GlobalSphericalMercator(YAxis.TMS, 0, 18, "OSM");
            var tileSource = new TileSource(new LocalFileTileProvider(tileDir, sourceFolder), schema);
            _map.Layers.Add(new TileLayer(tileSource) { Name = "OSM" });

            // Node-Layer
            _nodeLayer = new MemoryLayer("Nodes") { Features = _nodeFeatures, Style = null };
            _map.Layers.Add(_nodeLayer);

            // Eigener-Standort-Layer
            _myPosLayer = new MemoryLayer("MyPosition") { Features = _myPosFeatures, Style = null };
            _map.Layers.Add(_myPosLayer);

            MapControl.Map = _map;
            MapControl.MouseRightButtonUp += MapControl_RightClick;
            MapControl.MouseLeftButtonUp += MapControl_LeftClick;

            // Karte auf eigenen Standort zentrieren
            var center = SphericalMercator.FromLonLat(_currentSettings.MyLongitude, _currentSettings.MyLatitude);
            // Resolution ~611 entspricht Zoom-Level 8 in Web-Mercator
            _map.Home = n => n.CenterOnAndZoomTo(new MPoint(center.x, center.y), 611.0);

            UpdateMyPositionPin();

            var sourceTileDir = Path.Combine(tileDir, sourceFolder);
            MapStatusText.Text = Directory.Exists(sourceTileDir) && Directory.EnumerateFiles(sourceTileDir, "*.png", SearchOption.AllDirectories).Any()
                ? "" : "Keine Tiles – bitte herunterladen";

            // Copyright-Hinweis basierend auf Kartenquelle setzen
            UpdateMapCopyright();
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR initializing map: {ex.Message}");
        }
    }

    private void UpdateMapCopyright()
    {
        MapCopyrightText.Text = _currentSettings.MapSource switch
        {
            "osmtopo" => "© OpenStreetMap contributors, © OpenTopoMap (CC-BY-SA)",
            "osmdark" => "© OpenStreetMap contributors",
            _ => "© OpenStreetMap contributors"
        };
    }

    private void UpdateMyPositionPin()
    {
        _myPosFeatures.Clear();
        var pos = SphericalMercator.FromLonLat(_currentSettings.MyLongitude, _currentSettings.MyLatitude);
        var label = string.IsNullOrEmpty(_currentSettings.StationName) ? "Ich" : _currentSettings.StationName;
        Services.Logger.WriteLine($"UpdateMyPositionPin: label='{label}' lat={_currentSettings.MyLatitude:F6}, lon={_currentSettings.MyLongitude:F6}");
        var feature = new PointFeature(new MPoint(pos.x, pos.y));
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.Blue),
            Outline = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 2),
            SymbolScale = 0.6
        });
        feature.Styles.Add(new LabelStyle
        {
            Text = label,
            ForeColor = Mapsui.Styles.Color.Blue,
            BackColor = new Mapsui.Styles.Brush(Mapsui.Styles.Color.White),
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Top,
            Offset = new Offset(0, -20)
        });
        _myPosFeatures.Add(feature);
        _myPosLayer?.DataHasChanged();
        MapControl.Refresh();
    }

    private void UpdateNodePin(NodeInfo node)
    {
        if (!node.Latitude.HasValue || !node.Longitude.HasValue)
        {
            Services.Logger.WriteLine($"UpdateNodePin: {node.Id} ({node.ShortName}) – kein GPS, wird übersprungen");
            return;
        }
        Services.Logger.WriteLine($"UpdateNodePin: {node.Id} ({node.ShortName}) lat={node.Latitude:F6}, lon={node.Longitude:F6}");

        var pos = SphericalMercator.FromLonLat(node.Longitude.Value, node.Latitude.Value);
        var mPoint = new MPoint(pos.x, pos.y);
        _nodePinPositions[node.NodeId] = mPoint;

        // Alten Pin entfernen
        _nodeFeatures.RemoveAll(f => f["nodeid"] is uint id && id == node.NodeId);

        var feature = new PointFeature(new MPoint(pos.x, pos.y));
        feature["nodeid"] = node.NodeId;

        // Determine pin color
        Mapsui.Styles.Color pinColor = Mapsui.Styles.Color.Red; // Default
        if (!string.IsNullOrEmpty(node.ColorHex))
        {
            try
            {
                var wpfColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(node.ColorHex);
                pinColor = new Mapsui.Styles.Color(wpfColor.R, wpfColor.G, wpfColor.B, wpfColor.A);
            }
            catch
            {
                // Keep default color if conversion fails
            }
        }

        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Fill = new Mapsui.Styles.Brush(pinColor),
            Outline = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 2),
            SymbolScale = 0.5
        });

        // Build label text with note if available
        var labelText = string.IsNullOrEmpty(node.ShortName) ? node.Id : node.ShortName;
        if (!string.IsNullOrEmpty(node.Note))
        {
            labelText += $" ({node.Note})";
        }

        feature.Styles.Add(new LabelStyle
        {
            Text = labelText,
            ForeColor = Mapsui.Styles.Color.Black,
            BackColor = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(255, 255, 255, 180)),
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Top,
            Offset = new Offset(0, -20)
        });
        _nodeFeatures.Add(feature);
        if (_nodeLayer != null)
        {
            _nodeLayer.Features = _nodeFeatures;
            _nodeLayer.DataHasChanged();
            MapControl.Refresh();
        }
    }

    private void MapControl_RightClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var screenPos = e.GetPosition(MapControl);
            if (MapControl.Map == null) return;
            var worldPos = MapControl.Map.Navigator.Viewport.ScreenToWorld(screenPos.X, screenPos.Y);

            // Hit-Test: Node in der Nähe?
            NodeInfo? hitNode = null;
            double minDist = 20; // Pixel-Schwellwert

            foreach (var (nodeId, pinWorld) in _nodePinPositions)
            {
                var pinScreen = MapControl.Map.Navigator.Viewport.WorldToScreen(pinWorld);
                var dist = Math.Sqrt(Math.Pow(screenPos.X - pinScreen.X, 2) + Math.Pow(screenPos.Y - pinScreen.Y, 2));
                if (dist < minDist)
                {
                    hitNode = _nodes.FirstOrDefault(n => n.NodeId == nodeId);
                    minDist = dist;
                }
            }

            var menu = new ContextMenu();

            if (hitNode != null)
            {
                _mapContextMenuNode = hitNode;
                var dmItem = new MenuItem { Header = "💬 DM senden" };
                dmItem.Click += (s, ev) => { if (_mapContextMenuNode != null) OpenDmToNode(_mapContextMenuNode); };
                menu.Items.Add(dmItem);

                var infoItem = new MenuItem { Header = "ℹ️ Node Info" };
                infoItem.Click += (s, ev) => { if (_mapContextMenuNode != null) ShowNodeInfoDialog(_mapContextMenuNode); };
                menu.Items.Add(infoItem);

                menu.Items.Add(new Separator());

                // Color submenu
                var colorMenu = new MenuItem { Header = "🎨 Farbe setzen" };
                var colors = new[]
                {
                    ("Grün", "#00FF00"),
                    ("Blau", "#0080FF"),
                    ("Gelb", "#FFFF00"),
                    ("Orange", "#FF8000"),
                    ("Lila", "#8000FF"),
                    ("Braun", "#804000"),
                    ("Pink", "#FF00FF"),
                    ("Türkis", "#00FFFF")
                };

                foreach (var (label, colorHex) in colors)
                {
                    var textBlock = new TextBlock
                    {
                        Text = $"■ {label}",
                        Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)),
                        FontWeight = FontWeights.Bold
                    };
                    var colorItem = new MenuItem { Header = textBlock, Tag = colorHex };
                    colorItem.Click += (s, ev) =>
                    {
                        if (_mapContextMenuNode != null && s is MenuItem mi && mi.Tag is string c)
                            SetNodeColorInternal(_mapContextMenuNode, c);
                    };
                    colorMenu.Items.Add(colorItem);
                }

                colorMenu.Items.Add(new Separator());
                var removeColorItem = new MenuItem { Header = "❌ Farbe entfernen" };
                removeColorItem.Click += (s, ev) =>
                {
                    if (_mapContextMenuNode != null)
                        RemoveNodeColorInternal(_mapContextMenuNode);
                };
                colorMenu.Items.Add(removeColorItem);
                menu.Items.Add(colorMenu);

                // Note option
                var noteItem = new MenuItem { Header = "📝 Notiz bearbeiten..." };
                noteItem.Click += (s, ev) =>
                {
                    if (_mapContextMenuNode != null)
                        EditNodeNoteInternal(_mapContextMenuNode);
                };
                menu.Items.Add(noteItem);
            }
            else
            {
                var lonLat = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);
                var clickLat = lonLat.lat;
                var clickLon = lonLat.lon;

                var setPosItem = new MenuItem { Header = $"📍 Eigenen Standort hier setzen ({clickLat:F4}, {clickLon:F4})" };
                setPosItem.Click += (s, ev) => SetMyPosition(clickLat, clickLon);
                menu.Items.Add(setPosItem);
            }

            menu.PlacementTarget = MapControl;
            menu.IsOpen = true;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR map right-click: {ex.Message}");
        }
    }

    private void MapControl_LeftClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var screenPos = e.GetPosition(MapControl);
            if (MapControl.Map == null) return;

            NodeInfo? hitNode = null;
            double minDist = 20;

            foreach (var (nodeId, pinWorld) in _nodePinPositions)
            {
                var pinScreen = MapControl.Map.Navigator.Viewport.WorldToScreen(pinWorld);
                var dist = Math.Sqrt(Math.Pow(screenPos.X - pinScreen.X, 2) + Math.Pow(screenPos.Y - pinScreen.Y, 2));
                if (dist < minDist)
                {
                    hitNode = _nodes.FirstOrDefault(n => n.NodeId == nodeId);
                    minDist = dist;
                }
            }

            if (hitNode != null && hitNode.Latitude.HasValue && hitNode.Longitude.HasValue)
            {
                var km = HaversineKm(_currentSettings.MyLatitude, _currentSettings.MyLongitude,
                                     hitNode.Latitude.Value, hitNode.Longitude.Value);
                MapStatusText.Text = $"{hitNode.ShortName} ({hitNode.Id}): {km:F2} km Entfernung | Last seen: {hitNode.LastSeen}";
            }
            else
            {
                MapStatusText.Text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR map left-click: {ex.Message}");
        }
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private void SetMyPosition(double lat, double lon)
    {
        _currentSettings = _currentSettings with { MyLatitude = lat, MyLongitude = lon };
        SettingsService.Save(_currentSettings);
        UpdateMyPositionPin();
        Services.Logger.WriteLine($"Eigener Standort gesetzt: {lat:F6}, {lon:F6}");
    }

    private void OpenDmToNode(NodeInfo node)
    {
        if (_dmWindow == null || !_dmWindow.IsVisible)
        {
            _dmWindow = new DirectMessagesWindow(_protocolService, _myNodeId);
            _dmWindow.Show();
        }
        _dmWindow.OpenChatWithNode(node.NodeId, node.Name, node.ColorHex);
    }

    private void ShowNodeInfoDialog(NodeInfo node)
    {
        var win = new NodeInfoWindow(node) { Owner = this };
        win.ShowDialog();
    }

    private void DownloadTiles_Click(object sender, RoutedEventArgs e)
    {
        // Tile-Server URL direkt aus TextBox übernehmen (auch ohne vorheriges Speichern)
        var currentTileUrl = string.IsNullOrWhiteSpace(TileServerUrlTextBox.Text)
            ? "https://tile.schwarzes-seelenreich.de/osm/{z}/{x}/{y}.png"
            : TileServerUrlTextBox.Text.Trim();

        // Update the appropriate URL based on current map source
        switch (_currentSettings.MapSource)
        {
            case "osm":
                TileDownloaderService.OSMTileUrl = currentTileUrl;
                break;
            case "osmtopo":
                TileDownloaderService.OSMTopoTileUrl = currentTileUrl;
                break;
            case "osmdark":
                TileDownloaderService.OSMDarkTileUrl = currentTileUrl;
                break;
        }

        var win = new TileDownloaderWindow(_currentSettings.MapSource) { Owner = this };
        win.ShowDialog();
        // Nach Download: Map-Status aktualisieren
        UpdateMapTileStatus();
    }

    private void UpdateMapTileStatus()
    {
        var tileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");
        var sourceFolder = _currentSettings.MapSource;
        var sourceTileDir = Path.Combine(tileDir, sourceFolder);
        MapStatusText.Text = Directory.Exists(sourceTileDir) && Directory.EnumerateFiles(sourceTileDir, "*.png", SearchOption.AllDirectories).Any()
            ? "" : "Keine Tiles – bitte herunterladen";
    }

    private void MapSourceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MapSourceComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
            return;

        var newSource = item.Tag as string ?? "osm";
        if (newSource == _currentSettings.MapSource)
            return;  // Keine Änderung

        // Update URL TextBox to show the URL for the selected map source
        TileServerUrlTextBox.Text = newSource switch
        {
            "osm" => _currentSettings.OSMTileUrl,
            "osmtopo" => _currentSettings.OSMTopoTileUrl,
            "osmdark" => _currentSettings.OSMDarkTileUrl,
            _ => _currentSettings.OSMTileUrl
        };

        // Settings aktualisieren
        _currentSettings = _currentSettings with { MapSource = newSource };
        Services.SettingsService.Save(_currentSettings);
        Services.Logger.WriteLine($"Map source changed to: {newSource}");

        // Karte neu laden mit neuer Quelle
        InitializeMap();
        UpdateMapTileStatus();
    }

    private async void ImportTilesFromZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Zip-Datei mit Tiles auswählen",
            Filter = "Zip-Dateien (*.zip)|*.zip|Alle Dateien (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        var tileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");
        var win = new ZipImportWindow { Owner = this };
        win.Show();

        await win.ImportFromZipAsync(dialog.FileName, tileDir);

        // Map-Status aktualisieren
        UpdateMapTileStatus();
    }

    private void MapZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (_map != null)
        {
            var res = _map.Navigator.Viewport.Resolution;
            _map.Navigator.ZoomTo(res / 2);
            MapControl.Refresh();
        }
    }

    private void MapZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (_map != null)
        {
            var res = _map.Navigator.Viewport.Resolution;
            _map.Navigator.ZoomTo(res * 2);
            MapControl.Refresh();
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private class LocalFileTileProvider : ITileProvider
    {
        private readonly string _baseDir;
        private readonly string _sourceFolder;

        public LocalFileTileProvider(string baseDir, string sourceFolder)
        {
            _baseDir = baseDir;
            _sourceFolder = sourceFolder;
        }

        public Task<byte[]?> GetTileAsync(TileInfo tileInfo)
        {
            var z = tileInfo.Index.Level;
            var x = tileInfo.Index.Col;
            // BruTile TMS hat Row 0 im Süden, OSM-Dateien haben Y=0 im Norden → konvertieren
            var yOsm = (1 << z) - 1 - tileInfo.Index.Row;
            // Neuer Pfad: maptiles/{source}/{z}/{x}/{y}.png
            var path = Path.Combine(_baseDir, _sourceFolder, z.ToString(), x.ToString(), $"{yOsm}.png");
            if (File.Exists(path))
            {
                try { return Task.FromResult<byte[]?>(File.ReadAllBytes(path)); } catch { }
            }
            return Task.FromResult<byte[]?>(null);
        }
    }

    #endregion

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
            UpdateStatusBar($"Aktiver Kanal: {channel.Name}");
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

    private void LoadRegions()
    {
        RegionComboBox.ItemsSource = new[] { "Unset", "US", "EU_868", "CN", "JP", "ANZ", "KR", "TW", "RU", "IN", "NZ_865", "TH", "LORA_24", "UA_868", "MY_919", "MY_433", "SG_923", "WLAN" };
        RegionComboBox.SelectedIndex = 0;
    }

    private void LoadModemPresets()
    {
        ModemPresetComboBox.ItemsSource = new[] { "LONG_FAST", "LONG_SLOW", "VERY_LONG_SLOW", "MEDIUM_FAST", "MEDIUM_SLOW", "SHORT_FAST", "SHORT_SLOW", "LONG_MODERATE" };
        ModemPresetComboBox.SelectedIndex = 0;
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e)
    {
        RefreshPorts();
        UpdateStatusBar("Ports aktualisiert");
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

            if (_bluetoothDevices.Count == 0)
            {
                MessageBox.Show("Keine Bluetooth-Geräte gefunden.\n\nStellen Sie sicher, dass:\n- Bluetooth aktiviert ist\n- Das Gerät eingeschaltet ist\n- Das Gerät im BLE-Modus ist", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[BLE] ERROR scanning Bluetooth: {ex.Message}");
            Services.Logger.WriteLine($"[BLE] Stack trace: {ex.StackTrace}");
            MessageBox.Show($"Bluetooth-Scan fehlgeschlagen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
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

                ConnectButton.Content = "Verbinden";
                UpdateStatusBar("Getrennt");
                SetConnectionStatus(ConnectionStatus.Disconnected);
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Disconnect error: {ex.Message}");
                UpdateStatusBar("Fehler beim Trennen");
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
                        MessageBox.Show("Bitte wählen Sie einen COM Port aus.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        MessageBox.Show("Bitte wählen Sie ein Bluetooth-Gerät aus.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        MessageBox.Show("Bitte geben Sie einen Hostnamen oder IP-Adresse ein.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!int.TryParse(TcpPortTextBox.Text, out var port) || port <= 0 || port > 65535)
                    {
                        MessageBox.Show("Bitte geben Sie einen gültigen Port (1-65535) ein.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                ConnectButton.IsEnabled = false;
                UpdateStatusBar($"Verbinde mit {displayName}...");
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

                // Wire up connection state changed
                _connectionService.ConnectionStateChanged += OnConnectionStateChanged;

                // Connect
                await _connectionService.ConnectAsync(connectionParams);

                // Save last used connection settings
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
                SettingsService.Save(_currentSettings);

                // GUI sofort als "Verbunden" anzeigen
                ConnectButton.Content = "Trennen";
                ConnectButton.IsEnabled = true;
                UpdateStatusBar($"Verbunden mit {displayName} - Initialisiere...");
                SetConnectionStatus(ConnectionStatus.Initializing);

                // Initialisierung im Hintergrund starten (nicht blockieren!)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _protocolService.InitializeAsync();
                        Dispatcher.BeginInvoke(() =>
                        {
                            UpdateStatusBar($"Verbunden mit {displayName} - Bereit");
                            SetConnectionStatus(ConnectionStatus.Ready);
                        });
                    }
                    catch (Exception initEx)
                    {
                        Services.Logger.WriteLine($"Initialization error: {initEx.Message}");
                        Dispatcher.BeginInvoke(() =>
                        {
                            UpdateStatusBar($"Verbunden mit {displayName} - Init-Fehler");
                            SetConnectionStatus(ConnectionStatus.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Verbindung fehlgeschlagen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("Verbindung fehlgeschlagen");
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
            "Möchten Sie wirklich einen NOTRUF (Alert Bell) senden?\n\nDies wird als wichtige Benachrichtigung an alle Empfänger gesendet!",
            "Notruf bestätigen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            AlertBellButton.IsEnabled = false;

            // Send Alert Bell - use EMOJI 🔔 (as used by other clients)
            string alertMessage;
            var additionalText = MessageTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(additionalText))
            {
                // Bell emoji + user text
                alertMessage = "🔔 " + additionalText;
                MessageTextBox.Clear();
            }
            else
            {
                // Bell emoji + standard text (compatible with other Meshtastic clients)
                alertMessage = "🔔 Alert Bell Character!";
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

            UpdateStatusBar("Notruf gesendet!");
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
            await _protocolService.SendTextMessageAsync(message, 0xFFFFFFFF, (uint)_activeChannelIndex);

            // Zeige gesendete Nachricht in der Liste
            var activeChannel = _channels.FirstOrDefault(c => c.Index == _activeChannelIndex);
            var channelName = activeChannel?.Name ?? $"Kanal {_activeChannelIndex}";

            var sentMessage = new MessageItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                From = "Ich",
                Message = message,
                Channel = _activeChannelIndex.ToString(),
                ChannelName = channelName,
                IsViaMqtt = false
            };
            _allMessages.Add(sentMessage);
            _messages.Add(sentMessage);
            MessageListView.ScrollIntoView(sentMessage);

            // Log die gesendete Nachricht
            Services.MessageLogger.LogChannelMessage(_activeChannelIndex, channelName, "Ich", message, false);

            MessageTextBox.Clear();
            UpdateStatusBar($"Nachricht gesendet (Kanal {_activeChannelIndex})");
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
            $"Kanal '{selectedChannel.Name}' (Index {selectedChannel.Index}) wirklich löschen?",
            "Kanal löschen", MessageBoxButton.YesNo, MessageBoxImage.Question);

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
        var usedIndices = _channels.Select(c => c.Index).ToHashSet();
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
                ? "https://tile.schwarzes-seelenreich.de/osm/{z}/{x}/{y}.png"
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

            var settings = new AppSettings(
                DarkMode: DarkModeCheckBox.IsChecked == true,
                StationName: StationNameTextBox.Text,
                ShowEncryptedMessages: ShowEncryptedMessagesCheckBox.IsChecked == true,
                MyLatitude: _currentSettings.MyLatitude,
                MyLongitude: _currentSettings.MyLongitude,
                LastComPort: _currentSettings.LastComPort,
                LastTcpHost: _currentSettings.LastTcpHost,
                LastTcpPort: _currentSettings.LastTcpPort,
                MapSource: _currentSettings.MapSource,
                OSMTileUrl: osmUrl,
                OSMTopoTileUrl: osmTopoUrl,
                OSMDarkTileUrl: osmDarkUrl,
                NodeColors: _currentSettings.NodeColors,
                NodeNotes: _currentSettings.NodeNotes,
                DebugMessages: DebugMessagesCheckBox.IsChecked == true,
                DebugSerial: DebugSerialCheckBox.IsChecked == true,
                DebugDevice: DebugDeviceCheckBox.IsChecked == true,
                DebugBluetooth: DebugBluetoothCheckBox.IsChecked == true,
                AlertBellSound: AlertBellSoundCheckBox.IsChecked == true
            );
            _currentSettings = settings;
            SettingsService.Save(settings);
            StationNameLabel.Text = settings.StationName;
            _showEncryptedMessages = settings.ShowEncryptedMessages;
            TileDownloaderService.OSMTileUrl = settings.OSMTileUrl;
            TileDownloaderService.OSMTopoTileUrl = settings.OSMTopoTileUrl;
            TileDownloaderService.OSMDarkTileUrl = settings.OSMDarkTileUrl;
            _protocolService.SetDebugSerial(settings.DebugSerial);
            _protocolService.SetDebugDevice(settings.DebugDevice);
            BluetoothConnectionService.SetDebugEnabled(settings.DebugBluetooth);
            MessageBox.Show("Einstellungen gespeichert.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    StatusIndicator.Fill = Brushes.Gray;
                    StatusText.Text = "Nicht verbunden";

                    ActiveChannelComboBox.IsEnabled = false;
                    _messages.Clear();
                    _allMessages.Clear();
                    _nodes.Clear();
                    _allNodes.Clear();
                    _channels.Clear();
                    UpdateMeshHessenButtonState();
                    PacketCountText.Text = "Pakete: 0";
                }
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Error updating connection state in UI: {ex.Message}");
            }
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

                // Check for Alert Bell - both ASCII (0x07) and Emoji (🔔)
                bool hasAlertBell = !string.IsNullOrEmpty(message.Message) &&
                                   (message.Message.Contains('\u0007') || message.Message.Contains("🔔"));

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
                    message.Message = message.Message.Replace("\u0007", "").Replace("🔔", "");

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

                // Load sender color and note from settings
                var senderNode = _allNodes.FirstOrDefault(n => n.NodeId == message.FromId);
                if (senderNode != null)
                {
                    message.SenderShortName = senderNode.ShortName;
                    message.SenderColorHex = senderNode.ColorHex;
                    message.SenderNote = senderNode.Note;
                }

                // Speichere in ungefilterte Liste
                _allMessages.Add(message);

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
                {
                    _channels.Remove(existing);
                }
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

                UpdateStatusBar($"Kanal {channel.Index} empfangen: {channel.Name}");
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

                // Set hardware model and firmware version
                HardwareModelText.Text = deviceInfo.HardwareModel;
                FirmwareVersionText.Text = deviceInfo.FirmwareVersion;
                Services.Logger.WriteLine($"  Hardware: {deviceInfo.HardwareModel}");
                Services.Logger.WriteLine($"  Firmware: {deviceInfo.FirmwareVersion}");

                // Suche die eigene NodeInfo in der Node-Liste
                var myNode = _nodes.FirstOrDefault(n => n.NodeId == deviceInfo.NodeId);
                if (myNode != null)
                {
                    DeviceNameTextBox.Text = myNode.Name;
                    Services.Logger.WriteLine($"  Set device name: {myNode.Name}");
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
                                DeviceNameTextBox.Text = node.Name;
                                Services.Logger.WriteLine($"  Set device name (delayed): {node.Name}");
                            }
                        });
                    });
                }

                UpdateStatusBar($"Eigene Node-ID: {deviceInfo.NodeIdHex}");
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
                Services.Logger.WriteLine($"OnLoRaConfigReceived: Region={loraConfig.Region}, Preset={loraConfig.ModemPreset}");

                // Region setzen (Case-insensitive, Unterstriche ignorieren)
                var regionName = loraConfig.Region.ToString();
                var regionNormalized = regionName.Replace("_", "").ToUpperInvariant();
                bool regionSet = false;

                for (int i = 0; i < RegionComboBox.Items.Count; i++)
                {
                    var item = RegionComboBox.Items[i].ToString();
                    var itemNormalized = item?.Replace("_", "").ToUpperInvariant();

                    if (itemNormalized == regionNormalized)
                    {
                        RegionComboBox.SelectedIndex = i;
                        regionSet = true;
                        Services.Logger.WriteLine($"  Set Region to index {i}: {item}");
                        break;
                    }
                }
                if (!regionSet)
                {
                    Services.Logger.WriteLine($"  WARNING: Region '{regionName}' not found in ComboBox");
                }

                // Modem Preset setzen (Case-insensitive, Unterstriche ignorieren)
                var presetName = loraConfig.ModemPreset.ToString();
                var presetNormalized = presetName.Replace("_", "").ToUpperInvariant();
                bool presetSet = false;

                for (int i = 0; i < ModemPresetComboBox.Items.Count; i++)
                {
                    var item = ModemPresetComboBox.Items[i].ToString();
                    var itemNormalized = item?.Replace("_", "").ToUpperInvariant();

                    if (itemNormalized == presetNormalized)
                    {
                        ModemPresetComboBox.SelectedIndex = i;
                        presetSet = true;
                        Services.Logger.WriteLine($"  Set Preset to index {i}: {item}");
                        break;
                    }
                }
                if (!presetSet)
                {
                    Services.Logger.WriteLine($"  WARNING: Preset '{presetName}' not found in ComboBox");
                }

                UpdateStatusBar($"LoRa Config: {regionName}, {presetName}");
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"ERROR updating LoRa config in UI: {ex.Message}");
            }
        });
    }

    private void OnPacketCountChanged(object? sender, int count)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                PacketCountText.Text = $"Pakete: {count}";
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"ERROR updating packet count in UI: {ex.Message}");
            }
        });
    }

    private void UpdateStatusBar(string message)
    {
        StatusBarText.Text = message;
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        DebugLogTextBox.Clear();
        UpdateStatusBar("Debug-Log gelöscht");
    }

    private async void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = DebugLogTextBox.Text;
            if (string.IsNullOrEmpty(text))
            {
                UpdateStatusBar("Log ist leer");
                return;
            }

            CopyLogButton.IsEnabled = false;
            UpdateStatusBar("Kopiere Log...");

            // Clipboard operation im Hintergrund
            await Task.Run(() =>
            {
                try
                {
                    // WPF Clipboard benötigt STA thread
                    Thread thread = new Thread(() =>
                    {
                        try
                        {
                            Clipboard.SetDataObject(text, true);
                        }
                        catch (Exception ex)
                        {
                            Services.Logger.WriteLine($"Clipboard error: {ex.Message}");
                        }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join(1000); // Max 1 Sekunde warten
                }
                catch (Exception ex)
                {
                    Services.Logger.WriteLine($"Clipboard error: {ex.Message}");
                }
            });

            UpdateStatusBar("Log kopiert");
            CopyLogButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            UpdateStatusBar($"Fehler: {ex.Message}");
            CopyLogButton.IsEnabled = true;
        }
    }

    private void OpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = Services.Logger.GetLogFilePath();
            if (!string.IsNullOrEmpty(logPath) && System.IO.File.Exists(logPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true
                });
                UpdateStatusBar($"Log-Datei geöffnet: {logPath}");
            }
            else
            {
                MessageBox.Show("Log-Datei wurde nicht gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Öffnen der Log-Datei: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetConnectionStatus(ConnectionStatus status)
    {
        switch (status)
        {
            case ConnectionStatus.Disconnected:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Gray);
                StatusText.Text = "Nicht verbunden";
                break;
            case ConnectionStatus.Connecting:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Yellow);
                StatusText.Text = "Verbinde...";
                break;
            case ConnectionStatus.Initializing:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
                StatusText.Text = "Initialisiere...";
                break;
            case ConnectionStatus.Ready:
                StatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                StatusText.Text = "Verbunden";
                break;
            case ConnectionStatus.Disconnecting:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
                StatusText.Text = "Trenne...";
                break;
            case ConnectionStatus.Error:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                StatusText.Text = "Fehler";
                break;
        }
    }

    private void UpdateMessageFilterComboBox()
    {
        // Speichere aktuelle Auswahl
        var selectedFilter = MessageChannelFilterComboBox.SelectedItem as ChannelInfo;

        // Erstelle Liste mit "Alle" Option
        var filterItems = new List<ChannelInfo>();
        filterItems.Add(new ChannelInfo { Index = 999, Name = "Alle Kanäle", Role = "" });
        filterItems.AddRange(_channels);

        MessageChannelFilterComboBox.ItemsSource = filterItems;

        // Stelle Auswahl wieder her oder wähle "Alle"
        if (selectedFilter != null)
        {
            var restored = filterItems.FirstOrDefault(c => c.Index == selectedFilter.Index);
            MessageChannelFilterComboBox.SelectedItem = restored ?? filterItems[0];
        }
        else
        {
            MessageChannelFilterComboBox.SelectedIndex = 0;
        }
    }

    private void DarkMode_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            var isDarkMode = DarkModeCheckBox.IsChecked == true;
            ModernWpf.ThemeManager.Current.ApplicationTheme = isDarkMode
                ? ModernWpf.ApplicationTheme.Dark
                : ModernWpf.ApplicationTheme.Light;

            Services.Logger.WriteLine($"Theme changed to: {(isDarkMode ? "Dark" : "Light")}");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error changing theme: {ex.Message}");
        }
    }

    private void OpenDmWindow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_dmWindow == null)
            {
                _dmWindow = new DirectMessagesWindow(_protocolService, _myNodeId);
            }

            // Zeige Fenster
            if (_dmWindow.IsVisible)
            {
                _dmWindow.Activate(); // Bringe in den Vordergrund
            }
            else
            {
                _dmWindow.Show();
            }

            // Reset Button-Hervorhebung
            OpenDmWindowButton.FontWeight = FontWeights.Normal;
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR opening DM window: {ex.Message}");
            MessageBox.Show($"Fehler beim Öffnen des DM-Fensters: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MessageChannelFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            _messageChannelFilter = MessageChannelFilterComboBox.SelectedItem as ChannelInfo;

            // Reload messages with new filter from ALL messages
            _messages.Clear();

            foreach (var msg in _allMessages)
            {
                // Apply filter
                if (_messageChannelFilter == null || _messageChannelFilter.Index == 999)
                {
                    // "Alle Kanäle" ausgewählt - zeige alle
                    _messages.Add(msg);
                }
                else
                {
                    // Spezifischer Kanal ausgewählt - nur diesen zeigen
                    if (uint.TryParse(msg.Channel, out uint channelIndex))
                    {
                        if (channelIndex == _messageChannelFilter.Index)
                        {
                            _messages.Add(msg);
                        }
                    }
                }
            }

            Services.Logger.WriteLine($"Message filter changed to: {_messageChannelFilter?.Name ?? "Alle"} ({_messages.Count}/{_allMessages.Count} messages)");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error changing message filter: {ex.Message}");
        }
    }

    private void SendDmToNode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Hole ausgewählten Knoten
            var selectedNode = NodesListView.SelectedItem as Models.NodeInfo;
            if (selectedNode == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Knoten aus.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Prüfe ob eigener Knoten
            if (selectedNode.NodeId == _myNodeId)
            {
                MessageBox.Show("Sie können keine DM an sich selbst senden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Öffne/Erstelle DM-Fenster
            if (_dmWindow == null)
            {
                _dmWindow = new DirectMessagesWindow(_protocolService, _myNodeId);
            }

            // Öffne Chat mit diesem Knoten
            _dmWindow.OpenChatWithNode(selectedNode.NodeId, selectedNode.Name);

            Services.Logger.WriteLine($"Opening DM chat with node: {selectedNode.Name} ({selectedNode.Id})");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR in SendDmToNode_Click: {ex.Message}");
            MessageBox.Show($"Fehler beim Öffnen des Chats: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            // Disconnect synchronously to ensure clean shutdown
            if (_connectionService.IsConnected)
            {
                Services.Logger.WriteLine("Application closing...");
                _protocolService.Disconnect();
                System.Threading.Thread.Sleep(100);
                _connectionService.Disconnect();
                Services.Logger.WriteLine("Disconnected");
            }
            Services.Logger.Close();
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error during close: {ex.Message}");
            Services.Logger.Close();
        }

        // Force application shutdown
        Application.Current.Shutdown();

        base.OnClosing(e);
    }

    // ========== Node List Sorting and Filtering ==========

    private void NodeColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewColumnHeader header && header.Tag is string column)
        {
            // Toggle sort direction if same column, otherwise default to ascending
            if (_nodeSortColumn == column)
            {
                _nodeSortAscending = !_nodeSortAscending;
            }
            else
            {
                _nodeSortColumn = column;
                _nodeSortAscending = true;
            }

            ApplyNodeSortAndFilter();
        }
    }

    private void NodeContextMenu_NodeInfo_Click(object sender, RoutedEventArgs e)
    {
        if (NodesListView.SelectedItem is NodeInfo node)
            ShowNodeInfoDialog(node);
    }

    private void NodeContextMenu_ShowOnMap_Click(object sender, RoutedEventArgs e)
    {
        if (NodesListView.SelectedItem is not NodeInfo node || !node.Latitude.HasValue || !node.Longitude.HasValue)
        {
            MessageBox.Show("Position für diesen Node ist nicht bekannt.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MainTabs.SelectedIndex = 4;
        var nodePos = SphericalMercator.FromLonLat(node.Longitude.Value, node.Latitude.Value);
        if (_map != null)
        {
            _map.Navigator.CenterOnAndZoomTo(new MPoint(nodePos.x, nodePos.y), 76.0);
            MapControl.Refresh();
        }
    }

    private void NodeFilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyNodeSortAndFilter();
    }

    private void ApplyNodeSortAndFilter()
    {
        var filterText = NodeFilterTextBox?.Text?.ToLowerInvariant() ?? string.Empty;

        // Start with all nodes
        var filtered = _allNodes.AsEnumerable();

        // Apply filter
        if (!string.IsNullOrWhiteSpace(filterText))
        {
            filtered = filtered.Where(n =>
                n.Name.ToLowerInvariant().Contains(filterText) ||
                n.ShortName.ToLowerInvariant().Contains(filterText) ||
                n.Id.ToLowerInvariant().Contains(filterText) ||
                n.Distance.ToLowerInvariant().Contains(filterText) ||
                n.Snr.ToLowerInvariant().Contains(filterText) ||
                n.Rssi.ToLowerInvariant().Contains(filterText) ||
                n.Battery.ToLowerInvariant().Contains(filterText) ||
                n.LastSeen.ToLowerInvariant().Contains(filterText)
            );
        }

        // Apply sorting
        if (!string.IsNullOrEmpty(_nodeSortColumn))
        {
            filtered = _nodeSortColumn switch
            {
                "Name" => _nodeSortAscending
                    ? filtered.OrderBy(n => n.Name)
                    : filtered.OrderByDescending(n => n.Name),
                "ShortName" => _nodeSortAscending
                    ? filtered.OrderBy(n => n.ShortName)
                    : filtered.OrderByDescending(n => n.ShortName),
                "Id" => _nodeSortAscending
                    ? filtered.OrderBy(n => n.Id)
                    : filtered.OrderByDescending(n => n.Id),
                "Distance" => _nodeSortAscending
                    ? filtered.OrderBy(n => HasValidDistance(n.Distance) ? 0 : 1)
                             .ThenBy(n => ParseDistanceForSorting(n.Distance))
                             .ThenBy(n => n.ShortName)
                    : filtered.OrderBy(n => HasValidDistance(n.Distance) ? 0 : 1)
                             .ThenByDescending(n => ParseDistanceForSorting(n.Distance))
                             .ThenBy(n => n.ShortName),
                "Snr" => _nodeSortAscending
                    ? filtered.OrderBy(n => HasValidNumeric(n.Snr) ? 0 : 1)
                             .ThenBy(n => ParseNumericForSorting(n.Snr))
                             .ThenBy(n => n.ShortName)
                    : filtered.OrderBy(n => HasValidNumeric(n.Snr) ? 0 : 1)
                             .ThenByDescending(n => ParseNumericForSorting(n.Snr))
                             .ThenBy(n => n.ShortName),
                "Rssi" => _nodeSortAscending
                    ? filtered.OrderBy(n => HasValidNumeric(n.Rssi) ? 0 : 1)
                             .ThenBy(n => ParseNumericForSorting(n.Rssi))
                             .ThenBy(n => n.ShortName)
                    : filtered.OrderBy(n => HasValidNumeric(n.Rssi) ? 0 : 1)
                             .ThenByDescending(n => ParseNumericForSorting(n.Rssi))
                             .ThenBy(n => n.ShortName),
                "Battery" => _nodeSortAscending
                    ? filtered.OrderBy(n => ParseNumericForSorting(n.Battery))
                    : filtered.OrderByDescending(n => ParseNumericForSorting(n.Battery)),
                "LastSeen" => _nodeSortAscending
                    ? filtered.OrderBy(n => n.LastSeen)
                    : filtered.OrderByDescending(n => n.LastSeen),
                _ => filtered
            };
        }

        // Update UI
        _nodes.Clear();
        foreach (var node in filtered)
        {
            _nodes.Add(node);
        }
    }

    private bool HasValidDistance(string distance)
    {
        if (string.IsNullOrEmpty(distance) || distance == "-")
            return false;
        var cleaned = distance.Replace("km", "").Replace("m", "").Trim();
        // Use CurrentCulture to handle comma decimal separator
        return double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out _);
    }

    private bool HasValidNumeric(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "-")
            return false;
        var cleaned = value.Replace("%", "").Replace("dB", "").Trim();
        // Use CurrentCulture to handle comma decimal separator
        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var result))
        {
            // 0.0 is considered "no value" for RSSI/SNR
            return Math.Abs(result) >= 0.01;
        }
        return false;
    }

    private double ParseDistanceForSorting(string distance)
    {
        if (string.IsNullOrEmpty(distance) || distance == "-")
            return 0;

        var cleaned = distance.Replace("km", "").Replace("m", "").Trim();
        // Use CurrentCulture to handle comma decimal separator (DE: "160,2")
        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value))
        {
            // Convert meters to km if needed
            if (distance.EndsWith("m") && !distance.EndsWith("km"))
                return value / 1000.0;
            return value;
        }
        return 0;
    }

    private double ParseNumericForSorting(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "-")
            return 0;

        var cleaned = value.Replace("%", "").Replace("dB", "").Trim();
        // Use CurrentCulture to handle comma decimal separator (DE: "-10,8")
        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var result))
        {
            return result;
        }
        return 0;
    }

    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        // Haversine formula
        const double R = 6371; // Earth radius in km

        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private string FormatDistance(double distanceKm)
    {
        if (distanceKm < 1.0)
            return $"{(int)(distanceKm * 1000)}m";
        else if (distanceKm < 10.0)
            return $"{distanceKm:F2}km";
        else
            return $"{distanceKm:F1}km";
    }

    // ========== Message Context Menu Handlers ==========

    private NodeInfo? GetNodeFromSelectedMessage()
    {
        if (MessageListView.SelectedItem is not MessageItem msg) return null;
        return _allNodes.FirstOrDefault(n => n.NodeId == msg.FromId);
    }

    private void MessageContextMenu_SendDm_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSelectedMessage();
        if (node == null) { MessageBox.Show("Node nicht gefunden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (node.NodeId == _myNodeId) { MessageBox.Show("Sie können keine DM an sich selbst senden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information); return; }
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

        MainTabs.SelectedIndex = 4;
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

    // ========== Node Color and Note Management ==========

    private void SetNodeColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is string color && NodesListView.SelectedItem is NodeInfo node)
        {
            SetNodeColorInternal(node, color);
        }
    }

    private void RemoveNodeColor_Click(object sender, RoutedEventArgs e)
    {
        if (NodesListView.SelectedItem is NodeInfo node)
        {
            RemoveNodeColorInternal(node);
        }
    }

    private void EditNodeNote_Click(object sender, RoutedEventArgs e)
    {
        if (NodesListView.SelectedItem is NodeInfo node)
        {
            EditNodeNoteInternal(node);
        }
    }

    private void SetNodeColorInternal(NodeInfo node, string color)
    {
        try
        {
            node.ColorHex = color;
            _currentSettings.NodeColors[node.NodeId] = color;
            SettingsService.Save(_currentSettings);

            // Update in _allNodes
            var existing = _allNodes.FirstOrDefault(n => n.NodeId == node.NodeId);
            if (existing != null)
            {
                existing.ColorHex = color;
            }

            // Refresh display
            ApplyNodeSortAndFilter();
            UpdateNodePin(node);

            Services.Logger.WriteLine($"Set color {color} for node {node.Name} ({node.Id})");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error setting node color: {ex.Message}");
        }
    }

    private void RemoveNodeColorInternal(NodeInfo node)
    {
        try
        {
            node.ColorHex = string.Empty;
            _currentSettings.NodeColors.Remove(node.NodeId);
            SettingsService.Save(_currentSettings);

            // Update in _allNodes
            var existing = _allNodes.FirstOrDefault(n => n.NodeId == node.NodeId);
            if (existing != null)
            {
                existing.ColorHex = string.Empty;
            }

            // Refresh display
            ApplyNodeSortAndFilter();
            UpdateNodePin(node);

            Services.Logger.WriteLine($"Removed color from node {node.Name} ({node.Id})");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error removing node color: {ex.Message}");
        }
    }

    private void EditNodeNoteInternal(NodeInfo node)
    {
        try
        {
            var dialog = new System.Windows.Window
            {
                Title = $"Notiz für {node.Name}",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var textBox = new TextBox
            {
                Text = node.Note,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(textBox, 0);
            grid.Children.Add(textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(buttonPanel, 1);

            var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            okButton.Click += (s, ev) => { dialog.DialogResult = true; dialog.Close(); };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button { Content = "Abbrechen", Width = 80, IsCancel = true };
            cancelButton.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            buttonPanel.Children.Add(cancelButton);

            grid.Children.Add(buttonPanel);
            dialog.Content = grid;

            if (dialog.ShowDialog() == true)
            {
                var newNote = textBox.Text.Trim();
                node.Note = newNote;

                if (string.IsNullOrEmpty(newNote))
                {
                    _currentSettings.NodeNotes.Remove(node.NodeId);
                }
                else
                {
                    _currentSettings.NodeNotes[node.NodeId] = newNote;
                }

                SettingsService.Save(_currentSettings);

                // Update in _allNodes
                var existing = _allNodes.FirstOrDefault(n => n.NodeId == node.NodeId);
                if (existing != null)
                {
                    existing.Note = newNote;
                }

                // Refresh display
                ApplyNodeSortAndFilter();
                UpdateNodePin(node);

                Services.Logger.WriteLine($"Updated note for node {node.Name} ({node.Id}): {newNote}");
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error editing node note: {ex.Message}");
        }
    }

    private void PlayAlertSound()
    {
        try
        {
            // Play alarm sound in background thread
            Task.Run(() =>
            {
                try
                {
                    // Generate and play alarm WAV sound
                    var wavData = GenerateAlarmSound();
                    using (var ms = new MemoryStream(wavData))
                    {
                        var player = new System.Media.SoundPlayer(ms);
                        player.PlaySync();
                    }
                    Services.Logger.WriteLine("Alert sound played successfully");
                }
                catch (Exception ex)
                {
                    Services.Logger.WriteLine($"WAV playback failed: {ex.Message}, trying Console.Beep");
                    try
                    {
                        // Fallback to Console.Beep
                        for (int i = 0; i < 3; i++)
                        {
                            Console.Beep(1200, 150);
                            Thread.Sleep(100);
                        }
                    }
                    catch
                    {
                        // Last fallback: System sound
                        for (int i = 0; i < 5; i++)
                        {
                            System.Media.SystemSounds.Hand.Play();
                            Thread.Sleep(200);
                        }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error playing alert sound: {ex.Message}");
        }
    }

    private byte[] GenerateAlarmSound()
    {
        // Generate a simple alarm sound (siren effect) as WAV
        int sampleRate = 8000;
        int durationMs = 2000; // 2 seconds
        int numSamples = (sampleRate * durationMs) / 1000;

        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            // WAV header
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + numSamples); // File size - 8
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16); // Format chunk size
            writer.Write((short)1); // PCM
            writer.Write((short)1); // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate); // Byte rate
            writer.Write((short)1); // Block align
            writer.Write((short)8); // Bits per sample
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(numSamples);

            // Generate siren sound (alternating frequencies)
            double freq1 = 800.0; // Low frequency
            double freq2 = 1400.0; // High frequency
            double cycleDuration = 0.5; // Half second per cycle
            int cyclesamples = (int)(sampleRate * cycleDuration);

            for (int i = 0; i < numSamples; i++)
            {
                // Alternate between two frequencies
                int cyclePos = i % (cyclesamples * 2);
                double freq = (cyclePos < cyclesamples) ? freq1 : freq2;

                // Generate sine wave
                double angle = 2.0 * Math.PI * freq * i / sampleRate;
                double sample = Math.Sin(angle) * 127 + 128;

                writer.Write((byte)sample);
            }

            return ms.ToArray();
        }
    }

    private void ShowAlertBellAnimation()
    {
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                // Start blink animation
                var storyboard = new System.Windows.Media.Animation.Storyboard();

                // Create animation for opacity (blink effect)
                var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    AutoReverse = true,
                    RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(6) // 6 blinks (3 seconds)
                };

                System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnimation, AlertBellOverlay);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(Border.OpacityProperty));

                storyboard.Children.Add(opacityAnimation);

                // Show overlay and start animation
                AlertBellOverlay.Visibility = Visibility.Visible;

                storyboard.Completed += (s, e) =>
                {
                    AlertBellOverlay.Visibility = Visibility.Collapsed;
                };

                storyboard.Begin();

                // Flash window in taskbar
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                FlashWindow(hwnd, true);
            });
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error showing alert bell animation: {ex.Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);

    private void ShowAlertNotification(string nodeName, uint nodeId)
    {
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                _alertNodeId = nodeId;

                // Update notification text
                AlertNotificationText.Text = $"🚨 Notruf von {nodeName}!";

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
            MainTabs.SelectedIndex = 4; // Map is tab index 4 (0=Messages, 1=Nodes, 2=Channels, 3=Settings, 4=Map)

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
}
