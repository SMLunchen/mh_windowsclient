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
    private readonly ListBox    _nodeListBox;
    private readonly TextBlock  _nodeHintText;
    private readonly StackPanel _metricSection;
    private readonly StackPanel _daysSection;
    private readonly StackPanel _nodeSection;
    private readonly StackPanel _chartOptionsSection;
    private readonly TextBox    _thresholdBox;
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
        Height = 640;
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

        _nodeListBox = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            Height        = 130,
            Margin        = new Thickness(0, 0, 0, 12),
        };
        ScrollViewer.SetCanContentScroll(_nodeListBox, false);
        _nodeListBox.SelectionChanged += NodeList_SelectionChanged;
        PopulateNodeList(null);
        _nodeSection.Children.Add(_nodeListBox);
        outerStack.Children.Add(_nodeSection);

        // ── Title ──
        outerStack.Children.Add(MakeLabel(Loc("StrAddWidgetCustomTitle"), fgSubColor));
        _titleBox = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
        outerStack.Children.Add(_titleBox);

        // ── Chart options (threshold + MA) — only for line/area ──
        _chartOptionsSection = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        _chartOptionsSection.Children.Add(MakeLabel(Loc("StrDashboardThreshold") + " (leer = aus):", fgSubColor));
        _thresholdBox = new TextBox
        {
            Margin  = new Thickness(0, 0, 0, 8),
            ToolTip = Loc("StrDashboardThresholdTip"),
        };
        _chartOptionsSection.Children.Add(_thresholdBox);
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
        _thresholdBox.Text    = double.IsNaN(existing.Threshold) ? "" : existing.Threshold.ToString("G4");
        _maCheckBox.IsChecked = existing.ShowMovingAverage;

        // Pre-seed _selectedNodeIds so filter changes preserve selection
        _selectedNodeIds.Clear();
        foreach (var id in existing.NodeIds) _selectedNodeIds.Add(id);

        // Apply to listbox (UnselectAll works in both Single and Multiple mode)
        _suppressSelectionTracking = true;
        _nodeListBox.UnselectAll();
        foreach (ListBoxItem item in _nodeListBox.Items)
        {
            if (item.Tag is not uint id || !_selectedNodeIds.Contains(id)) continue;
            if (_nodeListBox.SelectionMode == SelectionMode.Single)
            {
                _nodeListBox.SelectedItem = item;
                break;
            }
            _nodeListBox.SelectedItems.Add(item);
        }
        _suppressSelectionTracking = false;
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

    private void NodeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionTracking) return;
        foreach (ListBoxItem item in e.AddedItems.OfType<ListBoxItem>())
            if (item.Tag is uint id) _selectedNodeIds.Add(id);
        foreach (ListBoxItem item in e.RemovedItems.OfType<ListBoxItem>())
            if (item.Tag is uint id) _selectedNodeIds.Remove(id);
    }

    private void PopulateNodeList(string? filter)
    {
        _suppressSelectionTracking = true;
        _nodeListBox.Items.Clear();

        IEnumerable<(uint id, string label, string shortName)> query = string.IsNullOrWhiteSpace(filter)
            ? _allNodes
            : _allNodes.Where(n => MatchesNode(filter, n));

        foreach (var (id, label, _) in query)
            _nodeListBox.Items.Add(new ListBoxItem { Tag = id, Content = label });

        // Restore previously-selected nodes; only auto-select first if nothing was selected before
        bool anyRestored = false;
        foreach (ListBoxItem item in _nodeListBox.Items)
        {
            if (item.Tag is not uint id || !_selectedNodeIds.Contains(id)) continue;
            if (_nodeListBox.SelectionMode == SelectionMode.Single)
            {
                _nodeListBox.SelectedItem = item;
                anyRestored = true;
                break;
            }
            _nodeListBox.SelectedItems.Add(item);
            anyRestored = true;
        }

        if (!anyRestored && _nodeListBox.Items.Count > 0 && _selectedNodeIds.Count == 0)
            _nodeListBox.SelectedIndex = 0;

        _suppressSelectionTracking = false;
    }

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
        bool isClock   = type == "clock";
        bool showChart = type is "line" or "area";
        _metricSection.Visibility       = isClock ? Visibility.Collapsed : Visibility.Visible;
        _daysSection.Visibility         = isClock ? Visibility.Collapsed : Visibility.Visible;
        _nodeSection.Visibility         = isClock ? Visibility.Collapsed : Visibility.Visible;
        _chartOptionsSection.Visibility = showChart ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateNodeSelectionMode()
    {
        bool allowMulti = WidgetTypes.FirstOrDefault(t => t.key == CurrentType).multiNode;

        if (allowMulti)
        {
            _nodeListBox.SelectionMode = SelectionMode.Multiple;
            _nodeHintText.Visibility   = Visibility.Collapsed;
        }
        else
        {
            _nodeListBox.SelectionMode = SelectionMode.Single;
            _nodeHintText.Visibility   = Visibility.Visible;
            if (_nodeListBox.SelectedItems.Count > 1)
            {
                var first = _nodeListBox.SelectedItem;
                _nodeListBox.UnselectAll();
                _nodeListBox.SelectedItem = first;
            }
        }
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

        var nodeIds = _nodeListBox.SelectedItems.OfType<ListBoxItem>()
                                  .Select(i => (uint)(i.Tag ?? 0u))
                                  .Where(id => id != 0)
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

        double threshold = double.TryParse(
            _thresholdBox.Text.Replace(",", "."),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double tv)
            ? tv : double.NaN;
        bool showMA = _maCheckBox.IsChecked == true;

        Result = new DashboardWidget(
            Id:                Guid.NewGuid().ToString("N")[..8],
            Type:              type,
            Metric:            metric,
            NodeIds:           nodeIds,
            Days:              days,
            Title:             title,
            Threshold:         threshold,
            ShowMovingAverage: showMA);
        DialogResult = true;
        Close();
    }

    private static TextBlock MakeLabel(string text, Color color) => new()
    {
        Text       = text,
        FontSize   = 11,
        Margin     = new Thickness(0, 0, 0, 3),
        Foreground = new SolidColorBrush(color),
    };
}
