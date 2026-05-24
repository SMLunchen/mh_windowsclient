using System.IO;
using System.Windows;
using System.Windows.Input;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using NetTopologySuite.Geometries;
using MeshhessenClient.Services;
using Mapsui.Nts;
using BruTile;
using BruTile.Predefined;

namespace MeshhessenClient;

public partial class TDeckRegionMapWindow : Window
{
    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    public double ResultNorth { get; private set; }
    public double ResultSouth { get; private set; }
    public double ResultEast  { get; private set; }
    public double ResultWest  { get; private set; }
    public bool   Accepted    { get; private set; }

    private readonly WritableLayer _drawLayer = new() { Style = null };

    // Completed shapes
    private readonly List<(MPoint Start, MPoint End)> _shapes = new();

    // Shape currently being drawn
    private bool   _isDrawing;
    private MPoint _currentStart = new();
    private MPoint _currentEnd   = new();

    // Draw mode flag – when false, Mapsui panning is active
    private bool _drawModeActive;

    private static readonly string LocalTileDir =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");

    public TDeckRegionMapWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetupMap();

        // Use Preview events so we can intercept before Mapsui's own pan handler
        MapControl.AddHandler(PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(Map_PreviewMouseDown), handledEventsToo: false);
        MapControl.AddHandler(PreviewMouseMoveEvent,
            new MouseEventHandler(Map_PreviewMouseMove), handledEventsToo: false);
        MapControl.AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(Map_PreviewMouseUp), handledEventsToo: false);
    }

    private void SetupMap()
    {
        var map = new Mapsui.Map();

        var localOsmDir = Path.Combine(LocalTileDir, "osm");
        bool hasLocalTiles = Directory.Exists(localOsmDir) &&
            Directory.EnumerateFiles(localOsmDir, "*.png", SearchOption.AllDirectories).Any();

        if (hasLocalTiles)
        {
            var schema   = new GlobalSphericalMercator(YAxis.TMS, 0, 18, "OSM");
            var provider = new LocalFileTileProvider(LocalTileDir, "osm");
            var source   = new TileSource(provider, schema);
            map.Layers.Add(new TileLayer(source) { Name = "Tiles" });
        }
        else
        {
            map.Layers.Add(OpenStreetMap.CreateTileLayer("MeshhessenClient/TDeckWizard"));
        }

        map.Layers.Add(_drawLayer);

        // Use Home callback – fires after the viewport is sized, so Resolutions are populated
        var center = SphericalMercator.FromLonLat(10.0, 51.3);
        map.Home = n => n.CenterOnAndZoomTo(new MPoint(center.x, center.y), 611.0);

        MapControl.Map = map;
    }

    // ──────────────────────────────────────────────────────────
    //  DRAW MODE TOGGLE
    // ──────────────────────────────────────────────────────────
    private void DrawModeBtn_Changed(object sender, RoutedEventArgs e)
    {
        _drawModeActive = DrawModeBtn.IsChecked == true;
        DrawModeBtn.Content = _drawModeActive
            ? Loc("StrTDeckMapDrawModeOn")
            : Loc("StrTDeckMapDrawModeOff");
        MapControl.Cursor = _drawModeActive ? Cursors.Cross : Cursors.Arrow;

        // If user switches back to nav mode mid-draw, discard the incomplete shape
        if (!_drawModeActive && _isDrawing)
        {
            _isDrawing = false;
            MapControl.ReleaseMouseCapture();
            _currentStart = new MPoint();
            _currentEnd   = new MPoint();
            UpdateDrawLayer();
        }
    }

    // ──────────────────────────────────────────────────────────
    //  MOUSE HANDLING (Preview = before Mapsui pan handler)
    // ──────────────────────────────────────────────────────────
    private void Map_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_drawModeActive) return;
        e.Handled = true;   // blocks Mapsui panning
        _isDrawing    = true;
        var pos       = e.GetPosition(MapControl);
        _currentStart = MapControl.Map.Navigator.Viewport.ScreenToWorld(pos.X, pos.Y);
        _currentEnd   = _currentStart;
        MapControl.CaptureMouse();
    }

    private void Map_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;
        e.Handled   = true;
        var pos     = e.GetPosition(MapControl);
        _currentEnd = MapControl.Map.Navigator.Viewport.ScreenToWorld(pos.X, pos.Y);
        UpdateDrawLayer();
    }

    private void Map_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        e.Handled = true;
        _isDrawing = false;
        MapControl.ReleaseMouseCapture();

        var pos     = e.GetPosition(MapControl);
        _currentEnd = MapControl.Map.Navigator.Viewport.ScreenToWorld(pos.X, pos.Y);

        // Only keep shapes with actual area
        if (Math.Abs(_currentEnd.X - _currentStart.X) > 1 &&
            Math.Abs(_currentEnd.Y - _currentStart.Y) > 1)
        {
            _shapes.Add((_currentStart, _currentEnd));
        }

        _currentStart = new MPoint();
        _currentEnd   = new MPoint();
        UpdateDrawLayer();
        UpdateCoordText();
    }

    // ──────────────────────────────────────────────────────────
    //  CLEAR BUTTONS
    // ──────────────────────────────────────────────────────────
    private void ClearLastBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_shapes.Count > 0) _shapes.RemoveAt(_shapes.Count - 1);
        UpdateDrawLayer();
        UpdateCoordText();
    }

    private void ClearAllBtn_Click(object sender, RoutedEventArgs e)
    {
        _shapes.Clear();
        _isDrawing    = false;
        _currentStart = new MPoint();
        _currentEnd   = new MPoint();
        MapControl.ReleaseMouseCapture();
        UpdateDrawLayer();
        UpdateCoordText();
    }

    // ──────────────────────────────────────────────────────────
    //  DRAW LAYER
    // ──────────────────────────────────────────────────────────
    private void UpdateDrawLayer()
    {
        _drawLayer.Clear();

        foreach (var (s, end) in _shapes)
            AddRect(s, end, completed: true);

        if (_isDrawing && (Math.Abs(_currentEnd.X - _currentStart.X) > 1 ||
                           Math.Abs(_currentEnd.Y - _currentStart.Y) > 1))
            AddRect(_currentStart, _currentEnd, completed: false);

        _drawLayer.DataHasChanged();
        AcceptBtn.IsEnabled = _shapes.Count > 0;
        ClearLastBtn.IsEnabled = _shapes.Count > 0;
        ClearAllBtn.IsEnabled  = _shapes.Count > 0;
    }

    private void AddRect(MPoint s, MPoint e, bool completed)
    {
        double minX = Math.Min(s.X, e.X), maxX = Math.Max(s.X, e.X);
        double minY = Math.Min(s.Y, e.Y), maxY = Math.Max(s.Y, e.Y);

        var ring = new LinearRing(new[]
        {
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        });

        var feature = new GeometryFeature
        {
            Geometry = new NetTopologySuite.Geometries.Polygon(ring)
        };
        feature.Styles.Add(new VectorStyle
        {
            Fill    = new Brush(completed ? new Color(30, 144, 255, 70)  : new Color(255, 165, 0, 70)),
            Outline = new Pen(completed  ? new Color(30, 144, 255, 220) : new Color(255, 165, 0, 220), 2)
        });
        _drawLayer.Add(feature);
    }

    // ──────────────────────────────────────────────────────────
    //  COORD TEXT + BBOX
    // ──────────────────────────────────────────────────────────
    private void UpdateCoordText()
    {
        if (_shapes.Count == 0)
        {
            CoordText.Text = Loc("StrTDeckMapPickNoSel");
            AcceptBtn.IsEnabled = false;
            return;
        }

        var (north, south, west, east) = ComputeUnionBbox();
        CoordText.Text = string.Format(Loc("StrTDeckMapPickSel"),
            _shapes.Count, north, south, west, east);
    }

    private (double North, double South, double West, double East) ComputeUnionBbox()
    {
        double north = double.MinValue, south = double.MaxValue;
        double west  = double.MaxValue, east  = double.MinValue;

        foreach (var (s, e) in _shapes)
        {
            var (lon1, lat1) = SphericalMercator.ToLonLat(s.X, s.Y);
            var (lon2, lat2) = SphericalMercator.ToLonLat(e.X, e.Y);
            north = Math.Max(north, Math.Max(lat1, lat2));
            south = Math.Min(south, Math.Min(lat1, lat2));
            west  = Math.Min(west,  Math.Min(lon1, lon2));
            east  = Math.Max(east,  Math.Max(lon1, lon2));
        }
        return (north, south, west, east);
    }

    // ──────────────────────────────────────────────────────────
    //  ACCEPT / CANCEL
    // ──────────────────────────────────────────────────────────
    private void AcceptBtn_Click(object sender, RoutedEventArgs e)
    {
        var (north, south, west, east) = ComputeUnionBbox();
        ResultNorth = north;
        ResultSouth = south;
        ResultWest  = west;
        ResultEast  = east;
        Accepted    = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => Close();
}
