using System.Windows;
using System.Windows.Input;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;

namespace MeshhessenClient;

/// <summary>
/// Small modal map popup to pick ONE exact position by hand (expert triangulation:
/// "position exactly known"). Shows the node's currently known position as an orange
/// reference marker; a click (without drag — panning/zooming works normally) returns
/// the coordinates. Uses the same raster tile source/cache as the main raster map.
/// </summary>
public partial class MapPickWindow : Window
{
    /// <summary>The picked coordinates when DialogResult is true.</summary>
    public (double Lat, double Lon) Picked { get; private set; }

    private Point _downPos;
    private bool _downSeen;

    public MapPickWindow(BruTile.ITileSource tileSource, double centerLat, double centerLon,
        string hint, (double Lat, double Lon)? referencePos)
    {
        InitializeComponent();
        HintText.Text = hint;

        var map = new Map();
        map.Layers.Add(new TileLayer(tileSource) { Name = "Base" });

        // Reference marker: where the node claims/was last known to be
        if (referencePos != null)
        {
            var (x, y) = SphericalMercator.FromLonLat(referencePos.Value.Lon, referencePos.Value.Lat);
            var feat = new PointFeature(new MPoint(x, y));
            feat.Styles.Add(new SymbolStyle
            {
                SymbolScale = 0.6,
                Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromArgb(200, 255, 140, 0)),
                Outline = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 2),
            });
            map.Layers.Add(new MemoryLayer("Reference") { Features = new[] { feat } });
        }

        PickMap.Map = map;
        Loaded += (_, _) =>
        {
            var (cx, cy) = SphericalMercator.FromLonLat(centerLon, centerLat);
            map.Navigator.CenterOnAndZoomTo(new MPoint(cx, cy), 9.6); // ≈ zoom 14
        };

        // Click vs. drag: only a (nearly) motionless left click picks — panning stays usable
        PickMap.PreviewMouseLeftButtonDown += (_, e) => { _downPos = e.GetPosition(PickMap); _downSeen = true; };
        PickMap.MouseLeftButtonUp += (_, e) =>
        {
            if (!_downSeen) return;
            _downSeen = false;
            var up = e.GetPosition(PickMap);
            if (Math.Abs(up.X - _downPos.X) > 5 || Math.Abs(up.Y - _downPos.Y) > 5) return; // drag = pan

            var world = PickMap.Map!.Navigator.Viewport.ScreenToWorld(up.X, up.Y);
            var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);
            Picked = (lat, lon);
            DialogResult = true;
            Close();
        };
        PickMap.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(PickMap);
            var world = PickMap.Map!.Navigator.Viewport.ScreenToWorld(p.X, p.Y);
            var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            CoordText.Text = $"{lat.ToString("0.00000", ci)}, {lon.ToString("0.00000", ci)}";
        };
    }
}
