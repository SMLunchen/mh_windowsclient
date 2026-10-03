using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeshhessenClient.Services;

/// <summary>
/// Renders an <see cref="EnvironmentField.Grid"/> into a smoothly (bilinearly) upsampled PNG,
/// coloured by a continuous value→colour ramp with a coverage alpha. Displayed as a MapLibre
/// image/raster layer with linear resampling → soft, blended edges instead of blocky cells.
/// </summary>
public static class EnvironmentRaster
{
    public sealed class Result
    {
        public string DataUri = "";      // "data:image/png;base64,…"
        public double MinLon, MinLat, MaxLon, MaxLat;   // image extent (grid-centre span)
    }

    /// <param name="rgbAt">normalised value t (0..1) → (r,g,b).</param>
    /// <param name="maxOpacity">peak fill opacity (0..1); base map stays readable.</param>
    /// <param name="bandCount">&gt;0 quantises the colour into that many discrete bands, so the
    /// colour changes exactly at the isotherm lines (filled-contour look). 0 = smooth gradient.</param>
    public static Result? Render(EnvironmentField.Grid g, Func<double, (byte r, byte g, byte b)> rgbAt,
        double min, double max, double maxOpacity, int bandCount = 0, int cellPixels = 10, int maxDim = 640)
    {
        if (g == null || g.Nx < 2 || g.Ny < 2) return null;

        int w = Math.Clamp(g.Nx * cellPixels, 128, maxDim);
        int h = Math.Clamp(g.Ny * cellPixels, 128, maxDim);
        double span = max - min;

        // Colour LUT (256) so we don't re-parse the gradient per pixel. When banded, snap the
        // sample to the band centre so colour is flat within a band and steps at each boundary.
        var lut = new (byte r, byte g, byte b)[256];
        for (int k = 0; k < 256; k++)
        {
            double t = k / 255.0;
            if (bandCount > 0)
            {
                int bi = Math.Clamp((int)(t * bandCount), 0, bandCount - 1);
                t = (bi + 0.5) / bandCount;
            }
            lut[k] = rgbAt(t);
        }

        var buf = new byte[w * h * 4];   // BGRA32, straight alpha
        for (int py = 0; py < h; py++)
        {
            // image row 0 = north (maxLat); grid row 0 = south → flip Y
            double gy = (1.0 - (double)py / (h - 1)) * (g.Ny - 1);
            int j0 = (int)Math.Floor(gy); int j1 = Math.Min(j0 + 1, g.Ny - 1); double fy = gy - j0;
            for (int px = 0; px < w; px++)
            {
                double gx = (double)px / (w - 1) * (g.Nx - 1);
                int i0 = (int)Math.Floor(gx); int i1 = Math.Min(i0 + 1, g.Nx - 1); double fx = gx - i0;

                // bilinear over the four grid centres, ignoring NaN (no coverage)
                double vsum = 0, wsum = 0, asum = 0;
                void Acc(int i, int j, double wgt)
                {
                    int idx = j * g.Nx + i;
                    double a = g.Alpha[idx];
                    double v = g.Values[idx];
                    asum += wgt * a;
                    if (!double.IsNaN(v)) { vsum += wgt * v; wsum += wgt; }
                }
                Acc(i0, j0, (1 - fx) * (1 - fy));
                Acc(i1, j0, fx * (1 - fy));
                Acc(i0, j1, (1 - fx) * fy);
                Acc(i1, j1, fx * fy);

                int o = (py * w + px) * 4;
                if (wsum <= 0 || asum <= 0.001) { buf[o + 3] = 0; continue; }  // transparent
                double value = vsum / wsum;
                double t = span > 0 ? Math.Clamp((value - min) / span, 0, 1) : 0;
                var c = lut[(int)(t * 255)];
                byte alpha = (byte)Math.Clamp(asum * maxOpacity * 255, 0, 255);
                buf[o + 0] = c.b; buf[o + 1] = c.g; buf[o + 2] = c.r; buf[o + 3] = alpha;
            }
        }

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, w * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        var b64 = Convert.ToBase64String(ms.ToArray());

        // Image spans the grid-centre extent (pixels map centre-to-centre).
        return new Result
        {
            DataUri = "data:image/png;base64," + b64,
            MinLon = g.Lon0 + 0.5 * g.DLon,
            MaxLon = g.Lon0 + (g.Nx - 0.5) * g.DLon,
            MinLat = g.Lat0 + 0.5 * g.DLat,
            MaxLat = g.Lat0 + (g.Ny - 0.5) * g.DLat,
        };
    }
}
