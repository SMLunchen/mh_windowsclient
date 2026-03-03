using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MeshhessenClient.Models;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class TelemetryWindow : Window
{
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

        Loaded += (_, _) => Refresh();
    }

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
            var neighbors = _db.GetNeighborStats(_node.NodeId, _days, _nodeNames);

            string rangeName = _days == 0 ? "gesamt" : $"letzten {_days} Tage";
            FooterInfoText.Text = $"Auswertung der {rangeName}";
            DataCountText.Text  = $"({signal.TotalPackets} Pakete)";

            // ── Per-Node Status Ampel (Signal / Energie / ACK / Kanal) ──
            var signalLed = signal.DaySnrMedian switch
            {
                null   => LedState.NoData,
                >= 5f  => LedState.Good,
                >= -5f => LedState.Warning,
                _      => LedState.Alert
            };
            SetLed(NodeSignalLed, signalLed,
                $"SNR-Empfangsqualität (Median tags): {Fmt(signal.DaySnrMedian, " dB")}\n" +
                "Grün ≥5 dB | Gelb -5…5 dB | Rot <-5 dB");

            float? battVal = power.DayBatteryAvg ?? power.NightBatteryAvg;
            var battLed = battVal switch
            {
                null   => LedState.NoData,
                >= 50f => LedState.Good,
                >= 20f => LedState.Warning,
                _      => LedState.Alert
            };
            string battPeriod = power.DayBatteryAvg.HasValue ? "Tages-Ø" : "Nacht-Ø (kein Tages-Wert)";
            SetLed(NodeBatteryLed, battLed,
                $"Batterie {battPeriod}: {Fmt(battVal, "%")}\n" +
                "Grün ≥50% | Gelb 20–50% | Rot <20% | Grau = keine Telemetrie-Daten");

            var ackLed = routing.AckSuccessRate switch
            {
                null    => LedState.NoData,
                >= 0.9f => LedState.Good,
                >= 0.6f => LedState.Warning,
                _       => LedState.Alert
            };
            SetLed(NodeAckLed, ackLed,
                $"ACK-Erfolgsrate: {(routing.AckSuccessRate.HasValue ? $"{routing.AckSuccessRate * 100:F0}%" : "–")}\n" +
                "Grün ≥90% | Gelb 60–90% | Rot <60% | Grau = keine ACK-Anforderungen");

            var chanLed = airtime.ChannelUtilMax switch
            {
                null   => LedState.NoData,
                <= 15f => LedState.Good,
                <= 25f => LedState.Warning,
                _      => LedState.Alert
            };
            SetLed(NodeChanLed, chanLed,
                $"Kanalauslastung (Max): {Fmt(airtime.ChannelUtilMax, "%")}\n" +
                "Grün ≤15% | Gelb 15–25% | Rot >25% | Grau = keine Airtime-Daten");

            // ── Signal ──
            SnrDayMedian.Text    = Fmt(signal.DaySnrMedian,   " dB");
            SnrNightMedian.Text  = Fmt(signal.NightSnrMedian, " dB");
            RssiDayMedian.Text   = Fmt(signal.DayRssiMedian,  " dBm");
            RssiNightMedian.Text = Fmt(signal.NightRssiMedian," dBm");
            SnrMinMax.Text       = $"{Fmt(signal.SnrMin, " dB")} / {Fmt(signal.SnrMax, " dB")}";
            SnrVariance.Text     = signal.SnrVariance.HasValue ? $"{signal.SnrVariance:F2}" : "–";
            RssiMinMax.Text      = $"{Fmt(signal.RssiMin, " dBm")} / {Fmt(signal.RssiMax, " dBm")}";
            TotalPackets.Text    = signal.TotalPackets.ToString();
            // Color coding for signal values
            SnrDayMedian.Foreground    = SnrBrush(signal.DaySnrMedian);
            SnrNightMedian.Foreground  = SnrBrush(signal.NightSnrMedian);
            RssiDayMedian.Foreground   = RssiBrush(signal.DayRssiMedian);
            RssiNightMedian.Foreground = RssiBrush(signal.NightRssiMedian);
            // Gesamtbereich: color by worst (min) value in the range
            SnrMinMax.Foreground  = SnrBrush(signal.SnrMin);
            RssiMinMax.Foreground = RssiBrush(signal.RssiMin);

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
            HopCostText.Text          = hopSamples > 0 ? $"{hopCost:0.00}" : "–";
            RouteChangeRateText.Text  = hopSamples > 0 ? $"{routeChangeRate:0.00}/h" : "–";
            TracerouteCountText.Text  = hopSamples > 0 ? hopSamples.ToString() : "–";

            // ── Neighbors ──
            NeighborGrid.ItemsSource = neighbors.Select(n => new NeighborRow(n)).ToList();
        }
        catch (Exception ex)
        {
            FooterInfoText.Text = $"Fehler: {ex.Message}";
        }
    }

    private void OpenPlotWindow_Click(object sender, RoutedEventArgs e)
    {
        var win = new PlotWindow(_db, _nodeNames, preselectedNodeId: _node.NodeId)
        {
            Owner = this
        };
        win.Show();
    }

    private static void SetLed(Ellipse led, LedState state, string tooltip)
    {
        led.Fill = state switch
        {
            LedState.Good    => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            LedState.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
            LedState.Alert   => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
            _                => new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75)),
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
        public string NeighborName      { get; }
        public string MedianSnrDisplay  { get; }
        public string MedianRssiDisplay { get; }
        public string PacketsPerHourDisplay { get; }
        public string LastSeenDisplay   { get; }

        public NeighborRow(NeighborStats s)
        {
            NeighborName          = s.NeighborName;
            MedianSnrDisplay      = s.MedianSnr.HasValue  ? $"{s.MedianSnr:F1} dB"  : "–";
            MedianRssiDisplay     = s.MedianRssi.HasValue ? $"{s.MedianRssi:F0} dBm" : "–";
            PacketsPerHourDisplay = $"{s.PacketsPerHour:F1}";
            LastSeenDisplay       = s.LastSeen > DateTime.MinValue
                ? s.LastSeen.ToString("dd.MM. HH:mm") : "–";
        }
    }
}
