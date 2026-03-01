using System.Windows;
using System.Windows.Controls;
using MeshhessenClient.Models;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class TelemetryWindow : Window
{
    private readonly NodeInfo _node;
    private readonly TelemetryDatabaseService _db;
    private readonly Dictionary<uint, string> _nodeNames;
    private readonly double _lat, _lon;
    private int _days = 7;

    public TelemetryWindow(
        NodeInfo node,
        TelemetryDatabaseService db,
        Dictionary<uint, string> nodeNames,
        double lat, double lon)
    {
        InitializeComponent();
        _node      = node;
        _db        = db;
        _nodeNames = nodeNames;
        _lat       = lat;
        _lon       = lon;

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
            var signal  = _db.GetSignalStats(_node.NodeId, _days);
            var power   = _db.GetPowerStats(_node.NodeId, _days);
            var airtime = _db.GetAirtimeStats(_node.NodeId, _days);
            var routing = _db.GetRoutingStats(_node.NodeId, _days);
            var neighbors = _db.GetNeighborStats(_node.NodeId, _days, _nodeNames);

            string rangeName = _days == 0 ? "gesamt" : $"letzten {_days} Tage";
            FooterInfoText.Text = $"Auswertung der {rangeName}";
            DataCountText.Text  = $"({signal.TotalPackets} Pakete)";

            // ── Signal ──
            SnrDayMedian.Text    = Fmt(signal.DaySnrMedian,   " dB");
            SnrNightMedian.Text  = Fmt(signal.NightSnrMedian, " dB");
            RssiDayMedian.Text   = Fmt(signal.DayRssiMedian,  " dBm");
            RssiNightMedian.Text = Fmt(signal.NightRssiMedian," dBm");
            SnrMinMax.Text       = $"{Fmt(signal.SnrMin, " dB")} / {Fmt(signal.SnrMax, " dB")}";
            SnrVariance.Text     = signal.SnrVariance.HasValue ? $"{signal.SnrVariance:F2}" : "–";
            RssiMinMax.Text      = $"{Fmt(signal.RssiMin, " dBm")} / {Fmt(signal.RssiMax, " dBm")}";
            TotalPackets.Text    = signal.TotalPackets.ToString();

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

            // ── Airtime ──
            CuDayAvg.Text   = Fmt(airtime.DayChannelUtilAvg,   "%");
            CuNightAvg.Text = Fmt(airtime.NightChannelUtilAvg, "%");
            CuMax.Text      = Fmt(airtime.ChannelUtilMax,      "%");
            AuDayAvg.Text   = Fmt(airtime.DayAirTxUtilAvg,     "%");
            AuNightAvg.Text = Fmt(airtime.NightAirTxUtilAvg,   "%");
            AuMax.Text      = Fmt(airtime.AirTxUtilMax,        "%");

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
