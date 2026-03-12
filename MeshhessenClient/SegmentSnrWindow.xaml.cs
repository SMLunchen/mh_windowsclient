using System.Windows;
using MeshhessenClient.Services;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace MeshhessenClient;

public partial class SegmentSnrWindow : Window
{
    public SegmentSnrWindow(
        string fromLabel,
        string toLabel,
        List<TelemetryDatabaseService.SegmentSnrPoint> points,
        TelemetryDatabaseService.SegmentSnrStats? stats,
        double myLat,
        double myLon)
    {
        InitializeComponent();

        TitleText.Text = $"SNR-Verlauf: {fromLabel} → {toLabel}";

        if (points.Count == 0)
        {
            StatusText.Text = "Keine historischen SNR-Daten für dieses Segment in der DB.";
            return;
        }

        var model = new PlotModel
        {
            Background          = OxyColor.FromArgb(0, 0, 0, 0),
            PlotAreaBorderColor = OxyColor.FromRgb(100, 100, 100),
        };

        // X axis: time
        var xAxis = new DateTimeAxis
        {
            Position           = AxisPosition.Bottom,
            StringFormat       = "dd.MM HH:mm",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(60, 150, 150, 150),
            TextColor          = OxyColors.LightGray,
            TicklineColor      = OxyColors.LightGray,
        };
        model.Axes.Add(xAxis);

        var yAxis = new LinearAxis
        {
            Position           = AxisPosition.Left,
            Title              = "SNR (dB)",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(50, 150, 150, 150),
            TextColor          = OxyColors.LightGray,
            TicklineColor      = OxyColors.LightGray,
            TitleColor         = OxyColors.LightGray,
        };
        model.Axes.Add(yAxis);

        // Add night-time shading bands using SunriseSunsetService
        if (myLat != 0 || myLon != 0)
        {
            var minDate = points.Min(p => p.Timestamp).Date;
            var maxDate = points.Max(p => p.Timestamp).Date.AddDays(1);
            for (var d = minDate; d < maxDate; d = d.AddDays(1))
            {
                var (sr, ss) = SunriseSunsetService.GetSunriseSunset(myLat, myLon, d);
                // Night before sunrise
                var nightStart = DateTimeAxis.ToDouble(d);
                var sunriseD   = DateTimeAxis.ToDouble(d.Add(sr));
                model.Annotations.Add(new RectangleAnnotation
                {
                    MinimumX   = nightStart,
                    MaximumX   = sunriseD,
                    Fill       = OxyColor.FromArgb(30, 0, 80, 180),
                    Layer      = AnnotationLayer.BelowSeries,
                });
                // Night after sunset
                var sunsetD  = DateTimeAxis.ToDouble(d.Add(ss));
                var nightEnd = DateTimeAxis.ToDouble(d.AddDays(1));
                model.Annotations.Add(new RectangleAnnotation
                {
                    MinimumX   = sunsetD,
                    MaximumX   = nightEnd,
                    Fill       = OxyColor.FromArgb(30, 0, 80, 180),
                    Layer      = AnnotationLayer.BelowSeries,
                });
            }
        }

        // Main SNR line series
        var series = new LineSeries
        {
            Title           = "SNR",
            Color           = OxyColor.FromRgb(33, 150, 243),
            StrokeThickness = 1.8,
            MarkerType      = points.Count < 200 ? MarkerType.Circle : MarkerType.None,
            MarkerSize      = 3,
            MarkerFill      = OxyColor.FromRgb(33, 150, 243),
        };
        foreach (var pt in points)
            series.Points.Add(DateTimeAxis.CreateDataPoint(pt.Timestamp, pt.Snr));
        model.Series.Add(series);

        Plot.Model = model;

        // Status line with stats
        if (stats != null)
            StatusText.Text = $"{points.Count} Messpunkte | Min: {stats.Min:F1} dB | Avg: {stats.Avg:F1} dB | Max: {stats.Max:F1} dB  (blau = Nacht)";
        else
            StatusText.Text = $"{points.Count} Messpunkte  (blau = Nacht)";
    }
}
