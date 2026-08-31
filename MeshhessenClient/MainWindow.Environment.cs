// Umweltdaten auf der Karte: Messwert-Boxen (Raster + Vektor), Heatmap (Vektor),
// Metrik-Auswahl und Node-Ausschluss. Partial class von MainWindow.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MeshhessenClient.Models;
using MeshhessenClient.Services;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Projections;
using Mapsui.Extensions;
using Mapsui.Nts;
using NetTopologySuite.Geometries;

namespace MeshhessenClient;

public partial class MainWindow
{
    // Raster info-box layer (parallel to _nodeLayer; created in InitializeMap).
    private MemoryLayer? _envLayer;
    private readonly List<IFeature> _envFeatures = new();

    // Raster heatmap: the interpolated field as a georeferenced image + the contour/border lines.
    private MemoryLayer? _envFillLayer;
    private List<IFeature> _envFillFeatures = new();
    private MemoryLayer? _envLineLayer;
    private readonly List<IFeature> _envLineFeatures = new();

    private readonly HashSet<uint> _envDisabledNodes = new();
    private readonly HashSet<uint> _envNodeIds = new();   // nodes that report env data (→ green marker)

    // Raster hover-mode boxes: data per node + which one is currently shown.
    private readonly Dictionary<uint, (TelemetryDatabaseService.EnvReading r, NodeInfo n)> _envRasterBoxData = new();
    private string _envRasterMode = "always";
    private uint _envHoverNodeId;
    private bool _envControlsBuilt;
    private bool _envLoading;                    // guards control handlers during programmatic updates
    private string _envNodeSig = "";             // signature of the current exclusion-list node set
    private System.Windows.Threading.DispatcherTimer? _envRefreshTimer;

    private const int EnvHeatmapDays = 2;        // history window feeding the time-weighted field
    private const double EnvInfluenceKm = 30.0;  // per-sensor reach for the interpolated field

    // ── UI wiring ─────────────────────────────────────────────────────────────

    /// <summary>Applies the feature's on/off state to the map toolbar: shows/hides the
    /// 🌡️ button, the raster heatmap hint, and (re)renders boxes + heatmap. Called at
    /// map init and after the settings dialog is saved.</summary>
    public void ApplyEnvironmentUi()
    {
        bool on = _currentSettings.ShowEnvironmentData;
        if (EnvBtn != null) EnvBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        if (!on)
        {
            if (EnvPopup != null) EnvPopup.IsOpen = false;
            ClearEnvRasterLayer();
            ClearEnvRasterSurface();
            ClearEnvVector();
            return;
        }

        EnsureEnvControlsBuilt();
        if (EnvHeatmapRasterHint != null)
            EnvHeatmapRasterHint.Visibility = Visibility.Collapsed;   // heatmap now works on the raster map too
        RefreshEnvironmentData();
    }

    private void EnsureEnvControlsBuilt()
    {
        if (!_envControlsBuilt)
        {
            _envLoading = true;
            EnvMetricCombo.Items.Clear();
            foreach (var m in EnvironmentMetricInfo.All)
            {
                var item = new ComboBoxItem { Tag = m.Key };
                item.SetResourceReference(ContentControl.ContentProperty, m.LabelKey);
                EnvMetricCombo.Items.Add(item);
            }
            _envControlsBuilt = true;
            _envLoading = false;
        }
        SyncEnvControlStates();
    }

    private void SyncEnvControlStates()
    {
        _envLoading = true;
        ComboBoxItem? boxSel = null;
        foreach (ComboBoxItem it in EnvBoxModeCombo.Items)
            if ((it.Tag as string) == _currentSettings.EnvBoxMode) { boxSel = it; break; }
        EnvBoxModeCombo.SelectedItem = boxSel ?? (EnvBoxModeCombo.Items.Count > 1 ? EnvBoxModeCombo.Items[1] : null);
        EnvHeatmapCheck.IsChecked = _currentSettings.EnvShowHeatmap;

        var key = _currentSettings.EnvMetric;
        ComboBoxItem? sel = null;
        foreach (ComboBoxItem it in EnvMetricCombo.Items)
            if ((it.Tag as string) == key) { sel = it; break; }
        EnvMetricCombo.SelectedItem = sel ?? (EnvMetricCombo.Items.Count > 0 ? EnvMetricCombo.Items[0] : null);

        _envDisabledNodes.Clear();
        foreach (var s in _currentSettings.EnvDisabledNodes.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (uint.TryParse(s.Trim(), out var id)) _envDisabledNodes.Add(id);
        _envLoading = false;
    }

    private void PersistEnv(Func<AppSettings, AppSettings> mutate)
    {
        _currentSettings = mutate(_currentSettings);
        SettingsService.Save(_currentSettings);
    }

    private void EnvBtn_Click(object sender, RoutedEventArgs e)
    {
        EnvPopup.IsOpen = !EnvPopup.IsOpen;
        if (EnvPopup.IsOpen) RefreshEnvironmentData();
    }

    private void EnvBoxModeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_envLoading) return;
        if (EnvBoxModeCombo.SelectedItem is ComboBoxItem it && it.Tag is string mode)
            PersistEnv(s => s with { EnvBoxMode = mode });
        RefreshEnvironmentData();
    }

    private void EnvHeatmapCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_envLoading) return;
        PersistEnv(s => s with { EnvShowHeatmap = EnvHeatmapCheck.IsChecked == true });
        RefreshEnvironmentData();
    }

    private void EnvMetricCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_envLoading) return;
        if (EnvMetricCombo.SelectedItem is ComboBoxItem it && it.Tag is string key)
            PersistEnv(s => s with { EnvMetric = key });
        RefreshEnvironmentData();
    }

    private void EnvNodeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_envLoading) return;
        if (sender is CheckBox cb && cb.Tag is uint id)
        {
            if (cb.IsChecked == true) _envDisabledNodes.Remove(id);
            else _envDisabledNodes.Add(id);
            var csv = string.Join(",", _envDisabledNodes);
            PersistEnv(s => s with { EnvDisabledNodes = csv });
            RefreshEnvironmentData();
        }
    }

    /// <summary>Debounced refresh; called whenever node/telemetry data changes.</summary>
    private void ScheduleEnvRefresh()
    {
        if (!_currentSettings.ShowEnvironmentData) return;
        if (_envRefreshTimer == null)
        {
            _envRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _envRefreshTimer.Tick += (_, _) => { _envRefreshTimer!.Stop(); RefreshEnvironmentData(); };
        }
        _envRefreshTimer.Stop();
        _envRefreshTimer.Start();
    }

    // ── Core refresh ──────────────────────────────────────────────────────────

    private void RefreshEnvironmentData()
    {
        if (!_currentSettings.ShowEnvironmentData || _db == null)
        {
            ClearEnvRasterLayer();
            ClearEnvVector();
            return;
        }

        List<TelemetryDatabaseService.EnvReading> readings;
        try { readings = _db.GetLatestEnvironmentPerNode(); }
        catch (Exception ex) { Services.Logger.WriteLine($"[Env] query error: {ex.Message}"); return; }

        // Positioned nodes only (a value box / heat point needs a location).
        var nodeById = new Dictionary<uint, NodeInfo>();
        foreach (var n in _allNodes)
            if (n.Latitude.HasValue && n.Longitude.HasValue) nodeById[n.NodeId] = n;

        var contributing = new List<(TelemetryDatabaseService.EnvReading r, NodeInfo n)>();
        foreach (var r in readings)
            if (nodeById.TryGetValue(r.NodeId, out var n)) contributing.Add((r, n));

        // Node exclusion list — rebuild only when the contributing node set changed.
        var sig = string.Join(",", contributing.Select(c => c.r.NodeId).OrderBy(x => x));
        if (sig != _envNodeSig)
        {
            _envNodeSig = sig;
            BuildEnvNodeList(contributing.Select(c => c.n).ToList());
        }

        // Track which nodes report env data (drives the green marker); re-render pins on change.
        var envIds = new HashSet<uint>(contributing.Select(c => c.r.NodeId));
        if (!envIds.SetEquals(_envNodeIds))
        {
            _envNodeIds.Clear();
            foreach (var id in envIds) _envNodeIds.Add(id);
            ScheduleNodePinPushToVectorMap();
            if (!UseVectorMap) foreach (var (_, n) in contributing) UpdateNodePin(n);
        }

        var active = contributing.Where(c => !_envDisabledNodes.Contains(c.r.NodeId)).ToList();

        var mode = _currentSettings.EnvBoxMode;
        if (mode == "off")
        {
            _envRasterMode = "off"; _envRasterBoxData.Clear(); _envHoverNodeId = 0;
            ClearEnvRasterLayer();
            PushEnvBoxesToVector("[]", "off");
        }
        else PushEnvBoxes(active, mode);

        if (_currentSettings.EnvShowHeatmap)
            PushEnvSurface(active.Select(c => c.n).ToList());
        else
            ClearEnvSurfaceBoth();
    }

    // ── Info boxes ────────────────────────────────────────────────────────────

    private static string EnvNodeTitle(NodeInfo n) =>
        string.IsNullOrEmpty(n.ShortName) ? n.Id : n.ShortName;

    private List<string> BuildEnvLines(TelemetryDatabaseService.EnvReading r)
    {
        var lines = new List<string>();
        foreach (var m in EnvironmentMetricInfo.All)
            if (r.Values.TryGetValue(m.Key, out var v))
                lines.Add($"{Loc(m.LabelKey)}: {EnvironmentMetricInfo.Format(m.Key, v)}");
        return lines;
    }

    private void PushEnvBoxes(List<(TelemetryDatabaseService.EnvReading r, NodeInfo n)> items, string mode)
    {
        bool dark = ModernWpf.ThemeManager.Current.ActualApplicationTheme == ModernWpf.ApplicationTheme.Dark;

        if (UseVectorMap)
        {
            ClearEnvRasterLayer();
            var arr = items.Select(it => new
            {
                id    = it.n.NodeId,
                lon   = it.n.Longitude!.Value,
                lat   = it.n.Latitude!.Value,
                title = EnvNodeTitle(it.n),
                lines = BuildEnvLines(it.r),
                time  = it.r.Timestamp.ToString("dd.MM. HH:mm"),
                dark
            });
            PushEnvBoxesToVector(JsonSerializer.Serialize(arr), mode);
        }
        else
        {
            PushEnvBoxesToVector("[]", "off");   // clear vector side if it was active before
            _envRasterMode = mode;
            _envRasterBoxData.Clear();
            foreach (var it in items) _envRasterBoxData[it.n.NodeId] = it;

            if (mode == "hover")
            {
                // Show only the box of the node currently hovered (if still present).
                if (_envHoverNodeId != 0 && _envRasterBoxData.TryGetValue(_envHoverNodeId, out var hov))
                    BuildEnvRasterBoxes(new List<(TelemetryDatabaseService.EnvReading, NodeInfo)> { hov }, dark);
                else
                    ClearEnvRasterLayer();
            }
            else
            {
                BuildEnvRasterBoxes(items, dark);
            }
        }
    }

    /// <summary>Raster-map hover: shows only the hovered node's value box (mode "hover").</summary>
    private void MapControl_EnvHover(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (UseVectorMap || !_currentSettings.ShowEnvironmentData || _envRasterMode != "hover") return;
        if (MapControl.Map == null || _envRasterBoxData.Count == 0) return;

        var screenPos = e.GetPosition(MapControl);
        uint hit = 0; double minDist = 22;
        foreach (var kv in _envRasterBoxData)
        {
            if (!_nodePinPositions.TryGetValue(kv.Key, out var pinWorld)) continue;
            var s = MapControl.Map.Navigator.Viewport.WorldToScreen(pinWorld);
            var dist = Math.Sqrt(Math.Pow(screenPos.X - s.X, 2) + Math.Pow(screenPos.Y - s.Y, 2));
            if (dist < minDist) { minDist = dist; hit = kv.Key; }
        }

        if (hit == _envHoverNodeId) return;   // unchanged
        _envHoverNodeId = hit;
        if (hit == 0) { ClearEnvRasterLayer(); return; }

        bool dark = ModernWpf.ThemeManager.Current.ActualApplicationTheme == ModernWpf.ApplicationTheme.Dark;
        BuildEnvRasterBoxes(new List<(TelemetryDatabaseService.EnvReading, NodeInfo)> { _envRasterBoxData[hit] }, dark);
    }

    private void BuildEnvRasterBoxes(List<(TelemetryDatabaseService.EnvReading r, NodeInfo n)> items, bool dark)
    {
        _envFeatures.Clear();
        var boxFill = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(255, 250, 205, 235)); // light yellow
        foreach (var (r, n) in items)
        {
            var pos = SphericalMercator.FromLonLat(n.Longitude!.Value, n.Latitude!.Value);
            var feature = new PointFeature(new MPoint(pos.x, pos.y));

            var sb = new StringBuilder(EnvNodeTitle(n));
            foreach (var line in BuildEnvLines(r)) sb.Append('\n').Append(line);
            sb.Append('\n').Append(r.Timestamp.ToString("dd.MM. HH:mm"));

            feature.Styles.Add(new LabelStyle
            {
                Text = sb.ToString(),
                Font = new Mapsui.Styles.Font { FontFamily = "Segoe UI", Size = 11 },
                ForeColor = Mapsui.Styles.Color.Black,   // dark text on the light-yellow box = high contrast
                BackColor = boxFill,
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Bottom,
                Offset = new Offset(0, 22)
            });
            _envFeatures.Add(feature);
        }
        if (_envLayer != null) { _envLayer.Features = _envFeatures; _envLayer.DataHasChanged(); }
        MapControl?.Refresh();
    }

    // ── Interpolated value field / heatmap (vector map + raster map) ──────────

    private void PushEnvSurface(List<NodeInfo> nodes)
    {
        var metric = EnvironmentMetricInfo.ByKey(_currentSettings.EnvMetric) ?? EnvironmentMetricInfo.All[0];
        var now = DateTime.Now;

        // Each contributing node → one sensor point with its time-weighted representative value.
        var sensors = new List<EnvironmentField.Sensor>();
        foreach (var n in nodes)
        {
            var series = _db!.GetTimeSeries(new[] { n.NodeId }, metric.Key, EnvHeatmapDays);
            if (series.Count == 0) continue;

            var samples = series.Select(p => ((now - p.Timestamp).TotalHours, p.Value)).ToList();
            var rep = EnvironmentHeatmapBuilder.Representative(samples);
            if (rep is not { } r) continue;

            sensors.Add(new EnvironmentField.Sensor(
                n.Longitude!.Value, n.Latitude!.Value, r.Value, Math.Max(0.05, r.Recency)));
        }

        var grid = sensors.Count > 0 ? EnvironmentField.Build(sensors, EnvInfluenceKm) : null;
        Services.Logger.WriteLine($"[Env] surface push: metric={metric.Key} sensors={sensors.Count} " +
                                  $"disabled=[{string.Join(",", _envDisabledNodes)}] " +
                                  $"grid={(grid == null ? "none" : $"{grid.Nx}x{grid.Ny}")} vector={UseVectorMap}");
        if (grid == null) { ClearEnvSurfaceBoth(); return; }

        // Discrete colour bands so the fill colour changes exactly at the isotherm lines.
        int nBands = Math.Max(1, (int)Math.Round((metric.HeatMax - metric.HeatMin) / metric.Step));
        var img = EnvironmentRaster.Render(grid, RgbBytesAt, metric.HeatMin, metric.HeatMax, maxOpacity: 0.6, bandCount: nBands);
        if (img == null) { ClearEnvSurfaceBoth(); return; }

        // Contour lines at the interior band boundaries (5° etc.).
        var levels = new List<double>();
        for (int k = 1; k < nBands; k++) levels.Add(metric.HeatMin + k * metric.Step);
        var segs = RoundSegs(EnvironmentContours.Build(grid, levels));

        // Labels ON the isotherm lines: anchor at the actual contour crossing, value = the
        // warmer adjacent zone's average. Thinned to ~one per 0.12° cell along the lines.
        var zoneAvg = EnvironmentZones.ZoneAverages(grid, metric.HeatMin, metric.Step, nBands);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var seenLbl = new HashSet<(int, int)>();
        var labels = new List<(double lon, double lat, string t)>();
        foreach (var (lon, lat, warmerCell) in EnvironmentContours.LineLabelAnchors(grid, levels))
        {
            if (warmerCell < 0 || warmerCell >= zoneAvg.Length || double.IsNaN(zoneAvg[warmerCell])) continue;
            int gx = (int)Math.Floor(lon / 0.12), gy = (int)Math.Floor(lat / 0.12);
            if (seenLbl.Add((gx, gy)))
                labels.Add((lon, lat, zoneAvg[warmerCell].ToString("0.00", inv)));
        }
        Services.Logger.WriteLine($"[Env] line labels: {labels.Count} (contours segs={segs.Count})");

        // Outer border of the whole covered area: iso-line of the coverage (alpha) field.
        var alphaGrid = new EnvironmentField.Grid
        {
            Nx = grid.Nx, Ny = grid.Ny, Lon0 = grid.Lon0, Lat0 = grid.Lat0,
            DLon = grid.DLon, DLat = grid.DLat, Values = grid.Alpha, Alpha = grid.Alpha
        };
        var border = RoundSegs(EnvironmentContours.Build(alphaGrid, new[] { 0.15 }));

        if (UseVectorMap)
        {
            ClearEnvRasterSurface();
            var payload = new
            {
                image = img.DataUri,
                coords = new[]
                {
                    new[] { img.MinLon, img.MaxLat },   // top-left (NW)
                    new[] { img.MaxLon, img.MaxLat },   // top-right (NE)
                    new[] { img.MaxLon, img.MinLat },   // bottom-right (SE)
                    new[] { img.MinLon, img.MinLat },   // bottom-left (SW)
                },
                contours = segs,
                border = border,
                labels = labels.Select(p => new { lon = Math.Round(p.lon, 5), lat = Math.Round(p.lat, 5), t = p.t }).ToList(),
                bands = EnvironmentMetricInfo.Bands(metric),
                legend = new { label = Loc(metric.LabelKey), unit = metric.Unit, min = metric.HeatMin, max = metric.HeatMax, step = metric.Step }
            };
            PushEnvSurfaceToVector(JsonSerializer.Serialize(payload));
        }
        else
        {
            PushEnvSurfaceToVector("null");   // clear vector side if it was active before
            DrawEnvSurfaceRaster(img, segs, border, labels);
            UpdateEnvLegendWpf(metric);
        }
    }

    private void ClearEnvSurfaceBoth()
    {
        PushEnvSurfaceToVector("null");
        ClearEnvRasterSurface();
    }

    // ── Raster heatmap rendering (Mapsui) ─────────────────────────────────────

    private void DrawEnvSurfaceRaster(EnvironmentRaster.Result img, List<double[]> contours, List<double[]> border,
        List<(double lon, double lat, string t)> labels)
    {
        var min = SphericalMercator.FromLonLat(img.MinLon, img.MinLat);
        var max = SphericalMercator.FromLonLat(img.MaxLon, img.MaxLat);
        var rect = new MRect(min.x, min.y, max.x, max.y);
        var pngBytes = Convert.FromBase64String(img.DataUri.Substring(img.DataUri.IndexOf(',') + 1));

        var rf = new RasterFeature(new MRaster(pngBytes, rect));
        rf.Styles.Add(new RasterStyle());
        _envFillFeatures = new List<IFeature> { rf };
        if (_envFillLayer != null) { _envFillLayer.Features = _envFillFeatures; _envFillLayer.DataHasChanged(); }

        _envLineFeatures.Clear();
        AddEnvLineSegs(contours, new Mapsui.Styles.Color(40, 40, 40, 128), 1.0);
        AddEnvLineSegs(border,   new Mapsui.Styles.Color(30, 30, 30, 217), 1.8);

        // Isotherm value labels (weather-report style): white number on a solid red box.
        var lblFill = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(198, 40, 40, 235));
        foreach (var lp in labels)
        {
            var pos = SphericalMercator.FromLonLat(lp.lon, lp.lat);
            var f = new PointFeature(new MPoint(pos.x, pos.y));
            f.Styles.Add(new LabelStyle
            {
                Text = lp.t,
                Font = new Mapsui.Styles.Font { FontFamily = "Segoe UI", Size = 11 },
                ForeColor = Mapsui.Styles.Color.White,
                BackColor = lblFill,
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center
            });
            _envLineFeatures.Add(f);
        }
        if (_envLineLayer != null) { _envLineLayer.Features = _envLineFeatures; _envLineLayer.DataHasChanged(); }

        MapControl?.Refresh();
    }

    private void AddEnvLineSegs(List<double[]> segs, Mapsui.Styles.Color color, double width)
    {
        if (segs.Count == 0) return;
        // One MultiLineString instead of thousands of features — else each pan frame renders
        // them all individually and the raster map effectively freezes.
        var lines = new LineString[segs.Count];
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            var p1 = SphericalMercator.FromLonLat(s[0], s[1]);
            var p2 = SphericalMercator.FromLonLat(s[2], s[3]);
            lines[i] = new LineString(new[] { new Coordinate(p1.x, p1.y), new Coordinate(p2.x, p2.y) });
        }
        var gf = new GeometryFeature { Geometry = new MultiLineString(lines) };
        gf.Styles.Add(new VectorStyle { Line = new Mapsui.Styles.Pen(color, width) });
        _envLineFeatures.Add(gf);
    }

    private void ClearEnvRasterSurface()
    {
        bool changed = _envFillFeatures.Count > 0 || _envLineFeatures.Count > 0;
        _envFillFeatures = new List<IFeature>();
        if (_envFillLayer != null) { _envFillLayer.Features = _envFillFeatures; _envFillLayer.DataHasChanged(); }
        _envLineFeatures.Clear();
        if (_envLineLayer != null) { _envLineLayer.Features = _envLineFeatures; _envLineLayer.DataHasChanged(); }
        UpdateEnvLegendWpf(null);
        if (changed) MapControl?.Refresh();
    }

    /// <summary>WPF legend overlay for the raster map (the vector map has its own HTML legend).</summary>
    private void UpdateEnvLegendWpf(EnvMetric? metric)
    {
        if (EnvLegendPanel == null) return;
        if (metric == null || UseVectorMap) { EnvLegendPanel.Visibility = Visibility.Collapsed; return; }

        EnvLegendTitle.Text = Loc(metric.LabelKey) + (string.IsNullOrEmpty(metric.Unit) ? "" : $" ({metric.Unit})");
        EnvLegendRows.Children.Clear();
        var bands = EnvironmentMetricInfo.BandList(metric);
        for (int i = bands.Count - 1; i >= 0; i--)   // hottest band on top
        {
            var b = bands[i];
            var p = b.Rgb.Split(',');
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 15, Height = 11,
                Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0, 0, 0)),
                StrokeThickness = 1,
                Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(
                    byte.Parse(p[0]), byte.Parse(p[1]), byte.Parse(p[2])))
            });
            row.Children.Add(new TextBlock
            {
                Text = $" {b.Lo:0.#}–{b.Hi:0.#}",
                FontSize = 11, Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            EnvLegendRows.Children.Add(row);
        }
        EnvLegendPanel.Visibility = Visibility.Visible;
    }

    private static List<double[]> RoundSegs(List<double[]> segs) =>
        segs.Select(s => new[] { Math.Round(s[0], 5), Math.Round(s[1], 5), Math.Round(s[2], 5), Math.Round(s[3], 5) }).ToList();

    /// <summary>Gradient colour at normalised t (0..1) as RGB bytes (for the raster renderer).</summary>
    private static (byte r, byte g, byte b) RgbBytesAt(double t)
    {
        var parts = EnvironmentMetricInfo.RgbAt(t).Split(',');
        return ((byte)int.Parse(parts[0]), (byte)int.Parse(parts[1]), (byte)int.Parse(parts[2]));
    }

    // ── Node exclusion list ───────────────────────────────────────────────────

    private void BuildEnvNodeList(List<NodeInfo> nodes)
    {
        if (EnvNodesPanel == null) return;
        _envLoading = true;
        EnvNodesPanel.Children.Clear();

        if (nodes.Count == 0)
        {
            var tb = new TextBlock
            {
                FontStyle = FontStyles.Italic,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            tb.SetResourceReference(TextBlock.TextProperty, "StrEnvNoNodes");
            EnvNodesPanel.Children.Add(tb);
            _envLoading = false;
            return;
        }

        foreach (var n in nodes.OrderBy(x => string.IsNullOrEmpty(x.ShortName) ? x.Id : x.ShortName))
        {
            var cb = new CheckBox
            {
                Content = string.IsNullOrEmpty(n.ShortName) ? n.Id : $"{n.ShortName} ({n.Id})",
                Tag = n.NodeId,
                IsChecked = !_envDisabledNodes.Contains(n.NodeId),
                Margin = new Thickness(0, 1, 0, 1),
                FontSize = 12
            };
            cb.Checked += EnvNodeCheck_Changed;
            cb.Unchecked += EnvNodeCheck_Changed;
            EnvNodesPanel.Children.Add(cb);
        }
        _envLoading = false;
    }

    // ── Push helpers ──────────────────────────────────────────────────────────

    private void ClearEnvRasterLayer()
    {
        if (_envFeatures.Count == 0) return;
        _envFeatures.Clear();
        if (_envLayer != null) { _envLayer.Features = _envFeatures; _envLayer.DataHasChanged(); }
        MapControl?.Refresh();
    }

    private void ClearEnvVector()
    {
        PushEnvBoxesToVector("[]", "off");
        PushEnvSurfaceToVector("null");
    }

    private void PushEnvBoxesToVector(string json, string mode)
    {
        if (UseVectorMap) ExecVectorScript($"setEnvBoxes({json}, {JsonSerializer.Serialize(mode)})");
    }
    private void PushEnvSurfaceToVector(string json) { if (UseVectorMap) ExecVectorScript($"setEnvSurface({json})"); }
}
