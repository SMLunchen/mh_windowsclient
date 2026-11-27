using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using BruTile;
using MeshhessenClient.Models;

namespace MeshhessenClient;

/// <summary>
/// "Lagebild" tab: a fixed 70 km situation map around our position in phosphor green, Pip-Boy style.
/// Raster tiles of the configured map are fetched once per layout, reduced to a coarse pixelated
/// monochrome and clipped to the scope circle. Every packet we hear appears at the sender's position and
/// fades after the configured time; directed requests to us play the sonar ping (sender → us → sender),
/// traceroutes draw their recorded path. No zoom, no pan — it is a scope, not a map.
/// </summary>
public partial class RequestEchoWindow
{
    private const double ScopeRadiusKm = 70;
    // Base map at full resolution: a reduced one made place names unreadable as soon as the window grew
    // (bigger scope → next tile zoom → smaller labels). The coarse look comes from drawing the tiles
    // enlarged with nearest-neighbour instead (see ScopeTileUpscale), plus the posterised green ramp.
    private const int    ScopePixelation = 1;
    private const double ScopeTileUpscale = 2.0;   // tiles may be drawn up to 2× before the next zoom level is used

    private Func<(double lat, double lon)?>? _myPosition;
    private Func<uint, (double lat, double lon)?>? _nodePosition;
    private Func<ITileSource?>? _tileSource;
    private Func<IReadOnlyList<(uint Id, string Role, double Lat, double Lon)>>? _routerNodes;

    private readonly Grid   _scopeHost    = new();
    private readonly Image  _scopeMap     = new() { Stretch = Stretch.Fill, IsHitTestVisible = false,
                                                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _scopeStatic  = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly Canvas _scopeRouters = new() { ClipToBounds = true };   // permanent infrastructure markers
    // No background brush: an opaque-to-hit-testing canvas would swallow mouse events meant for the router markers below
    private readonly Canvas _scopeLive    = new() { ClipToBounds = true };
    private readonly Canvas _scopePing    = new() { ClipToBounds = true, IsHitTestVisible = false };
    private bool _scopeShowRouters = ScopeRoutersDefault;
    private int _scopeTickCount;
    private readonly TextBlock _scopeStatus = new()
    {
        FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 360,
        Visibility = Visibility.Collapsed, IsHitTestVisible = false,
    };
    private readonly TextBlock _scopeFooter = new() { FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(10, 2, 10, 6) };
    private readonly DispatcherTimer _scopeTick     = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _scopeRelayout = new() { Interval = TimeSpan.FromMilliseconds(400) };

    /// <summary>Defaults handed in by the main window from the saved settings (the window is created per open).</summary>
    public static int ScopeTtlDefault = 45;
    public static int ScopeBrightnessDefault = 25;
    public static bool ScopeRoutersDefault = true;
    /// <summary>Raised when the user changes display time, map brightness or the router overlay:
    /// (ttl seconds, brightness %, routers shown).</summary>
    public event Action<int, int, bool>? ScopeSettingsChanged;

    private ScopeProjection? _scope;
    private double _scopeTtlSeconds = ScopeTtlDefault;
    private int _scopeBrightness = ScopeBrightnessDefault;
    private (byte[] Px, int W, int H, double Cx, double Cy, double R)? _scopeRaw;   // tile composite before the phosphor mapping
    private CancellationTokenSource? _scopeMapCts;
    private DateTime _scopeLastPacketUtc;
    private int _scopeUnplaced;

    private sealed class ScopeItem
    {
        public uint NodeId;
        public DateTime ExpiresUtc;
        /// <summary>While a draw-in animation runs, the ageing timer must not touch Opacity (it would
        /// reveal relay dots before the stroke reaches them).</summary>
        public DateTime HoldOpacityUntilUtc;
        public readonly List<FrameworkElement> Visuals = new();
    }
    private readonly List<ScopeItem> _scopeItems = new();
    private readonly Dictionary<uint, ScopeItem> _scopeBlips = new();

    /// <summary>Hook up the data sources (called from the constructor before the tab is built).</summary>
    private void InitScope(Func<(double lat, double lon)?>? myPosition, Func<uint, (double lat, double lon)?>? nodePosition,
        Func<ITileSource?>? tileSource, Func<IReadOnlyList<(uint Id, string Role, double Lat, double Lon)>>? routerNodes)
    {
        _myPosition   = myPosition;
        _nodePosition = nodePosition;
        _tileSource   = tileSource;
        _routerNodes  = routerNodes;
    }

    private FrameworkElement BuildScopeTab()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Top bar: how long a heard packet stays (30–60 s) + legend
        var bar = new DockPanel { Margin = new Thickness(10, 6, 10, 4), LastChildFill = true };
        var slider = new Slider
        {
            Minimum = 30, Maximum = 60, Value = _scopeTtlSeconds, TickFrequency = 5, IsSnapToTickEnabled = true,
            Width = 150, VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc("StrFunkEchoScopeTtlTip"),
        };
        var ttlLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center, FontSize = 11, MinWidth = 86,
            Margin = new Thickness(8, 0, 12, 0), Foreground = new SolidColorBrush(_fgSub),
        };
        void UpdateTtl()
        {
            _scopeTtlSeconds = slider.Value;
            ttlLabel.Text = string.Format(Loc("StrFunkEchoScopeTtl"), (int)slider.Value);
        }
        slider.ValueChanged += (_, _) => { UpdateTtl(); RaiseScopeSettings(); };
        UpdateTtl();

        // Base-map brightness: nodes and paths always glow at full phosphor, the map underneath is dimmed so
        // they stand out — taste varies with the tile style, so it is a slider (saved in the settings).
        var brightSlider = new Slider
        {
            Minimum = 20, Maximum = 100, Value = _scopeBrightness, TickFrequency = 5, IsSnapToTickEnabled = true,
            Width = 120, VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc("StrFunkEchoScopeBrightnessTip"),
        };
        var brightLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center, FontSize = 11, MinWidth = 70,
            Margin = new Thickness(8, 0, 12, 0), Foreground = new SolidColorBrush(_fgSub),
        };
        void UpdateBrightness(bool remap)
        {
            _scopeBrightness = (int)brightSlider.Value;
            brightLabel.Text = string.Format(Loc("StrFunkEchoScopeBrightness"), _scopeBrightness);
            if (remap) RemapScopeMap();
        }
        brightSlider.ValueChanged += (_, _) => { UpdateBrightness(remap: true); RaiseScopeSettings(); };
        UpdateBrightness(remap: false);

        // Infrastructure overlay: routers/repeaters with a known position stay on the map permanently
        var routersBox = new CheckBox
        {
            Content = Loc("StrFunkEchoScopeRouters"), IsChecked = _scopeShowRouters, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            ToolTip = Loc("StrFunkEchoScopeRoutersTip"),
        };
        routersBox.Checked   += (_, _) => { _scopeShowRouters = true;  DrawScopeRouters(); RaiseScopeSettings(); };
        routersBox.Unchecked += (_, _) => { _scopeShowRouters = false; DrawScopeRouters(); RaiseScopeSettings(); };

        var legend = new TextBlock
        {
            Text = Loc("StrFunkEchoScopeLegend"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(_fgMuted), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        DockPanel.SetDock(slider, Dock.Left);
        DockPanel.SetDock(ttlLabel, Dock.Left);
        DockPanel.SetDock(brightSlider, Dock.Left);
        DockPanel.SetDock(brightLabel, Dock.Left);
        DockPanel.SetDock(routersBox, Dock.Left);
        bar.Children.Add(slider);
        bar.Children.Add(ttlLabel);
        bar.Children.Add(brightSlider);
        bar.Children.Add(brightLabel);
        bar.Children.Add(routersBox);
        bar.Children.Add(legend);
        grid.Children.Add(bar);

        // Scope: base map (pixelated bitmap) → static rings → live blips/paths → ping overlay → status text
        _scopeHost.Background = new SolidColorBrush(Color.FromRgb(0x01, 0x05, 0x03));
        _scopeHost.ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(_scopeMap, BitmapScalingMode.NearestNeighbor);
        _scopeStatus.Foreground = new SolidColorBrush(PhosMid);
        _scopeHost.Children.Add(_scopeMap);
        _scopeHost.Children.Add(_scopeStatic);
        _scopeHost.Children.Add(_scopeRouters);
        _scopeHost.Children.Add(_scopeLive);
        _scopeHost.Children.Add(_scopePing);
        _scopeHost.Children.Add(_scopeStatus);
        _scopeHost.SizeChanged += (_, _) => { _scopeRelayout.Stop(); _scopeRelayout.Start(); };
        Grid.SetRow(_scopeHost, 1);
        grid.Children.Add(_scopeHost);

        _scopeFooter.Foreground = new SolidColorBrush(PhosMid);
        Grid.SetRow(_scopeFooter, 2);
        grid.Children.Add(_scopeFooter);

        _scopeRelayout.Tick += (_, _) => { _scopeRelayout.Stop(); RelayoutScope(); };
        _scopeTick.Tick += (_, _) =>
        {
            AgeScopeItems();
            if (++_scopeTickCount % 240 == 0) DrawScopeRouters();   // every 60 s: routers learned meanwhile
        };
        _scopeTick.Start();
        Closed += (_, _) => { _scopeTick.Stop(); _scopeRelayout.Stop(); _scopeMapCts?.Cancel(); };

        UpdateScopeFooter();
        return grid;
    }

    // ── layout: projection + static scope drawing + base map ─────────────────

    private void RelayoutScope()
    {
        double w = _scopeHost.ActualWidth, h = _scopeHost.ActualHeight;
        if (w < 80 || h < 80) return;

        var center = _myPosition?.Invoke();
        if (center == null)
        {
            _scope = null;
            _scopeMap.Source = null;
            _scopeStatic.Children.Clear();
            ClearScopeItems();
            ShowScopeStatus(Loc("StrFunkEchoScopeNoPos"));
            return;
        }

        double cx = w / 2, cy = h / 2, r = Math.Min(w, h) / 2 - 18;
        _scope = new ScopeProjection(center.Value.lat, center.Value.lon, ScopeRadiusKm, r, cx, cy, maxUpscale: ScopeTileUpscale);
        _scopeStatus.Visibility = Visibility.Collapsed;
        ClearScopeItems();   // old items were placed for another projection
        DrawScopeStatic(_scope);
        DrawScopeRouters();
        _ = BuildScopeMapAsync(_scope, (int)Math.Ceiling(w), (int)Math.Ceiling(h));
    }

    private void ShowScopeStatus(string text)
    {
        _scopeStatus.Text = text;
        _scopeStatus.Visibility = Visibility.Visible;
    }

    /// <summary>Re-map the cached tile composite with the current brightness (slider) — no refetch.</summary>
    private void RemapScopeMap()
    {
        if (_scopeRaw is not { } raw) return;
        try { _scopeMap.Source = MapToPhosphor(raw.Px, raw.W, raw.H, raw.Cx, raw.Cy, raw.R, _scopeBrightness); }
        catch (Exception ex) { Services.Logger.WriteLine($"[Scope] map remap failed: {ex.Message}"); }
    }

    private void RaiseScopeSettings() =>
        ScopeSettingsChanged?.Invoke((int)_scopeTtlSeconds, _scopeBrightness, _scopeShowRouters);

    /// <summary>Infrastructure nodes (router, router-client, repeater, router-late) with a known position as
    /// permanent diamond markers — the backbone stays visible between packets. Inside the radius only;
    /// heard-packet blips draw on top of them.</summary>
    private void DrawScopeRouters()
    {
        _scopeRouters.Children.Clear();
        if (_scope == null || !_scopeShowRouters || _routerNodes == null) return;
        IReadOnlyList<(uint Id, string Role, double Lat, double Lon)> routers;
        try { routers = _routerNodes(); }
        catch (Exception ex) { Services.Logger.WriteLine($"[Scope] router list failed: {ex.Message}"); return; }

        var stroke = new SolidColorBrush(PhosMid);
        var fill   = new SolidColorBrush(Color.FromArgb(70, PhosMid.R, PhosMid.G, PhosMid.B));
        var text   = new SolidColorBrush(Color.FromArgb(200, PhosMid.R, PhosMid.G, PhosMid.B));
        foreach (var r in routers)
        {
            var (x, y) = _scope.ToScope(r.Lat, r.Lon);
            if (_scope.ClampToRim(x, y, 6).Outside) continue;

            var marker = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,-6 L6,0 L0,6 L-6,0 Z"),
                Stroke = stroke, StrokeThickness = 1.5, Fill = fill,
                ToolTip = $"{_fullName(r.Id)}\n{r.Role}",
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            Canvas.SetLeft(marker, x);
            Canvas.SetTop(marker, y);
            marker.ContextMenu = BuildNodeContextMenu(r.Id, route: null);
            _scopeRouters.Children.Add(marker);

            var label = new TextBlock
            {
                Text = _shortLabel(r.Id), FontFamily = new FontFamily("Consolas"), FontSize = 9,
                Foreground = text, IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, x + 8);
            Canvas.SetTop(label, y - 7);
            _scopeRouters.Children.Add(label);
        }
    }

    /// <summary>Range rings (1/3, 2/3, 1 × 70 km), crosshair, north mark and our mast in the centre.</summary>
    private void DrawScopeStatic(ScopeProjection p)
    {
        _scopeStatic.Children.Clear();
        double cx = p.CenterX, cy = p.CenterY, maxR = p.RadiusPx;

        var rim = new Ellipse
        {
            Width = maxR * 2, Height = maxR * 2,
            Stroke = new SolidColorBrush(PhosDim), StrokeThickness = 1.5,
        };
        Canvas.SetLeft(rim, cx - maxR);
        Canvas.SetTop(rim, cy - maxR);
        _scopeStatic.Children.Add(rim);

        var gridBrush = new SolidColorBrush(Color.FromArgb(120, PhosGrid.R, PhosGrid.G, PhosGrid.B));
        foreach (double rr in new[] { 1.0 / 3, 2.0 / 3 })
        {
            var ring = new Ellipse
            {
                Width = maxR * 2 * rr, Height = maxR * 2 * rr,
                Stroke = gridBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 },
            };
            Canvas.SetLeft(ring, cx - maxR * rr);
            Canvas.SetTop(ring, cy - maxR * rr);
            _scopeStatic.Children.Add(ring);
        }
        foreach (double rr in new[] { 1.0 / 3, 2.0 / 3, 1.0 })
        {
            var km = new TextBlock
            {
                Text = string.Format(Loc("StrFunkEchoScopeRing"), Math.Round(ScopeRadiusKm * rr)),
                FontFamily = new FontFamily("Consolas"), FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(200, PhosDim.R, PhosDim.G, PhosDim.B)),
            };
            Canvas.SetLeft(km, cx + 4);
            Canvas.SetTop(km, cy - maxR * rr + 2);
            _scopeStatic.Children.Add(km);
        }
        _scopeStatic.Children.Add(new Line { X1 = cx - maxR, Y1 = cy, X2 = cx + maxR, Y2 = cy, Stroke = gridBrush, StrokeThickness = 1 });
        _scopeStatic.Children.Add(new Line { X1 = cx, Y1 = cy - maxR, X2 = cx, Y2 = cy + maxR, Stroke = gridBrush, StrokeThickness = 1 });

        var north = new TextBlock
        {
            Text = "N", FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(PhosMid),
        };
        north.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(north, cx - north.DesiredSize.Width / 2);
        Canvas.SetTop(north, cy - maxR - 16);
        _scopeStatic.Children.Add(north);

        var mast = new System.Windows.Shapes.Path   // System.IO.Path is in scope too (tile streams)
        {
            Stroke = new SolidColorBrush(PhosBright), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M0,-7 L-4.8,7 H4.8 Z M-3,2.8 H3 M-2,-1.4 H2 M0,-7 V-10"),
            Effect = new DropShadowEffect { Color = PhosBright, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 },
        };
        Canvas.SetLeft(mast, cx);
        Canvas.SetTop(mast, cy);
        _scopeStatic.Children.Add(mast);
    }

    /// <summary>Fetch the tiles covering the scope (cache-backed provider of the configured map), draw them
    /// at the scope scale, then turn the picture into coarse phosphor green: luminance → 6-step green ramp,
    /// inverted when the map is light (OSM) so land goes dark and lines glow, pixelated 2×, clipped to the
    /// circle with a soft vignette. Cancelled and redone when the scope is laid out again.</summary>
    private async Task BuildScopeMapAsync(ScopeProjection p, int w, int h)
    {
        _scopeMapCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scopeMapCts = cts;

        ITileSource? source = null;
        try { source = _tileSource?.Invoke(); } catch (Exception ex) { Services.Logger.WriteLine($"[Scope] tile source: {ex.Message}"); }
        if (source == null)
        {
            _scopeMap.Source = null;
            ShowScopeStatus(Loc("StrFunkEchoScopeNoMap"));
            return;
        }

        var (x0, x1, y0, y1) = p.TileRange();
        int z = p.Zoom, maxIdx = (1 << z) - 1;
        var fetches = new List<Task<(int X, int Y, byte[]? Png)>>();
        for (int tx = x0; tx <= x1; tx++)
            for (int ty = y0; ty <= y1; ty++)
            {
                int fx = tx, fy = ty;
                fetches.Add(Task.Run(async () =>
                {
                    try
                    {
                        // BruTile schema is TMS (row 0 = south); the providers convert back to OSM y.
                        var bytes = await source.GetTileAsync(new TileInfo { Index = new TileIndex(fx, maxIdx - fy, z) });
                        return (fx, fy, bytes);
                    }
                    catch { return (fx, fy, (byte[]?)null); }
                }, cts.Token));
            }

        List<(int X, int Y, byte[] Png)> tiles;
        try
        {
            var results = await Task.WhenAll(fetches);
            tiles = results.Where(t => t.Png is { Length: > 0 }).Select(t => (t.X, t.Y, t.Png!)).ToList();
        }
        catch (OperationCanceledException) { return; }
        if (cts.IsCancellationRequested || !ReferenceEquals(_scope, p)) return;

        try
        {
            _scopeMap.Width = w;
            _scopeMap.Height = h;
            double k = 1.0 / ScopePixelation;
            var raw = RenderScopeRaw(p, w, h, tiles, out int rw, out int rh);
            _scopeRaw = (raw, rw, rh, p.CenterX * k, p.CenterY * k, p.RadiusPx * k);
            RemapScopeMap();
            if (tiles.Count == 0) ShowScopeStatus(Loc("StrFunkEchoScopeNoMap"));
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[Scope] map render failed: {ex.Message}");
            _scopeMap.Source = null;
        }
    }

    /// <summary>Draw the fetched tiles at scope scale into a raw Pbgra32 composite (kept so the brightness
    /// slider can re-map without refetching).</summary>
    private static byte[] RenderScopeRaw(ScopeProjection p, int w, int h, List<(int X, int Y, byte[] Png)> tiles, out int rw, out int rh)
    {
        rw = Math.Max(1, w / ScopePixelation);
        rh = Math.Max(1, h / ScopePixelation);
        double k = 1.0 / ScopePixelation;

        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);   // blocky enlargement = the pixel look
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, rw, rh));
            foreach (var (tx, ty, png) in tiles)
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = new MemoryStream(png);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                var (x, y, size) = p.TileRect(tx, ty);
                dc.DrawImage(img, new Rect(x * k, y * k, size * k + 0.5, size * k + 0.5));
            }
        }
        var rtb = new RenderTargetBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        int stride = rw * 4;
        var px = new byte[stride * rh];
        rtb.CopyPixels(px, stride, 0);
        return px;
    }

    /// <summary>Phosphor mapping of the raw composite (works on a copy — the raw stays cached for the
    /// brightness slider). Brightness scales the whole green ramp; nodes and paths are drawn on top at full
    /// phosphor, so a dimmer map makes them stand out.</summary>
    private static BitmapSource MapToPhosphor(byte[] raw, int rw, int rh, double cx, double cy, double r, int brightnessPercent)
    {
        int stride = rw * 4;
        var px = (byte[])raw.Clone();
        double bright = Math.Clamp(brightnessPercent, 10, 100) / 100.0;

        // Luminance histogram inside the circle → percentile stretch, so every map style uses the full green
        // range: pale styles with grey labels (the Meshhessen OSM tiles) and dark styles with dim roads alike
        // came out as uniform murk with a fixed mapping. Light maps (median bright) are inverted so land goes
        // dark and lines/labels glow; dark maps keep their polarity.
        var hist = new int[256];
        int total = 0;
        for (int y = 0; y < rh; y += 2)
            for (int x = 0; x < rw; x += 2)
            {
                double dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > r * r) continue;
                int i = y * stride + x * 4;
                hist[(299 * px[i + 2] + 587 * px[i + 1] + 114 * px[i]) / 1000]++;
                total++;
            }
        int Percentile(double q)
        {
            if (total == 0) return q < 0.5 ? 0 : 255;
            long target = (long)(q * total), seen = 0;
            for (int v = 0; v < 256; v++) { seen += hist[v]; if (seen >= target) return v; }
            return 255;
        }
        int lo = Percentile(0.03), hi = Percentile(0.97), median = Percentile(0.5);
        if (hi - lo < 40) { lo = Math.Max(0, median - 60); hi = Math.Min(255, median + 60); }   // nearly flat input
        bool invert = median > 128;
        double span = Math.Max(1, hi - lo);

        // 6-step phosphor ramp (dark → bright)
        var ramp = new (byte R, byte G, byte B)[]
        {
            (0x01, 0x05, 0x03), (0x06, 0x24, 0x18), (0x0C, 0x44, 0x2E), (0x14, 0x6E, 0x4B), (0x20, 0xA6, 0x72), (0x4A, 0xE8, 0xA8),
        };
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                int i = y * stride + x * 4;
                double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > r) { px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0; continue; }

                int lum = (299 * px[i + 2] + 587 * px[i + 1] + 114 * px[i]) / 1000;
                double v = Math.Clamp((lum - lo) / span, 0, 1);
                if (invert) v = 1 - v;
                v = Math.Pow(v, 1.5);   // keep labels/lines bright, push forests and area fills into the dark steps
                double vignette = d > r * 0.8 ? 1 - (d - r * 0.8) / (r * 0.2) * 0.6 : 1;
                int level = (int)Math.Round(Math.Clamp(v * vignette, 0, 1) * (ramp.Length - 1));
                var c = ramp[level];
                px[i]     = (byte)(c.B * bright);
                px[i + 1] = (byte)(c.G * bright);
                px[i + 2] = (byte)(c.R * bright);
                px[i + 3] = 255;
            }

        var wb = new WriteableBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, rw, rh), px, stride, 0);
        wb.Freeze();
        return wb;
    }

    // ── live content ─────────────────────────────────────────────────────────

    /// <summary>Called from the main window (UI thread) for every decoded packet while the window is open.</summary>
    public void NotifyPacket(PacketObservation obs, uint myNodeId)
    {
        if (!IsVisible || _scope == null) return;
        var p = _scope;
        _scopeLastPacketUtc = DateTime.UtcNow;

        // Traceroute paths run through the stations we know — drawn whether or not the sender itself has a
        // position (a reply from a node without one used to be dropped here before the path was drawn, so
        // only traceroutes to located nodes ever showed up).
        if (obs.IsTraceroute) DrawTraceroutePath(obs, myNodeId);

        var pos = _nodePosition?.Invoke(obs.FromId);
        if (pos == null)
        {
            _scopeUnplaced++;
            UpdateScopeFooter();
            return;
        }

        var (x, y) = p.ToScope(pos.Value.lat, pos.Value.lon);
        var (bx, by, outside) = p.ClampToRim(x, y, 10);
        double distKm = ScopeProjection.DistanceKm(p.CenterLat, p.CenterLon, pos.Value.lat, pos.Value.lon);
        PlaceBlip(obs, bx, by, outside, distKm);

        if (obs.IsAddressedTo(myNodeId))
        {
            // To us: a request gets the full sonar ping (out, "reflected" back, blip) — our node's answer is
            // not mirrored to the client, so the way back is implied. A reply or DM to us is one inbound ring.
            if (obs.IsRequestTo(myNodeId) || (obs.Portnum == 1 && !obs.IsReply))
                PlaySonarPing(_scopePing, bx, by, p.CenterX, p.CenterY);
            else
                PlayRingTo(_scopePing, bx, by, p.CenterX, p.CenterY);
        }
        else if (!obs.IsBroadcast && _nodePosition?.Invoke(obs.ToId) is { } to)
        {
            // An exchange between two other stations we overhear: one ring per packet in its direction —
            // the request there, the reply back.
            var (tx0, ty0) = p.ToScope(to.lat, to.lon);
            var (tx, ty, _) = p.ClampToRim(tx0, ty0, 10);
            PlayRingTo(_scopePing, bx, by, tx, ty);
        }
        else
        {
            PulseAt(_scopePing, bx, by);
        }

        UpdateScopeFooter();
    }

    private void PlaceBlip(PacketObservation obs, double x, double y, bool outside, double distKm)
    {
        var expires = DateTime.UtcNow.AddSeconds(_scopeTtlSeconds);
        if (_scopeBlips.TryGetValue(obs.FromId, out var existing))
        {
            existing.ExpiresUtc = expires;   // heard again: stay longer, full brightness
            foreach (var v in existing.Visuals) v.Opacity = 1;
            return;
        }

        var item = new ScopeItem { NodeId = obs.FromId, ExpiresUtc = expires };
        const double dotR = 5;
        var dot = new Ellipse
        {
            Width = dotR * 2, Height = dotR * 2,
            Fill = outside ? Brushes.Transparent : new SolidColorBrush(PhosBright),
            Stroke = new SolidColorBrush(PhosBright), StrokeThickness = outside ? 1.5 : 0,
            Effect = new DropShadowEffect { Color = PhosBright, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 },
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = $"{_fullName(obs.FromId)}\n{Math.Round(distKm)} km" + (obs.Hops is int hp ? $" · ↪ {hp}" : "") + (obs.ViaMqtt ? " · MQTT" : ""),
        };
        Canvas.SetLeft(dot, x - dotR);
        Canvas.SetTop(dot, y - dotR);
        dot.ContextMenu = BuildNodeContextMenu(obs.FromId, route: null);
        // pop in instead of appearing
        var popScale = new ScaleTransform(0.2, 0.2);
        dot.RenderTransformOrigin = new Point(0.5, 0.5);
        dot.RenderTransform = popScale;
        var popIn = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
        popScale.BeginAnimation(ScaleTransform.ScaleXProperty, popIn);
        popScale.BeginAnimation(ScaleTransform.ScaleYProperty, popIn);
        _scopeLive.Children.Add(dot);
        item.Visuals.Add(dot);

        string text = _shortLabel(obs.FromId);
        if (outside) text = string.Format(Loc("StrFunkEchoScopeOutside"), text, Math.Round(distKm));
        else if (obs.Hops is int hops && hops > 0) text += $" ↪{hops}";
        var label = new TextBlock
        {
            Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(230, PhosMid.R, PhosMid.G, PhosMid.B)),
            IsHitTestVisible = false,
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lx = x - label.DesiredSize.Width / 2, ly = y + dotR + 2;
        // keep the label inside the scope host
        lx = Math.Clamp(lx, 2, Math.Max(2, _scopeHost.ActualWidth - label.DesiredSize.Width - 2));
        if (ly + label.DesiredSize.Height > _scopeHost.ActualHeight - 2) ly = y - dotR - label.DesiredSize.Height - 2;
        Canvas.SetLeft(label, lx);
        Canvas.SetTop(label, ly);
        _scopeLive.Children.Add(label);
        item.Visuals.Add(label);

        _scopeItems.Add(item);
        _scopeBlips[obs.FromId] = item;
    }

    /// <summary>Traceroute path through the known positions: request = origin → relays so far → destination;
    /// reply = origin → relays → destination (solid) and destination → relays back → origin (dashed).</summary>
    private void DrawTraceroutePath(PacketObservation obs, uint myNodeId)
    {
        if (_scope == null) return;
        var item = new ScopeItem { NodeId = obs.FromId, ExpiresUtc = DateTime.UtcNow.AddSeconds(_scopeTtlSeconds) };

        if (obs.IsReply)
        {
            // packet.From = the traced node, packet.To = the requester
            var towards = new List<uint> { obs.ToId };
            if (obs.Route != null) towards.AddRange(obs.Route);
            towards.Add(obs.FromId);
            var forwardDuration = AddPath(item, towards, dashed: false, delay: TimeSpan.Zero, myNodeId);

            if (obs.RouteBack != null)
            {
                var back = new List<uint> { obs.FromId };
                back.AddRange(obs.RouteBack);
                back.Add(obs.ToId);
                AddPath(item, back, dashed: true, delay: forwardDuration, myNodeId);   // the reply travels after the request
            }
        }
        else
        {
            var towards = new List<uint> { obs.FromId };
            if (obs.Route != null) towards.AddRange(obs.Route);
            if (!obs.IsBroadcast) towards.Add(obs.ToId);
            AddPath(item, towards, dashed: false, delay: TimeSpan.Zero, myNodeId);
        }

        if (item.Visuals.Count > 0) _scopeItems.Add(item);
    }

    /// <summary>Draw a path through the known positions, animated: the stroke runs from the first node to
    /// the last (dash-offset trick: one dash as long as the whole line, offset animated to zero), each relay
    /// dot pops when the stroke reaches it, a dashed leg switches to its dash pattern once drawn. Returns the
    /// draw-in duration (plus delay) so a following leg can start when this one has arrived.</summary>
    private TimeSpan AddPath(ScopeItem item, List<uint> nodeIds, bool dashed, TimeSpan delay, uint myNodeId)
    {
        if (_scope == null) return delay;
        var points = new PointCollection();
        var ids = new List<uint>();   // node per point (unknown positions are skipped)
        foreach (var id in nodeIds)
        {
            if (id == myNodeId)
            {
                // We are the scope centre by definition — the node list often has no position for our own
                // node (no GPS), which would drop us from every path that starts or ends here.
                points.Add(new Point(_scope.CenterX, _scope.CenterY));
                ids.Add(id);
                continue;
            }
            var pos = _nodePosition?.Invoke(id);
            if (pos == null) continue;
            var (x, y) = _scope.ToScope(pos.Value.lat, pos.Value.lon);
            var (bx, by, _) = _scope.ClampToRim(x, y, 10);
            points.Add(new Point(bx, by));
            ids.Add(id);
        }
        if (points.Count < 2) return delay;

        // cumulative length per vertex (scope px) → timing of the relay dots
        var cumulative = new double[points.Count];
        for (int i = 1; i < points.Count; i++)
        {
            double dx = points[i].X - points[i - 1].X, dy = points[i].Y - points[i - 1].Y;
            cumulative[i] = cumulative[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
        double length = Math.Max(cumulative[^1], 1);
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(length * 3.3, 700, 3000));   // ~300 px/s, 0.7–3 s: visibly travelling
        var holdUntil = DateTime.UtcNow + delay + duration + TimeSpan.FromMilliseconds(300);
        if (holdUntil > item.HoldOpacityUntilUtc) item.HoldOpacityUntilUtc = holdUntil;

        double thickness = dashed ? 1.5 : 2;
        double units = length / thickness + 2;   // dash lengths are multiples of the stroke thickness
        var line = new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Color.FromArgb(dashed ? (byte)170 : (byte)230, PhosMid.R, PhosMid.G, PhosMid.B)),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeDashCap = PenLineCap.Round,
            StrokeDashArray = new DoubleCollection { units, units },
            StrokeDashOffset = units,   // pattern starts in the gap → nothing visible yet
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = PhosMid, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8 },
        };
        var draw = new DoubleAnimation(units, 0, duration) { BeginTime = delay };
        draw.Completed += (_, _) =>
        {
            line.BeginAnimation(Shape.StrokeDashOffsetProperty, null);   // release the animated value
            line.StrokeDashOffset = 0;
            line.StrokeDashArray = dashed ? new DoubleCollection { 4, 3 } : null;
        };
        line.BeginAnimation(Shape.StrokeDashOffsetProperty, draw);
        _scopeLive.Children.Add(line);
        item.Visuals.Add(line);

        for (int i = 0; i < points.Count; i++)
        {
            var pt = points[i];
            var relay = new Ellipse
            {
                Width = 6, Height = 6, Fill = new SolidColorBrush(PhosMid), IsHitTestVisible = false,
                Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var pop = new ScaleTransform(0.2, 0.2);
            relay.RenderTransform = pop;
            Canvas.SetLeft(relay, pt.X - 3);
            Canvas.SetTop(relay, pt.Y - 3);
            _scopeLive.Children.Add(relay);
            item.Visuals.Add(relay);

            var at = delay + TimeSpan.FromMilliseconds(duration.TotalMilliseconds * cumulative[i] / length);
            var show = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = at };
            var grow = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = at };
            // the ageing timer drives Opacity later; release the animation so it can
            show.Completed += (_, _) => { relay.BeginAnimation(UIElement.OpacityProperty, null); relay.Opacity = 1; };
            relay.BeginAnimation(UIElement.OpacityProperty, show);
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

            // Short name at every station on the path — not ours (the mast) and not one that already carries
            // a blip label — fading in together with its dot.
            if (ids[i] != myNodeId && !_scopeBlips.ContainsKey(ids[i]))
            {
                var name = new TextBlock
                {
                    Text = _shortLabel(ids[i]), FontFamily = new FontFamily("Consolas"), FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(230, PhosMid.R, PhosMid.G, PhosMid.B)),
                    IsHitTestVisible = false, Opacity = 0,
                };
                Canvas.SetLeft(name, pt.X + 6);
                Canvas.SetTop(name, pt.Y + 3);
                _scopeLive.Children.Add(name);
                item.Visuals.Add(name);
                var showName = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = at };
                showName.Completed += (_, _) => { name.BeginAnimation(UIElement.OpacityProperty, null); name.Opacity = 1; };
                name.BeginAnimation(UIElement.OpacityProperty, showName);
            }
        }

        return delay + duration;
    }

    /// <summary>Short pulse at a station that was merely heard (no request to us).</summary>
    private void PulseAt(Canvas overlay, double x, double y)
    {
        const double targetR = 16;
        var ring = new Ellipse
        {
            Width = targetR * 2, Height = targetR * 2,
            Stroke = new SolidColorBrush(PhosBright), StrokeThickness = 1.5, Opacity = 0.8,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var scale = new ScaleTransform(0.3, 0.3);
        ring.RenderTransform = scale;
        Canvas.SetLeft(ring, x - targetR);
        Canvas.SetTop(ring, y - targetR);
        overlay.Children.Add(ring);
        var dur = TimeSpan.FromMilliseconds(600);
        var grow = new DoubleAnimation(0.3, 1.0, dur);
        var fade = new DoubleAnimation(0.8, 0.0, dur);
        fade.Completed += (_, _) => overlay.Children.Remove(ring);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Movie-style sonar ping shared by the radar and the scope: an expanding ring from the
    /// sender reaches our centre, gets "reflected" and travels back out to the sender, which then blips.</summary>
    private void PlaySonarPing(Canvas overlay, double x, double y, double cx, double cy)
    {
        double dist = Math.Max(Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)), 12);
        // outbound (sender → us), then reflected (us → sender), then a blip at the sender
        ExpandRing(overlay, x, y, dist, () => ExpandRing(overlay, cx, cy, dist, () => BlipAt(overlay, x, y)));
    }

    /// <summary>One-way ring: expands from the sender until it reaches the receiver, which then blips —
    /// a reply to us, or an exchange between two other stations we overhear (request there, reply back).</summary>
    private void PlayRingTo(Canvas overlay, double sx, double sy, double tx, double ty)
    {
        double dist = Math.Max(Math.Sqrt((tx - sx) * (tx - sx) + (ty - sy) * (ty - sy)), 12);
        ExpandRing(overlay, sx, sy, dist, () => BlipAt(overlay, tx, ty));
    }

    /// <summary>Ring expanding from (ox, oy) to <paramref name="targetR"/> in 1 s, fading as it grows.</summary>
    private void ExpandRing(Canvas overlay, double ox, double oy, double targetR, Action? completed)
    {
        var dur = TimeSpan.FromMilliseconds(1000);
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
        overlay.Children.Add(ring);

        var grow = new DoubleAnimation(0.04, 1.0, dur);
        var fade = new DoubleAnimation(0.9, 0.25, dur);
        grow.Completed += (_, _) =>
        {
            overlay.Children.Remove(ring);
            completed?.Invoke();
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Short bright flash at a station (the ping "arriving").</summary>
    private void BlipAt(Canvas overlay, double x, double y)
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
        overlay.Children.Add(blip);
        var pop  = new DoubleAnimation(0.3, 1.4, TimeSpan.FromMilliseconds(450));
        var gone = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(450));
        gone.Completed += (_, _) => overlay.Children.Remove(blip);
        blipScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        blipScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        blip.BeginAnimation(UIElement.OpacityProperty, gone);
    }

    // ── ageing ───────────────────────────────────────────────────────────────

    /// <summary>Fade items over the last third of their life, drop them when expired.</summary>
    private void AgeScopeItems()
    {
        if (_scopeItems.Count == 0) return;
        var now = DateTime.UtcNow;
        double fadeWindow = Math.Max(5, _scopeTtlSeconds * 0.35);
        for (int i = _scopeItems.Count - 1; i >= 0; i--)
        {
            var item = _scopeItems[i];
            double remaining = (item.ExpiresUtc - now).TotalSeconds;
            if (remaining <= 0)
            {
                foreach (var v in item.Visuals) _scopeLive.Children.Remove(v);
                _scopeItems.RemoveAt(i);
                if (_scopeBlips.TryGetValue(item.NodeId, out var b) && ReferenceEquals(b, item)) _scopeBlips.Remove(item.NodeId);
                continue;
            }
            if (now < item.HoldOpacityUntilUtc) continue;   // draw-in animation owns Opacity for now
            double opacity = Math.Clamp(remaining / fadeWindow, 0.08, 1);
            foreach (var v in item.Visuals) v.Opacity = opacity;
        }
        UpdateScopeFooter();
    }

    private void ClearScopeItems()
    {
        _scopeLive.Children.Clear();
        _scopePing.Children.Clear();
        _scopeItems.Clear();
        _scopeBlips.Clear();
    }

    private void UpdateScopeFooter()
    {
        string last = _scopeLastPacketUtc == default ? "–" : _scopeLastPacketUtc.ToLocalTime().ToString("HH:mm:ss");
        _scopeFooter.Text = string.Format(Loc("StrFunkEchoScopeFooter"), _scopeBlips.Count, last, _scopeUnplaced);
    }
}
