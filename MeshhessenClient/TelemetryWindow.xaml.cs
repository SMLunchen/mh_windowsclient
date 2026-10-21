using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MeshhessenClient.Models;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class TelemetryWindow : Window
{
    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    private static readonly SolidColorBrush DefaultBrush = new(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA));
    private readonly NodeInfo _node;
    private readonly TelemetryDatabaseService _db;
    private readonly Dictionary<uint, string> _nodeNames;
    private readonly double _lat, _lon;
    private readonly int _shortHours;
    private readonly int _longDays;
    private int _days = 7;

    public TelemetryWindow(
        NodeInfo node,
        TelemetryDatabaseService db,
        Dictionary<uint, string> nodeNames,
        double lat, double lon,
        int signalWeatherWindowHours = 6,
        int signalAntennaWindowDays  = 7)
    {
        InitializeComponent();
        _node       = node;
        _db         = db;
        _nodeNames  = nodeNames;
        _lat        = lat;
        _lon        = lon;
        _shortHours = signalWeatherWindowHours;
        _longDays   = signalAntennaWindowDays;

        NodeNameText.Text = node.LongName;
        NodeIdText.Text   = node.Id;
        Title = $"Telemetrie – {node.LongName}";

        _db.Latitude  = lat;
        _db.Longitude = lon;

        // Context menu on the neighbor grid: elevation profile of the clicked link.
        // WPF DataGrids do NOT select on right-click (same bug class as the node list),
        // so the clicked row is resolved by a visual-tree walk when the menu opens and
        // pinned by id — SelectedItem may be null or stale at menu-click time.
        var profileItem = new System.Windows.Controls.MenuItem();
        profileItem.SetResourceReference(System.Windows.Controls.MenuItem.HeaderProperty, "StrLinkProfileMenu");
        profileItem.Click += (_, _) =>
        {
            if (_contextNeighborId != 0)
                LinkProfileRequested?.Invoke(this, _contextNeighborId);
        };
        NeighborGrid.ContextMenu = new System.Windows.Controls.ContextMenu { Items = { profileItem } };
        NeighborGrid.ContextMenuOpening += (_, e) =>
        {
            _contextNeighborId = 0;
            var dep = e.OriginalSource as System.Windows.DependencyObject;
            while (dep != null && dep is not System.Windows.Controls.DataGridRow)
                dep = dep is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(dep)
                    : LogicalTreeHelper.GetParent(dep);
            if (dep is System.Windows.Controls.DataGridRow { Item: NeighborRow row } gridRow
                && row.NeighborId != 0)
            {
                _contextNeighborId = row.NeighborId;
                gridRow.IsSelected = true;
            }
            else
            {
                e.Handled = true; // empty area or legacy row (no id) → no menu
            }
        };

        Loaded += (_, _) => Refresh();

        // Win32 quirk: when the ACTIVE owned window is destroyed, Windows picks the
        // next window to activate — with ownerless sibling windows around (dashboard,
        // plot windows) this sometimes minimized the whole client to the taskbar.
        // Activating the owner BEFORE destruction keeps it in front.
        Closing += (_, _) => { try { Owner?.Activate(); } catch { } };
    }

    // Neighbor pinned at context-menu open (right-click does not select in DataGrids)
    private uint _contextNeighborId;

    /// <summary>Raised from the neighbor grid's context menu: show the terrain/Fresnel
    /// elevation profile of the link between this node and the given neighbor.</summary>
    public event EventHandler<uint>? LinkProfileRequested;

    private void OnRangeChanged(object sender, RoutedEventArgs e)
    {
        _days = Range7d.IsChecked  == true ? 7
              : Range14d.IsChecked == true ? 14
              : Range30d.IsChecked == true ? 30
              : 0;
        Refresh();
    }

    private void Refresh()
    {
        // Guard: might be called during InitializeComponent() before fields are assigned
        if (_db == null || _node == null) return;
        try
        {
            var signal    = _db.GetSignalStats(_node.NodeId, _days);
            var power     = _db.GetPowerStats(_node.NodeId, _days);
            var airtime   = _db.GetAirtimeStats(_node.NodeId, _days);
            var routing   = _db.GetRoutingStats(_node.NodeId, _days);
            // Direct-RF neighbors (0-hop reception, traceroute hops, NeighborInfo).
            // The rf_links table starts empty and fills over time, so while there is
            // little evidence we fall back to the legacy "heard nodes" grouping — once
            // enough direct links are collected, the view switches for good.
            var rfNeighbors = _db.GetRfNeighbors(_node.NodeId, _days, _nodeNames);

            string rangeName = _days == 0 ? Loc("StrTelAll") : $"{_days}d";
            FooterInfoText.Text = string.Format(Loc("StrTelFooterInfo"), rangeName);
            DataCountText.Text  = $"({signal.TotalPackets} {Loc("StrTelPacketsLabel")})";

            // ── Per-Node Status Ampel (Signal / Energie / ACK / Kanal) ──
            var signalLed = signal.DaySnrMedian switch
            {
                null   => LedState.NoData,
                >= 5f  => LedState.Good,
                >= -5f => LedState.Warning,
                _      => LedState.Alert
            };
            SetLed(NodeSignalLed, signalLed,
                string.Format(Loc("StrTelSignalLedTooltip"), Fmt(signal.DaySnrMedian, " dB")));

            float? battVal = power.DayBatteryAvg ?? power.NightBatteryAvg;
            var battLed = battVal switch
            {
                null   => LedState.NoData,
                >= 50f => LedState.Good,
                >= 20f => LedState.Warning,
                _      => LedState.Alert
            };
            string battPeriod = power.DayBatteryAvg.HasValue ? Loc("StrTelBatteryPeriodDay") : Loc("StrTelBatteryPeriodNight");
            SetLed(NodeBatteryLed, battLed,
                string.Format(Loc("StrTelBatteryLedTooltip"), battPeriod, Fmt(battVal, "%")));

            var nodeTrend = _db.GetSingleNodeTrend(_node.NodeId, _shortHours, _longDays, _nodeNames);
            var trendLed  = nodeTrend == null || nodeTrend.PointCount < 5 ? LedState.NoData
                          : nodeTrend.ShortSlope >  0.3f                  ? LedState.Good
                          : nodeTrend.ShortSlope > -0.5f                  ? LedState.Warning
                          :                                                  LedState.Alert;
            string trendTip = nodeTrend == null || nodeTrend.PointCount < 5
                ? Loc("StrTelTrendLedTooltipNoData")
                : string.Format(Loc("StrTelTrendLedTooltip"),
                    _shortHours, $"{nodeTrend.ShortSlope:+0.00;-0.00}",
                    _longDays,   $"{nodeTrend.LongSlope:+0.00;-0.00}");
            SetLed(NodeAckLed, trendLed, trendTip);

            var chanLed = airtime.ChannelUtilMax switch
            {
                null   => LedState.NoData,
                <= 15f => LedState.Good,
                <= 25f => LedState.Warning,
                _      => LedState.Alert
            };
            SetLed(NodeChanLed, chanLed,
                string.Format(Loc("StrTelChanLedTooltip"), Fmt(airtime.ChannelUtilMax, "%")));

            // ── Signal ──
            SnrDayMedian.Text    = Fmt(signal.DaySnrMedian,   " dB");
            SnrNightMedian.Text  = Fmt(signal.NightSnrMedian, " dB");
            RssiDayMedian.Text   = Fmt(signal.DayRssiMedian,  " dBm");
            RssiNightMedian.Text = Fmt(signal.NightRssiMedian," dBm");
            SnrMinMax.Inlines.Clear();
            SnrMinMax.Inlines.Add(new System.Windows.Documents.Run(Fmt(signal.SnrMin, " dB"))  { Foreground = SnrBrush(signal.SnrMin)  });
            SnrMinMax.Inlines.Add(new System.Windows.Documents.Run(" / ")                       { Foreground = DefaultBrush });
            SnrMinMax.Inlines.Add(new System.Windows.Documents.Run(Fmt(signal.SnrMax, " dB"))  { Foreground = SnrBrush(signal.SnrMax)  });
            SnrVariance.Text     = signal.SnrVariance.HasValue ? $"{signal.SnrVariance:F2}" : "–";
            SnrVariance.Foreground = VarianceBrush(signal.SnrVariance);
            RssiMinMax.Inlines.Clear();
            RssiMinMax.Inlines.Add(new System.Windows.Documents.Run(Fmt(signal.RssiMin, " dBm")) { Foreground = RssiBrush(signal.RssiMin) });
            RssiMinMax.Inlines.Add(new System.Windows.Documents.Run(" / ")                        { Foreground = DefaultBrush });
            RssiMinMax.Inlines.Add(new System.Windows.Documents.Run(Fmt(signal.RssiMax, " dBm")) { Foreground = RssiBrush(signal.RssiMax) });
            TotalPackets.Text    = signal.TotalPackets.ToString();
            // Color coding for signal values
            SnrDayMedian.Foreground    = SnrBrush(signal.DaySnrMedian);
            SnrNightMedian.Foreground  = SnrBrush(signal.NightSnrMedian);
            RssiDayMedian.Foreground   = RssiBrush(signal.DayRssiMedian);
            RssiNightMedian.Foreground = RssiBrush(signal.NightRssiMedian);
            // Gesamtbereich: Min/Max je individuell eingefärbt (via Inlines, s.o.)
            // Delta Nacht − Tag
            float? snrDelta  = signal.NightSnrMedian.HasValue  && signal.DaySnrMedian.HasValue
                ? signal.NightSnrMedian  - signal.DaySnrMedian  : null;
            float? rssiDelta = signal.NightRssiMedian.HasValue && signal.DayRssiMedian.HasValue
                ? signal.NightRssiMedian - signal.DayRssiMedian : null;
            SnrDelta.Text            = snrDelta.HasValue  ? $"{snrDelta:+0.0;-0.0;0} dB"  : "–";
            RssiDelta.Text           = rssiDelta.HasValue ? $"{rssiDelta:+0.0;-0.0;0} dBm" : "–";
            SnrDelta.Foreground      = DeltaBrush(snrDelta);
            RssiDelta.Foreground     = DeltaBrush(rssiDelta);

            // ── Power ──
            BatDayAvg.Text    = Fmt(power.DayBatteryAvg,    "%");
            BatNightAvg.Text  = Fmt(power.NightBatteryAvg,  "%");
            BatNightDrop.Text = power.NightBatteryDrop.HasValue
                ? $"{power.NightBatteryDrop:+0.0;-0.0;0}%" : "–";
            VoltDayAvg.Text    = Fmt(power.DayVoltageAvg,   " V");
            VoltNightAvg.Text  = Fmt(power.NightVoltageAvg, " V");
            VoltNightDrop.Text = power.NightVoltageDrop.HasValue
                ? $"{power.NightVoltageDrop:+0.000;-0.000;0.000} V" : "–";
            VoltMinMax.Text = $"{Fmt(power.VoltageMin, " V")} / {Fmt(power.VoltageMax, " V")}";
            LastUptime.Text = power.LastUptimeSeconds.HasValue
                ? FormatUptime(power.LastUptimeSeconds.Value) : "–";
            TelReadings.Text = power.TotalReadings.ToString();
            // Color coding for battery values
            BatDayAvg.Foreground   = BatBrush(power.DayBatteryAvg);
            BatNightAvg.Foreground = BatBrush(power.NightBatteryAvg);

            // ── Airtime ──
            CuDayAvg.Text   = Fmt(airtime.DayChannelUtilAvg,   "%");
            CuNightAvg.Text = Fmt(airtime.NightChannelUtilAvg, "%");
            CuMax.Text      = Fmt(airtime.ChannelUtilMax,      "%");
            AuDayAvg.Text   = Fmt(airtime.DayAirTxUtilAvg,     "%");
            AuNightAvg.Text = Fmt(airtime.NightAirTxUtilAvg,   "%");
            AuMax.Text      = Fmt(airtime.AirTxUtilMax,        "%");
            // Color coding for channel utilization
            CuDayAvg.Foreground = ChanBrush(airtime.DayChannelUtilAvg);
            CuNightAvg.Foreground = ChanBrush(airtime.NightChannelUtilAvg);
            CuMax.Foreground    = ChanBrush(airtime.ChannelUtilMax);

            // ── Routing ──
            HopsStats.Text  = routing.AvgHops.HasValue
                ? $"{routing.AvgHops:F1} Ø  /  {routing.MinHops} min  /  {routing.MaxHops} max" : "–";
            AckRate.Text    = routing.AckSuccessRate.HasValue
                ? $"{routing.AckSuccessRate * 100:F0}%" : "–";
            AckCounts.Text  = $"{routing.AckRequested} / {routing.AckReceived}";
            UniqueFrom.Text = routing.UniqueFromNodes.ToString();
            RoutingTotal.Text = routing.TotalPackets.ToString();
            HopDistText.Text = routing.HopDistribution.Count > 0
                ? string.Join("  |  ", routing.HopDistribution.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} Hop: {kv.Value}×"))
                : "Keine Daten";

            // ── Routing: Pfad-Analyse ──
            var (hopCost, hopSamples) = _db.GetHopCost(_node.NodeId, _days > 0 ? _days : _longDays);
            float routeChangeRate     = _db.GetRouteChangeRate(_node.NodeId, _days > 0 ? _days : _longDays);
            int tracerouteRows        = _db.CountTracerouteRows(_node.NodeId, _days > 0 ? _days : _longDays);
            HopCostText.Text          = hopSamples > 0 ? $"{hopCost:0.00}" : "–";
            RouteChangeRateText.Text  = hopSamples > 0 ? $"{routeChangeRate:0.00}/h" : "–";
            TracerouteCountText.Text  = tracerouteRows > 0 ? tracerouteRows.ToString() : "–";

            // ── LocalStats (only shown once the node has reported them) ──
            var ls = _db.GetLatestLocalStats(_node.NodeId);
            LocalStatsSection.Visibility = ls != null ? Visibility.Visible : Visibility.Collapsed;
            if (ls != null)
            {
                NoiseFloorText.Text = ls.NoiseFloor.HasValue ? $"{ls.NoiseFloor} dBm" : "–";
                double badPct  = ls.PacketsRx > 0 ? 100.0 * ls.PacketsRxBad / ls.PacketsRx : 0;
                double dupePct = ls.PacketsRx > 0 ? 100.0 * ls.RxDupe / ls.PacketsRx : 0;
                // Current rates from the delta between the last two polls (needs ≥2 readings)
                string RateSuffix(string metric)
                {
                    var pts = _db.GetTimeSeries(new[] { _node.NodeId }, metric, _days);
                    return pts.Count > 0 ? $"  ·  ≈{pts[^1].Value:0.#} {Loc("StrUnitPacketsPerHour")}" : "";
                }
                RxStatsText.Text = $"{ls.PacketsRx}  ({Loc("StrTelRxBad")} {ls.PacketsRxBad} = {badPct:0.0}%, " +
                                   $"{Loc("StrTelRxDupe")} {ls.RxDupe} = {dupePct:0.0}%){RateSuffix("rx_pph")}";
                TxStatsText.Text = $"{ls.PacketsTx}  ({Loc("StrTelTxDropped")} {ls.TxDropped}){RateSuffix("tx_pph")}";
                RelayStatsText.Text = $"{ls.TxRelay}  ({Loc("StrTelRelayCanceled")} {ls.TxRelayCanceled})";
                NodeCountsText.Text = $"{ls.OnlineNodes} / {ls.TotalNodes}  ·  {ls.Timestamp.ToLocalTime():dd.MM. HH:mm}";
            }

            // ── Neighbors: direct RF links, legacy fallback while data is sparse ──
            // ≥3 partners: the data is obviously rich enough → RF view immediately.
            // 1–2 partners: trust it once collection has run long enough (a week) —
            // remote nodes genuinely have few neighbors, that's a finding, not a gap.
            // 0 partners: legacy "heard" list (something is better than nothing).
            var collectingSince = _db.GetRfCollectionStart(); // local time (FromUnix)
            bool matured = collectingSince.HasValue &&
                           DateTime.Now - collectingSince.Value >= TimeSpan.FromDays(7);
            bool useRf = rfNeighbors.Count >= 3 || (rfNeighbors.Count >= 1 && matured);
            if (useRf)
            {
                NeighborSectionText.Text = Loc("StrTtNeighborSection");
                NeighborGrid.ItemsSource = rfNeighbors
                    .Select(n => new NeighborRow(n, Loc))
                    .ToList();
            }
            else
            {
                NeighborSectionText.Text = Loc("StrTtNeighborLegacy");
                NeighborGrid.ItemsSource = _db.GetNeighborStats(_node.NodeId, _days, _nodeNames)
                    .Select(s => NeighborRow.FromLegacy(s, Loc))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            FooterInfoText.Text = $"Fehler: {ex.Message}";
        }
    }

    /// <summary>Raised when the (hidden) triangulate button is clicked — the main window
    /// estimates the node's position from its RF neighbors and plots it on the map.</summary>
    public event EventHandler<Models.NodeInfo>? TriangulateRequested;

    /// <summary>Expert variant: the main window first asks for the target's TX power and
    /// antenna gain, which adjust the link budget in the distance model.</summary>
    public event EventHandler<Models.NodeInfo>? TriangulateExpertRequested;

    private int _footerClicks;
    private DateTime _footerClickAt;

    // Easter egg: triple-click the "analysis of last X days" footer to reveal the
    // triangulation button (position estimation from RF neighbors + signal strength).
    private void FooterInfoText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _footerClickAt).TotalSeconds > 1.5) _footerClicks = 0;
        _footerClickAt = now;
        if (++_footerClicks >= 3)
        {
            _footerClicks = 0;
            TriangulateButton.Visibility = Visibility.Visible;
            TriangulateExpertButton.Visibility = Visibility.Visible;
        }
    }

    private void TriangulateButton_Click(object sender, RoutedEventArgs e) =>
        TriangulateRequested?.Invoke(this, _node);

    private void TriangulateExpertButton_Click(object sender, RoutedEventArgs e) =>
        TriangulateExpertRequested?.Invoke(this, _node);

    private void OpenPlotWindow_Click(object sender, RoutedEventArgs e)
    {
        var win = new PlotWindow(_db, _nodeNames, preselectedNodeId: _node.NodeId) { Owner = this };
        win.Show();
        win.Activate();
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e)
    {
        var win = new TelemetryDashboardWindow(_db, _nodeNames);
        win.Show();
        win.Activate();
    }

    private static void SetLed(Ellipse led, LedState state, string tooltip)
    {
        led.Fill = state switch
        {
            LedState.Good    => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            LedState.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
            LedState.Alert   => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
            _                => new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
        };
        ToolTipService.SetToolTip(led, tooltip);
    }

    // Smooth red→yellow→green gradient; t=0 → red, t=1 → green
    private static SolidColorBrush GradientBrush(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte r, g, b;
        if (t < 0.5f)
        {
            // Red (0xF4,0x43,0x36) → Yellow (0xFF,0xC1,0x07)
            float p = t * 2f;
            r = (byte)(0xF4 + (0xFF - 0xF4) * p);
            g = (byte)(0x43 + (0xC1 - 0x43) * p);
            b = (byte)(0x36 + (0x07 - 0x36) * p);
        }
        else
        {
            // Yellow (0xFF,0xC1,0x07) → Green (0x4C,0xAF,0x50)
            float p = (t - 0.5f) * 2f;
            r = (byte)(0xFF + (0x4C - 0xFF) * p);
            g = (byte)(0xC1 + (0xAF - 0xC1) * p);
            b = (byte)(0x07 + (0x50 - 0x07) * p);
        }
        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
    }

    // SNR: -15 dB = red, +15 dB = green
    private static SolidColorBrush SnrBrush(float? v)
        => v == null ? DefaultBrush : GradientBrush((v.Value + 15f) / 30f);
    // RSSI: -120 dBm = red, -60 dBm = green
    private static SolidColorBrush RssiBrush(float? v)
        => v == null ? DefaultBrush : GradientBrush((v.Value + 120f) / 60f);
    // Battery: 0% = red, 100% = green
    private static SolidColorBrush BatBrush(float? v)
        => v == null ? DefaultBrush : GradientBrush(v.Value / 100f);
    // ChannelUtil: 0% = green, 50%+ = red (inverted)
    private static SolidColorBrush ChanBrush(float? v)
        => v == null ? DefaultBrush : GradientBrush(1f - v.Value / 50f);
    // Delta: positive (better at night) = green, negative = amber — neither is strictly "bad"
    private static SolidColorBrush DeltaBrush(float? v)
        => v == null ? DefaultBrush
         : v >= 0   ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
    // Variance: low (stable) = green, high (unstable) = red; scale 0–30 dB²
    private static SolidColorBrush VarianceBrush(float? v)
        => v == null ? DefaultBrush : GradientBrush(1f - Math.Min(1f, v.Value / 30f));

    private static string Fmt(float? value, string unit, string fmt = "F1")
        => value.HasValue ? $"{value.Value.ToString(fmt)}{unit}" : "–";

    private static string FormatUptime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }

    // ── View model for DataGrid ────────────────────────────────────────────
    private class NeighborRow
    {
        public uint   NeighborId     { get; }  // 0 = legacy row (no link profile)
        public string NeighborName   { get; }
        public string SnrInDisplay   { get; }  // selected node hears the partner at …
        public string SnrOutDisplay  { get; }  // partner hears the selected node at …
        public string SourcesDisplay { get; }
        public string LastSeenDisplay { get; }

        public NeighborRow(TelemetryDatabaseService.RfNeighbor n, Func<string, string> loc)
        {
            NeighborId    = n.NeighborId;
            NeighborName  = n.NeighborName;
            SnrInDisplay  = n.SnrIn.HasValue  ? $"{n.SnrIn:F1} dB"  : "–";
            SnrOutDisplay = n.SnrOut.HasValue ? $"{n.SnrOut:F1} dB" : "–";
            SourcesDisplay = string.Join(", ", n.Sources.Select(s => s switch
            {
                "rx"           => loc("StrRfSrcRx"),
                "traceroute"   => loc("StrRfSrcTraceroute"),
                "neighborinfo" => loc("StrRfSrcNeighborInfo"),
                _              => s,
            }));
            LastSeenDisplay = n.LastSeen > DateTime.MinValue
                ? n.LastSeen.ToLocalTime().ToString("dd.MM. HH:mm") : "–";
        }

        private NeighborRow(string name, string snrIn, string sources, string lastSeen)
        {
            NeighborName = name;
            SnrInDisplay = snrIn;
            SnrOutDisplay = "–";
            SourcesDisplay = sources;
            LastSeenDisplay = lastSeen;
        }

        /// <summary>Legacy fallback row (all heard packets, any hop count) used while
        /// the rf_links table has not collected enough direct-link evidence yet.</summary>
        public static NeighborRow FromLegacy(NeighborStats s, Func<string, string> loc) => new(
            s.NeighborName,
            s.MedianSnr.HasValue ? $"{s.MedianSnr:F1} dB" : "–",
            loc("StrRfSrcLegacy"),
            s.LastSeen > DateTime.MinValue ? s.LastSeen.ToString("dd.MM. HH:mm") : "–");
    }
}
