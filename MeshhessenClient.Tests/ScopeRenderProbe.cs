using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BruTile;
using BruTile.Predefined;
using MeshhessenClient.Models;
using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// On-demand layout probe for the situation map ("Lagebild") tab of the Funk-Echo window: builds the window
/// with fake positions around Frankfurt, lets it fetch real OSM tiles (network!), feeds a few packets and
/// renders the tab to a PNG. Runs only when MH_SCOPE_PROBE_PNG names the output file; otherwise a no-op.
/// </summary>
public class ScopeRenderProbe
{
    [Fact]
    public void Render_scope_tab_to_png()
    {
        var outPath = Environment.GetEnvironmentVariable("MH_SCOPE_PROBE_PNG");
        if (string.IsNullOrEmpty(outPath)) return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"mh_scope_probe_{Guid.NewGuid():N}.db");
        var cacheDir = Path.Combine(Path.GetTempPath(), "mh_scope_probe_tiles");
        Directory.CreateDirectory(cacheDir);

        const uint Me = 0x75fb6baa, A = 0x11111111, B = 0x22222222, C = 0x33333333, D = 0x44444444, Far = 0x55555555, E = 0x66666666, F = 0x77777777;
        var positions = new Dictionary<uint, (double lat, double lon)>
        {
            [Me]  = (50.11, 8.68),   // Frankfurt
            [A]   = (50.00, 8.27),   // Mainz
            [B]   = (50.23, 8.45),   // Feldberg
            [C]   = (49.87, 8.65),   // Darmstadt
            [D]   = (50.58, 8.68),   // Gießen
            [Far] = (51.05, 9.80),   // ~130 km, outside
            [E]   = (50.40, 8.55),   // Butzbach — relay only, never heard directly (path label test)
            [F]   = (50.30, 8.90),   // Büdingen — router, never heard (permanent marker test)
        };
        var names = new Dictionary<uint, string> { [A] = "MZ-1", [B] = "FLDB", [C] = "DA-4", [D] = "GI-7", [Far] = "KS-2", [E] = "BTZB", [F] = "RTR-1" };
        var routers = new List<(uint Id, string Role, double Lat, double Lon)> { (F, "Router", 50.30, 8.90), (B, "Router", 50.23, 8.45) };

        using var db = new TelemetryDatabaseService(dbPath);
        WpfTestApp.Run(() =>
        {
            foreach (var (key, color) in new (string, Color)[]
            {
                ("SystemControlForegroundBaseLowBrush",    Color.FromRgb(0xD0, 0xD0, 0xD0)),
                ("SystemControlForegroundBaseMediumBrush", Color.FromRgb(0x70, 0x70, 0x70)),
                ("SystemControlBackgroundAltHighBrush",    Color.FromRgb(0xF4, 0xF4, 0xF4)),
            })
            {
                if (Application.Current!.TryFindResource(key) == null) Application.Current.Resources[key] = new SolidColorBrush(color);
            }

            // MH_SCOPE_PROBE_TILES overrides the tile template (e.g. the Meshhessen servers the client uses by default)
            var template = Environment.GetEnvironmentVariable("MH_SCOPE_PROBE_TILES");
            if (string.IsNullOrEmpty(template)) template = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
            var sub = "probe_" + Math.Abs(template.GetHashCode()).ToString("x");
            ITileSource TileSource() => new TileSource(
                new CachingHttpTileProvider(cacheDir, sub, template, useHttpCacheHeaders: true),
                new GlobalSphericalMercator(YAxis.TMS, 0, 18, "OSM"));

            var win = new MeshhessenClient.RequestEchoWindow(db,
                id => names.TryGetValue(id, out var n) ? n : $"!{id:x8}",
                id => names.TryGetValue(id, out var n) ? n + " Node" : $"!{id:x8}",
                () => true,
                myPosition: () => positions[Me],
                nodePosition: id => positions.TryGetValue(id, out var p) ? p : null,
                tileSource: TileSource,
                routerNodes: () => routers)
            {
                Width  = double.TryParse(Environment.GetEnvironmentVariable("MH_SCOPE_PROBE_WIDTH"),  out var pw) ? pw : 640,
                Height = double.TryParse(Environment.GetEnvironmentVariable("MH_SCOPE_PROBE_HEIGHT"), out var ph) ? ph : 700,
            };
            win.Show();

            var tabs = ((Grid)win.Content).Children.OfType<TabControl>().First();
            tabs.SelectedIndex = 2;   // Lagebild
            Pump(TimeSpan.FromSeconds(8));   // layout debounce + tile fetch + compose

            DateTime now = DateTime.UtcNow;
            win.NotifyPacket(new PacketObservation(A, 0xFFFFFFFF, 1, 0, false, now, 1, false, 0, null, null), Me);
            win.NotifyPacket(new PacketObservation(C, 0xFFFFFFFF, 3, 1, false, now, 2, false, 0, null, null), Me);
            win.NotifyPacket(new PacketObservation(Far, 0xFFFFFFFF, 67, 3, true, now, 3, false, 0, null, null), Me);
            // traceroute reply from D to us via E and B, back via B (E is only ever seen as a relay → path label)
            win.NotifyPacket(new PacketObservation(D, Me, 70, 1, false, now, 4, false, 77, new List<uint> { E, B }, new List<uint> { B }), Me);
            // request to us from B → sonar ping out and back
            win.NotifyPacket(new PacketObservation(B, Me, 4, 0, false, now, 5, true, 0, null, null), Me);
            // overheard request from A to C → one-way ring A → C
            win.NotifyPacket(new PacketObservation(A, C, 4, 0, false, now, 6, true, 0, null, null), Me);
            Pump(TimeSpan.FromMilliseconds(700));   // mid-animation

            var rtb = new RenderTargetBitmap((int)Math.Ceiling(win.ActualWidth), (int)Math.Ceiling(win.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(win);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(outPath)) enc.Save(fs);
            win.Close();
        });
        try { File.Delete(dbPath); } catch { /* best effort */ }
    }

    /// <summary>Run the dispatcher for a while so timers, async continuations and animations progress.</summary>
    private static void Pump(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Thread.Sleep(50);
        }
    }
}
