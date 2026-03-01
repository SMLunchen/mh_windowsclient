using System.IO;
using System.Windows;
using System.Windows.Controls;
using MeshhessenClient.Models;
using MeshhessenClient.Services;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace MeshhessenClient;

public partial class PlotWindow : Window
{
    private readonly TelemetryDatabaseService _db;
    private readonly Dictionary<uint, string> _nodeNames;
    private int _days = 7;
    private TimeSpan _rollingWindow = TimeSpan.Zero; // Zero = raw
    private bool _ready;

    // Color palette for series
    private static readonly OxyColor[] Palette =
    {
        OxyColor.FromRgb( 33, 150, 243),  // Blue
        OxyColor.FromRgb(244,  67,  54),  // Red
        OxyColor.FromRgb( 76, 175,  80),  // Green
        OxyColor.FromRgb(255, 152,   0),  // Orange
        OxyColor.FromRgb(156,  39, 176),  // Purple
        OxyColor.FromRgb(  0, 188, 212),  // Cyan
        OxyColor.FromRgb(233,  30,  99),  // Pink
        OxyColor.FromRgb(121, 85,  72),   // Brown
    };

    public PlotWindow(
        TelemetryDatabaseService db,
        Dictionary<uint, string> nodeNames,
        uint preselectedNodeId = 0)
    {
        InitializeComponent();
        _db        = db;
        _nodeNames = nodeNames;

        // Populate node list
        foreach (var kv in nodeNames.OrderBy(k => k.Value))
        {
            var item = new ListBoxItem
            {
                Tag     = kv.Key,
                Content = kv.Value,
            };
            NodeListBox.Items.Add(item);
            if (kv.Key == preselectedNodeId)
                NodeListBox.SelectedItems.Add(item);
        }

        // Pre-select SNR
        if (MetricListBox.Items.Count > 0)
            MetricListBox.SelectedIndex = 0;

        MetricListBox.SelectionChanged += OnSelectionChanged;

        _ready = true;
        Loaded += (_, _) => RefreshPlot();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshPlot();
    private void OnPlotRangeChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _days = PlotRange7d.IsChecked  == true ? 7
              : PlotRange14d.IsChecked == true ? 14
              : PlotRange30d.IsChecked == true ? 30
              : 0;
        RefreshPlot();
    }

    private void OnViewChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _rollingWindow = ViewAvg1h.IsChecked  == true ? TimeSpan.FromHours(1)
                       : ViewAvg6h.IsChecked  == true ? TimeSpan.FromHours(6)
                       : ViewAvg24h.IsChecked == true ? TimeSpan.FromHours(24)
                       : TimeSpan.Zero;
        RefreshPlot();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshPlot();

    private void RefreshPlot()
    {
        if (!_ready) return;
        var selectedNodes = NodeListBox.SelectedItems.OfType<ListBoxItem>()
                                       .Select(i => (uint)(i.Tag ?? 0u))
                                       .Where(id => id != 0)
                                       .ToArray();

        var selectedMetrics = MetricListBox.SelectedItems.OfType<ListBoxItem>()
                                           .Select(i => i.Tag as string ?? "snr")
                                           .ToArray();

        if (selectedNodes.Length == 0 || selectedMetrics.Length == 0)
        {
            StatusText.Text = "Bitte Node und Metrik wählen.";
            Plot.Model      = null;
            return;
        }

        try
        {
            var model = new PlotModel
            {
                Background          = OxyColor.FromArgb(0, 0, 0, 0),
                PlotAreaBorderColor = OxyColor.FromRgb(100, 100, 100),
            };
            model.Legends.Add(new Legend
            {
                LegendPosition   = LegendPosition.TopLeft,
                LegendBackground = OxyColor.FromArgb(160, 30, 30, 30),
                LegendTextColor  = OxyColors.LightGray,
            });

            // X axis: time
            model.Axes.Add(new DateTimeAxis
            {
                Position   = AxisPosition.Bottom,
                StringFormat = "dd.MM HH:mm",
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(60, 150, 150, 150),
                TextColor  = OxyColors.LightGray,
                TicklineColor = OxyColors.LightGray,
            });

            // Keep track of unique axis keys per metric type
            var yAxes = new Dictionary<string, string>(); // metric → axisKey
            int colorIndex = 0;

            foreach (var metric in selectedMetrics)
            {
                string yUnit = metric switch
                {
                    "snr"          => "dB",
                    "rssi"         => "dBm",
                    "battery"      => "%",
                    "voltage"      => "V",
                    "channel_util" => "%",
                    "air_tx_util"  => "%",
                    "hop_count"    => "",
                    _              => "",
                };
                string axisKey = metric switch
                {
                    "rssi"         => "rssi",
                    "battery" or "channel_util" or "air_tx_util" => "pct",
                    "voltage"      => "volt",
                    "hop_count"    => "hops",
                    _              => "db",
                };

                if (!yAxes.ContainsKey(axisKey))
                {
                    model.Axes.Add(new LinearAxis
                    {
                        Key      = axisKey,
                        Position = yAxes.Count % 2 == 0 ? AxisPosition.Left : AxisPosition.Right,
                        Title    = yUnit,
                        MajorGridlineStyle = LineStyle.Dot,
                        MajorGridlineColor = OxyColor.FromArgb(50, 150, 150, 150),
                        TextColor    = OxyColors.LightGray,
                        TicklineColor = OxyColors.LightGray,
                        TitleColor   = OxyColors.LightGray,
                    });
                    yAxes[axisKey] = axisKey;
                }

                string metricLabel = metric switch
                {
                    "snr"          => "SNR",
                    "rssi"         => "RSSI",
                    "battery"      => "Batterie",
                    "voltage"      => "Spannung",
                    "channel_util" => "Ch-Util",
                    "air_tx_util"  => "Air-TX",
                    "hop_count"    => "Hops",
                    _              => metric,
                };

                var points = _db.GetTimeSeries(selectedNodes, metric, _days);
                if (_rollingWindow > TimeSpan.Zero)
                    points = TelemetryDatabaseService.RollingAverage(points, _rollingWindow);

                foreach (var nodeId in selectedNodes)
                {
                    var nodePoints = points.Where(p => p.NodeId == nodeId)
                                          .OrderBy(p => p.Timestamp)
                                          .ToList();
                    if (nodePoints.Count == 0) continue;

                    _nodeNames.TryGetValue(nodeId, out var nodeName);
                    string seriesTitle = selectedNodes.Length == 1
                        ? metricLabel
                        : $"{nodeName ?? $"!{nodeId:x8}"} – {metricLabel}";

                    var color = Palette[colorIndex % Palette.Length];
                    colorIndex++;

                    var series = new LineSeries
                    {
                        Title          = seriesTitle,
                        Color          = color,
                        StrokeThickness = 1.5,
                        MarkerType     = nodePoints.Count < 100 ? MarkerType.Circle : MarkerType.None,
                        MarkerSize     = 3,
                        YAxisKey       = axisKey,
                    };

                    foreach (var pt in nodePoints)
                        series.Points.Add(DateTimeAxis.CreateDataPoint(pt.Timestamp, pt.Value));

                    model.Series.Add(series);
                }
            }

            Plot.Model  = model;
            int totalPts = model.Series.OfType<LineSeries>().Sum(s => s.Points.Count);
            string noDataHint = model.Series.Count == 0 ? " – keine DB-Einträge im Zeitraum" : "";
            StatusText.Text = $"{selectedNodes.Length} Node(s) × {selectedMetrics.Length} Metrik(en) – {model.Series.Count} Serien, {totalPts} Punkte{noDataHint}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Fehler: {ex.Message}";
        }
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (Plot.Model == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Plot als PNG speichern",
            Filter     = "PNG-Bild (*.png)|*.png",
            DefaultExt = ".png",
            FileName   = $"telemetry_{DateTime.Now:yyyyMMdd_HHmm}.png",
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var exporter = new OxyPlot.Wpf.PngExporter { Width = 1200, Height = 700 };
                using var stream = File.Create(dlg.FileName);
                exporter.Export(Plot.Model, stream);
                StatusText.Text = $"Gespeichert: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export fehlgeschlagen: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
