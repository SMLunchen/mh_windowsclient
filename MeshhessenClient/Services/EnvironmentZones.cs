namespace MeshhessenClient.Services;

/// <summary>
/// Groups the interpolated grid into contiguous same-band zones (the areas between isotherms)
/// and returns each cell's zone <b>average value</b>. Combined with the isotherm line anchors
/// from <see cref="EnvironmentContours.LineLabelAnchors"/> this yields the "weather-report"
/// value written ON each line. Pure and testable.
/// </summary>
public static class EnvironmentZones
{
    /// <summary>Per-cell average value of the cell's zone (4-connected same-band component).
    /// NaN where the cell has no coverage. Indexed like <c>grid.Values</c> (j*Nx+i).</summary>
    public static double[] ZoneAverages(EnvironmentField.Grid g, double min, double step, int nBands)
    {
        int nx = g?.Nx ?? 0, ny = g?.Ny ?? 0, n = nx * ny;
        var avg = new double[n];
        for (int i = 0; i < n; i++) avg[i] = double.NaN;
        if (g == null || nx < 1 || ny < 1 || step <= 0) return avg;

        var band = new int[n];
        for (int i = 0; i < n; i++)
        {
            double v = g.Values[i];
            band[i] = (double.IsNaN(v) || g.Alpha[i] <= 0.1)
                ? -1 : Math.Clamp((int)Math.Floor((v - min) / step), 0, nBands - 1);
        }

        var visited = new bool[n];
        var stack = new Stack<int>();
        for (int start = 0; start < n; start++)
        {
            if (visited[start] || band[start] < 0) continue;
            int b = band[start];
            var cells = new List<int>();
            stack.Clear(); stack.Push(start); visited[start] = true;
            double sumV = 0;
            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                cells.Add(idx); sumV += g.Values[idx];
                int ci = idx % nx, cj = idx / nx;
                void Try(int ni, int nj)
                {
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= ny) return;
                    int k = nj * nx + ni;
                    if (!visited[k] && band[k] == b) { visited[k] = true; stack.Push(k); }
                }
                Try(ci - 1, cj); Try(ci + 1, cj); Try(ci, cj - 1); Try(ci, cj + 1);
            }
            double a = sumV / cells.Count;
            foreach (var c in cells) avg[c] = a;
        }
        return avg;
    }
}
