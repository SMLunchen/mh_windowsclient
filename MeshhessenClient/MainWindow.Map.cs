// Karte & Vektorkarte (Mapsui-Raster + MapLibre/WebView2, Bridge)
// Ausgelagert aus MainWindow.xaml.cs (partial class) – reine Umsortierung, keine Logikaenderung.

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

public partial class MainWindow
{
    #region Karte

    // Custom mode: URLs as configured in the settings
    private string GetUrlForSource(string source) => source switch
    {
        "osm"     => _currentSettings.OSMTileUrl,
        "osmtopo" => _currentSettings.OSMTopoTileUrl,
        "osmdark" => _currentSettings.OSMDarkTileUrl,
        _         => _currentSettings.OSMTileUrl
    };

    // Meshhessen mode: always the official servers, independent of the URL fields
    private static string GetMeshhessenUrlForSource(string source) => source switch
    {
        "osmtopo" => "https://tile.meshhessenclient.de/opentopo/{z}/{x}/{y}.png",
        "osmdark" => "https://tile.meshhessenclient.de/dark/{z}/{x}/{y}.png",
        _         => "https://tile.meshhessenclient.de/osm/{z}/{x}/{y}.png"
    };

    // The tile URL actually in effect for the current map mode and the given source.
    // The configured custom URL fields apply ONLY in custom mode; every other mode
    // uses its own server (online-own + offline → official Meshhessen servers,
    // online-osm → public OSM). Mirrors the live-map provider selection in
    // InitializeMap, so downloads and map queries stay in sync.
    private string GetActiveTileUrl(string source) => _currentSettings.MapMode switch
    {
        "online-custom" => GetUrlForSource(source),
        "online-osm"    => "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        _               => GetMeshhessenUrlForSource(source),
    };

    // Vector rendering is available in all modes except online-osm (no public vector OSM server)
    private bool UseVectorMap =>
        _currentSettings.MapRenderMode == "vector" && _currentSettings.MapMode != "online-osm";

    private void InitializeMap()
    {
        try
        {
            if (UseVectorMap)
            {
                InitializeVectorMap();
                return;
            }

            MapControl.Visibility = Visibility.Visible;
            VectorMapView.Visibility = Visibility.Collapsed;
            MapCopyrightText.Visibility = Visibility.Visible;
            MapOverlaySeparator.Visibility = Visibility.Collapsed;
            MapLayersBtn.Visibility = Visibility.Collapsed;
            PlaceMapLegendsForRenderMode();

            _map = new Mapsui.Map();

            var tileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");
            var sourceFolder = _currentSettings.MapSource;  // "osm", "osmtopo", oder "osmdark"
            var schema = new GlobalSphericalMercator(YAxis.TMS, 0, 18, "OSM");

            BruTile.ITileProvider tileProvider = _currentSettings.MapMode switch
            {
                "online-osm" => new Services.CachingHttpTileProvider(
                    tileDir, "osm_online",
                    "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
                    useHttpCacheHeaders: true),
                "online-own" => new Services.CachingHttpTileProvider(
                    tileDir, sourceFolder,
                    GetMeshhessenUrlForSource(sourceFolder),
                    useHttpCacheHeaders: false),
                "online-custom" => new Services.CachingHttpTileProvider(
                    tileDir, sourceFolder,
                    GetUrlForSource(sourceFolder),
                    useHttpCacheHeaders: false),
                _ => new LocalFileTileProvider(tileDir, sourceFolder)
            };

            var tileSource = new TileSource(tileProvider, schema);
            _map.Layers.Add(new TileLayer(tileSource) { Name = "OSM" });

            // Neighbour-lines layer (rendered below node pins)
            _neighborLinesLayer = new MemoryLayer("NeighborLines") { Features = _neighborLineFeatures, Style = null };
            _map.Layers.Add(_neighborLinesLayer);

            // Node-Layer
            _nodeLayer = new MemoryLayer("Nodes") { Features = _nodeFeatures, Style = null };
            _map.Layers.Add(_nodeLayer);

            // Eigener-Standort-Layer
            _myPosLayer = new MemoryLayer("MyPosition") { Features = _myPosFeatures, Style = null };
            _map.Layers.Add(_myPosLayer);

            MapControl.Map = _map;
            MapControl.MouseRightButtonUp += MapControl_RightClick;
            // PreviewMouseLeftButtonDown fires before Mapsui starts pan tracking.
            // Mark handled on segment hit ? Mapsui never starts pan, map stays put.
            MapControl.PreviewMouseLeftButtonDown += MapControl_LeftClick_Preview;
            // MouseMove: cursor feedback when hovering near a segment
            MapControl.MouseMove += MapControl_MouseMoveSegmentHover;

            // Karte auf eigenen Standort zentrieren
            var center = SphericalMercator.FromLonLat(_currentSettings.MyLongitude, _currentSettings.MyLatitude);
            // Resolution ~611 entspricht Zoom-Level 8 in Web-Mercator
            _map.Home = n => n.CenterOnAndZoomTo(new MPoint(center.x, center.y), 611.0);

            UpdateMyPositionPin();

            if (_currentSettings.MapMode is "online-own" or "online-osm" or "online-custom")
            {
                MapStatusText.Text = "";
            }
            else
            {
                var sourceTileDir = Path.Combine(tileDir, sourceFolder);
                MapStatusText.Text = Directory.Exists(sourceTileDir) && Directory.EnumerateFiles(sourceTileDir, "*.png", SearchOption.AllDirectories).Any()
                    ? "" : Loc("StrNoTiles");
            }

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

    // ── Vektor-Karte (MapLibre GL JS in WebView2) ────────────────────────────

    private string GetVectorStyleUrl()
    {
        // online-own always uses the official server; custom + offline use the configured style URLs
        if (_currentSettings.MapMode == "online-own")
        {
            var host = Services.VectorTileCacheService.DefaultVectorHost;
            return _currentSettings.MapSource switch
            {
                "osmtopo" => $"https://{host}/styles/opentopo.json",
                "osmdark" => $"https://{host}/styles/dark.json",
                _ => $"https://{host}/styles/osm.json"
            };
        }
        return _currentSettings.MapSource switch
        {
            "osmtopo" => _currentSettings.VectorStyleTopoUrl,
            "osmdark" => _currentSettings.VectorStyleDarkUrl,
            _ => _currentSettings.VectorStyleOsmUrl
        };
    }

    private async void InitializeVectorMap()
    {
        try
        {
            MapControl.Visibility = Visibility.Collapsed;
            VectorMapView.Visibility = Visibility.Visible;
            // Attribution (© OpenMapTiles © OSM) is rendered inside the map page –
            // WPF cannot draw on top of the WebView2 HWND (airspace)
            MapCopyrightText.Visibility = Visibility.Collapsed;
            MapOverlaySeparator.Visibility = Visibility.Visible;
            MapLayersBtn.Visibility = Visibility.Visible;
            PlaceMapLegendsForRenderMode();

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var cacheDir = Path.Combine(baseDir, "vectortiles");
            _vectorTileCache ??= new Services.VectorTileCacheService(cacheDir);
            _vectorTileCache.OfflineMode = _currentSettings.MapMode == "offline";
            _vectorTileCache.RegisterStyleUrls(new[]
            {
                _currentSettings.VectorStyleOsmUrl,
                _currentSettings.VectorStyleTopoUrl,
                _currentSettings.VectorStyleDarkUrl
            });

            if (_vectorMapReady)
            {
                // Map page already running – just switch the style
                await VectorMapView.CoreWebView2.ExecuteScriptAsync(
                    $"setStyle({JsonSerializer.Serialize(GetVectorStyleUrl())})");
                // Re-sync everything (covers raster->vector switches where pushes were skipped)
                PushMyPositionToVectorMap();
                PushNodePinsToVectorMap();
                PushWaypointsToVectorMap();
                PushNeighborLinesToVectorMap();
                foreach (var (key, json) in _vectorLineJson.ToList())
                    ExecVectorScript($"setLines({JsonSerializer.Serialize(key)}, {json})");
            }
            else if (!_vectorMapInitStarted)
            {
                _vectorMapInitStarted = true;
                var assetsDir = Services.VectorTileCacheService.ExtractAssets(cacheDir);

                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    userDataFolder: Path.Combine(baseDir, "webview2-data"));
                await VectorMapView.EnsureCoreWebView2Async(env);

                var core = VectorMapView.CoreWebView2;
                var version = System.Reflection.Assembly.GetExecutingAssembly()
                                  .GetName().Version?.ToString(3) ?? "1.0.0";
                // Server ACL requires MeshhessenClient/* UA – all requests go through
                // our interceptor (own HttpClient), this covers anything that slips past
                core.Settings.UserAgent =
                    $"MeshhessenClient/{version} (+https://meshhessenclient.de; contact: admin@meshhessenclient.de)";
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;

                core.SetVirtualHostNameToFolderMapping("meshmap.local", assetsDir,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                core.AddWebResourceRequestedFilter("*",
                    Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += VectorMap_WebResourceRequested;
                core.WebMessageReceived += VectorMap_WebMessageReceived;
                core.NavigationCompleted += VectorMap_NavigationCompleted;

                core.Navigate("https://meshmap.local/map.html");
                // NavigationCompleted starts the map with the current settings
            }

            UpdateMapTileStatus();
        }
        catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            Services.Logger.WriteLine("WebView2 runtime not found - falling back to raster map");
            FallbackToRasterMap(showMessage: true);
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR initializing vector map: {ex.Message}");
            FallbackToRasterMap(showMessage: false);
        }
    }

    private void FallbackToRasterMap(bool showMessage)
    {
        _vectorMapInitStarted = false;
        _currentSettings = _currentSettings with { MapRenderMode = "raster" };
        Services.SettingsService.Save(_currentSettings);
        if (MapRenderRasterRadio != null)
            MapRenderRasterRadio.IsChecked = true;   // handler sees no change, no re-init loop
        ApplyMapModeUi(_currentSettings.MapMode);
        InitializeMap();
        if (showMessage)
            MessageBox.Show(Loc("StrVectorNoWebView2"), Loc("StrMapRenderVector"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void VectorMap_NavigationCompleted(object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            Services.Logger.WriteLine($"[VectorMap] Navigation failed: {e.WebErrorStatus}");
            return;
        }
        _vectorMapReady = true;
        try
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var styleUrl = JsonSerializer.Serialize(GetVectorStyleUrl());
            var attribution = JsonSerializer.Serialize(Services.VectorTileCacheService.Attribution);
            var lang = JsonSerializer.Serialize(_currentSettings.Language);
            // Zoom 8 matches the raster map's home resolution (~611)
            await VectorMapView.CoreWebView2.ExecuteScriptAsync(
                $"initMap({styleUrl}, {_currentSettings.MyLongitude.ToString(ci)}, {_currentSettings.MyLatitude.ToString(ci)}, 8, {attribution}, {lang})");
            PushMyPositionToVectorMap();
            PushOverlaysToVectorMap();
            PushNodePinsToVectorMap();
            PushWaypointsToVectorMap();
            PushNeighborLinesToVectorMap();
            // Restore cached line overlays (traceroutes, paths) e.g. after raster->vector switch
            foreach (var (key, json) in _vectorLineJson.ToList())
                ExecVectorScript($"setLines({JsonSerializer.Serialize(key)}, {json})");

            // A "Show on map" click that opened the map applies its center now that JS is callable.
            if (_pendingVectorCenter is { } pc)
            {
                _pendingVectorCenter = null;
                ExecVectorScript($"setCenter({pc.Lon.ToString(ci)}, {pc.Lat.ToString(ci)}, {pc.Zoom})");
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[VectorMap] init script error: {ex.Message}");
        }
    }

    private async void PushMyPositionToVectorMap()
    {
        if (!_vectorMapReady) return;
        try
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var label = JsonSerializer.Serialize(
                string.IsNullOrEmpty(_activeStationName) ? "Ich" : _activeStationName);
            await VectorMapView.CoreWebView2.ExecuteScriptAsync(
                $"setMyPosition({_currentSettings.MyLongitude.ToString(ci)}, {_currentSettings.MyLatitude.ToString(ci)}, {label})");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[VectorMap] setMyPosition error: {ex.Message}");
        }
    }

    // Toggle handler for all overlay checkboxes (overlay key in Tag) – instances
    // live in the settings panel AND the map toolbar popup, kept in sync below
    private void MapOverlayCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_currentSettings is null) return;
        if (sender is not System.Windows.Controls.CheckBox box || box.Tag is not string key) return;

        var active = Services.MapOverlayRegistry.ParseActive(_currentSettings.MapOverlays);
        if (box.IsChecked == true) active.Add(key); else active.Remove(key);
        var csv = string.Join(",", active.OrderBy(k => k));
        if (csv == _currentSettings.MapOverlays) return;

        _currentSettings = _currentSettings with { MapOverlays = csv };
        Services.SettingsService.Save(_currentSettings);
        Services.Logger.WriteLine($"Map overlays changed: [{csv}]");
        PushOverlaysToVectorMap();
        SyncOverlayCheckboxes();
    }

    /// <summary>Creates one checkbox per registered overlay in the settings panel
    /// and in the map toolbar popup. Adding an overlay = one MapOverlayRegistry entry.</summary>
    private void BuildOverlayPanels()
    {
        foreach (var panel in new[] { VectorOverlaysPanel, MapLayersPopupPanel })
        {
            if (panel == null) continue;
            panel.Children.Clear();
            foreach (var overlay in Services.MapOverlayRegistry.All)
            {
                var text = new TextBlock { FontSize = 12 };
                text.SetResourceReference(TextBlock.TextProperty, overlay.NameResourceKey);
                var cb = new CheckBox { Tag = overlay.Key, Margin = new Thickness(0, 0, 0, 4), Content = text };
                cb.SetResourceReference(FrameworkElement.ToolTipProperty, overlay.NameResourceKey + "Tooltip");
                cb.Checked += MapOverlayCheck_Changed;
                cb.Unchecked += MapOverlayCheck_Changed;
                panel.Children.Add(cb);
            }
        }
    }

    private void SyncOverlayCheckboxes()
    {
        var active = Services.MapOverlayRegistry.ParseActive(_currentSettings?.MapOverlays);
        foreach (var panel in new[] { VectorOverlaysPanel, MapLayersPopupPanel })
        {
            if (panel == null) continue;
            foreach (var cb in panel.Children.OfType<System.Windows.Controls.CheckBox>())
                if (cb.Tag is string key)
                    cb.IsChecked = active.Contains(key);   // no-op change -> handler early-returns
        }
    }

    private void MapLayersBtn_Click(object sender, RoutedEventArgs e)
    {
        MapLayersPopup.PlacementTarget = MapLayersBtn;
        MapLayersPopup.IsOpen = !MapLayersPopup.IsOpen;
    }

    private void MapLegendBtn_Click(object sender, RoutedEventArgs e)
    {
        MapLegendBorder.Visibility = Visibility.Visible;   // re-show after ✕ inside the popup
        MapLegendPopup.PlacementTarget = MapLegendBtn;
        MapLegendPopup.IsOpen = !MapLegendPopup.IsOpen;
    }

    /// <summary>
    /// The legend and the traceroute list are WPF overlays inside the map grid.
    /// Over the WebView2 HWND they are invisible (airspace), so in vector mode
    /// they are reparented into the legend button popup and back for raster.
    /// </summary>
    private void PlaceMapLegendsForRenderMode()
    {
        if (MapLegendBorder == null || VectorLegendHost == null) return;

        if (UseVectorMap)
        {
            if (MapLegendBorder.Parent == MapAreaGrid)
            {
                MapAreaGrid.Children.Remove(MapLegendBorder);
                VectorLegendHost.Children.Add(MapLegendBorder);
            }
            if (TracerouteLegend.Parent == MapAreaGrid)
            {
                MapAreaGrid.Children.Remove(TracerouteLegend);
                VectorLegendHost.Children.Add(TracerouteLegend);
            }
            MapLegendBorder.Visibility = Visibility.Visible;   // may have been closed via ✕ on the raster map
            MapLegendBtn.Visibility = Visibility.Visible;
        }
        else
        {
            MapLegendPopup.IsOpen = false;
            if (VectorLegendHost.Children.Contains(MapLegendBorder))
            {
                VectorLegendHost.Children.Remove(MapLegendBorder);
                MapAreaGrid.Children.Add(MapLegendBorder);
            }
            if (VectorLegendHost.Children.Contains(TracerouteLegend))
            {
                VectorLegendHost.Children.Remove(TracerouteLegend);
                MapAreaGrid.Children.Add(TracerouteLegend);
            }
            MapLegendBtn.Visibility = Visibility.Collapsed;
        }
    }

    private async void PushOverlaysToVectorMap()
    {
        if (!_vectorMapReady) return;
        try
        {
            var active = Services.MapOverlayRegistry.ParseActive(_currentSettings.MapOverlays);
            foreach (var overlay in Services.MapOverlayRegistry.All)
            {
                var on = active.Contains(overlay.Key) ? "true" : "false";
                await VectorMapView.CoreWebView2.ExecuteScriptAsync(
                    $"setOverlay({JsonSerializer.Serialize(overlay.LayerPrefix)}, {on})");
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[VectorMap] setOverlay error: {ex.Message}");
        }
    }

    private void VectorMap_WebResourceRequested(object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            if (_vectorTileCache == null) return;
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
            if (!_vectorTileCache.IsHandledHost(uri)) return; // meshmap.local assets -> default handling

            var deferral = e.GetDeferral();
            _ = ServeVectorResourceAsync(e, deferral);
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[VectorMap] Request handler error: {ex.Message}");
        }
    }

    private async Task ServeVectorResourceAsync(
        Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs e,
        Microsoft.Web.WebView2.Core.CoreWebView2Deferral deferral)
    {
        try
        {
            var result = await _vectorTileCache!.GetResponseAsync(e.Request.Uri);
            e.Response = VectorMapView.CoreWebView2.Environment.CreateWebResourceResponse(
                result.Body != null ? new MemoryStream(result.Body) : null,
                result.StatusCode, result.ReasonPhrase, result.Headers);
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[VectorMap] Serve error {e.Request.Uri}: {ex.Message}");
            try
            {
                e.Response = VectorMapView.CoreWebView2.Environment.CreateWebResourceResponse(
                    null, 500, "Internal Error", "");
            }
            catch { }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void VectorMap_WebMessageReceived(object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "ready":
                    Services.Logger.WriteLine("[VectorMap] Style loaded");
                    MapStatusText.Text = "";
                    break;

                case "maperror":
                    var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
                    Services.Logger.WriteLine($"[VectorMap] Map error: {msg}");
                    break;

                case "nodeclick":
                {
                    var node = FindNodeFromMessage(root);
                    if (node?.Latitude != null && node.Longitude != null)
                    {
                        var km = HaversineKm(_currentSettings.MyLatitude, _currentSettings.MyLongitude,
                                             node.Latitude.Value, node.Longitude.Value);
                        MapStatusText.Text = string.Format(Loc("StrNodeDistanceStatus"), node.ShortName, node.Id, km, node.LastSeen);
                    }
                    break;
                }

                case "nodecontext":
                {
                    var node = FindNodeFromMessage(root);
                    if (node != null)
                        ShowMapContextMenu(node, null,
                            node.Latitude ?? 0, node.Longitude ?? 0, VectorMapView);
                    break;
                }

                case "waypointcontext":
                {
                    if (root.TryGetProperty("id", out var wid) && wid.TryGetUInt32(out var waypointId))
                    {
                        var wp = _waypoints.FirstOrDefault(w => w.Id == waypointId);
                        if (wp != null)
                            ShowMapContextMenu(null, wp, wp.Latitude, wp.Longitude, VectorMapView);
                    }
                    break;
                }

                case "mapcontext":
                {
                    if (root.TryGetProperty("lat", out var latEl) && root.TryGetProperty("lon", out var lonEl))
                        ShowMapContextMenu(null, null, latEl.GetDouble(), lonEl.GetDouble(), VectorMapView);
                    break;
                }

                case "lineclick":
                {
                    if (!root.TryGetProperty("props", out var props)) break;
                    if (!props.TryGetProperty("fromId", out var fEl) || !fEl.TryGetUInt32(out var fromId)) break;
                    if (!props.TryGetProperty("toId", out var tEl2) || !tEl2.TryGetUInt32(out var toId)) break;
                    float? snr = props.TryGetProperty("snr", out var sEl) && sEl.ValueKind == JsonValueKind.Number
                        ? (float)sEl.GetDouble() : null;
                    bool isMqtt = props.TryGetProperty("mqtt", out var mEl) && mEl.ValueKind == JsonValueKind.Number && mEl.GetInt32() == 1;
                    ShowTracerouteSegmentPopup(new SegmentHitTarget(new MPoint(0, 0), fromId, toId, snr, isMqtt));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[VectorMap] WebMessage error: {ex.Message}");
        }
    }

    private NodeInfo? FindNodeFromMessage(JsonElement root) =>
        root.TryGetProperty("id", out var idEl) && idEl.TryGetUInt32(out var nodeId)
            ? _allNodes.FirstOrDefault(n => n.NodeId == nodeId)
            : null;

    // ── Bridge: pins, waypoints, lines auf die Vektorkarte pushen ────────────

    // Line overlays keyed like the Mapsui layers ("neighbors", traceroute layerKey, "path_<id>").
    // Cached so a raster→vector switch or map reload can restore everything.
    private readonly Dictionary<string, string> _vectorLineJson = new();
    private System.Windows.Threading.DispatcherTimer? _vectorNodePushTimer;

    private async void ExecVectorScript(string script)
    {
        if (!_vectorMapReady) return;
        try { await VectorMapView.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex) { Services.Logger.WriteLine($"[VectorMap] script error: {ex.Message}"); }
    }

    /// <summary>
    /// Centers whichever map is currently active on a node position. Handles both the
    /// Mapsui raster map (<see cref="_map"/>) and the MapLibre vector map (WebView2) —
    /// the raster-only path used to leave the vector map un-centered on "Show on map".
    /// </summary>
    private void CenterMapOnNode(double lat, double lon)
    {
        var nodePos = SphericalMercator.FromLonLat(lon, lat);
        if (_map != null)
        {
            _map.Navigator.CenterOnAndZoomTo(new MPoint(nodePos.x, nodePos.y), 76.0);
            MapControl.Refresh();
        }
        if (UseVectorMap)
        {
            const int zoom = 12;
            if (_vectorMapReady)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                ExecVectorScript($"setCenter({lon.ToString(ci)}, {lat.ToString(ci)}, {zoom})");
            }
            else
            {
                // Map tab opened for the first time by this very click — apply once it's ready.
                _pendingVectorCenter = (lon, lat, zoom);
            }
        }
    }

    /// <summary>Removes every node pin from both maps (raster features + vector). Assumes
    /// <c>_allNodes</c> is already cleared so the vector push writes an empty set.</summary>
    private void ClearAllNodePinsFromMap()
    {
        _nodeFeatures.Clear();
        if (_nodeLayer != null)
        {
            _nodeLayer.Features = _nodeFeatures;
            _nodeLayer.DataHasChanged();
        }
        MapControl?.Refresh();
        PushNodePinsToVectorMap();
    }

    private static string CssColor(Mapsui.Styles.Color c) =>
        $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)})";

    // Node colors from settings are "#RRGGBB"; normalize "#AARRGGBB" for CSS
    private static string CssColorFromHex(string? hex, string fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        return hex.Length == 9 ? "#" + hex.Substring(3) : hex;
    }

    /// <summary>Debounced full push of all node pins (positions arrive in bursts).</summary>
    private void ScheduleNodePinPushToVectorMap()
    {
        if (!UseVectorMap) return;
        if (_vectorNodePushTimer == null)
        {
            _vectorNodePushTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _vectorNodePushTimer.Tick += (_, _) =>
            {
                _vectorNodePushTimer!.Stop();
                PushNodePinsToVectorMap();
            };
        }
        _vectorNodePushTimer.Stop();
        _vectorNodePushTimer.Start();
    }

    private void PushNodePinsToVectorMap()
    {
        if (!_vectorMapReady || !UseVectorMap) return;
        var nodes = _allNodes
            .Where(n => n.Latitude.HasValue && n.Longitude.HasValue)
            .Select(n => new
            {
                id = n.NodeId,
                lon = n.Longitude!.Value,
                lat = n.Latitude!.Value,
                color = CssColorFromHex(n.ColorHex, "#e53935"),
                label = (string.IsNullOrEmpty(n.ShortName) ? n.Id : n.ShortName)
                      + (string.IsNullOrEmpty(n.Note) ? "" : $" ({n.Note})")
            })
            .ToList();
        ExecVectorScript($"setNodes({JsonSerializer.Serialize(nodes)})");
    }

    private void PushWaypointsToVectorMap()
    {
        if (!_vectorMapReady || !UseVectorMap) return;
        var wps = _waypoints.Select(wp => new
        {
            id = wp.Id,
            lon = wp.Longitude,
            lat = wp.Latitude,
            label = $"{(wp.Icon > 0 ? char.ConvertFromUtf32((int)wp.Icon) : "📍")} {wp.Name}"
        }).ToList();
        ExecVectorScript($"setWaypoints({JsonSerializer.Serialize(wps)})");
    }

    private void PushVectorLines(string key, object featureCollection)
    {
        var json = JsonSerializer.Serialize(featureCollection);
        _vectorLineJson[key] = json;
        if (_vectorMapReady && UseVectorMap)
            ExecVectorScript($"setLines({JsonSerializer.Serialize(key)}, {json})");
    }

    private void RemoveVectorLines(string key)
    {
        _vectorLineJson.Remove(key);
        if (_vectorMapReady)
            ExecVectorScript($"removeLines({JsonSerializer.Serialize(key)})");
    }

    private static object LineFeature(double lon1, double lat1, double lon2, double lat2, object props) => new
    {
        type = "Feature",
        geometry = new { type = "LineString", coordinates = new[] { new[] { lon1, lat1 }, new[] { lon2, lat2 } } },
        properties = props
    };

    private static object LineFeatureCoords(IEnumerable<double[]> lonLatCoords, object props) => new
    {
        type = "Feature",
        geometry = new { type = "LineString", coordinates = lonLatCoords.ToArray() },
        properties = props
    };

    private static object PointFeatureGeo(double lon, double lat, object props) => new
    {
        type = "Feature",
        geometry = new { type = "Point", coordinates = new[] { lon, lat } },
        properties = props
    };

    private static object FeatureCollection(List<object> features) =>
        new { type = "FeatureCollection", features };

    /// <summary>Mirrors DrawNeighborLines() onto the vector map.</summary>
    private void PushNeighborLinesToVectorMap()
    {
        var features = new List<object>();
        if (_showNeighborLines && _myNodeId != 0)
        {
            var myNode = _allNodes.FirstOrDefault(n => n.NodeId == _myNodeId);
            double myLat = myNode?.Latitude ?? _currentSettings.MyLatitude;
            double myLon = myNode?.Longitude ?? _currentSettings.MyLongitude;

            if (myLat != 0 || myLon != 0)
            {
                var cutoff = _neighborPermanent ? DateTime.MinValue : DateTime.Now.AddHours(-24);
                var outlineCss = CssColor(Mapsui.Styles.Color.FromArgb(200, 20, 20, 20));

                foreach (var node in _allNodes)
                {
                    if (node.NodeId == _myNodeId) continue;
                    if (!node.DirectNeighborAt.HasValue || node.DirectNeighborAt < cutoff) continue;
                    if (!node.Latitude.HasValue || !node.Longitude.HasValue) continue;

                    Mapsui.Styles.Color color;
                    if (_neighborColorByAge)
                        color = NeighborColorByAge(node.DirectNeighborAt);
                    else
                    {
                        if (!node.DirectNeighborSnr.HasValue) continue;
                        color = NeighborColorBySnr(node.DirectNeighborSnr);
                    }

                    features.Add(LineFeature(myLon, myLat, node.Longitude.Value, node.Latitude.Value,
                        new { outline = 1, color = outlineCss, width = 4.5 }));
                    features.Add(LineFeature(myLon, myLat, node.Longitude.Value, node.Latitude.Value,
                        new { color = CssColor(color), width = 2.5 }));
                }
            }
        }

        if (features.Count == 0) RemoveVectorLines("neighbors");
        else PushVectorLines("neighbors", FeatureCollection(features));
    }

    private void UpdateMyPositionPin()
    {
        PushMyPositionToVectorMap();
        _myPosFeatures.Clear();
        var pos = SphericalMercator.FromLonLat(_currentSettings.MyLongitude, _currentSettings.MyLatitude);
        var label = string.IsNullOrEmpty(_activeStationName) ? "Ich" : _activeStationName;
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
            Font = new Mapsui.Styles.Font { FontFamily = "Segoe UI Emoji" },
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
            Font = new Mapsui.Styles.Font { FontFamily = "Segoe UI Emoji" },
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
        // Outside the layer guard: in vector-only mode _nodeLayer is null,
        // but neighbor lines still need refreshing (DrawNeighborLines pushes to the vector map)
        if (_showNeighborLines) DrawNeighborLines();
        ScheduleNodePinPushToVectorMap();
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
                    hitNode = _allNodes.FirstOrDefault(n => n.NodeId == nodeId);
                    minDist = dist;
                }
            }

            // Waypoint hit-test
            TelemetryDatabaseService.WaypointEntry? hitWaypoint = null;
            double minWpDist = 30;
            foreach (var (wpId, wpWorld) in _waypointPinPositions)
            {
                var wpScreen = MapControl.Map.Navigator.Viewport.WorldToScreen(wpWorld);
                var dist = Math.Sqrt(Math.Pow(screenPos.X - wpScreen.X, 2) + Math.Pow(screenPos.Y - wpScreen.Y, 2));
                if (dist < minWpDist)
                {
                    hitWaypoint = _waypoints.FirstOrDefault(w => w.Id == wpId);
                    minWpDist = dist;
                }
            }

            var lonLatClick = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);
            ShowMapContextMenu(hitNode, hitWaypoint, lonLatClick.lat, lonLatClick.lon, MapControl);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR map right-click: {ex.Message}");
        }
    }

    /// <summary>Shared map context menu – used by the raster right-click handler
    /// and the vector map bridge (nodecontext/waypointcontext/mapcontext).</summary>
    private void ShowMapContextMenu(NodeInfo? hitNode,
        TelemetryDatabaseService.WaypointEntry? hitWaypoint,
        double clickLat, double clickLon, UIElement placementTarget)
    {
        try
        {
            var menu = new ContextMenu();

            if (hitNode != null)
            {
                _mapContextMenuNode = hitNode;
                var dmItem = new MenuItem { Header = Loc("StrSendDm") };
                dmItem.Click += (s, ev) => { if (_mapContextMenuNode != null) OpenDmToNode(_mapContextMenuNode); };
                menu.Items.Add(dmItem);

                var infoItem = new MenuItem { Header = Loc("StrNodeInfo") };
                infoItem.Click += (s, ev) => { if (_mapContextMenuNode != null) ShowNodeInfoDialog(_mapContextMenuNode); };
                menu.Items.Add(infoItem);

                menu.Items.Add(new Separator());

                // Color submenu
                var colorMenu = new MenuItem { Header = Loc("StrSetColor") };
                var colors = new[]
                {
                    ("StrColorGreen", "#00FF00"),
                    ("StrColorBlue", "#0080FF"),
                    ("StrColorYellow", "#FFFF00"),
                    ("StrColorOrange", "#FF8000"),
                    ("StrColorPurple", "#8000FF"),
                    ("StrColorBrown", "#804000"),
                    ("StrColorPink", "#FF00FF"),
                    ("StrColorCyan", "#00FFFF")
                };

                foreach (var (key, colorHex) in colors)
                {
                    var textBlock = new TextBlock
                    {
                        Text = Loc(key),
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
                var removeColorItem = new MenuItem { Header = Loc("StrRemoveColor") };
                removeColorItem.Click += (s, ev) =>
                {
                    if (_mapContextMenuNode != null)
                        RemoveNodeColorInternal(_mapContextMenuNode);
                };
                colorMenu.Items.Add(removeColorItem);
                menu.Items.Add(colorMenu);

                // Note option
                var noteItem = new MenuItem { Header = Loc("StrEditNote") };
                noteItem.Click += (s, ev) =>
                {
                    if (_mapContextMenuNode != null)
                        EditNodeNoteInternal(_mapContextMenuNode);
                };
                menu.Items.Add(noteItem);

                menu.Items.Add(new Separator());

                // Pin
                var pinItem = new MenuItem { Header = hitNode.IsPinned ? Loc("StrUnpin") : Loc("StrPin") };
                pinItem.Click += (s, ev) =>
                {
                    if (_mapContextMenuNode != null) PinNodeInternal(_mapContextMenuNode);
                };
                menu.Items.Add(pinItem);

                // Path show/hide
                bool pathActive = hitNode != null && _pathLayers.ContainsKey(hitNode.NodeId);
                if (pathActive)
                {
                    var hidePathItem = new MenuItem { Header = Loc("StrHidePath") };
                    hidePathItem.Click += (s, ev) =>
                    {
                        if (_mapContextMenuNode != null) HidePathForNode(_mapContextMenuNode);
                    };
                    menu.Items.Add(hidePathItem);
                }
                else
                {
                    var showPathItem = new MenuItem { Header = Loc("StrShowPath") };
                    showPathItem.Click += (s, ev) =>
                    {
                        if (_mapContextMenuNode != null)
                            ShowPathForNode(_mapContextMenuNode);
                    };
                    menu.Items.Add(showPathItem);
                }

                // Traceroute
                var traceItem = new MenuItem { Header = Loc("StrTraceroute") };
                traceItem.Click += (s, ev) =>
                {
                    if (_mapContextMenuNode != null)
                        OpenTracerouteForNode(_mapContextMenuNode);
                };
                menu.Items.Add(traceItem);

                // Telemetry
                var telItem = new MenuItem { Header = Loc("StrTelemetry") };
                telItem.Click += (s, ev) =>
                {
                    if (_mapContextMenuNode != null)
                        OpenTelemetryForNode(_mapContextMenuNode);
                };
                menu.Items.Add(telItem);

                // Request information submenu
                menu.Items.Add(new Separator());
                var reqMenu = new MenuItem { Header = Loc("StrRequestInfo") };
                foreach (var (key, tag) in InfoRequestMenuEntries)
                {
                    var label = Loc(key);
                    var reqItem = new MenuItem { Header = label, Tag = tag };
                    reqItem.Click += (s, ev) =>
                    {
                        if (_mapContextMenuNode != null && s is MenuItem m && m.Tag is string t)
                            SendInfoRequest(_mapContextMenuNode, t, label);
                    };
                    reqMenu.Items.Add(reqItem);
                }
                menu.Items.Add(reqMenu);
            }
            else if (hitWaypoint != null)
            {
                var wp = hitWaypoint;
                var wpNameItem = new MenuItem { Header = $"?? {wp.Name}", IsEnabled = false };
                menu.Items.Add(wpNameItem);
                menu.Items.Add(new Separator());
                var deleteWpItem = new MenuItem { Header = Loc("StrDeleteWaypoint") };
                deleteWpItem.Click += async (s, ev) =>
                {
                    _waypoints.Remove(wp);
                    _db?.DeleteWaypoint(wp.Id);
                    RefreshWaypointLayer();
                    if (_connectionService?.IsConnected == true)
                    {
                        var deleteWp = wp with { Expire = 1 };
                        await _protocolService.SendWaypointAsync(deleteWp);
                    }
                };
                menu.Items.Add(deleteWpItem);
            }
            else
            {
                var setPosItem = new MenuItem { Header = string.Format(Loc("StrSetMyPosition"), $"{clickLat:F4}", $"{clickLon:F4}") };
                setPosItem.Click += (s, ev) => SetMyPosition(clickLat, clickLon);
                menu.Items.Add(setPosItem);

                menu.Items.Add(new Separator());
                var wpItem = new MenuItem { Header = Loc("StrCreateWaypoint") };
                wpItem.Click += (s, ev) => CreateWaypointAt(clickLat, clickLon);
                menu.Items.Add(wpItem);
            }

            menu.PlacementTarget = placementTarget;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR map context menu: {ex.Message}");
        }
    }

    // Fires on PreviewMouseLeftButtonDown – before Mapsui starts pan tracking.
    // If a segment is hit, mark e.Handled = true so Mapsui never pans the map.
    private void MapControl_LeftClick_Preview(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (MapControl.Map == null || _tracerouteSegmentHits.Count == 0) return;
            var screenPos = e.GetPosition(MapControl);
            var viewport = MapControl.Map.Navigator.Viewport;
            var worldClick = viewport.ScreenToWorld(screenPos.X, screenPos.Y);
            double thresholdWorld = 35.0 * viewport.Resolution;

            SegmentHitTarget? hitSeg = null;
            double minD = double.MaxValue;
            foreach (var segs in _tracerouteSegmentHits.Values)
                foreach (var seg in segs)
                {
                    var dx = worldClick.X - seg.Midpoint.X;
                    var dy = worldClick.Y - seg.Midpoint.Y;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < thresholdWorld && d < minD) { hitSeg = seg; minD = d; }
                }

            if (hitSeg != null)
            {
                e.Handled = true; // Prevent Mapsui from starting pan
                ShowTracerouteSegmentPopup(hitSeg);
            }
        }
        catch (Exception ex) { Services.Logger.WriteLine($"ERROR map preview click: {ex.Message}"); }
    }

    // MouseMove: change cursor and show status info when hovering near a segment
    private void MapControl_MouseMoveSegmentHover(object sender, MouseEventArgs e)
    {
        try
        {
            if (MapControl.Map == null || _tracerouteSegmentHits.Count == 0)
            {
                MapControl.Cursor = Cursors.Arrow;
                return;
            }
            var screenPos = e.GetPosition(MapControl);
            var viewport = MapControl.Map.Navigator.Viewport;
            var worldPos = viewport.ScreenToWorld(screenPos.X, screenPos.Y);
            double thresholdWorld = 28.0 * viewport.Resolution;

            foreach (var segs in _tracerouteSegmentHits.Values)
                foreach (var seg in segs)
                {
                    var dx = worldPos.X - seg.Midpoint.X;
                    var dy = worldPos.Y - seg.Midpoint.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < thresholdWorld)
                    {
                        MapControl.Cursor = Cursors.Hand;
                        string snrInfo = seg.CurrentSnr.HasValue ? $" | SNR: {seg.CurrentSnr.Value:F1} dB" : "";
                        string type = seg.IsMqtt ? "? MQTT" : "LoRa";
                        string from = seg.FromId == _myNodeId ? Loc("StrMe") : (_allNodes.FirstOrDefault(n => n.NodeId == seg.FromId)?.ShortName ?? $"!{seg.FromId:x4}");
                        string to   = seg.ToId   == _myNodeId ? Loc("StrMe") : (_allNodes.FirstOrDefault(n => n.NodeId == seg.ToId  )?.ShortName ?? $"!{seg.ToId  :x4}");
                        MapStatusText.Text = $"{type}: {from} ? {to}{snrInfo} – {Loc("StrLegendSnrHint")}";
                        return;
                    }
                }
            MapControl.Cursor = Cursors.Arrow;
        }
        catch { }
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
                    hitNode = _allNodes.FirstOrDefault(n => n.NodeId == nodeId);
                    minDist = dist;
                }
            }

            if (hitNode != null && hitNode.Latitude.HasValue && hitNode.Longitude.HasValue)
            {
                var km = HaversineKm(_currentSettings.MyLatitude, _currentSettings.MyLongitude,
                                     hitNode.Latitude.Value, hitNode.Longitude.Value);
                MapStatusText.Text = string.Format(Loc("StrNodeDistanceStatus"), hitNode.ShortName, hitNode.Id, km, hitNode.LastSeen);
                return;
            }

            // T3: Check for traceroute segment hits (world-space comparison, DPI-independent)
            SegmentHitTarget? hitSeg = null;
            double minSegDist = double.MaxValue;
            var viewport = MapControl.Map.Navigator.Viewport;
            var worldClick = viewport.ScreenToWorld(screenPos.X, screenPos.Y);
            // Convert 40px threshold to world units using current resolution
            double thresholdWorld = 40.0 * viewport.Resolution;
            foreach (var segs in _tracerouteSegmentHits.Values)
            {
                foreach (var seg in segs)
                {
                    var dx = worldClick.X - seg.Midpoint.X;
                    var dy = worldClick.Y - seg.Midpoint.Y;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < thresholdWorld && d < minSegDist) { hitSeg = seg; minSegDist = d; }
                }
            }

            if (hitSeg != null)
            {
                ShowTracerouteSegmentPopup(hitSeg);
                return;
            }

            MapStatusText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR map left-click: {ex.Message}");
        }
    }

    private void ShowTracerouteSegmentPopup(SegmentHitTarget seg)
    {
        string NodeLabel(uint id)
        {
            if (id == _myNodeId) return Loc("StrMe");
            var n = _allNodes.FirstOrDefault(x => x.NodeId == id);
            return n?.ShortName is { Length: > 0 } s ? s : $"!{id:x4}";
        }

        string fromLabel = NodeLabel(seg.FromId);
        string toLabel   = NodeLabel(seg.ToId);
        string header    = seg.IsMqtt ? $"? {fromLabel} ? {toLabel} (MQTT)" : $"{fromLabel} ? {toLabel}";

        var sb = new System.Text.StringBuilder(header);

        if (!seg.IsMqtt && _db != null)
        {
            if (seg.CurrentSnr.HasValue)
                sb.Append($" | Aktuell: {seg.CurrentSnr.Value:F1} dB");

            var stats = _db.GetSegmentSnrStats(seg.FromId, seg.ToId, days: 30);
            if (stats != null)
                sb.Append($" | 30d Min/Avg/Max: {stats.Min:F1}/{stats.Avg:F1}/{stats.Max:F1} dB ({stats.Count}–)");

            // T6: Open SNR chart popup
            var points = _db.GetSegmentSnrTimeSeries(seg.FromId, seg.ToId, days: 30);
            var win = new SegmentSnrWindow(fromLabel, toLabel, points, stats,
                _currentSettings.MyLatitude, _currentSettings.MyLongitude)
            { Owner = this };
            win.Show();
            win.Activate();
        }

        MapStatusText.Text = sb.ToString();
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
            _dmWindow.SetMessageDbManager(_messageDbManager);
            _dmWindow.Show();
        }
        _dmWindow.Activate();
        _dmWindow.OpenChatWithNode(node.NodeId, node.Name, node.ColorHex);
    }

    private void ShowNodeInfoDialog(NodeInfo node)
    {
        var win = new NodeInfoWindow(node) { Owner = this };
        win.ShowDialog();
    }

    private void DownloadTiles_Click(object sender, RoutedEventArgs e)
    {
        var source = _currentSettings.MapSource;

        // The custom "Tile-Server URL" field is honoured ONLY in custom mode – and
        // there the (possibly unsaved) textbox value wins, so the user can download
        // without saving first. Every other mode downloads from its own server, so
        // the source dropdown (osm/topo/dark) decides, not the URL field.
        string url;
        if (_currentSettings.MapMode == "online-custom")
            url = string.IsNullOrWhiteSpace(TileServerUrlTextBox.Text)
                ? GetUrlForSource(source)
                : TileServerUrlTextBox.Text.Trim();
        else
            url = GetActiveTileUrl(source);

        switch (source)
        {
            case "osm":     TileDownloaderService.OSMTileUrl     = url; break;
            case "osmtopo": TileDownloaderService.OSMTopoTileUrl = url; break;
            case "osmdark": TileDownloaderService.OSMDarkTileUrl = url; break;
        }

        var win = new TileDownloaderWindow(source) { Owner = this };
        win.ShowDialog();
        // Nach Download: Map-Status aktualisieren
        UpdateMapTileStatus();
    }

    private void DownloadVectorTiles_Click(object sender, RoutedEventArgs e)
    {
        var win = new VectorTileDownloaderWindow(_currentSettings) { Owner = this };
        win.ShowDialog();
        UpdateMapTileStatus();
    }

    private void UpdateMapTileStatus()
    {
        if (_currentSettings.MapMode is "online-own" or "online-osm" or "online-custom")
        {
            MapStatusText.Text = "";
            return;
        }

        if (UseVectorMap)
        {
            // Offline vector: usable once something was cached (lazy cache or future downloader)
            var vectorDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vectortiles");
            var hasData = Directory.Exists(vectorDir) && Directory.EnumerateDirectories(vectorDir)
                .Any(d => !Path.GetFileName(d).StartsWith("_"));
            MapStatusText.Text = hasData ? "" : Loc("StrNoVectorTiles");
            return;
        }

        var tileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");
        var sourceTileDir = Path.Combine(tileDir, _currentSettings.MapSource);
        MapStatusText.Text = Directory.Exists(sourceTileDir) && Directory.EnumerateFiles(sourceTileDir, "*.png", SearchOption.AllDirectories).Any()
            ? "" : Loc("StrNoTiles");
    }

    private void MapSourceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MapSourceComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
            return;

        var newSource = item.Tag as string ?? "osm";
        if (newSource == _currentSettings.MapSource)
            return;  // Keine Änderung

        // Update URL TextBoxes to show the URLs for the selected map source
        TileServerUrlTextBox.Text = newSource switch
        {
            "osm" => _currentSettings.OSMTileUrl,
            "osmtopo" => _currentSettings.OSMTopoTileUrl,
            "osmdark" => _currentSettings.OSMDarkTileUrl,
            _ => _currentSettings.OSMTileUrl
        };
        VectorStyleUrlTextBox.Text = newSource switch
        {
            "osmtopo" => _currentSettings.VectorStyleTopoUrl,
            "osmdark" => _currentSettings.VectorStyleDarkUrl,
            _ => _currentSettings.VectorStyleOsmUrl
        };

        // Settings aktualisieren
        _currentSettings = _currentSettings with { MapSource = newSource };
        Services.SettingsService.Save(_currentSettings);
        Services.Logger.WriteLine($"Map source changed to: {newSource}");

        // Karte neu laden mit neuer Quelle
        InitializeMap();
        UpdateMapTileStatus();
    }

    private void MapModeRadio_Changed(object sender, RoutedEventArgs e)
    {
        // Guard: RadioButton fires Checked during initial binding before _currentSettings is fully populated
        if (_currentSettings is null) return;

        var mode = MapModeOnlineOsmRadio?.IsChecked    == true ? "online-osm"
                 : MapModeOnlineOwnRadio?.IsChecked    == true ? "online-own"
                 : MapModeOnlineCustomRadio?.IsChecked == true ? "online-custom"
                 : "offline";

        if (mode == _currentSettings.MapMode) return;

        // Custom mode must not point at public OSM/OpenTopo servers (tile usage policy)
        if (mode == "online-custom" &&
            (Services.TileDownloaderService.IsPublicTileServer(_currentSettings.OSMTileUrl) ||
             Services.TileDownloaderService.IsPublicTileServer(_currentSettings.OSMTopoTileUrl) ||
             Services.TileDownloaderService.IsPublicTileServer(_currentSettings.OSMDarkTileUrl)))
        {
            MessageBox.Show(Loc("StrCustomTileServerPublicBlocked"), Loc("StrMapModeOnlineCustom"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            // Revert the radio selection to the previous mode
            MapModeOfflineRadio.IsChecked      = _currentSettings.MapMode is not ("online-own" or "online-osm");
            MapModeOnlineOwnRadio.IsChecked    = _currentSettings.MapMode == "online-own";
            MapModeOnlineOsmRadio.IsChecked    = _currentSettings.MapMode == "online-osm";
            MapModeOnlineCustomRadio.IsChecked = false;
            return;
        }

        // OSM mode forces standard map
        var newSource = (mode == "online-osm") ? "osm" : _currentSettings.MapSource;
        if (newSource != _currentSettings.MapSource)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in MapSourceComboBox.Items)
            {
                if ((item.Tag as string) == "osm")
                {
                    MapSourceComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        _currentSettings = _currentSettings with { MapMode = mode, MapSource = newSource };
        Services.SettingsService.Save(_currentSettings);
        Services.Logger.WriteLine($"Map mode changed to: {mode}");

        ApplyMapModeUi(mode);
        InitializeMap();
        UpdateMapTileStatus();
    }

    private void ApplyMapModeUi(string mode)
    {
        if (OsmWarnBorder is null) return; // Called before XAML is loaded

        var isOsm     = mode == "online-osm";
        var isOffline = mode is not ("online-own" or "online-osm" or "online-custom");
        var isVector  = _currentSettings?.MapRenderMode == "vector";

        OsmWarnBorder.Visibility        = isOsm     ? Visibility.Visible   : Visibility.Collapsed;
        MapSourceLockedHint.Visibility  = isOsm && !isVector ? Visibility.Visible : Visibility.Collapsed;
        TileServerPanel.Visibility      = !isOsm && !isVector ? Visibility.Visible : Visibility.Collapsed;
        VectorStylePanel.Visibility     = isVector && mode == "online-custom" ? Visibility.Visible : Visibility.Collapsed;
        // Vector offline packages get their own downloader in a later step
        TileDownloadPanel.Visibility    = isOffline && !isVector ? Visibility.Visible : Visibility.Collapsed;
        VectorPreviewHintBorder.Visibility = isVector ? Visibility.Visible : Visibility.Collapsed;
        VectorOsmFallbackHint.Visibility   = isVector && isOsm ? Visibility.Visible : Visibility.Collapsed;
        VectorOverlaysTitle.Visibility  = isVector ? Visibility.Visible : Visibility.Collapsed;
        VectorOverlaysHint.Visibility   = isVector ? Visibility.Visible : Visibility.Collapsed;
        VectorOverlaysPanel.Visibility  = isVector ? Visibility.Visible : Visibility.Collapsed;
        VectorDownloadPanel.Visibility  = isVector && !isOsm ? Visibility.Visible : Visibility.Collapsed;
        MapSourceComboBox.IsEnabled     = !isOsm;
    }

    private void MapRenderModeRadio_Changed(object sender, RoutedEventArgs e)
    {
        // Guard: RadioButton fires Checked during initial binding before settings are loaded
        if (_currentSettings is null) return;

        var mode = MapRenderVectorRadio?.IsChecked == true ? "vector" : "raster";
        if (mode == _currentSettings.MapRenderMode) return;

        _currentSettings = _currentSettings with { MapRenderMode = mode };
        Services.SettingsService.Save(_currentSettings);
        Services.Logger.WriteLine($"Map render mode changed to: {mode}");

        ApplyMapModeUi(_currentSettings.MapMode);
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

    private void TDeckMapBtn_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new TDeckWizardWindow { Owner = this };
        wizard.ShowDialog();
    }

    private void TDeckExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var exportWin = new TDeckExportWindow { Owner = this };
        exportWin.ShowDialog();
    }

    private void MapZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (UseVectorMap) { ExecVectorScript("zoomIn()"); return; }
        if (_map != null)
        {
            var res = _map.Navigator.Viewport.Resolution;
            _map.Navigator.ZoomTo(res / 2);
            MapControl.Refresh();
        }
    }

    private void MapZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (UseVectorMap) { ExecVectorScript("zoomOut()"); return; }
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

    // LocalFileTileProvider moved to Services/LocalFileTileProvider.cs

    #endregion
}
