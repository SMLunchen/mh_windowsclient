using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeshhessenClient.Services;

namespace MeshhessenClient;

/// <summary>
/// Live mirror of the connected device's display (FW 2.8, local connection only).
/// Opening starts continuous mirroring (set_display_mirror=true, first frame doubles as
/// the acknowledgement); closing stops it. Frames arrive as FromRadio.display_frame
/// chunks and are reassembled/rendered by <see cref="DisplayMirrorAssembler"/> —
/// MONO_VLSB (optionally palette-colorized) and RGB565 dirty rects.
/// </summary>
public class DisplayMirrorWindow : Window
{
    private readonly MeshtasticProtocolService _svc;
    private readonly DisplayMirrorAssembler _assembler = new();
    private DisplayMirrorAssembler.Palette? _palette;
    private readonly Image _image;
    private readonly TextBlock _waitText;
    private WriteableBitmap? _bitmap;
    private bool _mirrorStarted;

    private static string Loc(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public DisplayMirrorWindow(MeshtasticProtocolService svc)
    {
        _svc = svc;
        Title = Loc("StrMirrorWindowTitle");
        Width = 560; Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(13, 17, 23));

        _image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(12) };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

        _waitText = new TextBlock
        {
            Text = Loc("StrMirrorWaiting"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xC9, 0x97)),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var grid = new Grid();
        grid.Children.Add(_image);
        grid.Children.Add(_waitText);
        Content = grid;

        _svc.DisplayFrameReceived   += OnFrameChunk;
        _svc.DisplayPaletteReceived += OnPaletteChunk;

        // Win32 quirk (same as TelemetryWindow): activate the owner before the active owned
        // window is destroyed, or the main window can minimize to the taskbar.
        Closing += (_, _) => { try { Owner?.Activate(); } catch { } };

        Loaded += async (_, _) =>
        {
            try { await _svc.SetDisplayMirrorAsync(true); _mirrorStarted = true; }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"[DisplayMirror] start failed: {ex.Message}");
                _waitText.Text = ex.Message;
            }
        };
        Closed += async (_, _) =>
        {
            _svc.DisplayFrameReceived   -= OnFrameChunk;
            _svc.DisplayPaletteReceived -= OnPaletteChunk;
            if (_mirrorStarted)
            {
                try { await _svc.SetDisplayMirrorAsync(false); }
                catch { /* device may already be gone */ }
            }
        };
    }

    private void OnPaletteChunk(object? sender, Meshtastic.Protobufs.DisplayPalette chunk)
    {
        var complete = _assembler.HandlePaletteChunk(chunk);
        if (complete != null) _palette = complete;
    }

    private void OnFrameChunk(object? sender, Meshtastic.Protobufs.DisplayFrame chunk)
    {
        var frame = _assembler.HandleFrameChunk(chunk);
        if (frame == null) return;
        var pixels = DisplayMirrorAssembler.RenderBgra(frame, _palette);

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_bitmap == null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
                {
                    _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
                    _image.Source = _bitmap;
                    Title = $"{Loc("StrMirrorWindowTitle")} – {frame.Width}×{frame.Height}";
                }
                _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height),
                    pixels, frame.Width * 4, 0);
                if (_waitText.Visibility != Visibility.Collapsed)
                    _waitText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { Services.Logger.WriteLine($"[DisplayMirror] render failed: {ex.Message}"); }
        });
    }
}
