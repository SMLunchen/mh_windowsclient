namespace MeshhessenClient.Services;

/// <summary>
/// Marching-squares contour extraction over an <see cref="EnvironmentField.Grid"/>: for each
/// requested level (band boundary), returns the iso-value line segments in lon/lat — the
/// "lines between the temperature zones". Pure and testable.
/// </summary>
public static class EnvironmentContours
{
    // Edge → the two corner indices it connects (corners: 0=SW,1=SE,2=NE,3=NW).
    private static readonly int[,] EdgeCorners = { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 } };

    // Per marching-squares case (corner-≥-level bitmask a=1,b=2,c=4,d=8): edge pairs to join.
    private static readonly int[][] CaseEdges =
    {
        new int[0],            // 0
        new[] { 3, 0 },        // 1  a
        new[] { 0, 1 },        // 2  b
        new[] { 3, 1 },        // 3  ab
        new[] { 1, 2 },        // 4  c
        new[] { 3, 0, 1, 2 },  // 5  ac (saddle)
        new[] { 0, 2 },        // 6  bc
        new[] { 3, 2 },        // 7  abc
        new[] { 2, 3 },        // 8  d
        new[] { 2, 0 },        // 9  ad
        new[] { 0, 1, 2, 3 },  // 10 bd (saddle)
        new[] { 2, 1 },        // 11 abd
        new[] { 1, 3 },        // 12 cd
        new[] { 1, 0 },        // 13 acd
        new[] { 0, 3 },        // 14 bcd
        new int[0],            // 15
    };

    /// <summary>Returns segments as [lon1,lat1,lon2,lat2].</summary>
    public static List<double[]> Build(EnvironmentField.Grid g, IReadOnlyList<double> levels) =>
        BuildCore(g, levels).Select(x => x.seg).ToList();

    /// <summary>Anchor points for line labels: the midpoint of each contour segment (i.e. ON the
    /// drawn isotherm) together with the grid-cell index of a warmer-side corner (value ≥ level),
    /// so the caller can look up that zone's average value.</summary>
    public static IEnumerable<(double lon, double lat, int warmerCell)> LineLabelAnchors(
        EnvironmentField.Grid g, IReadOnlyList<double> levels)
    {
        foreach (var (seg, _, warmerCell) in BuildCore(g, levels))
            yield return ((seg[0] + seg[2]) / 2, (seg[1] + seg[3]) / 2, warmerCell);
    }

    private static IEnumerable<(double[] seg, double level, int warmerCell)> BuildCore(
        EnvironmentField.Grid g, IReadOnlyList<double> levels)
    {
        if (g == null || g.Nx < 2 || g.Ny < 2 || levels == null) yield break;

        for (int j = 0; j < g.Ny - 1; j++)
        {
            for (int i = 0; i < g.Nx - 1; i++)
            {
                // corners SW,SE,NE,NW (lattice = grid centres)
                double vSW = g.Values[j * g.Nx + i];
                double vSE = g.Values[j * g.Nx + i + 1];
                double vNE = g.Values[(j + 1) * g.Nx + i + 1];
                double vNW = g.Values[(j + 1) * g.Nx + i];
                if (double.IsNaN(vSW) || double.IsNaN(vSE) || double.IsNaN(vNE) || double.IsNaN(vNW)) continue;

                double lonW = g.Lon0 + (i + 0.5) * g.DLon, lonE = lonW + g.DLon;
                double latS = g.Lat0 + (j + 0.5) * g.DLat, latN = latS + g.DLat;
                double[] cx = { lonW, lonE, lonE, lonW };   // corner lon: SW,SE,NE,NW
                double[] cy = { latS, latS, latN, latN };   // corner lat
                double[] cv = { vSW, vSE, vNE, vNW };
                int[] cell = { j * g.Nx + i, j * g.Nx + i + 1, (j + 1) * g.Nx + i + 1, (j + 1) * g.Nx + i };

                foreach (var L in levels)
                {
                    int mask = (cv[0] >= L ? 1 : 0) | (cv[1] >= L ? 2 : 0) | (cv[2] >= L ? 4 : 0) | (cv[3] >= L ? 8 : 0);
                    var edges = CaseEdges[mask];
                    if (edges.Length == 0) continue;

                    int warmer = -1;   // a corner on the warmer side (value ≥ L)
                    for (int c = 0; c < 4; c++) if (cv[c] >= L) { warmer = cell[c]; break; }

                    for (int e = 0; e + 1 < edges.Length; e += 2)
                    {
                        var p1 = EdgeCross(edges[e], cx, cy, cv, L);
                        var p2 = EdgeCross(edges[e + 1], cx, cy, cv, L);
                        yield return (new[] { p1.lon, p1.lat, p2.lon, p2.lat }, L, warmer);
                    }
                }
            }
        }
    }

    private static (double lon, double lat) EdgeCross(int edge, double[] cx, double[] cy, double[] cv, double L)
    {
        int a = EdgeCorners[edge, 0], b = EdgeCorners[edge, 1];
        double denom = cv[b] - cv[a];
        double t = Math.Abs(denom) < 1e-9 ? 0.5 : (L - cv[a]) / denom;
        t = Math.Clamp(t, 0, 1);
        return (cx[a] + t * (cx[b] - cx[a]), cy[a] + t * (cy[b] - cy[a]));
    }
}
