namespace MeshhessenClient.Services;

/// <summary>
/// Pure, testable IDW (inverse-distance-weighted) interpolation of point sensors into a
/// value grid — the "weather-service" style continuous field. Each grid cell gets the
/// distance-weighted average of nearby sensors (within <c>influenceKm</c>), plus a coverage
/// alpha that fades to 0 at the influence radius so far-from-any-sensor areas stay blank
/// instead of extrapolating nonsense. Colouring/legend map the real value to a scale.
/// </summary>
public static class EnvironmentField
{
    public readonly record struct Sensor(double Lon, double Lat, double Value, double Confidence);

    public sealed class Grid
    {
        public double Lon0, Lat0, DLon, DLat;
        public int Nx, Ny;
        public double[] Values = Array.Empty<double>();  // row-major (j*Nx+i); NaN = no coverage
        public double[] Alpha  = Array.Empty<double>();  // 0..1

        public readonly record struct Cell(double Value, double Alpha);
    }

    public static Grid? Build(IReadOnlyList<Sensor> sensors, double influenceKm, int maxCells = 2800)
    {
        if (sensors == null || sensors.Count == 0 || influenceKm <= 0) return null;

        double minLon = double.MaxValue, minLat = double.MaxValue;
        double maxLon = double.MinValue, maxLat = double.MinValue;
        foreach (var s in sensors)
        {
            minLon = Math.Min(minLon, s.Lon); maxLon = Math.Max(maxLon, s.Lon);
            minLat = Math.Min(minLat, s.Lat); maxLat = Math.Max(maxLat, s.Lat);
        }

        double midLat = (minLat + maxLat) / 2;
        double cosLat = Math.Max(0.2, Math.Cos(midLat * Math.PI / 180));
        double marginLat = influenceKm / 111.0;
        double marginLon = influenceKm / (111.0 * cosLat);
        minLon -= marginLon; maxLon += marginLon;
        minLat -= marginLat; maxLat += marginLat;

        double widthKm  = (maxLon - minLon) * 111.0 * cosLat;
        double heightKm = (maxLat - minLat) * 111.0;
        double cellKm = Math.Max(influenceKm / 6.0, 0.5);   // ~6 cells across the influence radius
        int nx = Math.Clamp((int)Math.Round(widthKm  / cellKm), 8, 80);
        int ny = Math.Clamp((int)Math.Round(heightKm / cellKm), 8, 80);
        while ((long)nx * ny > maxCells)                     // cap payload/perf
        {
            if (nx >= ny) nx = Math.Max(8, nx - 2); else ny = Math.Max(8, ny - 2);
            if (nx == 8 && ny == 8) break;
        }

        double dLon = (maxLon - minLon) / nx, dLat = (maxLat - minLat) / ny;
        var values = new double[nx * ny];
        var alpha  = new double[nx * ny];
        double r2 = influenceKm * influenceKm;

        for (int j = 0; j < ny; j++)
        {
            double lat  = minLat + (j + 0.5) * dLat;
            double clat = Math.Max(0.2, Math.Cos(lat * Math.PI / 180));
            for (int i = 0; i < nx; i++)
            {
                double lon = minLon + (i + 0.5) * dLon;
                double wsum = 0, vsum = 0, nearest2 = double.MaxValue;
                foreach (var s in sensors)
                {
                    double dxKm = (lon - s.Lon) * 111.0 * clat;
                    double dyKm = (lat - s.Lat) * 111.0;
                    double d2 = dxKm * dxKm + dyKm * dyKm;
                    if (d2 > r2) continue;
                    double w = s.Confidence / (d2 + 1.0);    // 1/dist² with ~1km² epsilon
                    wsum += w; vsum += w * s.Value;
                    if (d2 < nearest2) nearest2 = d2;
                }
                int idx = j * nx + i;
                if (wsum <= 0) { values[idx] = double.NaN; alpha[idx] = 0; continue; }
                values[idx] = vsum / wsum;
                double nd = Math.Sqrt(nearest2);              // km to nearest sensor
                alpha[idx] = Math.Clamp(1.0 - nd / influenceKm, 0, 1);
            }
        }

        return new Grid
        {
            Lon0 = minLon, Lat0 = minLat, DLon = dLon, DLat = dLat,
            Nx = nx, Ny = ny, Values = values, Alpha = alpha
        };
    }
}
