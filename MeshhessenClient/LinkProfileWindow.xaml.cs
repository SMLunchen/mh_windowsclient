using System.Windows;
using MeshhessenClient.Services;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace MeshhessenClient;

/// <summary>
/// Elevation profile of one RF link: SRTM terrain (plus 4/3-earth bulge) between the
/// two nodes, the line of sight between the antennas and the 60 % first-Fresnel-zone
/// clearance at 868 MHz. Terrain comes from the Meshhessen terrain-RGB tiles via
/// TerrainService (cached, offline-capable once fetched).
/// </summary>
public partial class LinkProfileWindow : Window
{
    private readonly string _nameA, _nameB;
    private readonly double _latA, _lonA, _latB, _lonB, _hA, _hB;

    private static string Loc(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public LinkProfileWindow(
        string nameA, double latA, double lonA,
        string nameB, double latB, double lonB,
        double antennaHeightAM = TriangulationService.DefaultAntennaHeightM,
        double antennaHeightBM = TriangulationService.DefaultAntennaHeightM)
    {
        InitializeComponent();
        _nameA = nameA; _latA = latA; _lonA = lonA; _hA = antennaHeightAM;
        _nameB = nameB; _latB = latB; _lonB = lonB; _hB = antennaHeightBM;

        double distKm = TriangulationService.HaversineKm(latA, lonA, latB, lonB);
        TitleText.Text = $"{nameA}  →  {nameB}   ·   {distKm:0.0} km";
        Loaded += async (_, _) => await BuildAsync();
    }

    private async Task BuildAsync()
    {
        StatusText.Text = "…";
        TerrainGrid? grid = null;
        try
        {
            grid = await TerrainService.Instance.LoadGridAsync(
                Math.Min(_latA, _latB), Math.Min(_lonA, _lonB),
                Math.Max(_latA, _latB), Math.Max(_lonA, _lonB));
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[LinkProfile] terrain load failed: {ex.Message}");
        }
        if (grid == null || grid.ElevationAt(_latA, _lonA) == null || grid.ElevationAt(_latB, _lonB) == null)
        {
            StatusText.Text = Loc("StrLinkProfileNoData");
            return;
        }

        const double FreqMhz = 868.0;
        const int N = 240;
        double dKm = TriangulationService.HaversineKm(_latA, _lonA, _latB, _lonB);
        double dM = dKm * 1000.0;
        double lambda = 299.792458 / FreqMhz;
        const double EffEarthM = 4.0 / 3.0 * 6371000.0;

        double e1 = grid.ElevationAt(_latA, _lonA)!.Value + _hA;
        double e2 = grid.ElevationAt(_latB, _lonB)!.Value + _hB;

        var terrain = new AreaSeries
        {
            Title = Loc("StrLinkProfileTerrain"),
            Color = OxyColor.FromRgb(96, 140, 72),
            Fill  = OxyColor.FromArgb(110, 76, 125, 60),
            StrokeThickness = 1.5,
        };
        var los = new LineSeries
        {
            Title = Loc("StrLinkProfileLos"),
            Color = OxyColors.LightGray,
            StrokeThickness = 1.5,
        };
        var fresnel = new LineSeries
        {
            Title = Loc("StrLinkProfileFresnel"),
            Color = OxyColor.FromRgb(255, 160, 60),
            StrokeThickness = 1.2,
            LineStyle = LineStyle.Dash,
        };

        double minMargin = double.MaxValue, minMarginKm = 0;
        double baseMin = Math.Min(e1, e2);
        for (int i = 0; i <= N; i++)
        {
            double t = (double)i / N;
            double xKm = dKm * t;
            double? te = grid.ElevationAt(_latA + (_latB - _latA) * t, _lonA + (_lonB - _lonA) * t);
            if (te == null) continue;

            double d1 = Math.Max(dM * t, 1), d2 = Math.Max(dM * (1 - t), 1);
            double bulge = d1 * d2 / (2 * EffEarthM);
            double ground = te.Value + bulge;              // effective-earth profile view
            double losH = e1 + (e2 - e1) * t;
            double f1 = Math.Sqrt(lambda * d1 * d2 / dM);  // 1st Fresnel radius (m)

            terrain.Points.Add(new DataPoint(xKm, ground));
            los.Points.Add(new DataPoint(xKm, losH));
            fresnel.Points.Add(new DataPoint(xKm, losH - 0.6 * f1));

            baseMin = Math.Min(baseMin, ground);
            double margin = losH - 0.6 * f1 - ground;      // >0 = clear incl. Fresnel
            if (i > 0 && i < N && margin < minMargin) { minMargin = margin; minMarginKm = xKm; }
        }
        foreach (var p in terrain.Points) terrain.Points2.Add(new DataPoint(p.X, baseMin - 10));

        var model = new PlotModel
        {
            Background          = OxyColor.FromArgb(0, 0, 0, 0),
            PlotAreaBorderColor = OxyColor.FromRgb(100, 100, 100),
        };
        model.Legends.Add(new Legend
        {
            LegendPosition   = LegendPosition.TopRight,
            LegendBackground = OxyColor.FromArgb(160, 30, 30, 30),
            LegendTextColor  = OxyColors.LightGray,
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom, Title = "km",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(60, 150, 150, 150),
            TextColor = OxyColors.LightGray, TicklineColor = OxyColors.LightGray,
            TitleColor = OxyColors.LightGray, Minimum = 0, Maximum = dKm,
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left, Title = "m",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(50, 150, 150, 150),
            TextColor = OxyColors.LightGray, TicklineColor = OxyColors.LightGray,
            TitleColor = OxyColors.LightGray,
        });
        model.Series.Add(terrain);
        model.Series.Add(fresnel);
        model.Series.Add(los);
        Plot.Model = model;

        // Verdict: same math the triangulation uses
        double v = TriangulationService.PathFresnelV(grid.ElevationAt,
            _latA, _lonA, _hA, _latB, _lonB, _hB, FreqMhz, samples: N);
        double lossDb = TriangulationService.KnifeEdgeLossDb(v);
        StatusText.Text = lossDb <= 0
            ? string.Format(Loc("StrLinkProfileClear"), Math.Max(minMargin, 0).ToString("0"))
            : string.Format(Loc("StrLinkProfileBlocked"), lossDb.ToString("0"), minMarginKm.ToString("0.0"));
    }
}
