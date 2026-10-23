using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using MeshhessenClient.Models;
using MeshhessenClient.Services;
using ModernWpf;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.Wpf;

namespace MeshhessenClient;

public partial class TelemetryDashboardWindow : Window
{
    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    private static readonly string StoreFile =
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dashboards.json");

    private static bool FileExists(string path) => System.IO.File.Exists(path);
    private static string ReadAllText(string path) => System.IO.File.ReadAllText(path);
    private static void WriteAllText(string path, string text) => System.IO.File.WriteAllText(path, text);

    // ── Palette (same for both themes) ────────────────────────────────────────
    private static readonly OxyColor[] Palette =
    {
        OxyColor.FromRgb( 31, 111, 235),
        OxyColor.FromRgb( 63, 185, 126),
        OxyColor.FromRgb(255, 171,  76),
        OxyColor.FromRgb(240,  76,  76),
        OxyColor.FromRgb(162, 116, 255),
        OxyColor.FromRgb(  0, 205, 218),
        OxyColor.FromRgb(255, 123,  84),
        OxyColor.FromRgb( 82, 174, 255),
    };

    private static readonly Color[] WpfPalette =
    {
        Color.FromRgb( 31, 111, 235),
        Color.FromRgb( 63, 185, 126),
        Color.FromRgb(255, 171,  76),
        Color.FromRgb(240,  76,  76),
        Color.FromRgb(162, 116, 255),
        Color.FromRgb(  0, 205, 218),
        Color.FromRgb(255, 123,  84),
        Color.FromRgb( 82, 174, 255),
    };

    // ── Theme colors (instance; re-initialized when the app theme changes live) ──
    private bool _isDark;
    private Color _bgCard, _bgPage, _bgHeader, _borderCol;
    private Color _fgMain, _fgSub, _fgMuted;
    private OxyColor _oxyText, _oxyTick, _oxyGrid, _oxyLegendBg;
    private Color[] _uiPalette = Array.Empty<Color>();  // theme-adjusted accent colors for text/UI (not charts)

    // Fullscreen (F11) state
    private bool _isFullscreen;
    private WindowState _preFsState;
    private WindowStyle _preFsStyle;
    private ResizeMode  _preFsResize;

    // ── Fields ────────────────────────────────────────────────────────────────
    private readonly TelemetryDatabaseService _db;
    private readonly Dictionary<uint, string> _nodeNames;
    private readonly Dictionary<uint, string> _nodeShortNames;
    private DashboardStore _store = new();
    private Dashboard? _current;
    private bool _suppressComboEvent;
    private readonly DispatcherTimer _refreshTimer = new();
    private int _refreshIntervalSeconds = 30;
    private readonly Dictionary<string, DispatcherTimer> _clockTimers = new();

    // Per-refresh-cycle data cache, filled on a background thread (prefetch) so widget
    // building no longer blocks the UI thread with SQLite queries. The DB service opens
    // a fresh connection per call, so background access is safe. A cache miss simply
    // falls back to a synchronous query — never wrong data, at worst the old behavior.
    private readonly Dictionary<string, object> _dataCache = new();
    private int _renderGeneration; // invalidates in-flight prefetches when a newer render starts

    private T Cached<T>(string key, Func<T> fetch)
    {
        lock (_dataCache)
            if (_dataCache.TryGetValue(key, out var hit)) return (T)hit;
        var value = fetch();
        lock (_dataCache) _dataCache[key] = value!;
        return value;
    }

    private void ClearDataCache() { lock (_dataCache) _dataCache.Clear(); }

    private List<TimeSeriesPoint> GetSeries(uint nodeId, string metric, int days) =>
        Cached($"ts|{nodeId}|{metric}|{days}", () => _db.GetTimeSeries(new[] { nodeId }, metric, days));

    // Grid-based widgets cannot render an unbounded day range — these fallbacks for
    // Days == 0 ("Gesamt") match the DB service's own defaults. Mesh health uses a
    // 7-day analysis window (a health score over "all time" would be meaningless).
    private static int HeatmapDays(DashboardWidget w)    => w.Days > 0 ? w.Days : 30;
    private static int StatelineDays(DashboardWidget w)  => w.Days > 0 ? w.Days : 14;
    private static int MeshHealthDays(DashboardWidget w) => w.Days > 0 ? w.Days : 7;

    /// <summary>Runs the data queries a widget will need, mirroring BuildWidgetContent.
    /// Called on a background thread; results land in the cache.</summary>
    private void PrefetchWidgetData(DashboardWidget w)
    {
        switch (w.Type)
        {
            case "clock":
                return;
            case "heatmap":
                Cached($"hm|{w.NodeIds.FirstOrDefault()}|{w.Metric}|{HeatmapDays(w)}",
                    () => _db.GetHeatmapData(w.NodeIds.FirstOrDefault(), w.Metric, HeatmapDays(w)));
                return;
            case "stateline":
                Cached($"sl|{w.NodeIds.FirstOrDefault()}|{StatelineDays(w)}",
                    () => _db.GetNodeActivityGrid(w.NodeIds.FirstOrDefault(), StatelineDays(w)));
                return;
            case "candlestick":
                Cached($"cs|{w.NodeIds.FirstOrDefault()}|{w.Metric}|{w.Days}",
                    () => _db.GetCandlestickData(w.NodeIds.FirstOrDefault(), w.Metric, w.Days));
                return;
            case "meshhealth":
            {
                int days = MeshHealthDays(w);
                Cached($"mhs|{days}", () => _db.GetMeshHealthScore(days));
                var ids = w.NodeIds.Count > 0 ? w.NodeIds : _nodeNames.Keys.ToList();
                foreach (var id in ids)
                    Cached($"sa|{id}|{days}", () => _db.GetSignalAnalysis(id, 6, days, _nodeNames));
                return;
            }
            case "ranking":
            case "multistat":
            {
                var ids = w.NodeIds.Count > 0 ? w.NodeIds
                        : w.Type == "multistat" ? _nodeNames.Keys.Take(8).ToList()
                        : _nodeNames.Keys.ToList();
                foreach (var id in ids) GetSeries(id, w.Metric, w.Days);
                return;
            }
            case "gauge":
            case "stat":
            case "histogram":
                GetSeries(w.NodeIds.FirstOrDefault(), w.Metric, w.Days);
                return;
            default: // line, area, bar, scatter
                foreach (var id in w.NodeIds) GetSeries(id, w.Metric, w.Days);
                return;
        }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public TelemetryDashboardWindow(
        TelemetryDatabaseService db,
        Dictionary<uint, string> nodeNames,
        Dictionary<uint, string>? nodeShortNames = null)
    {
        InitializeComponent();
        _db             = db;
        _nodeShortNames = nodeShortNames ?? new Dictionary<uint, string>();

        // Merge live node names with any nodes known from the DB (for offline use)
        var merged = new Dictionary<uint, string>(nodeNames);
        foreach (var id in db.GetKnownNodeIds())
            if (!merged.ContainsKey(id))
                merged[id] = $"!{id:x8}";
        _nodeNames = merged;

        Closing += (_, _) =>
        {
            // Stop all timers — DispatcherTimers are rooted by the dispatcher, so a
            // forgotten timer keeps the closed window (and its 30 s DB refresh) alive
            // for the rest of the app's lifetime.
            _refreshTimer.Stop();
            foreach (var t in _clockTimers.Values) t.Stop();
            _clockTimers.Clear();
            _renderGeneration++; // cancel any in-flight prefetch result
            // Static event — would otherwise root this window instance forever
            ThemeManager.Current.ActualApplicationThemeChanged -= OnAppThemeChanged;
            SaveWindowPlacement();
            SaveStore();
        };

        InitThemeColors();
        ApplyThemeToWindow();
        // Follow live Windows/app theme changes (colors above are plain fields, so the
        // window would otherwise keep the stale palette until reopened)
        ThemeManager.Current.ActualApplicationThemeChanged += OnAppThemeChanged;

        LoadStore();                // before the combo: it preselects the stored interval
        RestoreWindowPlacement();
        PopulateAutoRefreshCombo();
        // Reopen with the dashboard that was active last time
        if (_store.LastDashboard is { } lastName)
            _current = _store.Dashboards.FirstOrDefault(d => d.Name == lastName);
        RebuildCombo();

        _refreshTimer.Tick += (_, _) => OnAutoRefreshTick();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F11) { ToggleFullscreen(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Escape && _isFullscreen) { ToggleFullscreen(); e.Handled = true; }
        };

        Loaded += (_, _) =>
        {
            RenderCurrentDashboard();
            StartAutoRefreshIfEnabled();
        };
    }

    private void InitThemeColors()
    {
        _isDark = ThemeManager.Current.ActualApplicationTheme == ApplicationTheme.Dark;

        if (_isDark)
        {
            _bgCard    = Color.FromRgb( 22,  27,  34);
            _bgPage    = Color.FromRgb( 13,  17,  23);
            _bgHeader  = Color.FromRgb( 21,  26,  35);
            _borderCol = Color.FromRgb( 48,  57,  74);
            _fgMain    = Color.FromRgb(201, 209, 217);
            _fgSub     = Color.FromRgb(139, 148, 158);
            _fgMuted   = Color.FromRgb( 72,  79,  88);
        }
        else
        {
            _bgCard    = Color.FromRgb(254, 254, 254);
            _bgPage    = Color.FromRgb(244, 246, 248);
            _bgHeader  = Color.FromRgb(248, 249, 252);
            _borderCol = Color.FromRgb(216, 220, 226);
            _fgMain    = Color.FromRgb( 30,  34,  40);
            _fgSub     = Color.FromRgb( 90,  99, 110);
            _fgMuted   = Color.FromRgb(160, 166, 174);
        }

        // Lighter accent colors for dark mode so text is readable on dark backgrounds
        _uiPalette = _isDark ? new[]
        {
            Color.FromRgb( 88, 166, 255),   // blue  (#58A6FF)
            Color.FromRgb( 87, 201, 150),   // green
            Color.FromRgb(255, 199, 103),   // amber
            Color.FromRgb(248, 107, 107),   // red
            Color.FromRgb(183, 148, 255),   // purple
            Color.FromRgb( 51, 225, 235),   // cyan
            Color.FromRgb(255, 157, 120),   // orange
            Color.FromRgb(128, 196, 255),   // light blue
        } : WpfPalette;

        _oxyText     = OxyColor.FromRgb(_fgSub.R, _fgSub.G, _fgSub.B);
        _oxyTick     = OxyColor.FromRgb(_borderCol.R, _borderCol.G, _borderCol.B);
        _oxyGrid     = OxyColor.FromArgb(60, _borderCol.R, _borderCol.G, _borderCol.B);
        _oxyLegendBg = _isDark
            ? OxyColor.FromArgb(180,  13, 17, 23)
            : OxyColor.FromArgb(220, 255, 255, 255);
    }

    private void OnAppThemeChanged(ThemeManager sender, object? args) => Dispatcher.Invoke(() =>
    {
        InitThemeColors();
        ApplyThemeToWindow();
        RenderCurrentDashboard();
    });

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _preFsState  = WindowState;
            _preFsStyle  = WindowStyle;
            _preFsResize = ResizeMode;
            WindowStyle  = WindowStyle.None;
            ResizeMode   = ResizeMode.NoResize;
            WindowState  = WindowState.Normal;     // re-trigger so the taskbar is covered
            WindowState  = WindowState.Maximized;
            _isFullscreen = true;
        }
        else
        {
            WindowStyle  = _preFsStyle;
            ResizeMode   = _preFsResize;
            WindowState  = _preFsState;
            _isFullscreen = false;
        }
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void RestoreWindowPlacement()
    {
        if (!double.IsNaN(_store.WindowWidth)  && _store.WindowWidth  >= MinWidth)  Width  = _store.WindowWidth;
        if (!double.IsNaN(_store.WindowHeight) && _store.WindowHeight >= MinHeight) Height = _store.WindowHeight;
        if (!double.IsNaN(_store.WindowLeft) && !double.IsNaN(_store.WindowTop))
        {
            // Only restore a position that is still on a connected screen
            double vl = SystemParameters.VirtualScreenLeft,  vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
            if (_store.WindowLeft >= vl - 8 && _store.WindowTop >= vt - 8 &&
                _store.WindowLeft + 120 <= vl + vw && _store.WindowTop + 80 <= vt + vh)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _store.WindowLeft;
                Top  = _store.WindowTop;
            }
        }
        if (_store.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        _store.WindowMaximized = WindowState == WindowState.Maximized && !_isFullscreen;
        var b = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (b.Width > 0 && b.Height > 0 && !double.IsInfinity(b.Width))
        {
            _store.WindowWidth  = b.Width;
            _store.WindowHeight = b.Height;
            _store.WindowLeft   = b.Left;
            _store.WindowTop    = b.Top;
        }
        _store.LastDashboard = _current?.Name;
    }

    private void ApplyThemeToWindow()
    {
        // Theme-aware brushes consumed by the XAML button styles via DynamicResource
        // (their custom templates bypass ModernWPF's theme foreground — issue #47)
        Resources["DashFg"]     = new SolidColorBrush(_fgMain);
        Resources["DashFgSub"]  = new SolidColorBrush(_fgSub);
        Resources["DashBorder"] = new SolidColorBrush(_borderCol);

        Background               = new SolidColorBrush(_bgPage);
        RootPanel.Background     = new SolidColorBrush(_bgPage);
        ToolbarBorder.Background = new SolidColorBrush(_bgCard);
        ToolbarBorder.BorderBrush = new SolidColorBrush(_borderCol);
        StatusBorder.Background  = new SolidColorBrush(_bgCard);
        StatusBorder.BorderBrush = new SolidColorBrush(_borderCol);
        CanvasScroller.Background = new SolidColorBrush(_bgPage);

        var subBrush = new SolidColorBrush(_fgSub);
        DashboardLabelText.Foreground = subBrush;
        AutoRefreshLabel.Foreground   = subBrush;
        StatusText.Foreground         = subBrush;
        LastRefreshText.Foreground    = new SolidColorBrush(_fgMuted);
        EmptyStateIcon.Foreground     = new SolidColorBrush(_fgMuted);
        EmptyStateText.Foreground     = new SolidColorBrush(_fgMuted);
    }

    // ── Auto-refresh ─────────────────────────────────────────────────────────

    private void PopulateAutoRefreshCombo()
    {
        var items = new[]
        {
            (0,    Loc("StrDashboardRefreshOff")),
            (10,   "10s"),
            (30,   "30s"),
            (60,   "60s"),
            (300,  "5 min"),
        };
        foreach (var (seconds, label) in items)
            AutoRefreshCombo.Items.Add(new ComboBoxItem { Tag = seconds, Content = label });

        // Restore the persisted interval (fallback: 30 s)
        int stored = _store.RefreshSeconds;
        int idx = Array.FindIndex(items, x => x.Item1 == stored);
        AutoRefreshCombo.SelectedIndex = idx >= 0 ? idx : 2;
    }

    private void AutoRefreshCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoRefreshCombo.SelectedItem is not ComboBoxItem item) return;
        _refreshIntervalSeconds = item.Tag is int s ? s : 0;
        if (_store.RefreshSeconds != _refreshIntervalSeconds)
        {
            _store.RefreshSeconds = _refreshIntervalSeconds;
            SaveStore();
        }
        StartAutoRefreshIfEnabled();
    }

    private void StartAutoRefreshIfEnabled()
    {
        _refreshTimer.Stop();
        if (_refreshIntervalSeconds <= 0) return;
        _refreshTimer.Interval = TimeSpan.FromSeconds(_refreshIntervalSeconds);
        _refreshTimer.Start();
    }

    private async void OnAutoRefreshTick()
    {
        // Dot lights while the refresh actually runs (it used to flash before the work)
        RefreshDot.Opacity = 1.0;
        await RefreshAllCardsInPlaceAsync();
        LastRefreshText.Text = string.Format(Loc("StrDashboardLastRefreshed"),
            DateTime.Now.ToString("HH:mm:ss"));
        await Task.Delay(400);
        RefreshDot.Opacity = 0.0;
    }

    private async Task RefreshAllCardsInPlaceAsync()
    {
        if (_current == null) return;
        var widgets = WidgetPanel.Children.OfType<Grid>()
            .Select(g => g.Tag).OfType<DashboardWidget>().ToList();
        // Nothing rendered yet (e.g. initial render still prefetching) — don't bump the
        // generation, that would cancel the in-flight initial render.
        if (widgets.Count == 0) return;
        int gen = ++_renderGeneration;

        // Fresh data each cycle: clear, then prefetch off the UI thread
        ClearDataCache();
        await Task.Run(() =>
        {
            foreach (var w in widgets)
                try { PrefetchWidgetData(w); } catch { /* builder falls back to sync query */ }
        });
        if (gen != _renderGeneration) return; // a newer render/refresh superseded us

        foreach (Grid container in WidgetPanel.Children.OfType<Grid>())
        {
            if (container.Tag is not DashboardWidget w) continue;
            if (container.Children.OfType<Border>().FirstOrDefault(b => b.Tag as string == "card") is not Border card) continue;
            if (card.Child is not DockPanel dock) continue;
            var oldContent = dock.Children.OfType<UIElement>()
                .FirstOrDefault(c => (c as FrameworkElement)?.Tag as string == "widgetcontent");
            if (oldContent == null) continue;
            int idx = dock.Children.IndexOf(oldContent);
            dock.Children.Remove(oldContent);
            var newContent = BuildWidgetContent(w);
            if (newContent is FrameworkElement fe) fe.Tag = "widgetcontent";
            dock.Children.Insert(idx, newContent);

            // Restore the user's zoom/pan — without this the view snapped back to the
            // full range on every refresh cycle.
            if (_zoomedWidgets.Contains(w.Id) &&
                PlotModelOf(oldContent) is { } oldModel &&
                PlotModelOf(newContent) is { } newModel &&
                oldModel.Axes.Count == newModel.Axes.Count)
            {
                for (int a = 0; a < newModel.Axes.Count; a++)
                    if (newModel.Axes[a].Position == oldModel.Axes[a].Position)
                        newModel.Axes[a].Zoom(oldModel.Axes[a].ActualMinimum, oldModel.Axes[a].ActualMaximum);
                newModel.InvalidatePlot(false);
            }
        }

        CheckThresholdAlerts(widgets);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadStore()
    {
        try
        {
            if (FileExists(StoreFile))
                _store = JsonSerializer.Deserialize<DashboardStore>(
                    ReadAllText(StoreFile), _jsonOptions) ?? new();
        }
        catch { _store = new(); }

        // Legacy single Threshold → AlarmHigh (Grafana-style zones since this version)
        for (int i = 0; i < _store.Dashboards.Count; i++)
        {
            var d = _store.Dashboards[i];
            if (!d.Widgets.Any(w => !double.IsNaN(w.Threshold))) continue;
            _store.Dashboards[i] = d with
            {
                Widgets = d.Widgets.Select(DashboardAlarms.Migrate).ToList()
            };
        }
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented    = true,
        NumberHandling   = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private void SaveStore()
    {
        try
        {
            WriteAllText(StoreFile, JsonSerializer.Serialize(_store, _jsonOptions));
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[Dashboard] SaveStore failed: {ex.Message}");
        }
    }

    // ── Combo management ─────────────────────────────────────────────────────

    private void RebuildCombo()
    {
        _suppressComboEvent = true;
        DashboardCombo.Items.Clear();
        foreach (var d in _store.Dashboards)
            DashboardCombo.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d });
        if (_current != null)
        {
            foreach (ComboBoxItem item in DashboardCombo.Items)
                if (item.Tag is Dashboard td && td == _current) { DashboardCombo.SelectedItem = item; break; }
        }
        if (DashboardCombo.SelectedItem == null && DashboardCombo.Items.Count > 0)
        {
            DashboardCombo.SelectedIndex = 0;
            _current = _store.Dashboards.Count > 0 ? _store.Dashboards[0] : null;
        }
        _suppressComboEvent = false;
    }

    private void DashboardCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressComboEvent) return;
        if (DashboardCombo.SelectedItem is ComboBoxItem { Tag: Dashboard d })
        {
            _current = d;
            _store.LastDashboard = d.Name;
            RenderCurrentDashboard();
        }
    }

    // ── Toolbar actions ──────────────────────────────────────────────────────

    private void NewDashboard_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptString(
            Loc("StrDashboardNewTitle"),
            Loc("StrDashboardNewPrompt"),
            $"{Loc("StrDashboardDefaultName")} {_store.Dashboards.Count + 1}");
        if (name == null) return;
        var d = new Dashboard(name, new List<DashboardWidget>());
        _store.Dashboards.Add(d);
        _current = d;
        SaveStore();
        RebuildCombo();
        RenderCurrentDashboard();
    }

    private void RenameDashboard_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var name = PromptString(
            Loc("StrDashboardRenameTitle"),
            Loc("StrDashboardNewPrompt"),
            _current.Name);
        if (name == null) return;
        var idx = _store.Dashboards.IndexOf(_current);
        if (idx < 0) return;
        _current = _current with { Name = name };
        _store.Dashboards[idx] = _current;
        SaveStore();
        RebuildCombo();
    }

    private void DeleteDashboard_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (MessageBox.Show(
                string.Format(Loc("StrDashboardDeleteConfirm"), _current.Name),
                Loc("StrDashboardTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _store.Dashboards.Remove(_current);
        _current = _store.Dashboards.FirstOrDefault();
        SaveStore();
        RebuildCombo();
        RenderCurrentDashboard();
    }

    private void AddWidget_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
        {
            MessageBox.Show(Loc("StrDashboardNoDashboard"),
                Loc("StrDashboardTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new AddWidgetDialog(_nodeNames, _nodeShortNames) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var widget = dlg.Result!;
        var idx = _store.Dashboards.IndexOf(_current);
        var newWidgets = new List<DashboardWidget>(_current.Widgets) { widget };
        _current = _current with { Widgets = newWidgets };
        _store.Dashboards[idx] = _current;
        SaveStore();
        RenderCurrentDashboard();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RenderCurrentDashboard();

    // ── Telemetry polling plan (active requests for LocalStats, env data, …) ──

    private static string PollTypeLabelKey(string type) => type switch
    {
        "LocalStats"         => "StrReqLocalStats",
        "EnvironmentMetrics" => "StrReqEnvMetrics",
        "DeviceMetrics"      => "StrReqDeviceMetrics",
        "AirQualityMetrics"  => "StrReqAirQuality",
        "PowerMetrics"       => "StrReqPowerMetrics",
        _                    => type,
    };

    private void PollingPlan_Click(object sender, RoutedEventArgs e)
    {
        var svc = TelemetryPollingService.Instance;
        if (svc == null) return;

        var plan    = svc.GetPlanSnapshot();
        var entries = new List<TelemetryPollingService.PollEntry>(plan.Entries);

        var dlg = new Window
        {
            Title  = Loc("StrDashPollTitle"),
            Width  = 700,
            MinWidth = 700,
            SizeToContent = SizeToContent.Height,
            Owner  = this,
            Background = new SolidColorBrush(_bgCard),
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = Loc("StrDashPollHint"),
            Foreground = new SolidColorBrush(_fgSub),
            FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var enabledCb = new CheckBox
        {
            Content = Loc("StrDashPollEnabled"),
            IsChecked = plan.Enabled,
            Foreground = new SolidColorBrush(_fgMain),
            Margin = new Thickness(0, 0, 0, 10),
        };
        stack.Children.Add(enabledCb);

        var listPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        stack.Children.Add(listPanel);

        void RebuildList()
        {
            listPanel.Children.Clear();
            foreach (var entry in entries)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var rm = new Button { Content = "✕", Width = 26, Height = 22, Padding = new Thickness(0), FontSize = 11 };
                var captured = entry;
                rm.Click += (_, _) => { entries.Remove(captured); RebuildList(); };
                DockPanel.SetDock(rm, Dock.Right);
                row.Children.Add(rm);
                row.Children.Add(new TextBlock
                {
                    Text = $"{NodeLabel(entry.NodeId)}  —  {Loc(PollTypeLabelKey(entry.Type))}  —  " +
                           string.Format(Loc("StrDashPollEvery"), entry.IntervalMinutes),
                    Foreground = new SolidColorBrush(_fgMain),
                    FontSize = 12, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                });
                listPanel.Children.Add(row);
            }
            if (entries.Count == 0)
                listPanel.Children.Add(new TextBlock
                {
                    Text = Loc("StrDashboardNoData"),
                    Foreground = new SolidColorBrush(_fgMuted), FontSize = 11,
                });
        }
        RebuildList();

        // Add row: node + type + interval
        var addRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var nodeCombo = new ComboBox { Width = 210, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var kv in _nodeNames.OrderBy(k => k.Value))
            nodeCombo.Items.Add(new ComboBoxItem { Tag = kv.Key, Content = kv.Value });
        if (nodeCombo.Items.Count > 0) nodeCombo.SelectedIndex = 0;

        var typeCombo = new ComboBox { Width = 185, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var t in TelemetryPollingService.AllowedTypes)
            typeCombo.Items.Add(new ComboBoxItem { Tag = t, Content = Loc(PollTypeLabelKey(t)) });
        typeCombo.SelectedIndex = 0;

        var intCombo = new ComboBox { Width = 130, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var m in new[] { 30, 60, 120, 240 })
            intCombo.Items.Add(new ComboBoxItem { Tag = m, Content = string.Format(Loc("StrDashPollEvery"), m) });
        intCombo.SelectedIndex = 1;

        var addBtn = new Button { Content = Loc("StrDashPollAdd"), Padding = new Thickness(10, 4, 10, 4) };
        addBtn.Click += (_, _) =>
        {
            if (nodeCombo.SelectedItem is not ComboBoxItem ni || ni.Tag is not uint nodeId) return;
            string type = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "LocalStats";
            int interval = (intCombo.SelectedItem as ComboBoxItem)?.Tag is int m ? m : 60;
            interval = Math.Max(interval, TelemetryPollingService.MinIntervalMinutes(type));

            if (entries.Any(x => x.NodeId == nodeId && x.Type == type))
            {
                MessageBox.Show(Loc("StrDashPollErrDup"), Loc("StrDashPollTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var nodes = entries.Select(x => x.NodeId).Append(nodeId).Distinct().Count();
            if (nodes > TelemetryPollingService.MaxNodes)
            {
                MessageBox.Show(Loc("StrDashPollErrMaxNodes"), Loc("StrDashPollTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            entries.Add(new TelemetryPollingService.PollEntry
            {
                NodeId = nodeId, Type = type, IntervalMinutes = interval,
            });
            RebuildList();
        };
        DockPanel.SetDock(addBtn, Dock.Right);
        addRow.Children.Add(addBtn);
        var comboRow = new StackPanel { Orientation = Orientation.Horizontal };
        comboRow.Children.Add(nodeCombo);
        comboRow.Children.Add(typeCombo);
        comboRow.Children.Add(intCombo);
        addRow.Children.Add(comboRow);
        stack.Children.Add(addRow);

        // OK / Cancel
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok     = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc("StrCancel"), Width = 95, IsCancel = true };
        ok.Click += (_, _) =>
        {
            var err = svc.TryUpdatePlan(enabledCb.IsChecked == true, entries);
            if (err != null)
            {
                MessageBox.Show(Loc(err), Loc("StrDashPollTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dlg.DialogResult = true;
            dlg.Close();
        };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        stack.Children.Add(btnRow);

        dlg.Content = stack;
        dlg.ShowDialog();
    }

    // ── Dashboard export / import (share dashboards as JSON) ─────────────────

    private void ExportDashboard_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var safeName = string.Join("_", _current.Name.Split(System.IO.Path.GetInvalidFileNameChars(),
            StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (safeName.Length == 0) safeName = "dashboard";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title    = Loc("StrDashboardExportTip"),
            Filter   = "Dashboard JSON (*.json)|*.json",
            FileName = $"{safeName}.json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            System.IO.File.WriteAllText(dlg.FileName,
                JsonSerializer.Serialize(_current, _jsonOptions), Encoding.UTF8);
            MessageBox.Show(string.Format(Loc("StrDashboardExportDone"), dlg.FileName),
                Loc("StrDashboardTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc("StrDashboardExportFailed")} {ex.Message}",
                Loc("StrDashboardTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportDashboard_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = Loc("StrDashboardImportTip"),
            Filter = "Dashboard JSON (*.json)|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var imported = JsonSerializer.Deserialize<Dashboard>(
                System.IO.File.ReadAllText(dlg.FileName), _jsonOptions);
            if (imported == null || imported.Widgets == null)
                throw new InvalidOperationException(Loc("StrDashboardImportInvalid"));

            // Fresh widget IDs (avoid collisions with existing widgets) + unique name
            var widgets = imported.Widgets
                .Select(w => DashboardAlarms.Migrate(w) with { Id = Guid.NewGuid().ToString("N")[..8] })
                .ToList();
            string name = string.IsNullOrWhiteSpace(imported.Name) ? "Import" : imported.Name;
            string unique = name;
            int n = 2;
            while (_store.Dashboards.Any(d => d.Name == unique)) unique = $"{name} ({n++})";

            var dash = new Dashboard(unique, widgets);
            _store.Dashboards.Add(dash);
            _current = dash;
            SaveStore();
            RebuildCombo();
            RenderCurrentDashboard();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc("StrDashboardImportFailed")} {ex.Message}",
                Loc("StrDashboardTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Zone alerts (Windows toasts) ─────────────────────────────────────────

    // Last observed zone per widget+node; notify only on transitions (the first
    // observation sets the baseline silently so opening the window never spams).
    private readonly Dictionary<string, AlarmZone> _alarmZones = new();

    private void CheckThresholdAlerts(IEnumerable<DashboardWidget> widgets)
    {
        foreach (var w in widgets)
        {
            if (!w.HasAlarmLevels) continue;
            foreach (var nodeId in w.NodeIds)
            {
                List<TimeSeriesPoint> pts;
                try { pts = GetSeries(nodeId, w.Metric, w.Days); }
                catch { continue; }
                if (pts.Count == 0) continue;
                double val = pts.OrderByDescending(p => p.Timestamp).First().Value;
                var zone = DashboardAlarms.Classify(w, val);

                string key = $"{w.Id}|{nodeId}";
                if (_alarmZones.TryGetValue(key, out var wasZone) && wasZone != zone)
                {
                    double limit = zone switch
                    {
                        AlarmZone.AlarmHigh => w.AlarmHigh,
                        AlarmZone.AlarmLow  => w.AlarmLow,
                        AlarmZone.WarnHigh  => w.WarnHigh,
                        AlarmZone.WarnLow   => w.WarnLow,
                        _                   => double.NaN,
                    };
                    string msgKey = zone switch
                    {
                        AlarmZone.AlarmHigh => "StrDashToastAlarmHigh",
                        AlarmZone.AlarmLow  => "StrDashToastAlarmLow",
                        AlarmZone.WarnHigh  => "StrDashToastWarnHigh",
                        AlarmZone.WarnLow   => "StrDashToastWarnLow",
                        _                   => "StrDashToastNormal",
                    };
                    ToastService.ShowMessage(
                        w.Title,
                        string.Format(Loc(msgKey),
                            NodeLabel(nodeId), val.ToString("G4"), limit.ToString("G4")));
                }
                _alarmZones[key] = zone;
            }
        }
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private async void RenderCurrentDashboard()
    {
        foreach (var t in _clockTimers.Values) t.Stop();
        _clockTimers.Clear();

        WidgetPanel.Children.Clear();
        if (_current == null || _current.Widgets.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            StatusText.Text = Loc("StrDashboardEmpty");
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;

        // Prefetch all widget data off the UI thread, then build the (fast) UI
        int gen = ++_renderGeneration;
        var widgets = _current.Widgets.ToList();
        StatusText.Text = Loc("StrDashboardLoading");
        ClearDataCache();
        await Task.Run(() =>
        {
            foreach (var w in widgets)
                try { PrefetchWidgetData(w); } catch { /* builder falls back to sync query */ }
        });
        if (gen != _renderGeneration) return; // superseded by a newer render or window close

        foreach (var w in widgets)
            WidgetPanel.Children.Add(BuildCard(w));

        CheckThresholdAlerts(widgets);

        // Allow dropping onto empty panel space → move to end
        WidgetPanel.AllowDrop = true;
        WidgetPanel.Drop -= WidgetPanel_Drop;
        WidgetPanel.Drop += WidgetPanel_Drop;
        WidgetPanel.DragOver -= WidgetPanel_DragOver;
        WidgetPanel.DragOver += WidgetPanel_DragOver;

        UpdateStatus();
        LastRefreshText.Text = string.Format(Loc("StrDashboardLastRefreshed"),
            DateTime.Now.ToString("HH:mm:ss"));
    }

    private void WidgetPanel_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("widget_id")) { e.Effects = DragDropEffects.Move; e.Handled = true; }
    }

    private void WidgetPanel_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("widget_id") || _current == null) return;
        string srcId = (string)e.Data.GetData("widget_id")!;
        // Move dragged widget to the end if it was dropped on panel background
        var srcIdx = _current.Widgets.FindIndex(x => x.Id == srcId);
        if (srcIdx < 0 || srcIdx == _current.Widgets.Count - 1) return;
        var newWidgets = new List<DashboardWidget>(_current.Widgets);
        var item = newWidgets[srcIdx];
        newWidgets.RemoveAt(srcIdx);
        newWidgets.Add(item);
        var dashIdx = _store.Dashboards.IndexOf(_current);
        _current = _current with { Widgets = newWidgets };
        _store.Dashboards[dashIdx] = _current;
        SaveStore();
        RenderCurrentDashboard();
    }

    private void UpdateStatus()
    {
        var count = _current?.Widgets.Count ?? 0;
        StatusText.Text = $"{_current?.Name ?? "–"}  ·  {count} {Loc("StrDashboardWidgetCount")}";
    }

    // ── Card builder ─────────────────────────────────────────────────────────

    private FrameworkElement BuildCard(DashboardWidget w)
    {
        var container = new Grid
        {
            Width  = Math.Max(280, w.Width),
            Height = Math.Max(200, w.Height),
            Margin = new Thickness(6),
            Tag    = w,
        };

        var card = new Border
        {
            Background      = new SolidColorBrush(_bgCard),
            BorderBrush     = new SolidColorBrush(_borderCol),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            ClipToBounds    = true,
            Tag             = "card",
            Effect = new DropShadowEffect
            {
                Color      = _isDark ? Color.FromRgb(0, 0, 0) : Color.FromRgb(120, 128, 138),
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity    = _isDark ? 0.55 : 0.15,
            },
        };

        var dock = new DockPanel { LastChildFill = true };

        // Header
        var headerBorder = new Border
        {
            Background      = new SolidColorBrush(_bgHeader),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush     = new SolidColorBrush(_borderCol),
            Padding         = new Thickness(8, 6, 6, 6),
        };
        DockPanel.SetDock(headerBorder, Dock.Top);

        var header = new DockPanel();

        // Right side: edit + close buttons
        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(rightButtons, Dock.Right);

        var editBtn = new Button
        {
            Content = "✎",
            Tag     = w,
            ToolTip = Loc("StrDashboardEditWidget"),
            Style   = TryFindResource("CardIconBtn") as Style,
            Foreground = new SolidColorBrush(_fgSub),
        };
        editBtn.Click += EditWidget_Click;
        rightButtons.Children.Add(editBtn);

        var closeBtn = new Button
        {
            Content = "×",
            Tag     = w,
            ToolTip = Loc("StrDashboardRemoveWidget"),
            Style   = TryFindResource("CardCloseBtn") as Style,
            Foreground = new SolidColorBrush(_fgSub),
            FontSize   = 14,
        };
        closeBtn.Click += RemoveWidget_Click;
        rightButtons.Children.Add(closeBtn);
        header.Children.Add(rightButtons);

        // Drag handle (left)
        var dragHandle = new TextBlock
        {
            Text       = "⠿",
            FontSize   = 13,
            Foreground = new SolidColorBrush(_fgMuted),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Cursor     = System.Windows.Input.Cursors.SizeAll,
            Margin     = new Thickness(0, 0, 6, 0),
            Padding    = new Thickness(2, 0, 2, 0),
        };
        DockPanel.SetDock(dragHandle, Dock.Left);
        // Track start point; only initiate DoDragDrop after minimum system drag distance
        var dragStartPoint = new System.Windows.Point(-1, -1);
        dragHandle.PreviewMouseLeftButtonDown += (_, e) =>
            dragStartPoint = e.GetPosition(dragHandle);
        dragHandle.PreviewMouseLeftButtonUp += (_, _) =>
            dragStartPoint = new System.Windows.Point(-1, -1);
        dragHandle.MouseMove += (_, e) =>
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            if (dragStartPoint.X < 0) return;
            var pos  = e.GetPosition(dragHandle);
            bool far = Math.Abs(pos.X - dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance
                    || Math.Abs(pos.Y - dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance;
            if (!far) return;
            dragStartPoint = new System.Windows.Point(-1, -1);
            DragDrop.DoDragDrop(dragHandle, new DataObject("widget_id", w.Id), DragDropEffects.Move);
        };
        header.Children.Add(dragHandle);

        // Type color badge
        Color typeColor = TypeColor(w.Type);
        string typeIcon = TypeIcon(w.Type);

        var badge = new Border
        {
            Background      = new SolidColorBrush(Color.FromArgb(40, typeColor.R, typeColor.G, typeColor.B)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(80, typeColor.R, typeColor.G, typeColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(5, 1, 5, 1),
            Margin          = new Thickness(0, 0, 7, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text       = typeIcon,
                Foreground = new SolidColorBrush(typeColor),
                FontSize   = 10,
            },
        };
        DockPanel.SetDock(badge, Dock.Left);
        header.Children.Add(badge);

        var titleText = new TextBlock
        {
            Text      = w.Title,
            Foreground = new SolidColorBrush(_fgMain),
            FontWeight = System.Windows.FontWeights.SemiBold,
            FontSize   = 12,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        DockPanel.SetDock(titleText, Dock.Left);
        header.Children.Add(titleText);

        headerBorder.Child = header;
        // Double-click on the header = edit (quicker than aiming for the ✎ icon)
        headerBorder.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2 && w.Type != "clock") EditWidgetCore(w);
        };
        dock.Children.Add(headerBorder);

        // Time-range presets bar — only for OxyPlot chart types
        bool isChartType = w.Type is "line" or "area" or "bar" or "scatter"
                                    or "heatmap" or "histogram" or "candlestick" or "stateline";
        if (isChartType)
        {
            var presetsBar = BuildTimePresetsBar(w);
            DockPanel.SetDock(presetsBar, Dock.Top);
            dock.Children.Add(presetsBar);
        }

        // Content — tagged so RefreshAllCardsInPlace can find it
        var widgetContent = BuildWidgetContent(w);
        if (widgetContent is FrameworkElement wfe) wfe.Tag = "widgetcontent";
        dock.Children.Add(widgetContent);
        card.Child = dock;
        container.Children.Add(card);

        // Context menu: exports + duplicate
        var ctxMenu = new ContextMenu();
        var exportItem = new MenuItem { Header = Loc("StrDashboardExportCsv") };
        exportItem.Click += (_, _) => ExportWidgetCsv(w);
        ctxMenu.Items.Add(exportItem);

        var pngItem = new MenuItem { Header = Loc("StrDashboardExportPng") };
        pngItem.Click += (_, _) => ExportWidgetPng(card, w);
        ctxMenu.Items.Add(pngItem);

        var copyItem = new MenuItem { Header = Loc("StrDashboardCopyImage") };
        copyItem.Click += (_, _) => CopyWidgetImage(card);
        ctxMenu.Items.Add(copyItem);

        ctxMenu.Items.Add(new Separator());
        var dupItem = new MenuItem { Header = Loc("StrDashboardDuplicate") };
        dupItem.Click += (_, _) => DuplicateWidget(w);
        ctxMenu.Items.Add(dupItem);
        container.ContextMenu = ctxMenu;

        // Drop target for reordering — on card so drag events fire over all card content
        card.AllowDrop = true;
        card.DragEnter += (_, e) =>
        {
            if (!e.Data.GetDataPresent("widget_id")) return;
            string srcId = (string)e.Data.GetData("widget_id")!;
            if (srcId != w.Id) card.BorderBrush = new SolidColorBrush(_uiPalette[0]);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        card.DragLeave += (_, e) =>
        {
            // Only reset when cursor truly leaves the card bounds (not just crossing child boundaries)
            var pos = e.GetPosition(card);
            if (pos.X < 0 || pos.Y < 0 || pos.X > card.ActualWidth || pos.Y > card.ActualHeight)
                card.BorderBrush = new SolidColorBrush(_borderCol);
        };
        card.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent("widget_id")) { e.Effects = DragDropEffects.Move; e.Handled = true; }
        };
        card.Drop += (_, e) =>
        {
            card.BorderBrush = new SolidColorBrush(_borderCol);
            if (!e.Data.GetDataPresent("widget_id")) return;
            string srcId = (string)e.Data.GetData("widget_id")!;
            if (srcId != w.Id) ReorderWidgets(srcId, w.Id);
            e.Handled = true;
        };

        // Resize grip — 30x30 hit area with visual in bottom-right
        var grip = new Thumb
        {
            Width  = 30,
            Height = 30,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment   = System.Windows.VerticalAlignment.Bottom,
            Cursor   = System.Windows.Input.Cursors.SizeNWSE,
            Opacity  = 0.8,
            Template = BuildResizeGripTemplate(_fgSub),
        };
        string widgetId = w.Id;
        grip.DragDelta += (_, delta) =>
        {
            double newW = Math.Max(280, container.Width  + delta.HorizontalChange);
            double newH = Math.Max(200, container.Height + delta.VerticalChange);
            container.Width  = newW;
            container.Height = newH;
            SaveWidgetSizeById(widgetId, newW, newH);
        };
        container.Children.Add(grip);

        // Tooltip
        container.ToolTip = w.Type == "clock"
            ? Loc("StrWidgetTypeClock")
            : $"{w.Metric}  |  {(w.Days > 0 ? $"{w.Days}d" : Loc("StrDaysAll"))}  |  " +
              $"{string.Join(", ", w.NodeIds.Select(id => _nodeNames.TryGetValue(id, out var n) ? n : $"!{id:x8}"))}";

        return container;
    }

    private UIElement BuildWidgetContent(DashboardWidget w)
    {
        try
        {
            return w.Type switch
            {
                "gauge"       => BuildGaugeContent(w),
                "stat"        => BuildStatContent(w),
                "clock"       => BuildClockContent(w),
                "heatmap"     => BuildPlotContent(BuildHeatmapModel(w), w),
                "bar"         => BuildPlotContent(BuildBarModel(w), w),
                "area"        => BuildPlotContent(BuildAreaModel(w), w),
                "scatter"     => BuildPlotContent(BuildScatterModel(w), w),
                "histogram"   => BuildPlotContent(BuildHistogramModel(w), w),
                "candlestick" => BuildPlotContent(BuildCandlestickModel(w), w),
                "stateline"   => BuildPlotContent(BuildStatelineModel(w), w),
                "ranking"     => BuildRankingContent(w),
                "multistat"   => BuildMultiStatContent(w),
                "meshhealth"  => BuildMeshHealthContent(w),
                _             => BuildPlotContent(BuildLineModel(w), w),
            };
        }
        catch (Exception ex)
        {
            return new TextBlock
            {
                Text         = $"Error: {ex.Message}",
                Foreground   = new SolidColorBrush(Color.FromRgb(240, 76, 76)),
                Margin       = new Thickness(12),
                TextWrapping = TextWrapping.Wrap,
            };
        }
    }

    private static Color TypeColor(string type) => type switch
    {
        "line"        => Color.FromRgb( 31, 111, 235),
        "area"        => Color.FromRgb( 63, 185, 126),
        "bar"         => Color.FromRgb(255, 171,  76),
        "gauge"       => Color.FromRgb(162, 116, 255),
        "stat"        => Color.FromRgb(  0, 205, 218),
        "heatmap"     => Color.FromRgb(255, 123,  84),
        "scatter"     => Color.FromRgb( 82, 174, 255),
        "clock"       => Color.FromRgb(255, 171,  76),
        "histogram"   => Color.FromRgb( 63, 185, 126),
        "candlestick" => Color.FromRgb(240,  76,  76),
        "stateline"   => Color.FromRgb(  0, 205, 218),
        "ranking"     => Color.FromRgb( 63, 185, 126),
        "multistat"   => Color.FromRgb(  0, 205, 218),
        "meshhealth"  => Color.FromRgb(255, 171,  76),
        _             => Color.FromRgb(139, 148, 158),
    };

    private static string TypeIcon(string type) => type switch
    {
        "line"        => "〜",
        "area"        => "▲",
        "bar"         => "▮",
        "gauge"       => "◎",
        "stat"        => "⬢",
        "heatmap"     => "⣿",
        "scatter"     => "⁘",
        "clock"       => "⏱",
        "histogram"   => "▦",
        "candlestick" => "🕯",
        "stateline"   => "▬",
        "ranking"     => "⊞",
        "multistat"   => "⊟",
        "meshhealth"  => "⬤",
        _             => "▪",
    };

    private static ControlTemplate BuildResizeGripTemplate(Color dotColor)
    {
        var factory = new FrameworkElementFactory(typeof(Canvas));
        factory.SetValue(FrameworkElement.WidthProperty, 30.0);
        factory.SetValue(FrameworkElement.HeightProperty, 30.0);
        factory.SetValue(Panel.BackgroundProperty, Brushes.Transparent); // hit-testable

        // Visual dots concentrated in bottom-right 16x16 area
        foreach (var (x, y) in new[] { (22.0, 12.0), (18.0, 16.0), (22.0, 16.0), (14.0, 20.0), (18.0, 20.0), (22.0, 20.0) })
        {
            var dot = new FrameworkElementFactory(typeof(Ellipse));
            dot.SetValue(FrameworkElement.WidthProperty, 2.8);
            dot.SetValue(FrameworkElement.HeightProperty, 2.8);
            dot.SetValue(Shape.FillProperty, new SolidColorBrush(dotColor));
            dot.SetValue(Canvas.LeftProperty, x);
            dot.SetValue(Canvas.TopProperty, y);
            factory.AppendChild(dot);
        }

        return new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
    }

    /// <summary>Save by widget ID, not by reference — avoids stale-instance bug.</summary>
    private void SaveWidgetSizeById(string widgetId, double width, double height)
    {
        if (_current == null) return;
        var idx = _store.Dashboards.IndexOf(_current);
        if (idx < 0) return;
        var widgetIdx = _current.Widgets.FindIndex(x => x.Id == widgetId);
        if (widgetIdx < 0) return;
        var updated = _current.Widgets[widgetIdx] with { Width = width, Height = height };
        var newWidgets = new List<DashboardWidget>(_current.Widgets);
        newWidgets[widgetIdx] = updated;
        _current = _current with { Widgets = newWidgets };
        _store.Dashboards[idx] = _current;

        // Also update the container's Tag so refresh-in-place sees current size
        foreach (Grid g in WidgetPanel.Children.OfType<Grid>())
            if (g.Tag is DashboardWidget tw && tw.Id == widgetId)
            {
                g.Tag = updated;
                break;
            }

        SaveStore();
    }

    private void ReorderWidgets(string sourceId, string targetId)
    {
        if (_current == null) return;
        var srcIdx = _current.Widgets.FindIndex(x => x.Id == sourceId);
        var tgtIdx = _current.Widgets.FindIndex(x => x.Id == targetId);
        if (srcIdx < 0 || tgtIdx < 0 || srcIdx == tgtIdx) return;

        var newWidgets = new List<DashboardWidget>(_current.Widgets);
        var item = newWidgets[srcIdx];
        newWidgets.RemoveAt(srcIdx);
        newWidgets.Insert(tgtIdx, item);

        var dashIdx = _store.Dashboards.IndexOf(_current);
        _current = _current with { Widgets = newWidgets };
        _store.Dashboards[dashIdx] = _current;
        SaveStore();
        RenderCurrentDashboard();
    }

    // ── Plot content wrapper ─────────────────────────────────────────────────

    // Widgets whose chart the user has zoomed/panned — the in-place auto-refresh
    // restores their view instead of snapping back to the full range.
    private readonly HashSet<string> _zoomedWidgets = new();

    private FrameworkElement BuildPlotContent(PlotModel model, DashboardWidget w)
    {
        var view = new PlotView
        {
            Model      = model,
            Background = Brushes.Transparent,
            Margin     = new Thickness(2, 0, 2, 4),
        };

        foreach (var ax in model.Axes)
            ax.AxisChanged += (_, e) =>
            {
                if (e.ChangeType is AxisChangeTypes.Zoom or AxisChangeTypes.Pan)
                    _zoomedWidgets.Add(w.Id);
                else if (e.ChangeType == AxisChangeTypes.Reset)
                    _zoomedWidgets.Remove(w.Id);
            };

        // Double-click = reset zoom/pan (discoverable, unlike OxyPlot's default bindings).
        // Hint tooltip only where zooming is actually possible (time charts).
        if (model.Axes.Any(a => a.IsZoomEnabled))
        {
            view.MouseDoubleClick += (_, _) =>
            {
                model.ResetAllAxes();
                _zoomedWidgets.Remove(w.Id);
                view.InvalidatePlot(false);
            };
            view.ToolTip = Loc("StrDashboardZoomHint");
            ToolTipService.SetInitialShowDelay(view, 1500);
        }

        if (ModelHasData(model)) return view;

        // Empty chart: overlay a "no data" hint instead of a bare coordinate system
        var overlay = new Grid();
        overlay.Children.Add(view);
        overlay.Children.Add(new TextBlock
        {
            Text                = Loc("StrDashboardNoData"),
            Foreground          = new SolidColorBrush(_fgMuted),
            FontSize            = 12,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment   = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible    = false,
        });
        return overlay;
    }

    private static bool ModelHasData(PlotModel model) => model.Series.Any(s => s switch
    {
        LineSeries ls         => ls.Points.Count > 0,   // AreaSeries derives from LineSeries
        ScatterSeries sc      => sc.Points.Count > 0,
        CandleStickSeries cs  => cs.Items.Count > 0,
        RectangleBarSeries rb => rb.Items.Any(i => i.Y1 > 0),
        HeatMapSeries hm      => hm.Data is { } d && d.Cast<double>().Any(v => !double.IsNaN(v) && v != 0),
        _                     => true,
    });

    /// <summary>Finds the PlotModel inside a widget-content element (PlotView or overlay grid).</summary>
    private static PlotModel? PlotModelOf(UIElement? content) =>
        (content as PlotView ?? (content as Grid)?.Children.OfType<PlotView>().FirstOrDefault())?.Model;

    // ── Clock widget ──────────────────────────────────────────────────────────

    private FrameworkElement BuildClockContent(DashboardWidget w)
    {
        var panel = new StackPanel
        {
            VerticalAlignment   = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(16),
        };

        var localTime = new TextBlock
        {
            FontSize   = 52,
            FontWeight = System.Windows.FontWeights.Light,
            Foreground = new SolidColorBrush(_fgMain),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
        };
        var utcTime = new TextBlock
        {
            FontSize   = 22,
            Foreground = new SolidColorBrush(_fgSub),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            Margin     = new Thickness(0, 6, 0, 0),
        };
        var date = new TextBlock
        {
            FontSize   = 12,
            Foreground = new SolidColorBrush(_fgMuted),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin     = new Thickness(0, 8, 0, 0),
        };

        void Update()
        {
            var now    = DateTime.Now;
            var utcNow = DateTime.UtcNow;
            localTime.Text = now.ToString("HH:mm:ss");
            utcTime.Text   = $"UTC  {utcNow:HH:mm:ss}";
            date.Text      = now.ToString("D"); // long date in the user's culture
        }
        Update();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Update();
        timer.Start();
        // In-place refresh rebuilds this content without going through
        // RenderCurrentDashboard — stop the previous timer or it leaks (one per cycle).
        if (_clockTimers.TryGetValue(w.Id, out var oldTimer)) oldTimer.Stop();
        _clockTimers[w.Id] = timer;

        panel.Children.Add(localTime);
        panel.Children.Add(utcTime);
        panel.Children.Add(date);

        return new Border { Child = panel };
    }

    // ── Gauge ────────────────────────────────────────────────────────────────

    private FrameworkElement BuildGaugeContent(DashboardWidget w)
    {
        var nodeId = w.NodeIds.FirstOrDefault();
        var series = GetSeries(nodeId, w.Metric, w.Days); // Days==0 = all, like the charts
        var last   = series.OrderByDescending(p => p.Timestamp).FirstOrDefault();
        double val = last?.Value ?? double.NaN;

        (double min, double max, string unit) = MetricRange(w.Metric);
        double pct = double.IsNaN(val)
            ? 0
            : Math.Clamp((val - min) / (max - min), 0, 1);

        // Zone-based color when warn/alarm levels are configured (Grafana-style) —
        // the old low=red→high=green ramp was simply wrong for "high is bad" metrics
        // like channel utilization. Without levels: neutral accent.
        Color zoneGreen = Color.FromRgb( 63, 185, 126);
        Color zoneWarn  = Color.FromRgb(255, 171,  76);
        Color zoneAlarm = Color.FromRgb(240,  76,  76);
        Color arcColor;
        if (w.HasAlarmLevels)
        {
            var zone = DashboardAlarms.Classify(w, val);
            arcColor = DashboardAlarms.IsAlarm(zone) ? zoneAlarm
                     : DashboardAlarms.IsWarn(zone)  ? zoneWarn
                     : zoneGreen;
        }
        else
        {
            arcColor = _uiPalette.Length > 0 ? _uiPalette[0] : Color.FromRgb(82, 174, 255);
        }

        string nodeName = nodeId != 0 && _nodeNames.TryGetValue(nodeId, out var nn) ? nn : "–";

        var grid = new Grid { Margin = new Thickness(8, 4, 8, 6) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Canvas sized so the full arc fits
        var canvas = new Canvas
        {
            Width  = 200,
            Height = 140,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment   = System.Windows.VerticalAlignment.Center,
        };
        Grid.SetRow(canvas, 0);

        // Visual-degrees system: 0° = top, 90° = right, 180° = bottom, 270° = left
        // Gauge opens at bottom: start at 225° (lower-left), sweep 270° clockwise → end at 135° (lower-right)
        double cx = 100, cy = 70, r = 56, strokeW = 11;
        double startVisual = 225, sweep = 270;

        // Track arc (background)
        canvas.Children.Add(BuildArcPath(cx, cy, r, startVisual, sweep,
            _isDark ? Color.FromRgb(33, 38, 45) : Color.FromRgb(226, 229, 234),
            strokeW));

        // Zone ring just outside the track: green/orange/red segments show where the
        // configured warn/alarm bounds sit on the scale (Grafana gauge style).
        if (w.HasAlarmLevels)
        {
            double ringR = r + 9;
            const double ringW = 3;
            double P(double v) => Math.Clamp((v - min) / (max - min), 0, 1);
            var cuts = new List<(double from, double to, Color color)>();
            double lo = 0;
            if (!double.IsNaN(w.AlarmLow)) { cuts.Add((lo, P(w.AlarmLow), zoneAlarm)); lo = P(w.AlarmLow); }
            if (!double.IsNaN(w.WarnLow) && P(w.WarnLow) > lo) { cuts.Add((lo, P(w.WarnLow), zoneWarn)); lo = P(w.WarnLow); }
            double hi = 1;
            var tail = new List<(double from, double to, Color color)>();
            if (!double.IsNaN(w.AlarmHigh)) { tail.Add((P(w.AlarmHigh), hi, zoneAlarm)); hi = P(w.AlarmHigh); }
            if (!double.IsNaN(w.WarnHigh) && P(w.WarnHigh) < hi) { tail.Add((P(w.WarnHigh), hi, zoneWarn)); hi = P(w.WarnHigh); }
            if (hi > lo) cuts.Add((lo, hi, zoneGreen));
            cuts.AddRange(tail);
            foreach (var (from, to, color) in cuts)
                if (to - from > 0.004)
                    canvas.Children.Add(BuildArcPath(cx, cy, ringR,
                        startVisual + sweep * from, sweep * (to - from), color, ringW));
        }

        // Value arc
        if (pct > 0.002)
            canvas.Children.Add(BuildArcPath(cx, cy, r, startVisual, sweep * pct,
                arcColor, strokeW));

        // Center value
        var valText = new TextBlock
        {
            Text       = double.IsNaN(val) ? "–" : $"{val:F1}",
            FontSize   = 30,
            FontWeight = System.Windows.FontWeights.Light,
            Foreground = new SolidColorBrush(_fgMain),
            Width          = 100,
            TextAlignment  = TextAlignment.Center,
        };
        Canvas.SetLeft(valText, cx - 50);
        Canvas.SetTop(valText,  cy - 26);
        canvas.Children.Add(valText);

        var unitText = new TextBlock
        {
            Text       = unit,
            FontSize   = 11,
            Foreground = new SolidColorBrush(_fgSub),
            Width          = 60,
            TextAlignment  = TextAlignment.Center,
        };
        Canvas.SetLeft(unitText, cx - 30);
        Canvas.SetTop(unitText,  cy + 10);
        canvas.Children.Add(unitText);

        // Min / max labels at arc endpoints
        var (sx, sy) = VisualPoint(cx, cy, r, startVisual);
        var (ex, ey) = VisualPoint(cx, cy, r, startVisual + sweep);
        AddCanvasLabel(canvas, $"{min:G4}", sx - 20, sy + 6,  9, _fgMuted);
        AddCanvasLabel(canvas, $"{max:G4}", ex - 10, ey + 6,  9, _fgMuted);

        var subPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        Grid.SetRow(subPanel, 1);

        string ts = last != null ? last.Timestamp.ToLocalTime().ToString("dd.MM HH:mm") : "–";

        subPanel.Children.Add(new TextBlock
        {
            Text       = nodeName,
            FontSize   = 10,
            Foreground = new SolidColorBrush(arcColor),
            Margin     = new Thickness(0, 0, 6, 0),
        });
        subPanel.Children.Add(new TextBlock
        {
            Text       = ts,
            FontSize   = 10,
            Foreground = new SolidColorBrush(_fgMuted),
        });

        grid.Children.Add(canvas);
        grid.Children.Add(subPanel);

        return new Border { Child = grid, Padding = new Thickness(4, 4, 4, 2) };
    }

    private static void AddCanvasLabel(Canvas c, string text, double left, double top,
        double fontSize, Color color)
    {
        var tb = new TextBlock
        {
            Text       = text,
            FontSize   = fontSize,
            Foreground = new SolidColorBrush(color),
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb,  top);
        c.Children.Add(tb);
    }

    /// <summary>
    /// Convert visual degrees (0 = top, 90 = right, 180 = bottom, 270 = left) to screen point.
    /// Accounts for WPF's Y-down coordinate system.
    /// </summary>
    private static (double x, double y) VisualPoint(double cx, double cy, double r, double visualDeg)
    {
        double mathRad = (90 - visualDeg) * Math.PI / 180.0;
        return (cx + r * Math.Cos(mathRad), cy - r * Math.Sin(mathRad));
    }

    private static System.Windows.Shapes.Path BuildArcPath(double cx, double cy, double r,
        double visualStartDeg, double sweepDeg, Color color, double thickness)
    {
        var (x1, y1) = VisualPoint(cx, cy, r, visualStartDeg);
        var (x2, y2) = VisualPoint(cx, cy, r, visualStartDeg + sweepDeg);

        var geo = new PathGeometry();
        var figure = new PathFigure { StartPoint = new System.Windows.Point(x1, y1) };
        figure.Segments.Add(new ArcSegment
        {
            Point          = new System.Windows.Point(x2, y2),
            Size           = new System.Windows.Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc     = sweepDeg > 180,
        });
        geo.Figures.Add(figure);

        return new System.Windows.Shapes.Path
        {
            Data               = geo,
            Stroke             = new SolidColorBrush(color),
            StrokeThickness    = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        };
    }

    private static Color InterpolateColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    // ── Stat card ─────────────────────────────────────────────────────────────

    private FrameworkElement BuildStatContent(DashboardWidget w)
    {
        var nodeId = w.NodeIds.FirstOrDefault();
        (double min, double max, string unit) = MetricRange(w.Metric);

        var pts = GetSeries(nodeId, w.Metric, w.Days)
                     .OrderByDescending(p => p.Timestamp).ToList();

        double current  = pts.Count > 0 ? pts[0].Value : double.NaN;
        double previous = pts.Count > 1 ? pts[1].Value : double.NaN;
        double delta    = !double.IsNaN(current) && !double.IsNaN(previous)
            ? current - previous : double.NaN;

        // Age of the value: show it, and gray out stale readings (> 24 h) so a dead
        // node doesn't look perfectly healthy
        DateTime? lastTs = pts.Count > 0 ? pts[0].Timestamp : null;
        bool stale = lastTs.HasValue && (DateTime.UtcNow - lastTs.Value) > TimeSpan.FromHours(24);

        string nodeName   = nodeId != 0 && _nodeNames.TryGetValue(nodeId, out var nn) ? nn : "–";
        Color accentColor = _uiPalette[0];

        var grid = new Grid { Margin = new Thickness(12, 10, 12, 10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var chip = new Border
        {
            Background      = new SolidColorBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(80, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };
        chip.Child = new TextBlock
        {
            Text       = w.Metric.ToUpperInvariant(),
            FontSize   = 9,
            Foreground = new SolidColorBrush(accentColor),
            FontWeight = System.Windows.FontWeights.SemiBold,
        };
        Grid.SetRow(chip, 0);

        // Value color follows the configured warn/alarm zones (stale wins: a dead node
        // must not look like a healthy green one)
        Color valColor = _fgMain;
        if (!stale && w.HasAlarmLevels && !double.IsNaN(current))
        {
            var zone = DashboardAlarms.Classify(w, current);
            valColor = DashboardAlarms.IsAlarm(zone) ? Color.FromRgb(240,  76,  76)
                     : DashboardAlarms.IsWarn(zone)  ? Color.FromRgb(255, 171,  76)
                     : Color.FromRgb( 63, 185, 126);
        }
        var bigVal = new TextBlock
        {
            Text = double.IsNaN(current) ? "–" : $"{current:F1}",
            FontSize   = 48,
            FontWeight = System.Windows.FontWeights.Light,
            Foreground = new SolidColorBrush(stale ? _fgMuted : valColor),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment   = System.Windows.VerticalAlignment.Center,
        };
        Grid.SetRow(bigVal, 1);

        var unitText = new TextBlock
        {
            Text       = unit,
            FontSize   = 14,
            Foreground = new SolidColorBrush(_fgSub),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin     = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(unitText, 2);

        var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetRow(footer, 3);

        var nodeLabel = new TextBlock
        {
            Text = lastTs.HasValue
                ? $"{nodeName} · {lastTs.Value.ToLocalTime():dd.MM HH:mm}"
                : nodeName,
            FontSize   = 10,
            Foreground = new SolidColorBrush(_fgMuted),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        DockPanel.SetDock(nodeLabel, Dock.Left);

        if (!double.IsNaN(delta))
        {
            bool up = delta >= 0;
            var deltaText = new TextBlock
            {
                Text       = $"{(up ? "↑" : "↓")} {Math.Abs(delta):F2}",
                FontSize   = 11,
                Foreground = new SolidColorBrush(up
                    ? Color.FromRgb(63, 185, 126)
                    : Color.FromRgb(240,  76,  76)),
                VerticalAlignment   = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            };
            DockPanel.SetDock(deltaText, Dock.Right);
            footer.Children.Add(deltaText);
        }
        footer.Children.Add(nodeLabel);

        grid.Children.Add(chip);
        grid.Children.Add(bigVal);
        grid.Children.Add(unitText);
        grid.Children.Add(footer);

        return new Border { Child = grid };
    }

    // ── Line chart ───────────────────────────────────────────────────────────

    private PlotModel BuildLineModel(DashboardWidget w)
    {
        (double min, double max, string unit) = MetricRange(w.Metric);
        var model = MakeBaseModel();
        AddTimeAxis(model, w.Days);
        AddLinearAxis(model, min, max, unit);

        int ci = 0;
        foreach (var nodeId in w.NodeIds)
        {
            var pts = GetSeries(nodeId, w.Metric, w.Days);
            if (pts.Count == 0) continue;
            var oxy  = Palette[ci % Palette.Length];
            var line = new LineSeries
            {
                Title               = NodeLabel(nodeId),
                Color               = oxy,
                StrokeThickness     = 1.8,
                MarkerType          = MarkerType.None,
                Decimator           = Decimator.Decimate, // "Gesamt" series can hold 10k+ points
                TrackerFormatString = "{0}\n{2:dd.MM HH:mm}\n{4:F2} " + unit,
            };
            // Timestamps are stored in UTC — plot in local time like every label in the app
            var sorted = pts.OrderBy(x => x.Timestamp)
                            .Select(p => new DataPoint(DateTimeAxis.ToDouble(p.Timestamp.ToLocalTime()), p.Value))
                            .ToList();
            foreach (var dp in sorted) line.Points.Add(dp);
            model.Series.Add(line);

            if (w.ShowMovingAverage && sorted.Count > 5)
            {
                var maLine = new LineSeries
                {
                    Title           = $"MA({NodeLabel(nodeId)})",
                    Color           = OxyColor.FromAColor(180, oxy),
                    StrokeThickness = 1.5,
                    LineStyle       = LineStyle.Dash,
                    MarkerType      = MarkerType.None,
                    Decimator       = Decimator.Decimate,
                    TrackerFormatString = "{0} MA\n{2:dd.MM HH:mm}\n{4:F2} " + unit,
                };
                foreach (var dp in MovingAverage(sorted, MaWindow(sorted))) maLine.Points.Add(dp);
                model.Series.Add(maLine);
            }
            ci++;
        }
        AddNightBands(model);
        AddAlarmAnnotations(model, w);
        FinalizeChart(model);
        return model;
    }

    // ── Area chart ────────────────────────────────────────────────────────────

    private PlotModel BuildAreaModel(DashboardWidget w)
    {
        (double min, double max, string unit) = MetricRange(w.Metric);
        var model = MakeBaseModel();
        AddTimeAxis(model, w.Days);
        AddLinearAxis(model, min, max, unit);

        int ci = 0;
        foreach (var nodeId in w.NodeIds)
        {
            var pts = GetSeries(nodeId, w.Metric, w.Days);
            if (pts.Count == 0) continue;
            var oxy    = Palette[ci % Palette.Length];
            var series = new AreaSeries
            {
                Title           = NodeLabel(nodeId),
                Color           = oxy,
                Fill            = OxyColor.FromAColor(55, oxy),
                ConstantY2      = min,
                StrokeThickness = 1.5,
                MarkerType      = MarkerType.None,
                Decimator       = Decimator.Decimate,
                TrackerFormatString = "{0}\n{2:dd.MM HH:mm}\n{4:F2} " + unit,
            };
            var sorted = pts.OrderBy(x => x.Timestamp)
                            .Select(p => new DataPoint(DateTimeAxis.ToDouble(p.Timestamp.ToLocalTime()), p.Value))
                            .ToList();
            foreach (var dp in sorted) series.Points.Add(dp);
            model.Series.Add(series);

            if (w.ShowMovingAverage && sorted.Count > 5)
            {
                var maLine = new LineSeries
                {
                    Title           = $"MA({NodeLabel(nodeId)})",
                    Color           = OxyColor.FromAColor(200, oxy),
                    StrokeThickness = 1.5,
                    LineStyle       = LineStyle.Dash,
                    MarkerType      = MarkerType.None,
                    Decimator       = Decimator.Decimate,
                    TrackerFormatString = "{0} MA\n{2:dd.MM HH:mm}\n{4:F2} " + unit,
                };
                foreach (var dp in MovingAverage(sorted, MaWindow(sorted))) maLine.Points.Add(dp);
                model.Series.Add(maLine);
            }
            ci++;
        }
        AddNightBands(model);
        AddAlarmAnnotations(model, w);
        FinalizeChart(model);
        return model;
    }

    private static readonly OxyColor AlarmColor = OxyColor.FromRgb(240,  76,  76);
    private static readonly OxyColor WarnColor  = OxyColor.FromRgb(255, 171,  76);

    /// <summary>Grafana-style zone annotations: dashed warn (orange) / alarm (red) lines,
    /// plus a faint red fill beyond the alarm bounds. Horizontal for time charts; for the
    /// bar chart (values on X) the lines are vertical instead.</summary>
    private static void AddAlarmAnnotations(PlotModel model, DashboardWidget w, bool vertical = false)
    {
        void AddLine(double y, OxyColor color, string prefix)
        {
            if (double.IsNaN(y)) return;
            model.Annotations.Add(new LineAnnotation
            {
                Type            = vertical ? LineAnnotationType.Vertical : LineAnnotationType.Horizontal,
                Y               = vertical ? 0 : y,
                X               = vertical ? y : 0,
                Color           = OxyColor.FromAColor(200, color),
                StrokeThickness = 1.5,
                LineStyle       = LineStyle.Dash,
                Text            = $"{prefix} {y:G4}",
                TextColor       = OxyColor.FromAColor(220, color),
                FontSize        = 9,
            });
        }
        void AddZone(double from, double to)
        {
            var rect = new RectangleAnnotation
            {
                Fill  = OxyColor.FromAColor(14, AlarmColor),
                Layer = AnnotationLayer.BelowSeries,
            };
            if (vertical) { rect.MinimumX = from; rect.MaximumX = to; }
            else          { rect.MinimumY = from; rect.MaximumY = to; }
            model.Annotations.Add(rect);
        }

        AddLine(w.WarnLow,   WarnColor,  "⚠");
        AddLine(w.WarnHigh,  WarnColor,  "⚠");
        AddLine(w.AlarmLow,  AlarmColor, "🚨");
        AddLine(w.AlarmHigh, AlarmColor, "🚨");
        if (!double.IsNaN(w.AlarmHigh)) AddZone(w.AlarmHigh,  w.AlarmHigh + 1e6);
        if (!double.IsNaN(w.AlarmLow))  AddZone(w.AlarmLow - 1e6, w.AlarmLow);
    }

    /// <summary>Trailing moving average over a TIME window (previously 12 data points,
    /// which smoothed over hours or days depending on how irregular the sampling was).</summary>
    private static IEnumerable<DataPoint> MovingAverage(IList<DataPoint> points, TimeSpan window)
    {
        double windowX = window.TotalDays; // DataPoint.X is an OADate (days)
        int start = 0;
        double sum = 0;
        for (int i = 0; i < points.Count; i++)
        {
            sum += points[i].Y;
            while (points[i].X - points[start].X > windowX) { sum -= points[start].Y; start++; }
            yield return new DataPoint(points[i].X, sum / (i - start + 1));
        }
    }

    /// <summary>MA window: 1/24 of the visible span, clamped to [30 min, 24 h].</summary>
    private static TimeSpan MaWindow(IList<DataPoint> sorted)
    {
        double spanDays = sorted[^1].X - sorted[0].X;
        var w = TimeSpan.FromDays(Math.Max(spanDays, 0) / 24.0);
        if (w < TimeSpan.FromMinutes(30)) w = TimeSpan.FromMinutes(30);
        if (w > TimeSpan.FromHours(24))   w = TimeSpan.FromHours(24);
        return w;
    }

    // ── Bar chart ─────────────────────────────────────────────────────────────

    private PlotModel BuildBarModel(DashboardWidget w)
    {
        (double min, double max, string unit) = MetricRange(w.Metric);
        var model = MakeBaseModel();

        var categoryAxis = new CategoryAxis
        {
            Position           = AxisPosition.Left,
            TextColor          = _oxyText,
            TicklineColor      = _oxyTick,
            MajorGridlineStyle = LineStyle.None,
            Minimum            = -0.5,
        };
        model.Axes.Add(categoryAxis);

        double baseVal = Math.Min(0, min);
        model.Axes.Add(new LinearAxis
        {
            Position           = AxisPosition.Bottom,
            Minimum            = baseVal, Maximum = max,
            Title              = unit,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = _oxyGrid,
            TextColor          = _oxyText,
            TicklineColor      = _oxyTick,
            TitleColor         = _oxyText,
        });

        int ci = 0;
        foreach (var nodeId in w.NodeIds)
        {
            var pts = GetSeries(nodeId, w.Metric, w.Days);
            double val  = pts.Count > 0
                ? pts.OrderByDescending(p => p.Timestamp).First().Value
                : double.NaN;
            string name = NodeLabel(nodeId);
            categoryAxis.Labels.Add(name.Length > 14 ? name[..14] : name);

            var barSeries = new BarSeries
            {
                FillColor           = Palette[ci % Palette.Length],
                StrokeColor         = OxyColors.Transparent,
                StrokeThickness     = 0,
                BaseValue           = baseVal,
                TrackerFormatString = name + ": {2:F2} " + unit,
                BarWidth            = 0.6,
            };
            barSeries.Items.Add(new BarItem(double.IsNaN(val) ? baseVal : val, ci));
            model.Series.Add(barSeries);
            ci++;
        }

        categoryAxis.Maximum = Math.Max(0.5, ci - 0.5);
        // Category snapshot: zooming has no meaning here
        foreach (var ax in model.Axes) { ax.IsZoomEnabled = false; ax.IsPanEnabled = false; }
        AddAlarmAnnotations(model, w, vertical: true);
        return model;
    }

    // ── Heatmap ────────────────────────────────────────────────────────────────

    private PlotModel BuildHeatmapModel(DashboardWidget w)
    {
        var nodeId = w.NodeIds.FirstOrDefault();
        int days   = HeatmapDays(w);
        var data   = Cached($"hm|{nodeId}|{w.Metric}|{days}",
            () => _db.GetHeatmapData(nodeId, w.Metric, days));

        (double min, double max, string unit) = MetricRange(w.Metric);

        // packet_count uses a 0-based scale; other metrics keep NaN for empty cells so
        // the color axis renders them as "no data" instead of fake minimum values.
        bool isCount = w.Metric == "packet_count";
        var clean = new double[days, 24];
        for (int d = 0; d < days; d++)
            for (int h = 0; h < 24; h++)
                clean[d, h] = data[d, h];

        // Color scale from the ACTUAL data range (not the theoretical metric range):
        // an SNR map pinned to -20..20 dB left typical -8..+8 data nearly monochrome.
        double colorMin = isCount ? 0 : min;
        double colorMax = isCount ? 1 : max;
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int d = 0; d < days; d++)
                for (int h = 0; h < 24; h++)
                {
                    double v = data[d, h];
                    if (double.IsNaN(v)) continue;
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
            if (!double.IsPositiveInfinity(lo))
            {
                if (hi - lo < 1.0) { double mid = (hi + lo) / 2; lo = mid - 0.5; hi = mid + 0.5; }
                colorMin = isCount ? 0 : lo;
                colorMax = hi;
            }
        }

        var model = MakeBaseModel();
        model.Legends.Clear();

        // Fixed day×hour grid — zooming/panning only ever produced nonsense ranges
        // like "-24 h … +42 h", so interaction is disabled entirely.
        var startDate = DateTime.Now.Date.AddDays(-(days - 1));
        model.Axes.Add(new LinearAxis
        {
            Position  = AxisPosition.Left,
            Title     = Loc("StrDashboardHourOfDay"),
            Minimum   = -0.5, Maximum = 23.5,
            MajorStep = 6,
            TextColor = _oxyText, TitleColor = _oxyText,
            IsZoomEnabled = false, IsPanEnabled = false,
        });
        model.Axes.Add(new LinearAxis
        {
            Position   = AxisPosition.Bottom,
            Minimum    = -0.5, Maximum = days - 0.5,
            TextColor  = _oxyText, TitleColor = _oxyText,
            IsZoomEnabled  = false, IsPanEnabled = false,
            LabelFormatter = d => startDate.AddDays(Math.Round(d)).ToString("dd.MM"),
        });
        model.Axes.Add(new LinearColorAxis
        {
            Position   = AxisPosition.Right,
            Minimum    = colorMin, Maximum = colorMax,
            Palette    = OxyPalettes.Viridis(64),
            Title      = unit,
            TextColor  = _oxyText, TitleColor = _oxyText,
            IsZoomEnabled = false, IsPanEnabled = false,
            // NaN cells ("no data") get a neutral background color clearly outside the
            // Viridis ramp — previously they were mapped to min-1 and looked like minima
            InvalidNumberColor = _isDark ? OxyColor.FromRgb(33, 38, 45) : OxyColor.FromRgb(230, 232, 236),
        });

        model.Series.Add(new HeatMapSeries
        {
            Data = clean, X0 = 0, X1 = days - 1, Y0 = 0, Y1 = 23, Interpolate = false,
        });
        return model;
    }

    // ── Scatter chart ─────────────────────────────────────────────────────────

    private PlotModel BuildScatterModel(DashboardWidget w)
    {
        (double min, double max, string unit) = MetricRange(w.Metric);
        var model = MakeBaseModel();
        AddTimeAxis(model, w.Days);
        AddLinearAxis(model, min, max, unit);

        int ci = 0;
        foreach (var nodeId in w.NodeIds)
        {
            var pts = GetSeries(nodeId, w.Metric, w.Days);
            if (pts.Count == 0) continue;
            var oxy    = Palette[ci % Palette.Length];
            var series = new ScatterSeries
            {
                Title                  = NodeLabel(nodeId),
                MarkerType             = MarkerType.Circle,
                MarkerSize             = 3,
                MarkerFill             = oxy,
                MarkerStroke           = OxyColor.FromAColor(100, oxy),
                MarkerStrokeThickness  = 0.5,
                TrackerFormatString    = "{0}\n{2:dd.MM HH:mm}\n{4:F2} " + unit,
            };
            foreach (var p in pts.OrderBy(x => x.Timestamp))
                series.Points.Add(new ScatterPoint(DateTimeAxis.ToDouble(p.Timestamp.ToLocalTime()), p.Value));
            model.Series.Add(series);
            ci++;
        }
        AddNightBands(model);
        AddAlarmAnnotations(model, w);
        FinalizeChart(model);
        return model;
    }

    // ── Histogram ─────────────────────────────────────────────────────────────

    private PlotModel BuildHistogramModel(DashboardWidget w)
    {
        (double rangeMin, double rangeMax, string unit) = MetricRange(w.Metric);
        var nodeId = w.NodeIds.FirstOrDefault();
        var pts    = GetSeries(nodeId, w.Metric, w.Days);

        // Bin over the ACTUAL data range, not the theoretical metric range — RSSI pinned
        // to -130..-20 dBm squeezed all data into two bars on a mostly empty axis.
        double min = rangeMin, max = rangeMax;
        if (pts.Count > 0)
        {
            min = pts.Min(p => p.Value);
            max = pts.Max(p => p.Value);
            if (max - min < 1e-6) { min -= 0.5; max += 0.5; }   // constant metric
            else
            {
                double pad = (max - min) * 0.02;                 // small visual margin
                min -= pad; max += pad;
            }
        }

        const int buckets = 20;
        double range = Math.Max(max - min, 1e-6);
        double bw    = range / buckets;
        int[] counts = new int[buckets];

        foreach (var p in pts)
        {
            int b = (int)Math.Floor((Math.Clamp(p.Value, min, max - 1e-9) - min) / bw);
            b = Math.Clamp(b, 0, buckets - 1);
            counts[b]++;
        }

        var model = MakeBaseModel();
        model.Legends.Clear();

        model.Axes.Add(new LinearAxis
        {
            Position           = AxisPosition.Bottom,
            Title              = unit,
            Minimum            = min, Maximum = max,
            TextColor          = _oxyText,
            TicklineColor      = _oxyTick,
            TitleColor         = _oxyText,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = _oxyGrid,
            IsZoomEnabled      = false, IsPanEnabled = false,
        });
        model.Axes.Add(new LinearAxis
        {
            Position           = AxisPosition.Left,
            Title              = Loc("StrDashboardCount"),
            Minimum            = 0,
            TextColor          = _oxyText,
            TicklineColor      = _oxyTick,
            TitleColor         = _oxyText,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = _oxyGrid,
            IsZoomEnabled      = false, IsPanEnabled = false,
        });

        var series = new RectangleBarSeries
        {
            FillColor       = OxyColor.FromAColor(200, Palette[0]),
            StrokeColor     = OxyColor.FromAColor(230, Palette[0]),
            StrokeThickness = 1,
        };
        for (int i = 0; i < buckets; i++)
        {
            double x0 = min + i * bw;
            double x1 = min + (i + 1) * bw;
            series.Items.Add(new RectangleBarItem(x0, 0, x1, counts[i]));
        }
        model.Series.Add(series);
        AddAlarmAnnotations(model, w, vertical: true);
        return model;
    }

    // ── Candlestick (daily OHLC) ──────────────────────────────────────────────

    private PlotModel BuildCandlestickModel(DashboardWidget w)
    {
        (double min, double max, string unit) = MetricRange(w.Metric);
        var nodeId  = w.NodeIds.FirstOrDefault();
        var candles = Cached($"cs|{nodeId}|{w.Metric}|{w.Days}",
            () => _db.GetCandlestickData(nodeId, w.Metric, w.Days));

        var model = MakeBaseModel();
        model.Legends.Clear();
        AddTimeAxis(model, w.Days);
        AddLinearAxis(model, min, max, unit);

        if (candles.Count == 0) return model;

        var series = new CandleStickSeries
        {
            IncreasingColor = OxyColor.FromRgb( 63, 185, 126),
            DecreasingColor = OxyColor.FromRgb(240,  76,  76),
            CandleWidth     = 0.6,
            StrokeThickness = 1.5,
        };
        // Doji candles: open ≈ close (within 3 % of the visible value span) carry no real
        // direction — tiny noise around the day boundary used to flip them red/green at
        // random. Render them neutral via a second series (OxyPlot colors per series).
        double span = candles.Max(c => c.High) - candles.Min(c => c.Low);
        double dojiEps = Math.Max(span * 0.03, 1e-9);
        var neutral = new CandleStickSeries
        {
            IncreasingColor = OxyColor.FromRgb(139, 148, 158),
            DecreasingColor = OxyColor.FromRgb(139, 148, 158),
            CandleWidth     = 0.6,
            StrokeThickness = 1.5,
        };
        foreach (var c in candles)
        {
            var item = new HighLowItem(
                DateTimeAxis.ToDouble(c.Time.ToLocalTime()), c.High, c.Low, c.Open, c.Close);
            if (Math.Abs(c.Close - c.Open) <= dojiEps) neutral.Items.Add(item);
            else series.Items.Add(item);
        }

        model.Series.Add(series);
        if (neutral.Items.Count > 0) model.Series.Add(neutral);
        AddAlarmAnnotations(model, w);
        FinalizeChart(model);
        return model;
    }

    // ── State timeline (node activity per hour) ───────────────────────────────

    private PlotModel BuildStatelineModel(DashboardWidget w)
    {
        var nodeId = w.NodeIds.FirstOrDefault();
        int days   = StatelineDays(w);
        var data   = Cached($"sl|{nodeId}|{days}", () => _db.GetNodeActivityGrid(nodeId, days));

        var model = MakeBaseModel();
        model.Legends.Clear();

        var startDate = DateTime.Now.Date.AddDays(-(days - 1));
        model.Axes.Add(new LinearAxis
        {
            Position   = AxisPosition.Left,
            Title      = Loc("StrDashboardHourOfDay"),
            Minimum    = -0.5, Maximum = 23.5,
            MajorStep  = 6,
            TextColor  = _oxyText, TitleColor = _oxyText,
            IsZoomEnabled = false, IsPanEnabled = false,
        });
        model.Axes.Add(new LinearAxis
        {
            Position   = AxisPosition.Bottom,
            Minimum    = -0.5, Maximum = days - 0.5,
            TextColor  = _oxyText, TitleColor = _oxyText,
            IsZoomEnabled  = false, IsPanEnabled = false,
            LabelFormatter = d => startDate.AddDays(Math.Round(d)).ToString("dd.MM"),
        });
        model.Axes.Add(new LinearColorAxis
        {
            Position = AxisPosition.None,
            Minimum  = 0, Maximum = 1,
            Palette  = OxyPalette.Interpolate(2,
                _isDark ? OxyColor.FromRgb(33, 38, 45) : OxyColor.FromRgb(230, 232, 236),
                OxyColor.FromRgb(63, 185, 126)),
        });

        model.Series.Add(new HeatMapSeries
        {
            Data = data, X0 = 0, X1 = days - 1, Y0 = 0, Y1 = 23, Interpolate = false,
        });
        return model;
    }

    // ── OxyPlot helpers ───────────────────────────────────────────────────────

    private PlotModel MakeBaseModel()
    {
        var model = new PlotModel
        {
            Background              = OxyColor.FromArgb(0, 0, 0, 0),
            PlotAreaBorderColor     = _oxyTick,
            PlotAreaBorderThickness = new OxyThickness(1),
            TextColor               = _oxyText,
        };
        model.Legends.Add(new Legend
        {
            LegendPosition        = LegendPosition.TopLeft,
            LegendBackground      = _oxyLegendBg,
            LegendBorder          = OxyColor.FromAColor(80, _oxyTick),
            LegendBorderThickness = 1,
            LegendTextColor       = OxyColor.FromRgb(_fgMain.R, _fgMain.G, _fgMain.B),
            LegendFontSize        = 10,
        });
        return model;
    }

    private void AddTimeAxis(PlotModel model, int days)
    {
        // Label format adapts to the range: a 1-day chart needs clock times, a
        // month-long chart only dates (0 = "Gesamt" → dates).
        string fmt = days == 1 ? "HH:mm"
                   : days is > 1 and <= 7 ? "dd.MM HH:mm"
                   : "dd.MM";
        model.Axes.Add(new DateTimeAxis
        {
            Position           = AxisPosition.Bottom,
            StringFormat       = fmt,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = _oxyGrid,
            TextColor          = _oxyText,
            TicklineColor      = _oxyTick,
        });
    }

    /// <summary>Night bands (sunset → sunrise, own position) behind the series, so
    /// nocturnal SNR/temperature patterns are visible at a glance. Skipped for ranges
    /// over ~a month to avoid annotation spam.</summary>
    private void AddNightBands(PlotModel model)
    {
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        foreach (var s in model.Series.OfType<LineSeries>())
            foreach (var p in s.Points) { if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X; }
        foreach (var s in model.Series.OfType<ScatterSeries>())
            foreach (var p in s.Points) { if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X; }
        if (double.IsInfinity(minX) || maxX - minX > 32) return; // X is in days (OADate)

        var fill = _isDark
            ? OxyColor.FromAColor(60, OxyColor.FromRgb(0, 0, 0))
            : OxyColor.FromAColor(16, OxyColor.FromRgb(31, 60, 110));

        var day = DateTimeAxis.ToDateTime(minX).Date.AddDays(-1);
        var end = DateTimeAxis.ToDateTime(maxX).Date.AddDays(1);
        for (; day <= end; day = day.AddDays(1))
        {
            var (_, sunset)  = SunriseSunsetService.GetSunriseSunset(_db.Latitude, _db.Longitude, day);
            var (sunrise, _) = SunriseSunsetService.GetSunriseSunset(_db.Latitude, _db.Longitude, day.AddDays(1));
            model.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = DateTimeAxis.ToDouble(day + sunset),
                MaximumX = DateTimeAxis.ToDouble(day.AddDays(1) + sunrise),
                Fill     = fill,
                Layer    = AnnotationLayer.BelowSeries,
            });
        }
    }

    private void AddLinearAxis(PlotModel model, double min, double max, string unit)
    {
        model.Axes.Add(new LinearAxis
        {
            Position           = AxisPosition.Left,
            Minimum            = min, Maximum = max,
            Title              = unit,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = _oxyGrid,
            TextColor          = _oxyText,
            TicklineColor      = _oxyTick,
            TitleColor         = _oxyText,
            // Grafana-style interaction: zoom/pan happen on the TIME axis only; the
            // value axis stays fixed so wheel-zoom never shears the chart diagonally.
            IsZoomEnabled      = false,
            IsPanEnabled       = false,
        });
    }

    /// <summary>Post-processing for value charts: widens the fixed Y range when data or
    /// the threshold line fall outside it (both were silently clipped before), and drops
    /// the legend when there is only one series — the card tooltip already names the node.</summary>
    private static void FinalizeChart(PlotModel model)
    {
        var axis = model.Axes.OfType<LinearAxis>().FirstOrDefault(a => a.Position == AxisPosition.Left);
        if (axis != null)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            void Consider(double v) { if (double.IsNaN(v)) return; if (v < lo) lo = v; if (v > hi) hi = v; }
            foreach (var s in model.Series)
                switch (s)
                {
                    case LineSeries ls: // AreaSeries derives from LineSeries
                        foreach (var p in ls.Points) Consider(p.Y);
                        break;
                    case ScatterSeries sc:
                        foreach (var p in sc.Points) Consider(p.Y);
                        break;
                    case CandleStickSeries cs:
                        foreach (var i in cs.Items) { Consider(i.Low); Consider(i.High); }
                        break;
                }
            foreach (var a in model.Annotations.OfType<LineAnnotation>())
                if (a.Type == LineAnnotationType.Horizontal) Consider(a.Y);
            if (!double.IsPositiveInfinity(lo))
            {
                double pad = Math.Max((hi - lo) * 0.05, 0.1);
                if (lo < axis.Minimum) axis.Minimum = lo - pad;
                if (hi > axis.Maximum) axis.Maximum = hi + pad;
            }
        }
        if (model.Series.Count <= 1) model.Legends.Clear();
    }

    // ── Edit / Remove widget ──────────────────────────────────────────────────

    private void EditWidget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DashboardWidget w) return;
        EditWidgetCore(w);
    }

    private void EditWidgetCore(DashboardWidget w)
    {
        if (_current == null) return;

        var dlg = new AddWidgetDialog(_nodeNames, _nodeShortNames, existing: w) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        // Preserve ID and size from the original
        var updated = dlg.Result! with { Id = w.Id, Width = w.Width, Height = w.Height };

        var idx = _store.Dashboards.IndexOf(_current);
        var widgetIdx = _current.Widgets.FindIndex(x => x.Id == w.Id);
        if (widgetIdx < 0 || idx < 0) return;

        var newWidgets = new List<DashboardWidget>(_current.Widgets);
        newWidgets[widgetIdx] = updated;
        _current = _current with { Widgets = newWidgets };
        _store.Dashboards[idx] = _current;
        SaveStore();
        RenderCurrentDashboard();
    }

    private void RemoveWidget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DashboardWidget w) return;
        if (_current == null) return;
        var idx = _store.Dashboards.IndexOf(_current);
        if (idx < 0) return;

        if (_clockTimers.TryGetValue(w.Id, out var ct)) { ct.Stop(); _clockTimers.Remove(w.Id); }

        var newWidgets = new List<DashboardWidget>(_current.Widgets);
        newWidgets.RemoveAll(x => x.Id == w.Id);
        _current = _current with { Widgets = newWidgets };
        _store.Dashboards[idx] = _current;
        SaveStore();

        var toRemove = WidgetPanel.Children.OfType<Grid>()
            .FirstOrDefault(g => g.Tag is DashboardWidget tw && tw.Id == w.Id);
        if (toRemove != null) WidgetPanel.Children.Remove(toRemove);
        UpdateStatus();

        if (_current.Widgets.Count == 0)
            EmptyState.Visibility = Visibility.Visible;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string NodeLabel(uint nodeId) =>
        _nodeNames.TryGetValue(nodeId, out var n) ? n : $"!{nodeId:x8}";

    private static (double min, double max, string unit) MetricRange(string metric) =>
        metric switch
        {
            "snr"          => (-20.0,   20.0,  "dB"),
            "rssi"         => (-130.0, -20.0,  "dBm"),
            "battery"      => (0.0,    100.0,  "%"),
            "voltage"      => (2.5,      5.0,  "V"),
            "channel_util" => (0.0,    100.0,  "%"),
            "air_tx_util"  => (0.0,    100.0,  "%"),
            "temperature"  => (-20.0,   60.0,  "°C"),
            "humidity"     => (0.0,    100.0,  "%"),
            "pressure"     => (950.0, 1050.0,  "hPa"),
            "packet_count" => (0.0,   100.0,   Loc("StrUnitPacketsPerHour")),
            "noise_floor"  => (-130.0, -70.0,  "dBm"),
            // Rates derived from LocalStats counter deltas between polls
            "rx_pph" or "tx_pph" or "rx_bad_pph" or "rx_dupe_pph" or "tx_relay_pph"
                           => (0.0,    100.0,  Loc("StrUnitPacketsPerHour")),
            // Cumulative since-boot counters — FinalizeChart widens the axis to the data
            "rx_bad" or "rx_dupe" or "tx_relay" or "tx_relay_canceled" or "tx_dropped"
                           => (0.0,    100.0,  ""),
            _              => (0.0,    100.0,  ""),
        };

    private string? PromptString(string title, string prompt, string defaultValue = "")
    {
        var dlg = new Window
        {
            Title  = title,
            Width  = 380, Height = 165,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(_bgCard),
        };
        if (Application.Current.MainWindow is Window mw) dlg.Owner = mw;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = prompt,
            Foreground = new SolidColorBrush(_fgSub),
            Margin = new Thickness(0, 0, 0, 8),
        });
        var box = new TextBox { Text = defaultValue };
        stack.Children.Add(box);
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok     = new Button { Content = "OK", Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc("StrCancel"), Width = 90, IsCancel = true };
        ok.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        stack.Children.Add(btnRow);
        dlg.Content = stack;
        box.Focus();
        box.SelectAll();

        return dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(box.Text)
            ? box.Text.Trim()
            : null;
    }

    // ── Time-presets bar ──────────────────────────────────────────────────────

    private FrameworkElement BuildTimePresetsBar(DashboardWidget w)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(8, 3, 8, 3),
        };

        var presets = new (int days, string label)[]
        {
            (1, "1d"), (7, "7d"), (14, "14d"), (30, "30d"), (0, Loc("StrDaysAll")),
        };
        foreach (var (days, label) in presets)
        {
            bool active = w.Days == days;
            var btn = new Button
            {
                Content    = label,
                Margin     = new Thickness(0, 0, 3, 0),
                Padding    = new Thickness(5, 1, 5, 1),
                FontSize   = 10,
                Tag        = (w.Id, days),
                Foreground = new SolidColorBrush(active ? _uiPalette[0] : _fgSub),
                FontWeight = active ? System.Windows.FontWeights.SemiBold
                                    : System.Windows.FontWeights.Normal,
            };
            btn.Click += (_, _) =>
            {
                var (wid, d) = ((string, int))btn.Tag!;
                UpdateWidgetDays(wid, d);
            };
            panel.Children.Add(btn);
        }

        // ⌀ MA only where it is actually rendered (line/area) — on heatmaps & co. the
        // button used to toggle a setting with zero visible effect. Thresholds moved
        // into the widget editor (✎) entirely.
        if (w.Type is "line" or "area")
        {
            panel.Children.Add(new TextBlock
            {
                Text = " | ",
                Foreground = new SolidColorBrush(_fgMuted),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = 10,
            });

            var maBtn = new Button
            {
                Content    = "⌀ MA",
                Margin     = new Thickness(0, 0, 3, 0),
                Padding    = new Thickness(5, 1, 5, 1),
                FontSize   = 10,
                ToolTip    = Loc("StrDashboardMovingAvg"),
                Tag        = w.Id,
                Foreground = new SolidColorBrush(w.ShowMovingAverage ? _uiPalette[0] : _fgSub),
                FontWeight = w.ShowMovingAverage ? System.Windows.FontWeights.SemiBold
                                                 : System.Windows.FontWeights.Normal,
            };
            maBtn.Click += (_, _) => ToggleMovingAverage((string)maBtn.Tag!);
            panel.Children.Add(maBtn);
        }

        return new Border
        {
            Background      = new SolidColorBrush(_bgHeader),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush     = new SolidColorBrush(_borderCol),
            Child           = panel,
            Tag             = "presetsbar",
        };
    }

    private void UpdateWidgetDays(string widgetId, int newDays)
    {
        if (_current == null) return;
        int dashIdx = _store.Dashboards.IndexOf(_current);
        if (dashIdx < 0) return;
        int wIdx = _current.Widgets.FindIndex(x => x.Id == widgetId);
        if (wIdx < 0) return;
        var updated = _current.Widgets[wIdx] with { Days = newDays };
        CommitWidgetUpdate(dashIdx, wIdx, updated);
        RefreshCardById(widgetId, updated);
    }

    private void ToggleMovingAverage(string widgetId)
    {
        if (_current == null) return;
        int dashIdx = _store.Dashboards.IndexOf(_current);
        if (dashIdx < 0) return;
        int wIdx = _current.Widgets.FindIndex(x => x.Id == widgetId);
        if (wIdx < 0) return;
        var old     = _current.Widgets[wIdx];
        var updated = old with { ShowMovingAverage = !old.ShowMovingAverage };
        CommitWidgetUpdate(dashIdx, wIdx, updated);
        RefreshCardById(widgetId, updated);
    }

    private void CommitWidgetUpdate(int dashIdx, int widgetIdx, DashboardWidget updated)
    {
        var newWidgets = new List<DashboardWidget>(_current!.Widgets);
        newWidgets[widgetIdx] = updated;
        _current = _current with { Widgets = newWidgets };
        _store.Dashboards[dashIdx] = _current;
        foreach (Grid g in WidgetPanel.Children.OfType<Grid>())
            if (g.Tag is DashboardWidget tw && tw.Id == updated.Id) { g.Tag = updated; break; }
        SaveStore();
    }

    private void RefreshCardById(string widgetId, DashboardWidget? withWidget = null)
    {
        // User interaction (range/MA/threshold change): drop the cycle cache so the
        // rebuilt card queries fresh data instead of a stale snapshot.
        ClearDataCache();
        for (int i = 0; i < WidgetPanel.Children.Count; i++)
        {
            if (WidgetPanel.Children[i] is not Grid container) continue;
            if (container.Tag is not DashboardWidget tw || tw.Id != widgetId) continue;
            var w       = withWidget ?? tw;
            var newCard = BuildCard(w);
            // Preserve the current size
            if (newCard is Grid ng)
            {
                ng.Width  = container.Width;
                ng.Height = container.Height;
                ng.Tag    = w with { Width = container.Width, Height = container.Height };
            }
            WidgetPanel.Children.RemoveAt(i);
            WidgetPanel.Children.Insert(i, newCard);
            return;
        }
    }

    // ── Node-Ranking widget ───────────────────────────────────────────────────

    private FrameworkElement BuildRankingContent(DashboardWidget w)
    {
        (_, _, string unit) = MetricRange(w.Metric);
        var nodeIds = w.NodeIds.Count > 0 ? w.NodeIds : _nodeNames.Keys.ToList();

        var rows = nodeIds
            .Select(id =>
            {
                var pts = GetSeries(id, w.Metric, w.Days);
                double val = pts.Count > 0
                    ? pts.OrderByDescending(p => p.Timestamp).First().Value
                    : double.NaN;
                return (id, val);
            })
            .Where(x => !double.IsNaN(x.val))
            .OrderByDescending(x => x.val)
            .ToList();

        var grid = new Grid { Margin = new Thickness(8, 6, 8, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddRankingCell(grid, "#",                0, 0, _fgMuted, true);
        AddRankingCell(grid, Loc("StrRankingNode"), 1, 0, _fgMuted, true);
        AddRankingCell(grid, unit,               2, 0, _fgMuted, true);

        if (rows.Count == 0)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var noData = new TextBlock
            {
                Text       = Loc("StrDashboardNoData"),
                Foreground = new SolidColorBrush(_fgMuted),
                FontSize   = 11,
                Margin     = new Thickness(4, 8, 4, 4),
            };
            Grid.SetRow(noData, 1);
            Grid.SetColumnSpan(noData, 3);
            grid.Children.Add(noData);
        }
        else
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var (id, val) = rows[i];
                Color rowColor = i == 0 ? _uiPalette[1]
                               : i == 1 ? _uiPalette[2]
                               : _fgMain;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int row = i + 1;
                AddRankingCell(grid, $"{i + 1}",  0, row, rowColor, false);
                AddRankingCell(grid, NodeLabel(id), 1, row, _fgMain, false);
                AddRankingCell(grid, $"{val:F1}",  2, row, rowColor, false);
            }
        }

        return new Border
        {
            Child = new ScrollViewer
            {
                Content = grid,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            Padding = new Thickness(2),
        };
    }

    private void AddRankingCell(Grid grid, string text, int col, int row, Color color, bool header)
    {
        var tb = new TextBlock
        {
            Text         = text,
            Foreground   = new SolidColorBrush(color),
            FontSize     = header ? 10 : 11,
            FontWeight   = header ? System.Windows.FontWeights.SemiBold
                                  : System.Windows.FontWeights.Normal,
            Padding      = new Thickness(3, 2, 3, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(tb,    row);
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    // ── Multi-Node-Stat widget ────────────────────────────────────────────────

    private FrameworkElement BuildMultiStatContent(DashboardWidget w)
    {
        (_, _, string unit) = MetricRange(w.Metric);
        var nodeIds = w.NodeIds.Count > 0 ? w.NodeIds : _nodeNames.Keys.Take(8).ToList();

        var wrap = new WrapPanel { Margin = new Thickness(6), Orientation = Orientation.Horizontal };

        int ci = 0;
        foreach (var id in nodeIds)
        {
            // Single query per node (this used to hit the DB twice: values + last)
            var pts = GetSeries(id, w.Metric, w.Days);
            var values = pts.Select(p => p.Value).ToList();
            double last = pts.Count > 0
                ? pts.OrderByDescending(p => p.Timestamp).First().Value
                : double.NaN;
            double min = values.Count > 0 ? values.Min() : double.NaN;
            double max = values.Count > 0 ? values.Max() : double.NaN;
            double avg = values.Count > 0 ? values.Average() : double.NaN;

            Color accent = _uiPalette[ci % _uiPalette.Length];
            string name  = NodeLabel(id);

            var tile = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(20, accent.R, accent.G, accent.B)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(5),
                Margin          = new Thickness(3),
                Padding         = new Thickness(8, 5, 8, 5),
                MinWidth        = 110,
            };

            var tileGrid = new Grid();
            tileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text         = name.Length > 14 ? name[..14] : name,
                FontSize     = 10,
                FontWeight   = System.Windows.FontWeights.SemiBold,
                Foreground   = new SolidColorBrush(accent),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetRow(nameText, 0);

            var bigText = new TextBlock
            {
                Text                = double.IsNaN(last) ? "–" : $"{last:F1}",
                FontSize            = 22,
                FontWeight          = System.Windows.FontWeights.Light,
                Foreground          = new SolidColorBrush(_fgMain),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            };
            Grid.SetRow(bigText, 1);

            var statsRow = new StackPanel { Orientation = Orientation.Horizontal };
            statsRow.Children.Add(MakeMultiStatMini("↓", min, _fgMuted));
            statsRow.Children.Add(MakeMultiStatMini(" ⌀", avg, _fgSub));
            statsRow.Children.Add(MakeMultiStatMini(" ↑", max, _fgMuted));
            Grid.SetRow(statsRow, 2);

            tileGrid.Children.Add(nameText);
            tileGrid.Children.Add(bigText);
            tileGrid.Children.Add(statsRow);
            tile.Child = tileGrid;
            wrap.Children.Add(tile);
            ci++;
        }

        return new Border
        {
            Child = new ScrollViewer
            {
                Content = wrap,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
    }

    private TextBlock MakeMultiStatMini(string prefix, double val, Color color) => new()
    {
        Text       = double.IsNaN(val) ? $"{prefix}–" : $"{prefix}{val:F1}",
        FontSize   = 9,
        Foreground = new SolidColorBrush(color),
        Margin     = new Thickness(0, 0, 3, 0),
    };

    // ── Mesh-Health widget ────────────────────────────────────────────────────

    private FrameworkElement BuildMeshHealthContent(DashboardWidget w)
    {
        var nodeIds  = w.NodeIds.Count > 0 ? w.NodeIds : _nodeNames.Keys.ToList();
        int days     = MeshHealthDays(w);
        int shortH   = 6;

        var stack = new StackPanel { Margin = new Thickness(6) };

        // ── Global mesh health score ──────────────────────────────────────────
        var global = Cached($"mhs|{days}", () => _db.GetMeshHealthScore(days));
        var globalTile = new Border
        {
            Background      = new SolidColorBrush(_isDark ? Color.FromRgb(21, 26, 35) : Color.FromRgb(245, 247, 250)),
            BorderBrush     = new SolidColorBrush(_borderCol),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(5),
            Margin          = new Thickness(0, 0, 0, 6),
            Padding         = new Thickness(10, 7, 10, 7),
            ToolTip         = global.Summary,
        };
        var gRow = new StackPanel { Orientation = Orientation.Horizontal };
        gRow.Children.Add(MeshLed(global.State, 12));
        gRow.Children.Add(new TextBlock
        {
            Text       = $" {Loc("StrMeshHealthScore")}: {global.Score:0}%",
            FontSize   = 12,
            FontWeight = System.Windows.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_fgMain),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        });
        var gDetails = new TextBlock
        {
            Text       = $"   {Loc("StrMeshHealthAvgPath")}: {global.AvgPathCost:F2}  |  " +
                         $"{Loc("StrMeshHealthRoutes")}: {global.RouteChangeRate:F1}/h  |  " +
                         $"{Loc("StrMeshHealthRx")}: {global.RxScore * 100:0}%  |  " +
                         $"{Loc("StrMeshHealthChannel")}: {global.ChannelUtilization:F1}%",
            FontSize   = 10,
            Foreground = new SolidColorBrush(_fgSub),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        gRow.Children.Add(gDetails);
        globalTile.Child = gRow;
        stack.Children.Add(globalTile);

        // ── Per-node tiles ────────────────────────────────────────────────────
        foreach (var id in nodeIds)
        {
            var analysis = Cached($"sa|{id}|{days}", () => _db.GetSignalAnalysis(id, shortH, days, _nodeNames));
            string name  = NodeLabel(id);

            var overallState = new[] { analysis.WeatherLed, analysis.AntennaLed, analysis.NeighborLed, analysis.PathLed }
                .Where(l => l != LedState.NoData)
                .DefaultIfEmpty(LedState.NoData)
                .Max();

            Color accent = LedColor(overallState);
            var tile = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(10, accent.R, accent.G, accent.B)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(35, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(5),
                Margin          = new Thickness(0, 0, 0, 4),
                Padding         = new Thickness(8, 6, 8, 6),
            };

            var tileStack = new StackPanel();

            // Name row
            var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            nameRow.Children.Add(MeshLed(overallState, 10));
            nameRow.Children.Add(new TextBlock
            {
                Text       = " " + name,
                FontSize   = 11,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(_fgMain),
            });
            tileStack.Children.Add(nameRow);

            // LED bubbles row
            var ledRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            ledRow.Children.Add(MeshLedCell(analysis.WeatherLed,  Loc("StrMeshHealthWeather")));
            ledRow.Children.Add(MeshLedCell(analysis.AntennaLed,  Loc("StrMeshHealthAntenna")));
            ledRow.Children.Add(MeshLedCell(analysis.NeighborLed, Loc("StrMeshHealthNeighbor")));
            ledRow.Children.Add(MeshLedCell(analysis.PathLed,     Loc("StrMeshHealthPath")));
            tileStack.Children.Add(ledRow);

            // Factor row: trend + neighbor count + hop cost
            var factors = new List<string>();
            if (analysis.Trends.Count > 0)
            {
                string arrow = analysis.AvgLongSlope < -0.1f ? "▼" : analysis.AvgLongSlope > 0.1f ? "▲" : "→";
                factors.Add($"SNR {arrow} {analysis.AvgLongSlope:+0.0;-0.0} dB/d");
            }
            if (analysis.TotalNeighbors > 0)
                factors.Add($"{analysis.TotalNeighbors} {Loc("StrMeshHealthNeighborCount")}");
            if (analysis.HopCost > 0)
                factors.Add($"{Loc("StrMeshHealthHopCost")}: {analysis.HopCost:F2}");
            if (!string.IsNullOrEmpty(analysis.ProblemNeighborName))
                factors.Add($"⚠ {analysis.ProblemNeighborName}");

            if (factors.Count > 0)
                tileStack.Children.Add(new TextBlock
                {
                    Text       = string.Join("   ", factors),
                    FontSize   = 9,
                    Foreground = new SolidColorBrush(_fgMuted),
                });

            tile.Child = tileStack;
            stack.Children.Add(tile);
        }

        if (nodeIds.Count == 0)
            stack.Children.Add(new TextBlock
            {
                Text       = Loc("StrDashboardNoData"),
                Foreground = new SolidColorBrush(_fgMuted),
                FontSize   = 11,
            });

        return new Border
        {
            Child = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
    }

    private static Color LedColor(LedState state) => state switch
    {
        LedState.Good    => Color.FromRgb(76, 175, 80),
        LedState.Warning => Color.FromRgb(255, 193, 7),
        LedState.Alert   => Color.FromRgb(244, 67, 54),
        _                => Color.FromRgb(189, 189, 189),
    };

    private static System.Windows.UIElement MeshLed(LedState state, int size)
    {
        var c = LedColor(state);
        return new System.Windows.Shapes.Ellipse
        {
            Width  = size, Height = size,
            Fill   = new SolidColorBrush(c),
            Effect = new DropShadowEffect { Color = c, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.65 },
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
    }

    private FrameworkElement MeshLedCell(LedState state, string label)
    {
        var cell = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var led = (System.Windows.Shapes.Ellipse)MeshLed(state, 10);
        led.Margin = new Thickness(0);
        led.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        cell.Children.Add(led);
        cell.Children.Add(new TextBlock
        {
            Text       = label,
            FontSize   = 8,
            Foreground = new SolidColorBrush(_fgMuted),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        });
        return cell;
    }

    // ── PNG export / clipboard / duplicate ────────────────────────────────────

    /// <summary>Renders the card visual at 2× resolution (works for every widget type,
    /// not just OxyPlot charts).</summary>
    private static System.Windows.Media.Imaging.RenderTargetBitmap RenderCardBitmap(Border card)
    {
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(card.ActualWidth * 2), (int)Math.Ceiling(card.ActualHeight * 2),
            192, 192, PixelFormats.Pbgra32);
        bmp.Render(card);
        return bmp;
    }

    private void ExportWidgetPng(Border card, DashboardWidget w)
    {
        var safeTitle = string.Join("_", w.Title.Split(System.IO.Path.GetInvalidFileNameChars(),
            StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (safeTitle.Length == 0) safeTitle = "widget";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title    = Loc("StrDashboardExportPng"),
            Filter   = "PNG (*.png)|*.png",
            FileName = $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmm}.png",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(RenderCardBitmap(card)));
            using var fs = System.IO.File.Create(dlg.FileName);
            encoder.Save(fs);
            MessageBox.Show(string.Format(Loc("StrDashboardExportDone"), dlg.FileName),
                Loc("StrDashboardTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc("StrDashboardExportFailed")} {ex.Message}",
                Loc("StrDashboardTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyWidgetImage(Border card)
    {
        try { Clipboard.SetImage(RenderCardBitmap(card)); }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc("StrDashboardExportFailed")} {ex.Message}",
                Loc("StrDashboardTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DuplicateWidget(DashboardWidget w)
    {
        if (_current == null) return;
        var idx = _store.Dashboards.IndexOf(_current);
        var wIdx = _current.Widgets.FindIndex(x => x.Id == w.Id);
        if (idx < 0 || wIdx < 0) return;
        var copy = w with { Id = Guid.NewGuid().ToString("N")[..8] };
        var newWidgets = new List<DashboardWidget>(_current.Widgets);
        newWidgets.Insert(wIdx + 1, copy);
        _current = _current with { Widgets = newWidgets };
        _store.Dashboards[idx] = _current;
        SaveStore();
        RenderCurrentDashboard();
    }

    // ── CSV Export ────────────────────────────────────────────────────────────

    private void ExportWidgetCsv(DashboardWidget w)
    {
        (_, _, string unit) = MetricRange(w.Metric);
        // Widget titles may contain characters that are invalid in filenames (":", "/" …)
        var safeTitle = string.Join("_", w.Title.Split(System.IO.Path.GetInvalidFileNameChars(),
            StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (safeTitle.Length == 0) safeTitle = "widget";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title    = Loc("StrDashboardExportCsv"),
            // Two flavours: German Excel expects ';' + decimal comma; the old comma format
            // with current-culture numbers produced broken columns on German systems.
            Filter   = $"{Loc("StrCsvFilterExcel")}|*.csv|{Loc("StrCsvFilterIntl")}|*.csv",
            FileName = $"{safeTitle}_{w.Metric}_{DateTime.Now:yyyyMMdd_HHmm}.csv",
        };
        if (dlg.ShowDialog() != true) return;

        bool excelDe = dlg.FilterIndex == 1;
        char sep     = excelDe ? ';' : ',';
        var numCulture = excelDe
            ? System.Globalization.CultureInfo.GetCultureInfo("de-DE")
            : System.Globalization.CultureInfo.InvariantCulture;
        string Quote(string s) =>
            s.IndexOf(sep) >= 0 || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

        try
        {
            var nodeIds = w.NodeIds.Count > 0 ? w.NodeIds : _nodeNames.Keys.ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(sep, "Timestamp", "Node", Quote($"{w.Metric} ({unit})")));

            foreach (var id in nodeIds)
            {
                string name = Quote(NodeLabel(id));
                // Export exactly the widget's range — Days==0 ("Gesamt") exports everything
                // (this used to silently cap at 30 days)
                var pts = _db.GetTimeSeries(new[] { id }, w.Metric, w.Days)
                             .OrderBy(p => p.Timestamp);
                foreach (var p in pts)
                    sb.AppendLine(string.Join(sep,
                        p.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                        name,
                        p.Value.ToString("F4", numCulture)));
            }

            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show(
                string.Format(Loc("StrDashboardExportDone"), dlg.FileName),
                Loc("StrDashboardTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc("StrDashboardExportFailed")} {ex.Message}",
                Loc("StrDashboardTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

internal static class VisualTreeHelperExt
{
    public static T? FindVisualChild<T>(this DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }
}
