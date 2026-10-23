using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MeshhessenClient.Models;
using MeshhessenClient.Services;
using ModernWpf;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using FontWeights = System.Windows.FontWeights;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Path = System.Windows.Shapes.Path;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace MeshhessenClient;

/// <summary>Funk-Echo window "Wer funkt mich an?": journal of directed requests other
/// stations sent to our node, plus a 7-day curiosity statistic. V1 — the interactive
/// radar view is planned as a third tab later.</summary>
public class RequestEchoWindow : Window
{
    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    private readonly TelemetryDatabaseService _db;
    private readonly Func<uint, string> _shortLabel;
    private readonly Func<uint, string> _fullName;
    private readonly Func<bool> _casualTone;
    private readonly Action<uint>? _openNodeInfo;
    private readonly Action<uint>? _showOnMap;
    private readonly Action<uint>? _openDm;
    private readonly Func<ModemPreset?>? _modemPreset;
    private readonly Action<uint, List<uint>>? _showRouteOnMap;

    // Radar state — retro sonar scope: black field, phosphor-green rings/markers, a
    // rotating sweep, and movie-style ping animations on a separate overlay canvas
    // (the overlay survives RenderRadar rebuilds, so running pings aren't cut off).
    private readonly Canvas _radarCanvas = new() { ClipToBounds = true };
    private readonly Canvas _pingOverlay = new() { ClipToBounds = true, Background = Brushes.Transparent, IsHitTestVisible = false };
    private readonly Border _drillPanel = new() { Visibility = Visibility.Collapsed };
    private List<TelemetryDatabaseService.RequestEchoRow> _radarRows = new();
    private uint _radarSelected;
    private readonly Dictionary<uint, (double X, double Y)> _radarPositions = new();
    private (double X, double Y, double MaxR) _radarScope;

    // Phosphor palette
    private static readonly Color PhosBright = Color.FromRgb(0x53, 0xFF, 0xB0);
    private static readonly Color PhosMid    = Color.FromRgb(0x20, 0xC9, 0x97);
    private static readonly Color PhosDim    = Color.FromRgb(0x17, 0x6B, 0x4E);
    private static readonly Color PhosGrid   = Color.FromRgb(0x14, 0x52, 0x3C);

    // Filters (shared range for journal + stats; type/search apply to the journal)
    private int _rangeDays = 7;
    private string? _typeFilter;
    private string _searchText = string.Empty;

    private readonly bool _isDark;
    private readonly Color _bgCard, _borderCol, _fgMain, _fgSub, _fgMuted;

    private readonly StackPanel _journalPanel = new();
    private readonly StackPanel _statsPanel   = new() { Margin = new Thickness(16) };
    private readonly DispatcherTimer _refreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private static readonly Dictionary<string, (string icon, Color color)> TypeStyle = new()
    {
        ["NodeInfo"]   = ("👋", Color.FromRgb(0x8B, 0x73, 0xD8)),
        ["Position"]   = ("📍", Color.FromRgb(0xDB, 0x9A, 0x44)),
        ["Telemetry"]  = ("🔋", Color.FromRgb(0x20, 0xC9, 0x97)),
        ["Traceroute"] = ("🛰️", Color.FromRgb(0x42, 0x8E, 0xCC)),
        ["Admin"]      = ("🛠️", Color.FromRgb(0xF0, 0x4C, 0x4C)),
    };

    public RequestEchoWindow(TelemetryDatabaseService db, Func<uint, string> shortLabel,
        Func<uint, string> fullName, Func<bool> casualTone,
        Action<uint>? openNodeInfo = null, Action<uint>? showOnMap = null, Action<uint>? openDm = null,
        Func<ModemPreset?>? modemPreset = null, Action<uint, List<uint>>? showRouteOnMap = null)
    {
        _db = db;
        _shortLabel = shortLabel;
        _fullName = fullName;
        _casualTone = casualTone;
        _openNodeInfo = openNodeInfo;
        _showOnMap = showOnMap;
        _openDm = openDm;
        _modemPreset = modemPreset;
        _showRouteOnMap = showRouteOnMap;

        _isDark    = ThemeManager.Current.ActualApplicationTheme == ApplicationTheme.Dark;
        _bgCard    = _isDark ? Color.FromRgb(22, 27, 34)    : Color.FromRgb(250, 251, 253);
        _borderCol = _isDark ? Color.FromRgb(48, 54, 61)    : Color.FromRgb(216, 222, 228);
        _fgMain    = _isDark ? Color.FromRgb(230, 237, 243) : Color.FromRgb(31, 35, 40);
        _fgSub     = _isDark ? Color.FromRgb(139, 148, 158) : Color.FromRgb(96, 105, 115);
        _fgMuted   = _isDark ? Color.FromRgb(110, 118, 129) : Color.FromRgb(140, 149, 158);

        Title  = Loc("StrFunkEchoTitle");
        Width  = 560;
        Height = 620;
        MinWidth  = 420;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(_isDark ? Color.FromRgb(13, 17, 23) : Color.FromRgb(243, 245, 247));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        root.Children.Add(BuildFilterBar());

        var tabs = new TabControl { Margin = new Thickness(8, 0, 8, 8) };
        tabs.Items.Add(new TabItem
        {
            Header  = Loc("StrFunkEchoTabJournal"),
            Content = new ScrollViewer
            {
                Content = _journalPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8, 4, 8, 8),
            },
        });
        tabs.Items.Add(new TabItem
        {
            Header  = Loc("StrFunkEchoTabRadar"),
            Content = BuildRadarTab(),
        });
        tabs.Items.Add(new TabItem
        {
            Header  = Loc("StrFunkEchoTabStats"),
            Content = new ScrollViewer
            {
                Content = _statsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        });
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        Content = root;

        _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); Refresh(); };

        // Win32 quirk (same as TelemetryWindow): activate the owner before the active
        // owned window is destroyed, or the main window can minimize to the taskbar.
        Closing += (_, _) => { try { Owner?.Activate(); } catch { } };

        Refresh();
    }

    /// <summary>Filter bar: time range (journal + stats), type and node search (journal).</summary>
    private FrameworkElement BuildFilterBar()
    {
        var bar = new WrapPanel { Margin = new Thickness(12, 10, 12, 6), Orientation = Orientation.Horizontal };

        var rangeCombo = new ComboBox { MinWidth = 110, Margin = new Thickness(0, 0, 8, 0), FontSize = 11 };
        foreach (var (days, label) in new[]
        {
            (1, Loc("StrFunkEchoRange24h")), (7, Loc("StrFunkEchoRange7d")), (30, Loc("StrFunkEchoRange30d")),
        })
            rangeCombo.Items.Add(new ComboBoxItem { Tag = days, Content = label });
        rangeCombo.SelectedIndex = 1;
        rangeCombo.SelectionChanged += (_, _) =>
        {
            if ((rangeCombo.SelectedItem as ComboBoxItem)?.Tag is int d) { _rangeDays = d; Refresh(); }
        };
        bar.Children.Add(rangeCombo);

        var typeCombo = new ComboBox { MinWidth = 130, Margin = new Thickness(0, 0, 8, 0), FontSize = 11 };
        typeCombo.Items.Add(new ComboBoxItem { Tag = null, Content = Loc("StrFunkEchoFilterAll") });
        foreach (var t in new[] { "NodeInfo", "Position", "Telemetry", "Traceroute", "Admin" })
        {
            var (icon, _) = TypeStyle[t];
            typeCombo.Items.Add(new ComboBoxItem { Tag = t, Content = $"{icon} {Loc("StrFunkEchoType" + t)}" });
        }
        typeCombo.SelectedIndex = 0;
        typeCombo.SelectionChanged += (_, _) =>
        {
            _typeFilter = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            Refresh();
        };
        bar.Children.Add(typeCombo);

        var searchBox = new TextBox
        {
            MinWidth = 140, FontSize = 11, Padding = new Thickness(6, 3, 6, 3),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc("StrFunkEchoFilterSearchTip"),
        };
        searchBox.TextChanged += (_, _) => { _searchText = searchBox.Text.Trim(); Refresh(); };
        bar.Children.Add(searchBox);

        return bar;
    }

    private bool MatchesFilters(TelemetryDatabaseService.RequestEchoRow row)
    {
        if (_typeFilter != null && row.Type != _typeFilter) return false;
        if (_searchText.Length == 0) return true;
        string hex = $"!{row.NodeId:x8}";
        return _shortLabel(row.NodeId).Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || _fullName(row.NodeId).Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || hex.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Called from the main window when a new request arrives while open.
    /// Refresh is debounced so a burst doesn't rebuild the list 30 times — but the sonar
    /// ping fires immediately for that movie moment.</summary>
    public void NotifyNewEvent(uint fromId = 0)
    {
        if (!IsVisible) return;
        if (fromId != 0) PingStation(fromId);
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    public void Refresh()
    {
        try
        {
            var rows = _db.GetRequestEchoJournal(DateTime.UtcNow.AddDays(-_rangeDays))
                          .Where(MatchesFilters).ToList();
            BuildJournal(rows);
            _radarRows = rows;
            RenderRadar();
            BuildStats(_db.GetRequestEchoStats(_rangeDays), rows);
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[FunkEcho] window refresh failed: {ex.Message}");
        }
    }

    // ── Journal ───────────────────────────────────────────────────────────────

    private void BuildJournal(List<TelemetryDatabaseService.RequestEchoRow> rows)
    {
        _journalPanel.Children.Clear();

        if (rows.Count == 0)
        {
            _journalPanel.Children.Add(new TextBlock
            {
                Text = Loc("StrFunkEchoEmpty"),
                Foreground = new SolidColorBrush(_fgMuted),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 24, 8, 0),
                FontSize = 12,
            });
            return;
        }

        DateTime today = DateTime.Now.Date;
        DateTime? currentGroup = null;
        foreach (var row in rows)
        {
            var localDay = row.LastUtc.ToLocalTime().Date;
            if (currentGroup != localDay)
            {
                currentGroup = localDay;
                string header = localDay == today ? Loc("StrFunkEchoToday")
                              : localDay == today.AddDays(-1) ? Loc("StrFunkEchoYesterday")
                              : localDay.ToString("dd.MM.yyyy");
                _journalPanel.Children.Add(new TextBlock
                {
                    Text = header,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(_fgSub),
                    Margin = new Thickness(2, 10, 0, 4),
                });
            }
            _journalPanel.Children.Add(BuildJournalRow(row));
        }
    }

    private FrameworkElement BuildJournalRow(TelemetryDatabaseService.RequestEchoRow row)
    {
        var (icon, color) = TypeStyle.TryGetValue(row.Type, out var s)
            ? s : ("❔", _fgSub);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(14),
            Width = 28, Height = 28,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = icon, FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        string who = _shortLabel(row.NodeId);
        bool casual = _casualTone();
        string tonePrefix = casual ? "StrFunkEchoCasual" : "StrFunkEchoFactual";
        string msg = row.Type == "Telemetry" && row.Detail != null
            ? string.Format(Loc(tonePrefix + "TelemetryDetail"), who, Loc("StrFunkEchoTelem" + row.Detail))
            : string.Format(Loc(tonePrefix + row.Type), who);

        string src = row.ViaMqtt ? Loc("StrFunkEchoSrcMqtt")
                   : row.Hops == 0 ? Loc("StrFunkEchoSrcDirect")
                   : row.Hops is int h ? string.Format(Loc("StrFunkEchoSrcHops"), h)
                   : "–";
        string typeLabel = row.Type == "Telemetry" && row.Detail != null
            ? $"{Loc("StrFunkEchoType" + row.Type)} · {Loc("StrFunkEchoTelem" + row.Detail)}"
            : Loc("StrFunkEchoType" + row.Type);
        var subParts = new List<string>
        {
            row.LastUtc.ToLocalTime().ToString("HH:mm"),
            typeLabel,
            src,
        };
        if (!string.IsNullOrEmpty(row.Route))
        {
            var hopsVia = row.Route.Split(',')
                .Select(idStr => uint.TryParse(idStr.TrimStart('!'),
                    System.Globalization.NumberStyles.HexNumber, null, out var id) ? _shortLabel(id) : idStr);
            subParts.Add(string.Format(Loc("StrFunkEchoVia"), string.Join(" → ", hopsVia)));
        }
        if (ExpectationText(row.Expected) is { } expText)
            subParts.Add(expText);

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var msgLine = new TextBlock
        {
            FontSize = 12.5,
            Foreground = new SolidColorBrush(_fgMain),
            TextWrapping = TextWrapping.Wrap,
        };
        msgLine.Inlines.Add(new System.Windows.Documents.Run(_fullName(row.NodeId) + "  ")
        {
            FontWeight = FontWeights.SemiBold,
        });
        msgLine.Inlines.Add(new System.Windows.Documents.Run(msg)
        {
            Foreground = new SolidColorBrush(_fgSub),
        });
        textPanel.Children.Add(msgLine);
        textPanel.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", subParts),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(_fgMuted),
            Margin = new Thickness(0, 1, 0, 0),
        });
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        if (row.Count > 1)
        {
            var countBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 1, 7, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = $"×{row.Count}",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(color),
                },
            };
            Grid.SetColumn(countBadge, 2);
            grid.Children.Add(countBadge);
        }

        var rowBorder = new Border
        {
            Background = new SolidColorBrush(_bgCard),
            BorderBrush = new SolidColorBrush(_borderCol),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 2, 0, 2),
            Child = grid,
        };

        // Context menu: jump to the requesting node's existing functions (+ inbound
        // route on the map for traceroute requests)
        rowBorder.ContextMenu = BuildNodeContextMenu(row.NodeId, row.Route);

        return rowBorder;
    }

    /// <summary>Human text for the stored reply-expectation code (null = none stored).</summary>
    private static string? ExpectationText(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        if (code.StartsWith("prec:") && int.TryParse(code[5..], out int bits))
        {
            double km = Services.TriangulationService.PrecisionBitsToKm(bits);
            string dist = km >= 1 ? $"±{km:F1} km" : $"±{km * 1000:F0} m";
            return string.Format(Loc("StrFunkEchoExpPrec"), dist);
        }
        return code switch
        {
            "yes"    => Loc("StrFunkEchoExpYes"),
            "no-pos" => Loc("StrFunkEchoExpNoPos"),
            "sensor" => Loc("StrFunkEchoExpSensor"),
            "auth"   => Loc("StrFunkEchoExpAuth"),
            _        => null,
        };
    }

    // ── Curiosity radar ──────────────────────────────────────────────────────

    private FrameworkElement BuildRadarTab()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _radarCanvas.Background = new SolidColorBrush(Color.FromRgb(0x01, 0x05, 0x03));
        _radarCanvas.SizeChanged += (_, _) => RenderRadar();
        grid.Children.Add(_radarCanvas);
        grid.Children.Add(_pingOverlay);   // same cell, on top

        _drillPanel.Background = new SolidColorBrush(_bgCard);
        _drillPanel.BorderBrush = new SolidColorBrush(_borderCol);
        _drillPanel.BorderThickness = new Thickness(0, 1, 0, 0);
        _drillPanel.Padding = new Thickness(10, 6, 10, 6);
        Grid.SetRow(_drillPanel, 1);
        grid.Children.Add(_drillPanel);

        return grid;
    }

    /// <summary>Retro sonar scope: us centered, requesters around us. More requests =
    /// closer + bigger + brighter phosphor green; the angle derives from the node id, so
    /// stations keep their place across refreshes. A sweep rotates like in the movies.</summary>
    private void RenderRadar()
    {
        _radarCanvas.Children.Clear();
        double w = _radarCanvas.ActualWidth, h = _radarCanvas.ActualHeight;
        if (w < 80 || h < 80) return;
        double cx = w / 2, cy = h / 2;
        double maxR = Math.Min(w, h) / 2 - 34;
        _radarScope = (cx, cy, maxR);
        _radarPositions.Clear();

        // Scope disc: subtle green-black radial gradient
        var scope = new Ellipse
        {
            Width = maxR * 2 + 24, Height = maxR * 2 + 24,
            Fill = new RadialGradientBrush(
                Color.FromRgb(0x07, 0x1E, 0x13),
                Color.FromRgb(0x01, 0x07, 0x04)),
            Stroke = new SolidColorBrush(PhosDim),
            StrokeThickness = 1.5,
        };
        Canvas.SetLeft(scope, cx - maxR - 12);
        Canvas.SetTop(scope, cy - maxR - 12);
        _radarCanvas.Children.Add(scope);

        // Range rings + crosshair
        foreach (double rr in new[] { 0.33, 0.66, 1.0 })
        {
            var ring = new Ellipse
            {
                Width = maxR * 2 * rr, Height = maxR * 2 * rr,
                Stroke = new SolidColorBrush(Color.FromArgb(150, PhosGrid.R, PhosGrid.G, PhosGrid.B)),
                StrokeThickness = 1,
            };
            Canvas.SetLeft(ring, cx - maxR * rr);
            Canvas.SetTop(ring, cy - maxR * rr);
            _radarCanvas.Children.Add(ring);
        }
        var gridBrush = new SolidColorBrush(Color.FromArgb(90, PhosGrid.R, PhosGrid.G, PhosGrid.B));
        _radarCanvas.Children.Add(new Line { X1 = cx - maxR, Y1 = cy, X2 = cx + maxR, Y2 = cy, Stroke = gridBrush, StrokeThickness = 1 });
        _radarCanvas.Children.Add(new Line { X1 = cx, Y1 = cy - maxR, X2 = cx, Y2 = cy + maxR, Stroke = gridBrush, StrokeThickness = 1 });

        // Rotating sweep (gradient wedge, 5 s per revolution)
        var wedge = new PathGeometry();
        const double wedgeDeg = 42;
        double rad = wedgeDeg * Math.PI / 180;
        var fig = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
        fig.Segments.Add(new LineSegment(new Point(maxR, 0), false));
        fig.Segments.Add(new ArcSegment(new Point(maxR * Math.Cos(rad), -maxR * Math.Sin(rad)),
            new Size(maxR, maxR), 0, false, SweepDirection.Counterclockwise, false));
        wedge.Figures.Add(fig);
        var sweepRotate = new RotateTransform(0);
        var sweep = new Path
        {
            Data = wedge,
            Fill = new LinearGradientBrush(
                Color.FromArgb(110, PhosMid.R, PhosMid.G, PhosMid.B),
                Color.FromArgb(0, PhosMid.R, PhosMid.G, PhosMid.B),
                new Point(1, 0.95), new Point(0.6, 0.2)),
            RenderTransform = sweepRotate,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(sweep, cx);
        Canvas.SetTop(sweep, cy);
        _radarCanvas.Children.Add(sweep);
        sweepRotate.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(5))
        {
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
        });

        var stations = _radarRows
            .GroupBy(r => r.NodeId)
            .Select(g => new
            {
                NodeId = g.Key,
                Total = g.Sum(r => r.Count),
                PerType = g.GroupBy(r => r.Type).ToDictionary(t => t.Key, t => t.Sum(r => r.Count)),
            })
            .OrderByDescending(s => s.Total)
            .Take(24)
            .ToList();

        if (stations.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = Loc("StrFunkEchoEmpty"),
                Foreground = new SolidColorBrush(PhosMid),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(120, w - 40),
            };
            _radarCanvas.Children.Add(empty);
            Canvas.SetLeft(empty, 20);
            Canvas.SetTop(empty, 20);
        }

        double maxLog = stations.Count > 0 ? Math.Log(stations[0].Total + 1) : 1;
        foreach (var s in stations)
        {
            double frac = Math.Log(s.Total + 1) / Math.Max(maxLog, 1e-9);     // 0..1, 1 = busiest
            var (x, y) = RadarPosition(s.NodeId, frac);
            _radarPositions[s.NodeId] = (x, y);

            // Brightness follows activity: dim phosphor → bright phosphor
            Color dotColor = LerpColor(PhosDim, PhosBright, 0.35 + 0.65 * frac);

            var line = new Line
            {
                X1 = cx, Y1 = cy, X2 = x, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb(110, PhosDim.R, PhosDim.G, PhosDim.B)),
                StrokeThickness = 1 + 1.5 * frac,
                StrokeDashArray = new DoubleCollection { 2, 3 },
            };
            _radarCanvas.Children.Add(line);

            double dotR = 5 + 6 * frac;
            var dot = new Ellipse
            {
                Width = dotR * 2, Height = dotR * 2,
                Fill = new SolidColorBrush(dotColor),
                Stroke = new SolidColorBrush(s.NodeId == _radarSelected ? Colors.White : dotColor),
                StrokeThickness = s.NodeId == _radarSelected ? 2 : 0,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = RadarTooltip(s.NodeId, s.Total, s.PerType),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = dotColor, BlurRadius = 10 + 8 * frac, ShadowDepth = 0, Opacity = 0.9,
                },
            };
            Canvas.SetLeft(dot, x - dotR);
            Canvas.SetTop(dot, y - dotR);
            uint nodeId = s.NodeId;
            dot.MouseLeftButtonDown += (_, e) => { SelectRadarStation(nodeId); e.Handled = true; };
            dot.ContextMenu = BuildNodeContextMenu(nodeId, route: null);
            _radarCanvas.Children.Add(dot);

            var label = new TextBlock
            {
                Text = _shortLabel(s.NodeId),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(220, PhosMid.R, PhosMid.G, PhosMid.B)),
                IsHitTestVisible = false,
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, x - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, y + dotR + 2);
            _radarCanvas.Children.Add(label);
        }

        // Our node in the center: bright mast with glow
        var mast = new Path
        {
            Stroke = new SolidColorBrush(PhosBright),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M0,-7 L-4.8,7 H4.8 Z M-3,2.8 H3 M-2,-1.4 H2 M0,-7 V-10"),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = PhosBright, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9,
            },
        };
        Canvas.SetLeft(mast, cx);
        Canvas.SetTop(mast, cy);
        _radarCanvas.Children.Add(mast);
        var meLabel = new TextBlock
        {
            Text = Loc("StrMe"),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(PhosMid),
            IsHitTestVisible = false,
        };
        meLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(meLabel, cx - meLabel.DesiredSize.Width / 2);
        Canvas.SetTop(meLabel, cy + 14);
        _radarCanvas.Children.Add(meLabel);
    }

    /// <summary>Stable scope position for a node: angle from the node id, radius from
    /// activity (busiest closest). Also used as ping fallback for brand-new stations.</summary>
    private (double X, double Y) RadarPosition(uint nodeId, double frac)
    {
        var (cx, cy, maxR) = _radarScope;
        double radius = maxR * (0.92 - 0.55 * frac);
        double angle = (nodeId * 2654435761u % 3600u) / 3600.0 * 2 * Math.PI;
        return (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    /// <summary>Movie-style sonar ping: an expanding ring from the sender reaches our
    /// center, gets "reflected" and travels back out to the sender, which then blips.</summary>
    public void PingStation(uint nodeId)
    {
        if (_radarScope.MaxR <= 0) return;
        var (cx, cy, _) = _radarScope;
        var (x, y) = _radarPositions.TryGetValue(nodeId, out var p)
            ? p
            : RadarPosition(nodeId, 0.2);   // brand-new station: deterministic fallback spot
        double dist = Math.Max(Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)), 12);
        var dur = TimeSpan.FromMilliseconds(1000);

        void Expand(double ox, double oy, double targetR, Action? completed)
        {
            var ring = new Ellipse
            {
                Width = targetR * 2, Height = targetR * 2,
                Stroke = new SolidColorBrush(PhosBright),
                StrokeThickness = 2,
                Opacity = 0.9,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var scale = new ScaleTransform(0.04, 0.04);
            ring.RenderTransform = scale;
            Canvas.SetLeft(ring, ox - targetR);
            Canvas.SetTop(ring, oy - targetR);
            _pingOverlay.Children.Add(ring);

            var grow = new System.Windows.Media.Animation.DoubleAnimation(0.04, 1.0, dur);
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0.9, 0.25, dur);
            grow.Completed += (_, _) =>
            {
                _pingOverlay.Children.Remove(ring);
                completed?.Invoke();
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // outbound (sender → us), then reflected (us → sender), then a blip at the sender
        Expand(x, y, dist, () => Expand(cx, cy, dist, () =>
        {
            var blip = new Ellipse
            {
                Width = 26, Height = 26,
                Fill = new SolidColorBrush(Color.FromArgb(160, PhosBright.R, PhosBright.G, PhosBright.B)),
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var blipScale = new ScaleTransform(0.3, 0.3);
            blip.RenderTransform = blipScale;
            Canvas.SetLeft(blip, x - 13);
            Canvas.SetTop(blip, y - 13);
            _pingOverlay.Children.Add(blip);
            var pop  = new System.Windows.Media.Animation.DoubleAnimation(0.3, 1.4, TimeSpan.FromMilliseconds(450));
            var gone = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(450));
            gone.Completed += (_, _) => _pingOverlay.Children.Remove(blip);
            blipScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            blipScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            blip.BeginAnimation(UIElement.OpacityProperty, gone);
        }));
    }

    private string RadarTooltip(uint nodeId, int total, Dictionary<string, int> perType)
    {
        var parts = perType.OrderByDescending(kv => kv.Value)
            .Select(kv => $"{(TypeStyle.TryGetValue(kv.Key, out var s) ? s.icon : "❔")} {kv.Value}");
        return $"{_fullName(nodeId)}\n{string.Format(Loc("StrFunkEchoNewCount"), total)}\n{string.Join("  ", parts)}";
    }

    private ContextMenu? BuildNodeContextMenu(uint nodeId, string? route)
    {
        var ctx = new ContextMenu();
        void Add(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action();
            ctx.Items.Add(item);
        }
        if (_openNodeInfo != null) Add(Loc("StrNodeInfo"), () => _openNodeInfo(nodeId));
        if (_showOnMap != null) Add(Loc("StrFunkEchoCtxMap"), () => _showOnMap(nodeId));
        if (_openDm != null) Add(Loc("StrSendDm"), () => _openDm(nodeId));
        if (_showRouteOnMap != null && !string.IsNullOrEmpty(route))
        {
            var relays = route.Split(',')
                .Select(idStr => uint.TryParse(idStr.TrimStart('!'),
                    System.Globalization.NumberStyles.HexNumber, null, out var id) ? id : 0u)
                .Where(id => id != 0).ToList();
            Add(Loc("StrFunkEchoCtxRoute"), () => _showRouteOnMap(nodeId, relays));
        }
        return ctx.Items.Count > 0 ? ctx : null;
    }

    /// <summary>Drilldown: hourly request series + type breakdown for the picked station.</summary>
    private void SelectRadarStation(uint nodeId)
    {
        _radarSelected = nodeId;
        RenderRadar();

        var rows = _radarRows.Where(r => r.NodeId == nodeId).ToList();
        int total = rows.Sum(r => r.Count);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(Loc("StrFunkEchoDrillTitle"), _fullName(nodeId), total),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_fgMain),
        });
        var breakdown = rows.GroupBy(r => r.Type)
            .OrderByDescending(g => g.Sum(r => r.Count))
            .Select(g =>
            {
                var (icon, _) = TypeStyle.TryGetValue(g.Key, out var s) ? s : ("❔", _fgSub);
                string label = g.Key == "Telemetry"
                    ? string.Join(", ", g.GroupBy(r => r.Detail ?? "")
                        .Select(d => $"{(d.Key.Length == 0 ? Loc("StrFunkEchoTelemGeneric") : Loc("StrFunkEchoTelem" + d.Key))} {d.Sum(r => r.Count)}"))
                    : g.Sum(r => r.Count).ToString();
                return $"{icon} {Loc("StrFunkEchoType" + g.Key)}: {label}";
            });
        panel.Children.Add(new TextBlock
        {
            Text = string.Join("   ·   ", breakdown),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(_fgSub),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4),
        });

        // Hourly bars from the dashboard metric query ("at 03:00 there were 24 requests")
        try
        {
            var series = _db.GetTimeSeries(new[] { nodeId }, "funk_echo", _rangeDays);
            if (series.Count > 0)
            {
                var model = new PlotModel { Background = OxyColors.Transparent, TextColor = OxyColor.FromRgb(_fgSub.R, _fgSub.G, _fgSub.B) };
                model.Axes.Add(new DateTimeAxis
                {
                    Position = AxisPosition.Bottom,
                    StringFormat = _rangeDays == 1 ? "HH:mm" : "dd.MM HH'h'",
                    TextColor = OxyColor.FromRgb(_fgSub.R, _fgSub.G, _fgSub.B),
                    IsZoomEnabled = false, IsPanEnabled = false,
                });
                model.Axes.Add(new LinearAxis
                {
                    Position = AxisPosition.Left, Minimum = 0,
                    TextColor = OxyColor.FromRgb(_fgSub.R, _fgSub.G, _fgSub.B),
                    MajorGridlineStyle = LineStyle.Dot,
                    MajorGridlineColor = OxyColor.FromAColor(60, OxyColor.FromRgb(_fgSub.R, _fgSub.G, _fgSub.B)),
                    IsZoomEnabled = false, IsPanEnabled = false,
                });
                var bars = new RectangleBarSeries
                {
                    FillColor = OxyColor.FromArgb(200, 0x20, 0xC9, 0x97),
                    StrokeThickness = 0,
                    TrackerFormatString = "{2:dd.MM HH:mm}: {4:0} " + Loc("StrUnitRequestsPerHour"),
                };
                foreach (var p in series.OrderBy(p => p.Timestamp))
                {
                    double x0 = DateTimeAxis.ToDouble(p.Timestamp);
                    bars.Items.Add(new RectangleBarItem(x0, 0, x0 + 1.0 / 24, p.Value));
                }
                model.Series.Add(bars);
                panel.Children.Add(new PlotView { Model = model, Height = 130, Background = Brushes.Transparent });
            }
        }
        catch (Exception ex) { Services.Logger.WriteLine($"[FunkEcho] drilldown failed: {ex.Message}"); }

        _drillPanel.Child = panel;
        _drillPanel.Visibility = Visibility.Visible;
    }

    // ── Statistics ────────────────────────────────────────────────────────────

    private void BuildStats((int TotalRequests, int Stations, Dictionary<string, int> PerType,
        Dictionary<string, int> TelemetryDetails,
        List<(uint NodeId, int Count)> TopNodes) stats,
        List<TelemetryDatabaseService.RequestEchoRow> rows)
    {
        _statsPanel.Children.Clear();

        string rangeLabel = _rangeDays switch
        {
            1  => Loc("StrFunkEchoRange24h"),
            30 => Loc("StrFunkEchoRange30d"),
            _  => Loc("StrFunkEchoRange7d"),
        };
        _statsPanel.Children.Add(new TextBlock
        {
            Text = string.Format(Loc("StrFunkEchoStatsTitleRange"), rangeLabel),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_fgMain),
            Margin = new Thickness(0, 0, 0, 10),
        });

        int traces = stats.PerType.TryGetValue("Traceroute", out var tr) ? tr : 0;
        var tiles = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
        tiles.Children.Add(StatTile(stats.TotalRequests.ToString(), Loc("StrFunkEchoStatRequests")));
        tiles.Children.Add(StatTile(stats.Stations.ToString(), Loc("StrFunkEchoStatStations")));
        tiles.Children.Add(StatTile(traces.ToString(), Loc("StrFunkEchoStatTraces")));

        // Response-airtime receipt: what our node's auto-replies roughly cost on air.
        // Counts only CERTAIN replies ("yes"/"prec:"), rated at the current modem preset.
        string airtimeText = "–";
        if (_modemPreset?.Invoke() is { } preset)
        {
            double totalMs = 0;
            foreach (var r in rows)
            {
                int hops = string.IsNullOrEmpty(r.Route) ? 0 : r.Route.Split(',').Length;
                if (LoRaAirtime.ResponseAirtimeMs(r.Type, r.Detail, hops, r.Expected, preset) is { } ms)
                    totalMs += ms * r.Count;
            }
            airtimeText = totalMs >= 1000 ? $"≈ {totalMs / 1000:F1} s" : $"≈ {totalMs:F0} ms";
        }
        var airtimeTile = StatTile(airtimeText, Loc("StrFunkEchoStatAirtime"));
        airtimeTile.ToolTip = Loc("StrFunkEchoAirtimeTip");
        tiles.Children.Add(airtimeTile);
        _statsPanel.Children.Add(tiles);

        if (stats.TotalRequests == 0)
        {
            _statsPanel.Children.Add(new TextBlock
            {
                Text = Loc("StrFunkEchoEmpty"),
                Foreground = new SolidColorBrush(_fgMuted),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            });
            return;
        }

        _statsPanel.Children.Add(new TextBlock
        {
            Text = Loc("StrFunkEchoStatWhat"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_fgSub),
            Margin = new Thickness(0, 4, 0, 6),
        });

        int max = stats.PerType.Values.DefaultIfEmpty(1).Max();
        foreach (var (type, count) in stats.PerType.OrderByDescending(kv => kv.Value))
        {
            var (icon, color) = TypeStyle.TryGetValue(type, out var s) ? s : ("❔", _fgSub);
            _statsPanel.Children.Add(BuildStatBarRow(
                $"{icon} {Loc("StrFunkEchoType" + type)}", count, max, color,
                indent: 0, barHeight: 10, fontSize: 11.5));

            // Telemetry sub-chart: breakdown by requested data group, indented below
            if (type == "Telemetry" && stats.TelemetryDetails.Count > 0)
            {
                int detailMax = stats.TelemetryDetails.Values.DefaultIfEmpty(1).Max();
                foreach (var (detail, dCount) in stats.TelemetryDetails.OrderByDescending(kv => kv.Value))
                {
                    string dLabel = detail.Length == 0
                        ? Loc("StrFunkEchoTelemGeneric")
                        : Loc("StrFunkEchoTelem" + detail);
                    _statsPanel.Children.Add(BuildStatBarRow(
                        "  └ " + dLabel, dCount, detailMax,
                        Color.FromArgb(200, color.R, color.G, color.B),
                        indent: 18, barHeight: 6, fontSize: 10.5));
                }
            }
        }

        if (stats.TopNodes.Count > 0)
        {
            var (topId, topCount) = stats.TopNodes[0];
            _statsPanel.Children.Add(new TextBlock
            {
                Text = string.Format(Loc("StrFunkEchoTopNode"), _fullName(topId), topCount),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xC9, 0x97)),
                Margin = new Thickness(0, 14, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    /// <summary>One labeled proportional bar row (used for the type chart and the
    /// indented telemetry sub-chart).</summary>
    private FrameworkElement BuildStatBarRow(string label, int count, int max, Color color,
        double indent, double barHeight, double fontSize)
    {
        var rowGrid = new Grid { Margin = new Thickness(indent, 2, 0, 2) };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130 + 18 - indent) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(indent > 0 ? _fgSub : _fgMain),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(labelText, 0);
        rowGrid.Children.Add(labelText);

        var barHost = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = barHeight };
        barHost.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(barHeight / 2),
        });
        barHost.Children.Add(new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(barHeight / 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0, // set after layout via SizeChanged (proportional)
            Tag = (double)count / Math.Max(1, max),
        });
        barHost.SizeChanged += (_, _) =>
        {
            foreach (var child in barHost.Children.OfType<Border>())
                if (child.Tag is double frac)
                    child.Width = Math.Max(4, barHost.ActualWidth * frac);
        };
        Grid.SetColumn(barHost, 1);
        rowGrid.Children.Add(barHost);

        var countText = new TextBlock
        {
            Text = count.ToString(),
            FontSize = fontSize,
            Foreground = new SolidColorBrush(_fgSub),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(countText, 2);
        rowGrid.Children.Add(countText);

        return rowGrid;
    }

    private FrameworkElement StatTile(string value, string label) => new Border
    {
        Background = new SolidColorBrush(_bgCard),
        BorderBrush = new SolidColorBrush(_borderCol),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(8),
        Margin = new Thickness(0, 0, 8, 0),
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = value, FontSize = 26, FontWeight = FontWeights.Light,
                    Foreground = new SolidColorBrush(_fgMain),
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = label, FontSize = 10.5,
                    Foreground = new SolidColorBrush(_fgSub),
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        },
    };
}
