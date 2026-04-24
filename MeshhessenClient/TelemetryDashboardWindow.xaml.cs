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

    // ── Theme colors (instance, set from theme detection) ─────────────────────
    private readonly bool _isDark;
    private readonly Color _bgCard, _bgPage, _bgHeader, _borderCol;
    private readonly Color _fgMain, _fgSub, _fgMuted;
    private readonly OxyColor _oxyText, _oxyTick, _oxyGrid, _oxyLegendBg;

    // ── Fields ────────────────────────────────────────────────────────────────
    private readonly TelemetryDatabaseService _db;
    private readonly Dictionary<uint, string> _nodeNames;
    private DashboardStore _store = new();
    private Dashboard? _current;
    private bool _suppressComboEvent;
    private readonly DispatcherTimer _refreshTimer = new();
    private int _refreshIntervalSeconds = 30;
    private readonly Dictionary<string, DispatcherTimer> _clockTimers = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public TelemetryDashboardWindow(
        TelemetryDatabaseService db,
        Dictionary<uint, string> nodeNames)
    {
        InitializeComponent();
        _db        = db;
        _nodeNames = nodeNames;

        // Detect theme
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

        _oxyText     = OxyColor.FromRgb(_fgSub.R, _fgSub.G, _fgSub.B);
        _oxyTick     = OxyColor.FromRgb(_borderCol.R, _borderCol.G, _borderCol.B);
        _oxyGrid     = OxyColor.FromArgb(60, _borderCol.R, _borderCol.G, _borderCol.B);
        _oxyLegendBg = _isDark
            ? OxyColor.FromArgb(180,  13, 17, 23)
            : OxyColor.FromArgb(220, 255, 255, 255);

        ApplyThemeToWindow();

        PopulateAutoRefreshCombo();
        LoadStore();
        RebuildCombo();

        _refreshTimer.Tick += (_, _) => OnAutoRefreshTick();

        Loaded += (_, _) =>
        {
            RenderCurrentDashboard();
            StartAutoRefreshIfEnabled();
        };
    }

    private void ApplyThemeToWindow()
    {
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
        AutoRefreshCombo.SelectedIndex = 2;
    }

    private void AutoRefreshCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoRefreshCombo.SelectedItem is not ComboBoxItem item) return;
        _refreshIntervalSeconds = item.Tag is int s ? s : 0;
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
        RefreshDot.Opacity = 1.0;
        await Task.Delay(600);
        RefreshDot.Opacity = 0.0;

        RefreshAllCardsInPlace();
        LastRefreshText.Text = string.Format(Loc("StrDashboardLastRefreshed"),
            DateTime.Now.ToString("HH:mm:ss"));
    }

    private void RefreshAllCardsInPlace()
    {
        if (_current == null) return;
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
        }
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadStore()
    {
        try
        {
            if (FileExists(StoreFile))
                _store = JsonSerializer.Deserialize<DashboardStore>(
                    ReadAllText(StoreFile)) ?? new();
        }
        catch { _store = new(); }
    }

    private void SaveStore()
    {
        try
        {
            WriteAllText(StoreFile,
                JsonSerializer.Serialize(_store,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
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
        var dlg = new AddWidgetDialog(_nodeNames) { Owner = this };
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

    // ── Rendering ────────────────────────────────────────────────────────────

    private void RenderCurrentDashboard()
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
        foreach (var w in _current.Widgets)
            WidgetPanel.Children.Add(BuildCard(w));

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

        // Context menu: CSV export
        var ctxMenu = new ContextMenu();
        var exportItem = new MenuItem
        {
            Header = Loc("StrDashboardExportCsv"),
            Tag    = w,
        };
        exportItem.Click += (_, _) => ExportWidgetCsv(w);
        ctxMenu.Items.Add(exportItem);
        container.ContextMenu = ctxMenu;

        // Drop target for reordering — with visual highlight feedback
        container.AllowDrop = true;
        container.DragEnter += (_, e) =>
        {
            if (!e.Data.GetDataPresent("widget_id")) return;
            string srcId = (string)e.Data.GetData("widget_id")!;
            if (srcId != w.Id) card.BorderBrush = new SolidColorBrush(Color.FromRgb(31, 111, 235));
            e.Effects = DragDropEffects.Move;
        };
        container.DragLeave += (_, _) =>
            card.BorderBrush = new SolidColorBrush(_borderCol);
        container.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent("widget_id")) { e.Effects = DragDropEffects.Move; e.Handled = true; }
        };
        container.Drop += (_, e) =>
        {
            card.BorderBrush = new SolidColorBrush(_borderCol);
            if (!e.Data.GetDataPresent("widget_id")) return;
            string srcId = (string)e.Data.GetData("widget_id")!;
            if (srcId != w.Id) ReorderWidgets(srcId, w.Id);
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
                "heatmap"     => BuildPlotContent(BuildHeatmapModel(w)),
                "bar"         => BuildPlotContent(BuildBarModel(w)),
                "area"        => BuildPlotContent(BuildAreaModel(w)),
                "scatter"     => BuildPlotContent(BuildScatterModel(w)),
                "histogram"   => BuildPlotContent(BuildHistogramModel(w)),
                "candlestick" => BuildPlotContent(BuildCandlestickModel(w)),
                "stateline"   => BuildPlotContent(BuildStatelineModel(w)),
                "ranking"     => BuildRankingContent(w),
                "multistat"   => BuildMultiStatContent(w),
                "meshhealth"  => BuildMeshHealthContent(w),
                _             => BuildPlotContent(BuildLineModel(w)),
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

    private FrameworkElement BuildPlotContent(PlotModel model) => new PlotView
    {
        Model      = model,
        Background = Brushes.Transparent,
        Margin     = new Thickness(2, 0, 2, 4),
    };

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
            date.Text      = now.ToString("dddd, dd. MMMM yyyy");
        }
        Update();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Update();
        timer.Start();
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
        var series = _db.GetTimeSeries(new[] { nodeId }, w.Metric, w.Days > 0 ? w.Days : 7);
        var last   = series.OrderByDescending(p => p.Timestamp).FirstOrDefault();
        double val = last?.Value ?? double.NaN;

        (double min, double max, string unit) = MetricRange(w.Metric);
        double pct = double.IsNaN(val)
            ? 0
            : Math.Clamp((val - min) / (max - min), 0, 1);

        // Color ramp: red (low) → amber → green (high)
        Color arcColor = pct < 0.5
            ? InterpolateColor(Color.FromRgb(240,  76,  76), Color.FromRgb(255, 171,  76), pct * 2)
            : InterpolateColor(Color.FromRgb(255, 171,  76), Color.FromRgb( 63, 185, 126), (pct - 0.5) * 2);

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

        var pts = _db.GetTimeSeries(new[] { nodeId }, w.Metric, w.Days > 0 ? w.Days : 7)
                     .OrderByDescending(p => p.Timestamp).ToList();

        double current  = pts.Count > 0 ? pts[0].Value : double.NaN;
        double previous = pts.Count > 1 ? pts[1].Value : double.NaN;
        double delta    = !double.IsNaN(current) && !double.IsNaN(previous)
            ? current - previous : double.NaN;

        string nodeName   = nodeId != 0 && _nodeNames.TryGetValue(nodeId, out var nn) ? nn : "–";
        Color accentColor = WpfPalette[0];

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

        var bigVal = new TextBlock
        {
            Text = double.IsNaN(current) ? "–" : $"{current:F1}",
            FontSize   = 48,
            FontWeight = System.Windows.FontWeights.Light,
            Foreground = new SolidColorBrush(_fgMain),
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
            Text       = nodeName,
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
        AddTimeAxis(model);
        AddLinearAxis(model, min, max, unit);

        int ci = 0;
        foreach (var nodeId in w.NodeIds)
        {
            var pts = _db.GetTimeSeries(new[] { nodeId }, w.Metric, w.Days);
            if (pts.Count == 0) continue;
            var oxy  = Palette[ci % Palette.Length];
            var line = new LineSeries
            {
                Title               = NodeLabel(nodeId),
                Color               = oxy,
                StrokeThickness     = 1.8,
                MarkerType          = MarkerType.None,
                TrackerFormatString = "{0}\n{2:dd.MM HH:mm}\n{4:F2} " + unit,
            };
            var sorted = pts.OrderBy(x => x.Timestamp)
                            .Select(p => new DataPoint(DateTimeAxis.ToDouble(p.Timestamp), p.Value))
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
                    TrackerFormatString = "{0} MA\n{2:dd.MM HH:mm}\n{4:F2} " + unit,
                };
                foreach (var dp in MovingAverage(sorted)) maLine.Points.Add(dp);
                model.Series.Add(maLine);
            }
            ci++;
        }
        AddThresholdAnnotation(model, w);
        return model;
    }

    // ── Area chart ────────────────────────────────────────────────────────────

    private PlotModel BuildAreaModel(DashboardWidget w)
    {
        (double min, double max, string unit) = MetricRange(w.Metric);
        var model = MakeBaseModel();
        AddTimeAxis(model);
        AddLinearAxis(model, min, max, unit);

        int ci = 0;
        foreach (var nodeId in w.NodeIds)
        {
            var pts = _db.GetTimeSeries(new[] { nodeId }, w.Metric, w.Days);
            if (pts.Count == 0) continue;
            var oxy    = Palette[ci % Palette.Length];
            var series = new AreaSeries
            {
                Title           = NodeLabel(nodeId),
                Color           = oxy,
                Fill            = OxyColor.FromAColor(30, oxy),
                StrokeThickness = 1.5,
                MarkerType      = MarkerType.None,
                TrackerFormatString = "{0}\n{2:dd.MM HH:mm}\n{4:F2} " + unit,
            };
            var sorted = pts.OrderBy(x => x.Timestamp)
                            .Select(p => new DataPoint(DateTimeAxis.ToDouble(p.Timestamp), p.Value))
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
                    TrackerFormatString = "{0} MA\n{2:dd.MM HH:mm}\n{4:F2} " + unit,
                };
                foreach (var dp in MovingAverage(sorted)) maLine.Points.Add(dp);
                model.Series.Add(maLine);
            }
            ci++;
        }
        AddThresholdAnnotation(model, w);
        return model;
    }

    private static void AddThresholdAnnotation(PlotModel model, DashboardWidget w)
    {
        if (double.IsNaN(w.Threshold)) return;
        model.Annotations.Add(new LineAnnotation
        {
            Type            = LineAnnotationType.Horizontal,
            Y               = w.Threshold,
            Color           = OxyColor.FromArgb(200, 240, 76, 76),
            StrokeThickness = 1.5,
            LineStyle       = LineStyle.Dash,
            Text            = $"⊥ {w.Threshold:G4}",
            TextColor       = OxyColor.FromArgb(200, 240, 76, 76),
            FontSize        = 9,
        });
    }

    private static IEnumerable<DataPoint> MovingAverage(IList<DataPoint> points, int window = 12)
    {
        for (int i = 0; i < points.Count; i++)
        {
            int start = Math.Max(0, i - window + 1);
            double sum = 0;
            int cnt = 0;
            for (int j = start; j <= i; j++) { sum += points[j].Y; cnt++; }
            yield return new DataPoint(points[i].X, sum / cnt);
        }
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
            var pts = _db.GetTimeSeries(new[] { nodeId }, w.Metric, w.Days);
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
        return model;
    }

    // ── Heatmap ────────────────────────────────────────────────────────────────

    private PlotModel BuildHeatmapModel(DashboardWidget w)
    {
        var nodeId = w.NodeIds.FirstOrDefault();
        int days   = w.Days > 0 ? w.Days : 14;
        var data   = _db.GetHeatmapData(nodeId, w.Metric, days);

        (double min, double max, string unit) = MetricRange(w.Metric);

        // For packet_count we want 0-based scale, not min-1 fill
        bool isCount = w.Metric == "packet_count";
        var clean = new double[days, 24];
        for (int d = 0; d < days; d++)
            for (int h = 0; h < 24; h++)
                clean[d, h] = isCount
                    ? data[d, h]
                    : (double.IsNaN(data[d, h]) ? min - 1 : data[d, h]);

        // Auto-scale packet_count based on actual max
        double colorMin = isCount ? 0 : min;
        double colorMax = isCount
            ? Math.Max(1, Enumerable.Range(0, days)
                .SelectMany(d => Enumerable.Range(0, 24).Select(h => data[d, h]))
                .DefaultIfEmpty(1).Max())
            : max;

        var model = MakeBaseModel();
        model.Legends.Clear();

        model.Axes.Add(new LinearAxis
        {
            Position  = AxisPosition.Left,
            Title     = Loc("StrDashboardHourOfDay"),
            Minimum   = -0.5, Maximum = 23.5,
            MajorStep = 6,
            TextColor = _oxyText, TitleColor = _oxyText,
        });
        model.Axes.Add(new LinearAxis
        {
            Position   = AxisPosition.Bottom,
            Title      = Loc("StrDashboardDayIndex"),
            TextColor  = _oxyText, TitleColor = _oxyText,
        });
        model.Axes.Add(new LinearColorAxis
        {
            Position   = AxisPosition.Right,
            Minimum    = colorMin, Maximum = colorMax,
            Palette    = OxyPalettes.Viridis(64),
            Title      = unit,
            TextColor  = _oxyText, TitleColor = _oxyText,
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
        AddTimeAxis(model);
        AddLinearAxis(model, min, max, unit);

        int ci = 0;
        foreach (var nodeId in w.NodeIds)
        {
            var pts = _db.GetTimeSeries(new[] { nodeId }, w.Metric, w.Days);
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
                series.Points.Add(new ScatterPoint(DateTimeAxis.ToDouble(p.Timestamp), p.Value));
            model.Series.Add(series);
            ci++;
        }
        return model;
    }

    // ── Histogram ─────────────────────────────────────────────────────────────

    private PlotModel BuildHistogramModel(DashboardWidget w)
    {
        (double min, double max, string unit) = MetricRange(w.Metric);
        var nodeId = w.NodeIds.FirstOrDefault();
        var pts    = _db.GetTimeSeries(new[] { nodeId }, w.Metric, w.Days);

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
        return model;
    }

    // ── Candlestick (daily OHLC) ──────────────────────────────────────────────

    private PlotModel BuildCandlestickModel(DashboardWidget w)
    {
        (double min, double max, string unit) = MetricRange(w.Metric);
        var nodeId  = w.NodeIds.FirstOrDefault();
        var candles = _db.GetCandlestickData(nodeId, w.Metric, w.Days);

        var model = MakeBaseModel();
        model.Legends.Clear();
        AddTimeAxis(model);
        AddLinearAxis(model, min, max, unit);

        if (candles.Count == 0) return model;

        var series = new CandleStickSeries
        {
            IncreasingColor = OxyColor.FromRgb( 63, 185, 126),
            DecreasingColor = OxyColor.FromRgb(240,  76,  76),
            CandleWidth     = 0.6,
            StrokeThickness = 1.5,
        };
        foreach (var c in candles)
            series.Items.Add(new HighLowItem(
                DateTimeAxis.ToDouble(c.Time), c.High, c.Low, c.Open, c.Close));

        model.Series.Add(series);
        return model;
    }

    // ── State timeline (node activity per hour) ───────────────────────────────

    private PlotModel BuildStatelineModel(DashboardWidget w)
    {
        var nodeId = w.NodeIds.FirstOrDefault();
        int days   = w.Days > 0 ? w.Days : 14;
        var data   = _db.GetNodeActivityGrid(nodeId, days);

        var model = MakeBaseModel();
        model.Legends.Clear();

        model.Axes.Add(new LinearAxis
        {
            Position   = AxisPosition.Left,
            Title      = Loc("StrDashboardHourOfDay"),
            Minimum    = -0.5, Maximum = 23.5,
            MajorStep  = 6,
            TextColor  = _oxyText, TitleColor = _oxyText,
        });
        model.Axes.Add(new LinearAxis
        {
            Position   = AxisPosition.Bottom,
            Title      = Loc("StrDashboardDayIndex"),
            TextColor  = _oxyText, TitleColor = _oxyText,
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

    private void AddTimeAxis(PlotModel model)
    {
        model.Axes.Add(new DateTimeAxis
        {
            Position           = AxisPosition.Bottom,
            StringFormat       = "dd.MM HH:mm",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = _oxyGrid,
            TextColor          = _oxyText,
            TicklineColor      = _oxyTick,
        });
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
        });
    }

    // ── Edit / Remove widget ──────────────────────────────────────────────────

    private void EditWidget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DashboardWidget w) return;
        if (_current == null) return;

        var dlg = new AddWidgetDialog(_nodeNames, existing: w) { Owner = this };
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
            "packet_count" => (0.0,   100.0,   "Pakete/h"),
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
                Foreground = new SolidColorBrush(active ? WpfPalette[0] : _fgSub),
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
            Foreground = new SolidColorBrush(w.ShowMovingAverage ? WpfPalette[0] : _fgSub),
            FontWeight = w.ShowMovingAverage ? System.Windows.FontWeights.SemiBold
                                             : System.Windows.FontWeights.Normal,
        };
        maBtn.Click += (_, _) => ToggleMovingAverage((string)maBtn.Tag!);
        panel.Children.Add(maBtn);

        panel.Children.Add(new TextBlock
        {
            Text = " " + Loc("StrDashboardThreshold") + ":",
            Foreground = new SolidColorBrush(_fgSub),
            FontSize = 10,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        });

        var threshBox = new TextBox
        {
            Width   = 52,
            Height  = 20,
            FontSize = 10,
            Padding = new Thickness(3, 0, 3, 0),
            Text    = double.IsNaN(w.Threshold) ? "" : w.Threshold.ToString("G4"),
            Tag     = w.Id,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin  = new Thickness(3, 0, 0, 0),
            ToolTip = Loc("StrDashboardThreshold"),
        };
        threshBox.LostFocus += (_, _) =>
        {
            string id  = (string)threshBox.Tag!;
            double val = double.TryParse(threshBox.Text.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v)
                ? v : double.NaN;
            UpdateWidgetThreshold(id, val);
        };
        panel.Children.Add(threshBox);

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

    private void UpdateWidgetThreshold(string widgetId, double threshold)
    {
        if (_current == null) return;
        int dashIdx = _store.Dashboards.IndexOf(_current);
        if (dashIdx < 0) return;
        int wIdx = _current.Widgets.FindIndex(x => x.Id == widgetId);
        if (wIdx < 0) return;
        var old = _current.Widgets[wIdx];
        bool sameNaN = double.IsNaN(old.Threshold) && double.IsNaN(threshold);
        if (sameNaN) return;
        if (!double.IsNaN(old.Threshold) && !double.IsNaN(threshold) &&
            Math.Abs(old.Threshold - threshold) < 1e-9) return;
        var updated = old with { Threshold = threshold };
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
                var pts = _db.GetTimeSeries(new[] { id }, w.Metric, w.Days > 0 ? w.Days : 7);
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
                Color rowColor = i == 0 ? WpfPalette[1]
                               : i == 1 ? WpfPalette[2]
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
            var values = _db.GetTimeSeries(new[] { id }, w.Metric, w.Days > 0 ? w.Days : 7)
                            .Select(p => p.Value).ToList();
            double last = values.Count > 0
                ? _db.GetTimeSeries(new[] { id }, w.Metric, w.Days > 0 ? w.Days : 7)
                      .OrderByDescending(p => p.Timestamp).First().Value
                : double.NaN;
            double min = values.Count > 0 ? values.Min() : double.NaN;
            double max = values.Count > 0 ? values.Max() : double.NaN;
            double avg = values.Count > 0 ? values.Average() : double.NaN;

            Color accent = WpfPalette[ci % WpfPalette.Length];
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
        var nodeIds     = w.NodeIds.Count > 0 ? w.NodeIds : _nodeNames.Keys.ToList();
        int lookbackDays = w.Days > 0 ? w.Days : 1;

        var wrap = new WrapPanel { Margin = new Thickness(6), Orientation = Orientation.Horizontal };

        foreach (var id in nodeIds)
        {
            var pts = _db.GetTimeSeries(new[] { id }, "snr", lookbackDays);
            double lastSnr = pts.Count > 0
                ? pts.OrderByDescending(p => p.Timestamp).First().Value
                : double.NaN;
            bool stale = pts.Count == 0 ||
                         (DateTime.UtcNow - pts.Max(p => p.Timestamp)).TotalHours > 24;

            Color ledColor;
            string status;
            if (stale || double.IsNaN(lastSnr))
            {
                ledColor = Color.FromRgb(240,  76,  76);
                status   = Loc("StrMeshHealthOffline");
            }
            else if (lastSnr >= -5)
            {
                ledColor = Color.FromRgb( 63, 185, 126);
                status   = Loc("StrMeshHealthGood");
            }
            else if (lastSnr >= -15)
            {
                ledColor = Color.FromRgb(255, 171,  76);
                status   = Loc("StrMeshHealthWeak");
            }
            else
            {
                ledColor = Color.FromRgb(240,  76,  76);
                status   = Loc("StrMeshHealthPoor");
            }

            string name = NodeLabel(id);

            var tile = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(12, ledColor.R, ledColor.G, ledColor.B)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(40, ledColor.R, ledColor.G, ledColor.B)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(5),
                Margin          = new Thickness(3),
                Padding         = new Thickness(8, 6, 8, 6),
                MinWidth        = 110,
                ToolTip         = $"{name}  SNR: {(double.IsNaN(lastSnr) ? "–" : $"{lastSnr:F1} dB")}  {status}",
            };

            var row = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };

            var led = new System.Windows.Shapes.Ellipse
            {
                Width  = 10,
                Height = 10,
                Fill   = new SolidColorBrush(ledColor),
                Effect = new DropShadowEffect
                {
                    Color       = ledColor,
                    BlurRadius  = 6,
                    ShadowDepth = 0,
                    Opacity     = 0.7,
                },
                Margin            = new Thickness(0, 0, 7, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };
            row.Children.Add(led);

            var info = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text         = name.Length > 14 ? name[..14] : name,
                FontSize     = 11,
                Foreground   = new SolidColorBrush(_fgMain),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            info.Children.Add(new TextBlock
            {
                Text       = double.IsNaN(lastSnr) ? "–" : $"{lastSnr:F1} dB",
                FontSize   = 9,
                Foreground = new SolidColorBrush(_fgMuted),
            });
            row.Children.Add(info);

            tile.Child = row;
            wrap.Children.Add(tile);
        }

        if (wrap.Children.Count == 0)
            wrap.Children.Add(new TextBlock
            {
                Text       = Loc("StrDashboardNoData"),
                Foreground = new SolidColorBrush(_fgMuted),
                FontSize   = 11,
                Margin     = new Thickness(4),
            });

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

    // ── CSV Export ────────────────────────────────────────────────────────────

    private void ExportWidgetCsv(DashboardWidget w)
    {
        (_, _, string unit) = MetricRange(w.Metric);
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title    = Loc("StrDashboardExportCsv"),
            Filter   = "CSV (*.csv)|*.csv",
            FileName = $"{w.Title}_{w.Metric}_{DateTime.Now:yyyyMMdd_HHmm}.csv",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var nodeIds = w.NodeIds.Count > 0 ? w.NodeIds : _nodeNames.Keys.ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp,Node,{w.Metric} ({unit})");

            foreach (var id in nodeIds)
            {
                string name = NodeLabel(id);
                var pts = _db.GetTimeSeries(new[] { id }, w.Metric, w.Days > 0 ? w.Days : 30)
                             .OrderBy(p => p.Timestamp);
                foreach (var p in pts)
                    sb.AppendLine($"{p.Timestamp:yyyy-MM-dd HH:mm:ss},{name},{p.Value:F4}");
            }

            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show(
                string.Format(Loc("StrDashboardExportDone"), dlg.FileName),
                Loc("StrDashboardTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export fehlgeschlagen: {ex.Message}",
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
