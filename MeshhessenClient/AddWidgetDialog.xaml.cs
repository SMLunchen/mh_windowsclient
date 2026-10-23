using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeshhessenClient.Models;
using ModernWpf;

namespace MeshhessenClient;

public class AddWidgetDialog : Window
{
    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    public DashboardWidget? Result { get; private set; }

    private readonly ComboBox   _widgetTypeCombo;
    private readonly ComboBox   _metricCombo;
    private readonly ComboBox   _daysCombo;
    private readonly TextBox    _titleBox;
    private readonly TextBox    _nodeSearchBox;
    private readonly StackPanel _nodeListPanel;
    private readonly TextBlock  _nodeHintText;
    private readonly TextBlock  _selCountText;
    private readonly StackPanel _metricSection;
    private readonly StackPanel _daysSection;
    private readonly StackPanel _nodeSection;
    private readonly StackPanel _chartOptionsSection;
    private readonly StackPanel _alarmSection;
    private readonly TextBox    _warnLowBox;
    private readonly TextBox    _warnHighBox;
    private readonly TextBox    _alarmLowBox;
    private readonly TextBox    _alarmHighBox;
    private readonly CheckBox   _maCheckBox;
    private readonly Dictionary<uint, string> _nodeNames;
    private readonly Dictionary<uint, string> _nodeShortNames;
    private readonly List<(uint id, string label, string shortName)> _allNodes;
    private readonly HashSet<uint> _selectedNodeIds = new();
    private bool _suppressSelectionTracking;

    private static readonly (string key, string label)[] Metrics =
    {
        ("snr",          "SNR (dB)"),
        ("rssi",         "RSSI (dBm)"),
        ("battery",      "Batterie (%)"),
        ("voltage",      "Spannung (V)"),
        ("channel_util", "Kanal-Auslastung (%)"),
        ("air_tx_util",  "TX-Auslastung (%)"),
        ("temperature",  "Temperatur (°C)"),
        ("humidity",     "Luftfeuchtigkeit (%)"),
        ("pressure",     "Luftdruck (hPa)"),
        ("packet_count", "Pakete pro Stunde"),
        ("noise_floor",       "Noise Floor (dBm)"),
        ("rx_pph",            "RX-Rate (Pakete/h)"),
        ("tx_pph",            "TX-Rate (Pakete/h)"),
        ("rx_bad_pph",        "RX fehlerhaft (Pakete/h)"),
        ("rx_dupe_pph",       "RX Duplikate (Pakete/h)"),
        ("tx_relay_pph",      "TX Relay (Pakete/h)"),
        ("rx_bad",            "RX fehlerhaft (gesamt)"),
        ("rx_dupe",           "RX Duplikate (gesamt)"),
        ("tx_relay",          "TX Relay (gesamt)"),
        ("tx_relay_canceled", "TX Relay abgebrochen (gesamt)"),
        ("tx_dropped",        "TX verworfen (gesamt)"),
    };

    private static readonly (string key, string labelKey, bool multiNode)[] WidgetTypes =
    {
        ("line",        "StrWidgetTypeLine",        true),
        ("area",        "StrWidgetTypeArea",        true),
        ("bar",         "StrWidgetTypeBar",         true),
        ("scatter",     "StrWidgetTypeScatter",     true),
        ("gauge",       "StrWidgetTypeGauge",       false),
        ("stat",        "StrWidgetTypeStat",        false),
        ("heatmap",     "StrWidgetTypeHeatmap",     false),
        ("histogram",   "StrWidgetTypeHistogram",   false),
        ("candlestick", "StrWidgetTypeCandlestick", false),
        ("stateline",   "StrWidgetTypeStateline",   false),
        ("ranking",     "StrWidgetTypeRanking",     true),
        ("multistat",   "StrWidgetTypeMultiStat",   true),
        ("meshhealth",  "StrWidgetTypeMeshHealth",  true),
        ("clock",       "StrWidgetTypeClock",       false),
    };

    public AddWidgetDialog(Dictionary<uint, string> nodeNames, Dictionary<uint, string>? nodeShortNames = null, DashboardWidget? existing = null)
    {
        _nodeNames      = nodeNames;
        _nodeShortNames = nodeShortNames ?? new Dictionary<uint, string>();
        _allNodes = nodeNames.OrderBy(kv => kv.Value)
                             .Select(kv =>
                             {
                                 string sn = _nodeShortNames.TryGetValue(kv.Key, out var s) && !string.IsNullOrWhiteSpace(s) ? s : string.Empty;
                                 string label = sn.Length > 0 ? $"{sn} | {kv.Value}" : kv.Value;
                                 return (kv.Key, label, sn);
                             })
                             .ToList();

        bool isDark = ThemeManager.Current.ActualApplicationTheme == ApplicationTheme.Dark;
        Color bgColor    = isDark ? Color.FromRgb(22, 27, 34)   : Color.FromRgb(250, 251, 253);
        Color fgSubColor = isDark ? Color.FromRgb(139, 148, 158) : Color.FromRgb(96, 105, 115);
        Color hintColor  = isDark ? Color.FromRgb(72, 79, 88)    : Color.FromRgb(170, 175, 183);

        Title  = existing != null ? Loc("StrDashboardEditWidget") : Loc("StrAddWidgetTitle");
        Width  = 460;
        Height = 740;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(bgColor);

        var outerStack = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };

        // ── Widget type ──
        outerStack.Children.Add(MakeLabel(Loc("StrAddWidgetType"), fgSubColor));
        _widgetTypeCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (key, labelKey, _) in WidgetTypes)
            _widgetTypeCombo.Items.Add(new ComboBoxItem { Tag = key, Content = Loc(labelKey) });
        _widgetTypeCombo.SelectedIndex = 0;
        _widgetTypeCombo.SelectionChanged += WidgetType_SelectionChanged;
        outerStack.Children.Add(_widgetTypeCombo);

        // ── Metric section ──
        _metricSection = new StackPanel();
        _metricSection.Children.Add(MakeLabel(Loc("StrAddWidgetMetric"), fgSubColor));
        _metricCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (key, label) in Metrics)
            _metricCombo.Items.Add(new ComboBoxItem { Tag = key, Content = label });
        _metricCombo.SelectedIndex = 0;
        _metricSection.Children.Add(_metricCombo);
        outerStack.Children.Add(_metricSection);

        // ── Days section ──
        _daysSection = new StackPanel();
        _daysSection.Children.Add(MakeLabel(Loc("StrAddWidgetDays"), fgSubColor));
        _daysCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (days, label) in new[]
        {
            (1,  Loc("StrDays1")),
            (7,  Loc("StrDays7")),
            (14, Loc("StrDays14")),
            (30, Loc("StrDays30")),
            (0,  Loc("StrDaysAll")),
        })
            _daysCombo.Items.Add(new ComboBoxItem { Tag = days, Content = label });
        _daysCombo.SelectedIndex = 1; // default 7 days
        _daysSection.Children.Add(_daysCombo);
        outerStack.Children.Add(_daysSection);

        // ── Node section ──
        _nodeSection = new StackPanel();

        var nodeLabelRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var nodeLabel    = MakeLabel(Loc("StrAddWidgetNodes"), fgSubColor);
        DockPanel.SetDock(nodeLabel, Dock.Left);
        _nodeHintText = new TextBlock
        {
            Text       = Loc("StrAddWidgetSingleNodeHint"),
            FontSize   = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(240, 76, 76)),
            VerticalAlignment   = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed,
            Margin     = new Thickness(6, 0, 0, 0),
        };
        DockPanel.SetDock(_nodeHintText, Dock.Right);
        nodeLabelRow.Children.Add(_nodeHintText);
        nodeLabelRow.Children.Add(nodeLabel);
        _nodeSection.Children.Add(nodeLabelRow);

        // Selection summary + select-all/none for the filtered view
        var selRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        _selCountText = new TextBlock
        {
            FontSize   = 10,
            Foreground = new SolidColorBrush(fgSubColor),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(_selCountText, Dock.Left);
        var selBtnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var selAllBtn  = MakeLinkButton(Loc("StrAddWidgetSelAll"));
        var selNoneBtn = MakeLinkButton(Loc("StrAddWidgetSelNone"));
        selAllBtn.Click += (_, _) =>
        {
            if (!CurrentTypeAllowsMulti()) return;
            foreach (var n in FilteredNodes(_nodeSearchBox.Text)) _selectedNodeIds.Add(n.id);
            PopulateNodeList(_nodeSearchBox.Text);
        };
        selNoneBtn.Click += (_, _) =>
        {
            _selectedNodeIds.Clear();
            PopulateNodeList(_nodeSearchBox.Text);
        };
        selBtnRow.Children.Add(selAllBtn);
        selBtnRow.Children.Add(selNoneBtn);
        DockPanel.SetDock(selBtnRow, Dock.Right);
        selRow.Children.Add(selBtnRow);
        selRow.Children.Add(_selCountText);
        _nodeSection.Children.Add(selRow);

        _nodeSearchBox = new TextBox
        {
            Padding  = new Thickness(6, 4, 6, 4),
            FontSize = 11,
        };
        _nodeSearchBox.TextChanged += NodeSearch_TextChanged;

        var searchHint = new TextBlock
        {
            Text       = Loc("StrAddWidgetNodeSearch"),
            FontSize   = 11,
            Foreground = new SolidColorBrush(hintColor),
            IsHitTestVisible = false,
            Margin     = new Thickness(8, 5, 0, 0),
        };
        var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        searchGrid.Children.Add(_nodeSearchBox);
        searchGrid.Children.Add(searchHint);
        _nodeSearchBox.TextChanged += (_, _) =>
        {
            searchHint.Visibility = string.IsNullOrEmpty(_nodeSearchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        };
        _nodeSection.Children.Add(searchGrid);

        // Checkbox list (explicit, unlike the old selection-highlight ListBox): selected
        // nodes are pinned on top and stay visible even when the filter doesn't match them.
        _nodeListPanel = new StackPanel();
        var nodeScroll = new ScrollViewer
        {
            Content = _nodeListPanel,
            Height  = 150,
            Margin  = new Thickness(0, 0, 0, 12),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(1),
            BorderBrush     = new SolidColorBrush(isDark ? Color.FromRgb(48, 54, 61) : Color.FromRgb(216, 222, 228)),
        };
        PopulateNodeList(null);
        _nodeSection.Children.Add(nodeScroll);
        outerStack.Children.Add(_nodeSection);

        // ── Title ──
        outerStack.Children.Add(MakeLabel(Loc("StrAddWidgetCustomTitle"), fgSubColor));
        _titleBox = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
        outerStack.Children.Add(_titleBox);

        // ── Warn/alarm zones (Grafana-style) — for value-based widget types ──
        _alarmSection = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        _alarmSection.Children.Add(MakeLabel(Loc("StrDashAlarmSection"), fgSubColor));

        var alarmGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        alarmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 90 });
        alarmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        alarmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        alarmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        alarmGrid.RowDefinitions.Add(new RowDefinition());
        alarmGrid.RowDefinitions.Add(new RowDefinition());
        alarmGrid.RowDefinitions.Add(new RowDefinition());

        TextBlock RowLabel(string text, Color color, int row)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(color), Margin = new Thickness(0, 0, 8, 6),
            };
            Grid.SetRow(tb, row); Grid.SetColumn(tb, 0);
            return tb;
        }
        TextBox ZoneBox(int row, int col)
        {
            var box = new TextBox { Margin = new Thickness(0, 0, 0, 6), FontSize = 11, MinWidth = 0 };
            Grid.SetRow(box, row); Grid.SetColumn(box, col);
            return box;
        }
        TextBlock ColHeader(string text, int col)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = 10, Foreground = new SolidColorBrush(fgSubColor),
                Margin = new Thickness(0, 0, 0, 2),
            };
            Grid.SetRow(tb, 0); Grid.SetColumn(tb, col);
            return tb;
        }

        alarmGrid.Children.Add(ColHeader(Loc("StrDashAlarmLowCol"), 1));
        alarmGrid.Children.Add(ColHeader(Loc("StrDashAlarmHighCol"), 3));
        alarmGrid.Children.Add(RowLabel(Loc("StrDashWarnLabel"),  Color.FromRgb(255, 171, 76), 1));
        alarmGrid.Children.Add(RowLabel(Loc("StrDashAlarmLabel"), Color.FromRgb(240,  76, 76), 2));
        _warnLowBox   = ZoneBox(1, 1);
        _warnHighBox  = ZoneBox(1, 3);
        _alarmLowBox  = ZoneBox(2, 1);
        _alarmHighBox = ZoneBox(2, 3);
        alarmGrid.Children.Add(_warnLowBox);
        alarmGrid.Children.Add(_warnHighBox);
        alarmGrid.Children.Add(_alarmLowBox);
        alarmGrid.Children.Add(_alarmHighBox);
        _alarmSection.Children.Add(alarmGrid);
        _alarmSection.Children.Add(new TextBlock
        {
            Text = Loc("StrDashAlarmHint"),
            FontSize = 10,
            Foreground = new SolidColorBrush(hintColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        outerStack.Children.Add(_alarmSection);

        // ── Chart options (MA overlay) — only for line/area ──
        _chartOptionsSection = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        _maCheckBox = new CheckBox
        {
            Content = Loc("StrDashboardMovingAvg"),
            Margin  = new Thickness(0, 0, 0, 12),
        };
        _chartOptionsSection.Children.Add(_maCheckBox);
        outerStack.Children.Add(_chartOptionsSection);

        // ── Buttons ──
        var btnRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var ok     = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc("StrCancel"), Width = 90, IsCancel = true };
        ok.Click += Ok_Click;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        outerStack.Children.Add(btnRow);

        Content = new ScrollViewer
        {
            Content = outerStack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        UpdateSectionVisibility();
        UpdateNodeSelectionMode();

        // Pre-fill if editing existing widget
        if (existing != null)
            PrefillFrom(existing);
    }

    private void PrefillFrom(DashboardWidget existing)
    {
        foreach (ComboBoxItem item in _widgetTypeCombo.Items)
            if (item.Tag is string t && t == existing.Type) { _widgetTypeCombo.SelectedItem = item; break; }

        foreach (ComboBoxItem item in _metricCombo.Items)
            if (item.Tag is string m && m == existing.Metric) { _metricCombo.SelectedItem = item; break; }

        foreach (ComboBoxItem item in _daysCombo.Items)
            if (item.Tag is int d && d == existing.Days) { _daysCombo.SelectedItem = item; break; }

        _titleBox.Text        = existing.Title;
        _maCheckBox.IsChecked = existing.ShowMovingAverage;

        static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
        var migrated = DashboardAlarms.Migrate(existing);
        _warnLowBox.Text   = Fmt(migrated.WarnLow);
        _warnHighBox.Text  = Fmt(migrated.WarnHigh);
        _alarmLowBox.Text  = Fmt(migrated.AlarmLow);
        _alarmHighBox.Text = Fmt(migrated.AlarmHigh);

        // Pre-seed _selectedNodeIds so filter changes preserve selection
        _selectedNodeIds.Clear();
        foreach (var id in existing.NodeIds) _selectedNodeIds.Add(id);
        PopulateNodeList(_nodeSearchBox.Text);
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    private void NodeSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        PopulateNodeList(_nodeSearchBox.Text);
    }

    private static bool MatchesNode(string needle, (uint id, string label, string shortName) n)
    {
        if (n.label.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (n.shortName.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        string hex = $"{n.id:x8}";
        if (hex.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if ($"!{hex}".Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (n.id.ToString().Contains(needle)) return true;
        if (hex.EndsWith(needle, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private IEnumerable<(uint id, string label, string shortName)> FilteredNodes(string? filter) =>
        string.IsNullOrWhiteSpace(filter) ? _allNodes : _allNodes.Where(n => MatchesNode(filter, n));

    private bool CurrentTypeAllowsMulti() =>
        WidgetTypes.FirstOrDefault(t => t.key == CurrentType).multiNode;

    /// <summary>Rebuild the checkbox list: selected nodes pinned on top (always visible,
    /// even when the filter doesn't match them), then the filtered rest. No auto-select.</summary>
    private void PopulateNodeList(string? filter)
    {
        _suppressSelectionTracking = true;
        _nodeListPanel.Children.Clear();

        var selected = _allNodes.Where(n => _selectedNodeIds.Contains(n.id));
        var rest     = FilteredNodes(filter).Where(n => !_selectedNodeIds.Contains(n.id));

        foreach (var (id, label, _) in selected.Concat(rest))
        {
            var cb = new CheckBox
            {
                Content   = label,
                Tag       = id,
                FontSize  = 11,
                MinWidth  = 0,
                Margin    = new Thickness(6, 2, 6, 2),
                IsChecked = _selectedNodeIds.Contains(id),
            };
            cb.Checked += (_, _) =>
            {
                if (_suppressSelectionTracking) return;
                uint nid = (uint)cb.Tag;
                if (!CurrentTypeAllowsMulti())
                {
                    // Radio behavior for single-node widget types
                    _selectedNodeIds.Clear();
                    _selectedNodeIds.Add(nid);
                    _suppressSelectionTracking = true;
                    foreach (CheckBox other in _nodeListPanel.Children.OfType<CheckBox>())
                        if (!ReferenceEquals(other, cb)) other.IsChecked = false;
                    _suppressSelectionTracking = false;
                }
                else
                {
                    _selectedNodeIds.Add(nid);
                }
                UpdateSelectionSummary();
            };
            cb.Unchecked += (_, _) =>
            {
                if (_suppressSelectionTracking) return;
                _selectedNodeIds.Remove((uint)cb.Tag);
                UpdateSelectionSummary();
            };
            _nodeListPanel.Children.Add(cb);
        }

        _suppressSelectionTracking = false;
        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary() =>
        _selCountText.Text = string.Format(Loc("StrAddWidgetSelCount"), _selectedNodeIds.Count);

    // ── Widget type changed ───────────────────────────────────────────────────

    private void WidgetType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSectionVisibility();
        UpdateNodeSelectionMode();
    }

    private string CurrentType =>
        (_widgetTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "line";

    private void UpdateSectionVisibility()
    {
        string type = CurrentType;
        bool isClock = type == "clock";
        bool showMA  = type is "line" or "area";
        // Zones make sense wherever a single metric value is judged against limits
        bool showAlarms = type is "line" or "area" or "bar" or "scatter"
                               or "gauge" or "stat" or "candlestick" or "histogram";
        _metricSection.Visibility       = isClock ? Visibility.Collapsed : Visibility.Visible;
        _daysSection.Visibility         = isClock ? Visibility.Collapsed : Visibility.Visible;
        _nodeSection.Visibility         = isClock ? Visibility.Collapsed : Visibility.Visible;
        _chartOptionsSection.Visibility = showMA ? Visibility.Visible : Visibility.Collapsed;
        _alarmSection.Visibility        = showAlarms ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateNodeSelectionMode()
    {
        bool allowMulti = CurrentTypeAllowsMulti();
        _nodeHintText.Visibility = allowMulti ? Visibility.Collapsed : Visibility.Visible;
        if (!allowMulti && _selectedNodeIds.Count > 1)
        {
            var keep = _allNodes.FirstOrDefault(n => _selectedNodeIds.Contains(n.id)).id;
            _selectedNodeIds.Clear();
            if (keep != 0) _selectedNodeIds.Add(keep);
        }
        PopulateNodeList(_nodeSearchBox.Text);
    }

    // ── OK handler ───────────────────────────────────────────────────────────

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string type = CurrentType;

        if (type == "clock")
        {
            string clockTitle = string.IsNullOrWhiteSpace(_titleBox.Text)
                ? Loc("StrWidgetTypeClock")
                : _titleBox.Text.Trim();
            Result = new DashboardWidget(
                Id:      Guid.NewGuid().ToString("N")[..8],
                Type:    "clock",
                Metric:  "none",
                NodeIds: new List<uint>(),
                Days:    0,
                Title:   clockTitle);
            DialogResult = true;
            Close();
            return;
        }

        // Stable order: as listed (alphabetical), not insertion order of the HashSet
        var nodeIds = _allNodes.Where(n => _selectedNodeIds.Contains(n.id))
                               .Select(n => n.id)
                               .ToList();
        if (nodeIds.Count == 0)
        {
            MessageBox.Show(Loc("StrAddWidgetNoNode"), Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string metric = (_metricCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "snr";
        int    days   = (_daysCombo.SelectedItem   as ComboBoxItem)?.Tag is int d ? d : 7;

        string metricLabel = (_metricCombo.SelectedItem as ComboBoxItem)?.Content as string ?? metric;
        string title = string.IsNullOrWhiteSpace(_titleBox.Text)
            ? metricLabel
            : _titleBox.Text.Trim();

        bool singleOnly = WidgetTypes.FirstOrDefault(t => t.key == type).multiNode == false;
        if (singleOnly && nodeIds.Count > 1)
            nodeIds = nodeIds.Take(1).ToList();

        static double ParseZone(TextBox box) => double.TryParse(
            box.Text.Replace(",", "."),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? v : double.NaN;
        bool showMA = _maCheckBox.IsChecked == true;

        Result = new DashboardWidget(
            Id:                Guid.NewGuid().ToString("N")[..8],
            Type:              type,
            Metric:            metric,
            NodeIds:           nodeIds,
            Days:              days,
            Title:             title,
            ShowMovingAverage: showMA,
            WarnLow:           ParseZone(_warnLowBox),
            WarnHigh:          ParseZone(_warnHighBox),
            AlarmLow:          ParseZone(_alarmLowBox),
            AlarmHigh:         ParseZone(_alarmHighBox));
        DialogResult = true;
        Close();
    }

    private static Button MakeLinkButton(string text) => new()
    {
        Content  = text,
        FontSize = 10,
        Padding  = new Thickness(6, 0, 6, 0),
        Margin   = new Thickness(4, 0, 0, 0),
        MinWidth = 0,
    };

    private static TextBlock MakeLabel(string text, Color color) => new()
    {
        Text       = text,
        FontSize   = 11,
        Margin     = new Thickness(0, 0, 0, 3),
        Foreground = new SolidColorBrush(color),
    };
}
