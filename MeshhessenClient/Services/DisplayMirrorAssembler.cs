using Meshtastic.Protobufs;

namespace MeshhessenClient.Services;

/// <summary>
/// Display-mirroring reassembly (FW 2.8): FromRadio.display_frame arrives as ordered,
/// contiguous chunks of one frame; a chunk with offset 0 starts a new frame and an
/// out-of-sequence chunk drops the partial one. Two formats:
/// <list type="bullet">
/// <item>MONO_VLSB — full frames; total_size = width * ceil(height/8), byte (y/8)*width+x,
/// bit y%8. Optionally colorized by a DisplayPalette (RGB565 colors, region table where
/// the highest region index wins).</item>
/// <item>RGB565 — LVGL dirty rects composited into a persistent little-endian canvas;
/// rect geometry is latched from the offset-0 chunk.</item>
/// </list>
/// Mirrors the Android client's DisplayMirrorManagerImpl/MirrorRectCompositor; every
/// device-controlled field is validated before any allocation.
/// </summary>
public sealed class DisplayMirrorAssembler
{
    public const int PixelsPerPage = 8;
    private const long MaxFrameBytes = 512 * 1024;   // generous cap: 800x480 mono = 48 KB, RGB565 = 768 KB rects
    private const int MaxPaletteRegions = 64;

    public sealed record Frame(int Width, int Height, uint FrameId, uint PaletteSignature, byte[] Pixels, bool IsRgb565);
    public sealed record PaletteRegion(int X, int Y, int Width, int Height, int OnColor, int OffColor);
    public sealed record Palette(uint Signature, int DefaultOn, int DefaultOff, IReadOnlyList<PaletteRegion> Regions);

    // ── mono full-frame state ──
    private byte[]? _buffer;
    private uint _frameId;
    private int _received;
    private int _frameWidth, _frameHeight;

    // ── RGB565 rect-compositor state ──
    private byte[]? _canvas;          // persistent little-endian RGB565 canvas (panel size)
    private int _canvasW, _canvasH;
    private byte[]? _rectBuffer;
    private uint _rectFrameId;
    private int _rectReceived;
    private (int X, int Y, int W, int H, int PanelW, int PanelH)? _rectArea;

    // ── palette state ──
    private readonly List<PaletteRegion> _paletteRegions = new();
    private uint _paletteSignature;
    private int _paletteTotal;
    private int _paletteReceived = -1;
    private int _paletteDefaultOn, _paletteDefaultOff;

    /// <summary>Feed one frame chunk; returns the completed frame or null.</summary>
    public Frame? HandleFrameChunk(DisplayFrame chunk) =>
        chunk.Format == DisplayFrame.Types.Format.Rgb565
            ? HandleRectChunk(chunk)
            : HandleMonoChunk(chunk);

    private Frame? HandleMonoChunk(DisplayFrame chunk)
    {
        var data = chunk.Data.ToByteArray();
        long total = chunk.TotalSize;
        long expected = (long)chunk.Width * (((long)chunk.Height + PixelsPerPage - 1) / PixelsPerPage);
        bool acceptable = chunk.Format == DisplayFrame.Types.Format.MonoVlsb
            && chunk.Width > 0 && chunk.Height > 0
            && expected == total && total > 0 && total <= MaxFrameBytes
            && chunk.Offset + (long)data.Length <= total;
        if (!acceptable) { _buffer = null; return null; }

        if (chunk.Offset == 0)
        {
            _buffer = new byte[total];
            _frameId = chunk.FrameId;
            _received = 0;
            // Geometry is authoritative on the first chunk; a later chunk may not transpose it.
            _frameWidth = (int)chunk.Width;
            _frameHeight = (int)chunk.Height;
        }

        var buf = _buffer;
        bool inSequence = buf != null && chunk.FrameId == _frameId && chunk.Offset == _received
            && buf.Length == total && (int)chunk.Width == _frameWidth && (int)chunk.Height == _frameHeight;
        if (!inSequence) { _buffer = null; return null; }

        Array.Copy(data, 0, buf!, (int)chunk.Offset, data.Length);
        _received += data.Length;
        if (_received < buf!.Length) return null;

        _buffer = null;
        return new Frame(_frameWidth, _frameHeight, _frameId, chunk.PaletteSignature, buf, IsRgb565: false);
    }

    private Frame? HandleRectChunk(DisplayFrame chunk)
    {
        var data = chunk.Data.ToByteArray();
        int rectW = chunk.RectWidth  > 0 ? (int)chunk.RectWidth  : (int)chunk.Width;
        int rectH = chunk.RectHeight > 0 ? (int)chunk.RectHeight : (int)chunk.Height;

        // Subtraction form: rect_x + rect_width overflows for hostile values.
        bool withinPanel = chunk.Width > 0 && chunk.Height > 0
            && chunk.RectX <= chunk.Width  && rectW <= (int)chunk.Width  - (int)chunk.RectX
            && chunk.RectY <= chunk.Height && rectH <= (int)chunk.Height - (int)chunk.RectY;
        long expected = withinPanel ? (long)rectW * rectH * 2 : -1;
        bool sized = expected == chunk.TotalSize && chunk.TotalSize > 0 && chunk.TotalSize <= MaxFrameBytes;

        var latched = _rectArea;
        bool inSequence = chunk.Offset == 0
            || (_rectBuffer?.Length == chunk.TotalSize && chunk.FrameId == _rectFrameId
                && chunk.Offset == _rectReceived && latched != null
                && (int)chunk.Width == latched.Value.PanelW && (int)chunk.Height == latched.Value.PanelH);

        if (!(sized && inSequence && chunk.Offset + (long)data.Length <= chunk.TotalSize))
        {
            _rectBuffer = null;
            return null;
        }

        if (chunk.Offset == 0)
        {
            _rectBuffer = new byte[chunk.TotalSize];
            _rectFrameId = chunk.FrameId;
            _rectReceived = 0;
            _rectArea = ((int)chunk.RectX, (int)chunk.RectY, rectW, rectH, (int)chunk.Width, (int)chunk.Height);
        }

        Array.Copy(data, 0, _rectBuffer!, (int)chunk.Offset, data.Length);
        _rectReceived += data.Length;
        if (_rectReceived < _rectBuffer!.Length) return null;

        // Rect complete → composite into the persistent canvas (recreated on panel change)
        var a = _rectArea!.Value;
        if (_canvas == null || _canvasW != a.PanelW || _canvasH != a.PanelH)
        {
            _canvas = new byte[a.PanelW * a.PanelH * 2];
            _canvasW = a.PanelW; _canvasH = a.PanelH;
        }
        for (int row = 0; row < a.H; row++)
        {
            Array.Copy(_rectBuffer, row * a.W * 2,
                _canvas, ((a.Y + row) * _canvasW + a.X) * 2, a.W * 2);
        }
        var buf = _rectBuffer;
        _rectBuffer = null;
        _ = buf;
        var snapshot = new byte[_canvas.Length];
        Array.Copy(_canvas, snapshot, _canvas.Length);
        return new Frame(_canvasW, _canvasH, _rectFrameId, 0, snapshot, IsRgb565: true);
    }

    /// <summary>Feed one palette chunk; returns the completed palette or null.</summary>
    public Palette? HandlePaletteChunk(DisplayPalette chunk)
    {
        if (chunk.RegionOffset == 0 && chunk.RegionTotal <= MaxPaletteRegions)
        {
            _paletteRegions.Clear();
            _paletteSignature = chunk.Signature;
            _paletteTotal = (int)chunk.RegionTotal;
            _paletteReceived = 0;
            // Defaults are authoritative on the first chunk; later chunks may omit them.
            _paletteDefaultOn = (int)chunk.DefaultOnColor;
            _paletteDefaultOff = (int)chunk.DefaultOffColor;
        }

        bool withinTotal = (int)chunk.RegionTotal == _paletteTotal && _paletteReceived >= 0
            && _paletteReceived + chunk.Regions.Count <= _paletteTotal;
        bool inSequence = chunk.Signature == _paletteSignature && (int)chunk.RegionOffset == _paletteReceived;
        if (!(withinTotal && inSequence))
        {
            _paletteRegions.Clear();
            _paletteReceived = -1; // poison until the next offset-0 chunk restarts
            return null;
        }

        foreach (var r in chunk.Regions)
            _paletteRegions.Add(new PaletteRegion((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height,
                (int)r.OnColor, (int)r.OffColor));
        _paletteReceived += chunk.Regions.Count;

        if (_paletteReceived != _paletteTotal) return null;
        _paletteReceived = -1; // complete; a surplus chunk must not extend it
        return new Palette(_paletteSignature, _paletteDefaultOn, _paletteDefaultOff, _paletteRegions.ToList());
    }

    public void Reset()
    {
        _buffer = null; _received = 0; _frameId = 0; _frameWidth = _frameHeight = 0;
        _canvas = null; _canvasW = _canvasH = 0; _rectBuffer = null; _rectArea = null;
        _paletteRegions.Clear(); _paletteReceived = -1; _paletteTotal = 0;
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    /// <summary>Expand RGB565 to 8-bit channels by bit replication (canonical expansion).</summary>
    public static (byte R, byte G, byte B) Rgb565(int v)
    {
        int r5 = (v >> 11) & 0x1F, g6 = (v >> 5) & 0x3F, b5 = v & 0x1F;
        return ((byte)((r5 << 3) | (r5 >> 2)), (byte)((g6 << 2) | (g6 >> 4)), (byte)((b5 << 3) | (b5 >> 2)));
    }

    /// <summary>Render a completed frame to BGRA32 pixels (row-major, for WriteableBitmap).
    /// The palette applies only when the frame references it by signature.</summary>
    public static uint[] RenderBgra(Frame frame, Palette? palette)
    {
        var px = new uint[frame.Width * frame.Height];
        if (frame.IsRgb565)
        {
            for (int i = 0; i < px.Length; i++)
            {
                int v = frame.Pixels[i * 2] | (frame.Pixels[i * 2 + 1] << 8); // little-endian
                var (r, g, b) = Rgb565(v);
                px[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            }
            return px;
        }

        var active = palette != null && frame.PaletteSignature != 0 && palette.Signature == frame.PaletteSignature
            ? palette : null;
        uint on  = Pack(active != null ? Rgb565(active.DefaultOn)  : ((byte)255, (byte)255, (byte)255));
        uint off = Pack(active != null ? Rgb565(active.DefaultOff) : ((byte)0, (byte)0, (byte)0));

        for (int y = 0; y < frame.Height; y++)
        {
            int page = (y / PixelsPerPage) * frame.Width;
            int bit = 1 << (y % PixelsPerPage);
            for (int x = 0; x < frame.Width; x++)
            {
                bool set = (frame.Pixels[page + x] & bit) != 0;
                uint color;
                var region = FindRegion(active, x, y);
                if (region != null)
                    color = Pack(Rgb565(set ? region.OnColor : region.OffColor));
                else
                    color = set ? on : off;
                px[y * frame.Width + x] = color;
            }
        }
        return px;

        static uint Pack((byte R, byte G, byte B) c) =>
            0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

        // Highest region index wins — the firmware's precedence.
        static PaletteRegion? FindRegion(Palette? p, int x, int y)
        {
            if (p == null) return null;
            for (int i = p.Regions.Count - 1; i >= 0; i--)
            {
                var r = p.Regions[i];
                if (x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height) return r;
            }
            return null;
        }
    }
}
