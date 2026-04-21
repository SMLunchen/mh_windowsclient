using System.Windows;
using System.Windows.Input;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;

namespace MeshhessenClient;

public partial class MapPickerWindow : Window
{
    public (double lat, double lon)? SelectedPosition { get; private set; }

    private Mapsui.Map? _map;
    private MemoryLayer? _markerLayer;
    private readonly List<IFeature> _markerFeatures = new();
    private readonly Func<TileLayer>? _tileLayerFactory;

    public MapPickerWindow(double initialLat, double initialLon, Func<TileLayer>? tileLayerFactory = null)
    {
        InitializeComponent();
        _tileLayerFactory = tileLayerFactory;
        Loaded += (_, _) => InitMap(initialLat, initialLon);
    }

    private void InitMap(double initialLat, double initialLon)
    {
        try
        {
            _map = new Mapsui.Map();

            // Use the same tile layer as the main map; fall back to online OSM if no factory provided
            var tileLayer = _tileLayerFactory?.Invoke() ?? OpenStreetMap.CreateTileLayer("MeshhessenClient/MapPicker");
            _map.Layers.Add(tileLayer);

            _markerLayer = new MemoryLayer("Marker") { Features = _markerFeatures, Style = null };
            _map.Layers.Add(_markerLayer);

            MapControl.Map = _map;
            MapControl.MouseLeftButtonUp += MapControl_Click;

            var center = SphericalMercator.FromLonLat(initialLon, initialLat);
            _map.Home = n => n.CenterOnAndZoomTo(new MPoint(center.x, center.y), 153.0); // ~zoom 12
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR MapPickerWindow InitMap: {ex.Message}");
        }
    }

    private void MapControl_Click(object sender, MouseButtonEventArgs e)
    {
        if (_map == null) return;
        try
        {
            var screenPos = e.GetPosition(MapControl);
            var viewport = _map.Navigator.Viewport;
            var worldPos = viewport.ScreenToWorld(screenPos.X, screenPos.Y);
            var lonLat = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);

            SelectedPosition = (lonLat.lat, lonLat.lon);
            AcceptButton.IsEnabled = true;

            CoordHintText.Text = $"{lonLat.lat:F6}, {lonLat.lon:F6}";

            // Update marker
            _markerFeatures.Clear();
            var feature = new PointFeature(worldPos.X, worldPos.Y);
            feature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 0.7,
                Fill = new Brush(Mapsui.Styles.Color.FromArgb(200, 220, 60, 60)),
                Outline = new Pen(Mapsui.Styles.Color.White, 2),
                SymbolType = SymbolType.Ellipse
            });
            _markerFeatures.Add(feature);
            _markerLayer?.DataHasChanged();
            MapControl.Refresh();
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR MapPickerWindow click: {ex.Message}");
        }
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
