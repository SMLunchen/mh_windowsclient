using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;
using ModelNodeInfo = MeshhessenClient.Models.NodeInfo;

namespace MeshhessenClient.Tests;

/// <summary>
/// On-demand layout probe: renders the traceroute window with a two-leg sample result to a PNG so the
/// layout can be inspected without a connected node. Runs only when MH_TR_PROBE_PNG names the output file;
/// in a normal test run it is a no-op.
/// </summary>
public class TracerouteWindowRenderProbe
{
    private const byte Start1 = 0x94;
    private const byte Start2 = 0xC3;

    private static byte[] Frame(FromRadio fr)
    {
        var payload = fr.ToByteArray();
        var frame = new byte[4 + payload.Length];
        frame[0] = Start1;
        frame[1] = Start2;
        frame[2] = (byte)(payload.Length >> 8);
        frame[3] = (byte)(payload.Length & 0xFF);
        Array.Copy(payload, 0, frame, 4, payload.Length);
        return frame;
    }

    private static FromRadio NodeDb(uint num, string shortName, string longName, double? lat = null, double? lon = null)
    {
        var ni = new NodeInfo { Num = num, User = new User { Id = $"!{num:x8}", ShortName = shortName, LongName = longName } };
        if (lat is double la && lon is double lo)
            ni.Position = new Position { LatitudeI = (int)(la * 1e7), LongitudeI = (int)(lo * 1e7) };
        return new FromRadio { NodeInfo = ni };
    }

    [Fact]
    public void Render_two_leg_result_to_png()
    {
        var outPath = Environment.GetEnvironmentVariable("MH_TR_PROBE_PNG");
        if (string.IsNullOrEmpty(outPath)) return;
        double width = double.TryParse(Environment.GetEnvironmentVariable("MH_TR_PROBE_WIDTH"), out var w) ? w : 780;

        WpfTestApp.Run(() =>
        {
            var app = Application.Current!;
            // The real app's theme dictionaries live in App.xaml; the test app has none, and the ModernWpf
            // control templates cannot run without them. Plain WPF templates plus the brushes the window
            // looks up are enough to inspect the layout (not the exact colours).
            foreach (var (key, color) in new (string, Color)[]
            {
                ("SystemControlForegroundBaseLowBrush",    Color.FromRgb(0xD0, 0xD0, 0xD0)),
                ("SystemControlForegroundBaseMediumBrush", Color.FromRgb(0x70, 0x70, 0x70)),
                ("SystemControlBackgroundAltHighBrush",    Color.FromRgb(0xF4, 0xF4, 0xF4)),
                ("SystemControlHighlightAccentBrush",      Color.FromRgb(0x19, 0x87, 0x54)),
            })
            {
                if (app.TryFindResource(key) == null) app.Resources[key] = new SolidColorBrush(color);
            }

            const uint Me = 0x75fb6baa, Dest = 0x9c942a1d, R1 = 0x11111111, R2 = 0x22222222;
            var fake = new FakeConnectionService(ConnectionType.Tcp);
            var svc = new MeshtasticProtocolService(fake);
            fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = Me } }));
            fake.Receive(Frame(NodeDb(Me, "ICH", "Mein Node", 50.11, 8.68)));
            fake.Receive(Frame(NodeDb(Dest, "MOB", "mobil.dadaniel.at", 50.21, 8.60)));
            fake.Receive(Frame(NodeDb(R1, "RLY1", "Relay Feldberg Nord Mast", 50.23, 8.45)));
            fake.Receive(Frame(NodeDb(R2, "RLY2", "Relay Taunus")));

            var target = new ModelNodeInfo
            {
                NodeId = Dest, Id = $"!{Dest:x8}", LongName = "mobil.dadaniel.at", Name = "mobil.dadaniel.at", ShortName = "MOB",
                Latitude = 50.21, Longitude = 8.60,
            };
            var win = new MeshhessenClient.TracerouteWindow(svc, target, Me) { Width = width, Height = 560 };
            ModernWpf.Controls.Primitives.WindowHelper.SetUseModernWindowStyle(win, false);   // no theme → plain chrome
            win.Show();

            // Reply: outbound via two relays (middle segment bridged), back via one relay.
            fake.Receive(Frame(new FromRadio
            {
                Packet = new MeshPacket
                {
                    From = Dest, To = Me, Id = 99, HopStart = 7, HopLimit = 5, RxSnr = 6.25f, RxRssi = -74,
                    Decoded = new Data
                    {
                        Portnum = PortNum.TracerouteApp, RequestId = 5,
                        Payload = new RouteDiscovery
                        {
                            Route = { R1, R2 }, SnrTowards = { 41, -128, 25 },
                            RouteBack = { R2 }, SnrBack = { 30, 25 },
                        }.ToByteString(),
                    },
                },
            }));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            win.UpdateLayout();

            var rtb = new RenderTargetBitmap((int)Math.Ceiling(win.ActualWidth), (int)Math.Ceiling(win.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(win);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(outPath)) enc.Save(fs);
            win.Close();
        });
    }
}
