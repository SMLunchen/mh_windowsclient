using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;

namespace MeshhessenClient.Tests;

/// <summary>
/// Display-mirroring reassembly (FW 2.8): chunked MONO_VLSB frames, RGB565 dirty rects,
/// palette chunking and the pixel/color rendering — semantics mirror the Android client
/// (DisplayMirrorManagerImpl / MirrorRectCompositor / MirrorFrameRenderer).
/// </summary>
public class DisplayMirrorTests
{
    private static DisplayFrame MonoChunk(int width, int height, uint frameId, int offset, byte[] data, int total, uint paletteSig = 0) => new()
    {
        Format = DisplayFrame.Types.Format.MonoVlsb,
        Width = (uint)width, Height = (uint)height,
        FrameId = frameId, Offset = (uint)offset, TotalSize = (uint)total,
        Data = ByteString.CopyFrom(data), PaletteSignature = paletteSig,
    };

    [Fact]
    public void Mono_frame_reassembles_from_three_chunks_in_order()
    {
        var asm = new DisplayMirrorAssembler();
        int w = 128, h = 64, total = w * h / 8; // 1024 bytes
        var full = new byte[total];
        full[0] = 0x01;              // pixel (0,0)
        full[total - 1] = 0x80;      // pixel (127,63)

        Assert.Null(asm.HandleFrameChunk(MonoChunk(w, h, 7, 0, full[..384], total)));
        Assert.Null(asm.HandleFrameChunk(MonoChunk(w, h, 7, 384, full[384..768], total)));
        var frame = asm.HandleFrameChunk(MonoChunk(w, h, 7, 768, full[768..], total));

        Assert.NotNull(frame);
        Assert.Equal((128, 64, 7u, false), (frame!.Width, frame.Height, frame.FrameId, frame.IsRgb565));
        Assert.Equal(full, frame.Pixels);

        // Default rendering: set = white, unset = black (no palette)
        var px = DisplayMirrorAssembler.RenderBgra(frame, null);
        Assert.Equal(0xFFFFFFFFu, px[0]);                  // (0,0) set
        Assert.Equal(0xFF000000u, px[1]);                  // (1,0) unset
        Assert.Equal(0xFFFFFFFFu, px[63 * 128 + 127]);     // (127,63) set
    }

    [Fact]
    public void Mono_out_of_sequence_chunk_drops_the_partial_frame()
    {
        var asm = new DisplayMirrorAssembler();
        int total = 128 * 64 / 8;
        Assert.Null(asm.HandleFrameChunk(MonoChunk(128, 64, 7, 0, new byte[384], total)));
        // wrong offset → dropped; the final chunk alone must not complete anything
        Assert.Null(asm.HandleFrameChunk(MonoChunk(128, 64, 7, 512, new byte[384], total)));
        Assert.Null(asm.HandleFrameChunk(MonoChunk(128, 64, 7, 768, new byte[256], total)));
    }

    [Fact]
    public void Mono_rejects_lying_geometry()
    {
        var asm = new DisplayMirrorAssembler();
        // total_size must equal width * ceil(height/8)
        Assert.Null(asm.HandleFrameChunk(MonoChunk(128, 64, 1, 0, new byte[100], total: 100)));
    }

    [Fact]
    public void Rgb565_rect_composites_into_persistent_canvas()
    {
        var asm = new DisplayMirrorAssembler();
        // 2x1 rect at (1,1) on a 4x2 panel: 2 pixels, little-endian RGB565
        // 0xF800 = pure red, 0x07E0 = pure green
        var rect = new byte[] { 0x00, 0xF8, 0xE0, 0x07 };
        var chunk = new DisplayFrame
        {
            Format = DisplayFrame.Types.Format.Rgb565,
            Width = 4, Height = 2, FrameId = 3,
            RectX = 1, RectY = 1, RectWidth = 2, RectHeight = 1,
            Offset = 0, TotalSize = 4, Data = ByteString.CopyFrom(rect),
        };
        var frame = asm.HandleFrameChunk(chunk);

        Assert.NotNull(frame);
        Assert.Equal((4, 2, true), (frame!.Width, frame.Height, frame.IsRgb565));

        var px = DisplayMirrorAssembler.RenderBgra(frame, null);
        Assert.Equal(0xFF000000u, px[0]);          // untouched canvas = black
        Assert.Equal(0xFFFF0000u, px[1 * 4 + 1]);  // red at (1,1)
        Assert.Equal(0xFF00FF00u, px[1 * 4 + 2]);  // green at (2,1)
    }

    [Fact]
    public void Palette_colorizes_matching_frames_only()
    {
        var asm = new DisplayMirrorAssembler();
        var palette = asm.HandlePaletteChunk(new DisplayPalette
        {
            Signature = 42, RegionOffset = 0, RegionTotal = 1,
            DefaultOnColor = 0xFFFF, DefaultOffColor = 0x0000,
            Regions = { new DisplayPalette.Types.ColorRegion
                { X = 0, Y = 0, Width = 1, Height = 1, OnColor = 0xF800, OffColor = 0x0000 } },
        });
        Assert.NotNull(palette);
        Assert.Equal(42u, palette!.Signature);

        int w = 8, h = 8, total = 8;
        var data = new byte[total];
        data[0] = 0x01; data[1] = 0x01;  // pixels (0,0) and (1,0) set

        // Frame references the palette → region red at (0,0), default white at (1,0)
        var asm2 = new DisplayMirrorAssembler();
        var frame = asm2.HandleFrameChunk(MonoChunk(w, h, 1, 0, data, total, paletteSig: 42))!;
        var px = DisplayMirrorAssembler.RenderBgra(frame, palette);
        Assert.Equal(0xFFFF0000u, px[0]);
        Assert.Equal(0xFFFFFFFFu, px[1]);

        // Signature mismatch → plain monochrome
        var frame2 = new DisplayMirrorAssembler().HandleFrameChunk(MonoChunk(w, h, 2, 0, data, total, paletteSig: 7))!;
        var px2 = DisplayMirrorAssembler.RenderBgra(frame2, palette);
        Assert.Equal(0xFFFFFFFFu, px2[0]);
    }

    [Fact]
    public void Palette_bad_continuation_poisons_until_restart()
    {
        var asm = new DisplayMirrorAssembler();
        Assert.Null(asm.HandlePaletteChunk(new DisplayPalette
        {
            Signature = 9, RegionOffset = 0, RegionTotal = 2,
            Regions = { new DisplayPalette.Types.ColorRegion { Width = 1, Height = 1 } },
        }));
        // wrong offset → poisoned
        Assert.Null(asm.HandlePaletteChunk(new DisplayPalette
        {
            Signature = 9, RegionOffset = 0, RegionTotal = 2,
            Regions = { new DisplayPalette.Types.ColorRegion { Width = 1, Height = 1 } },
        }));
        // …but an offset-0 restart with total 1 completes cleanly
        var p = asm.HandlePaletteChunk(new DisplayPalette
        {
            Signature = 10, RegionOffset = 0, RegionTotal = 1,
            Regions = { new DisplayPalette.Types.ColorRegion { Width = 1, Height = 1 } },
        });
        Assert.NotNull(p);
    }

    [Fact]
    public void Rgb565_expansion_uses_bit_replication()
    {
        Assert.Equal(((byte)255, (byte)0, (byte)0), DisplayMirrorAssembler.Rgb565(0xF800));
        Assert.Equal(((byte)0, (byte)255, (byte)0), DisplayMirrorAssembler.Rgb565(0x07E0));
        Assert.Equal(((byte)0, (byte)0, (byte)255), DisplayMirrorAssembler.Rgb565(0x001F));
        Assert.Equal(((byte)255, (byte)255, (byte)255), DisplayMirrorAssembler.Rgb565(0xFFFF));
    }
}
