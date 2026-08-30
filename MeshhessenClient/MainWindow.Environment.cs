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

namespace MeshhessenClient;

public partial class MainWindow
{
    // Raster info-box layer (parallel to _nodeLayer; created in InitializeMap).
    private MemoryLayer? _envLayer;
    private readonly List<IFeature> _envFeatures = new();

    private readonly HashSet<uint> _envDisabledNodes = new();
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
            ClearEnvVector();
            return;
        }

        EnsureEnvControlsBuilt();
        if (EnvHeatmapRasterHint != null)
            EnvHeatmapRasterHint.Visibility = UseVectorMap ? Visibility.Collapsed : Visibility.Visible;
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
        EnvBoxesCheck.IsChecked   = _currentSettings.EnvShowBoxes;
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

    private void EnvBoxesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_envLoading) return;
        PersistEnv(s => s with { EnvShowBoxes = EnvBoxesCheck.IsChecked == true });
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

        var active = contributing.Where(c => !_envDisabledNodes.Contains(c.r.NodeId)).ToList();

        if (_currentSettings.EnvShowBoxes) PushEnvBoxes(active);
        else { ClearEnvRasterLayer(); PushEnvBoxesToVector("[]"); }

        if (_currentSettings.EnvShowHeatmap && UseVectorMap)
            PushEnvSurface(active.Select(c => c.n).ToList());
        else
            PushEnvSurfaceToVector("null");
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

    private void PushEnvBoxes(List<(TelemetryDatabaseService.EnvReading r, NodeInfo n)> items)
    {
        bool dark = ModernWpf.ThemeManager.Current.ActualApplicationTheme == ModernWpf.ApplicationTheme.Dark;

        if (UseVectorMap)
        {
            ClearEnvRasterLayer();
            var arr = items.Select(it => new
            {
                lon   = it.n.Longitude!.Value,
                lat   = it.n.Latitude!.Value,
                title = EnvNodeTitle(it.n),
                lines = BuildEnvLines(it.r),
                time  = it.r.Timestamp.ToString("dd.MM. HH:mm"),
                dark
            });
            PushEnvBoxesToVector(JsonSerializer.Serialize(arr));
        }
        else
        {
            PushEnvBoxesToVector("[]");   // clear vector side if it was active before
            BuildEnvRasterBoxes(items, dark);
        }
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
                Font = new Mapsui.Styles.Font { FontFamily = "Segoe UI Emoji", Size = 11 },
                ForeColor = Mapsui.Styles.Color.Black,
                BackColor = boxFill,
                // Themed contrast outline around the text (white at night, black by day).
                Halo = new Mapsui.Styles.Pen(dark ? Mapsui.Styles.Color.White : Mapsui.Styles.Color.Black, 1),
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Bottom,
                Offset = new Offset(0, 22)
            });
            _envFeatures.Add(feature);
        }
        if (_envLayer != null) { _envLayer.Features = _envFeatures; _envLayer.DataHasChanged(); }
        MapControl?.Refresh();
    }

    // ── Interpolated value field / heatmap (vector map only) ──────────────────

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
                                  $"grid={(grid == null ? "none" : $"{grid.Nx}x{grid.Ny}")} vector={UseVectorMap}");
        if (grid == null) { PushEnvSurfaceToVector("null"); return; }

        var payload = new
        {
            lon0 = grid.Lon0, lat0 = grid.Lat0, dlon = grid.DLon, dlat = grid.DLat,
            nx = grid.Nx, ny = grid.Ny,
            v = grid.Values.Select(x => double.IsNaN(x) ? (double?)null : Math.Round(x, 2)).ToArray(),
            a = grid.Alpha.Select(x => Math.Round(x, 3)).ToArray(),
            stops = EnvironmentMetricInfo.ColorStops(metric),
            legend = new { label = Loc(metric.LabelKey), unit = metric.Unit, min = metric.HeatMin, max = metric.HeatMax }
        };
        PushEnvSurfaceToVector(JsonSerializer.Serialize(payload));
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
        PushEnvBoxesToVector("[]");
        PushEnvSurfaceToVector("null");
    }

    private void PushEnvBoxesToVector(string json)   { if (UseVectorMap) ExecVectorScript($"setEnvBoxes({json})"); }
    private void PushEnvSurfaceToVector(string json) { if (UseVectorMap) ExecVectorScript($"setEnvSurface({json})"); }
}
