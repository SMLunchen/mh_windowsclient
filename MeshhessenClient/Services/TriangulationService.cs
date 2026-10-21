namespace MeshhessenClient.Services;

/// <summary>
/// Position estimation from RF-link neighbors: signal → distance model plus weighted
/// least-squares trilateration. Pure math, no UI/DB dependencies (unit-tested).
/// </summary>
public static class TriangulationService
{
    /// <summary><paramref name="PosErrKm"/> = the anchor's OWN position uncertainty
    /// (e.g. from Meshtastic precision_bits truncation): within that radius the cost
    /// function applies no penalty (dead zone) and the anchor is down-weighted.
    /// <paramref name="WeightScale"/> boosts hard information (a reported-position prior
    /// outranks RF distance guesses). <paramref name="SigmaKm"/> = expected 1-σ error of
    /// <paramref name="DistKm"/> itself (from the model's dB spread); 0 = derive a
    /// default (30 % of the distance). Residuals are normalized by it, so a 5 km circle
    /// is allowed to miss by kilometers where a 1 km circle is not.</summary>
    public readonly record struct Anchor(
        double Lat, double Lon, double DistKm, double PosErrKm = 0, double WeightScale = 1,
        double SigmaKm = 0);

    // ── Expert mode: TX power / antenna gain of the target node ──────────────

    /// <summary>The implicit link budget the distance model is calibrated for:
    /// 22 dBm TX + 1 dBi antenna. Expert values are applied as the DELTA to this
    /// reference, so default expert inputs reproduce the normal model exactly.</summary>
    public const double ReferenceTxDbm = 22.0;
    public const double ReferenceAntennaDbi = 1.0;

    /// <summary>Adjusts a measured RSSI to the reference link budget before the distance
    /// model is applied. The full budget of one link counts the TRANSMITTING side's TX
    /// power + antenna gain and the RECEIVING side's antenna gain; the reference is
    /// 22 dBm + 1 dBi + 1 dBi. A stronger-than-reference link yields a lower effective
    /// RSSI → larger distance.</summary>
    public static double AdjustedRssi(double rssi, double txDbm, double txAntDbi, double rxAntDbi) =>
        rssi - ((txDbm + txAntDbi + rxAntDbi) - (ReferenceTxDbm + 2 * ReferenceAntennaDbi));

    /// <summary>Typical max TX power preset (dBm) by hardware model string — starting
    /// values for the expert dialog, always user-overridable.</summary>
    public static double TxPresetDbm(string? hardwareModel)
    {
        var h = (hardwareModel ?? "").ToUpperInvariant().Replace("_", "");
        if (h.Contains("WISMESH"))                    return 27; // RAK WisMesh 1 W: 0.5 W default (1 W = 30 dBm)
        if (h.Contains("RAK"))                        return 22; // RAK4631 (SX1262, max ~0.16 W)
        if (h.Contains("STATION"))                    return 30; // Station G1/G2 high power
        if (h.Contains("HELTEC") && h.Contains("V4")) return 27; // 0.5 W
        if (h.Contains("HELTEC"))                     return 21;
        if (h.Contains("TBEAM"))                      return 22;
        if (h.Contains("TLORA"))                      return 21;
        if (h.Contains("TDECK"))                      return 22;
        if (h.Contains("TECHO"))                      return 22;
        return 20;
    }

    /// <summary>dBm → watts, for the dialog's live hint.</summary>
    public static double DbmToWatts(double dbm) => Math.Pow(10, dbm / 10.0) / 1000.0;

    /// <summary>Robust central value of a measurement series: sorts, trims
    /// <paramref name="trimFraction"/> off each end (rounded down, so small samples
    /// are a plain median) and returns the median of the rest. Null when empty.
    /// Used to turn per-link SNR/RSSI histories into one stable value instead of
    /// whatever was measured last.</summary>
    public static double? TrimmedMedian(IReadOnlyList<double> values, double trimFraction = 0.05)
    {
        if (values == null || values.Count == 0) return null;
        var sorted = values.OrderBy(v => v).ToList();
        int trim = (int)(sorted.Count * trimFraction);
        if (sorted.Count - 2 * trim < 1) trim = (sorted.Count - 1) / 2;
        var kept = sorted.GetRange(trim, sorted.Count - 2 * trim);
        int mid = kept.Count / 2;
        return kept.Count % 2 == 1 ? kept[mid] : (kept[mid - 1] + kept[mid]) / 2.0;
    }

    // ── Terrain: line-of-sight / Fresnel analysis (DEM-backed) ────────────────

    /// <summary>Default antenna height above ground (m) — mesh nodes typically hang
    /// 2–3 m up (balcony/roof edge); overridable per node in the expert dialog.</summary>
    public const double DefaultAntennaHeightM = 2.5;

    /// <summary>Terrain context for <see cref="Estimate"/>: elevation sampler
    /// (m a.s.l., null = no data), antenna heights above ground (parallel to the
    /// anchor list) and the path-loss exponent that converts diffraction loss into
    /// the model's signal-distance space.</summary>
    public sealed class TerrainContext
    {
        public required Func<double, double, double?> ElevationAt { get; init; }
        public required double[] AnchorHeightM { get; init; }
        public double TargetHeightM { get; init; } = DefaultAntennaHeightM;
        public double Exponent { get; init; } = 2.5;
        public double FreqMhz { get; init; } = 868.0;
    }

    /// <summary>Knife-edge diffraction loss in dB for Fresnel parameter v
    /// (ITU-R P.526 approximation). ≤ −0.78 means the path is effectively clear
    /// (≈ 60 % first-Fresnel-zone clearance) → 0 dB.</summary>
    public static double KnifeEdgeLossDb(double v) =>
        v <= -0.78 ? 0 : 6.9 + 20 * Math.Log10(Math.Sqrt((v - 0.1) * (v - 0.1) + 1) + v - 0.1);

    /// <summary>Worst (maximum) Fresnel parameter v along the path between two
    /// antennas: terrain samples plus 4/3-earth bulge against the straight LOS line.
    /// Samples without elevation data are skipped; −10 = clear/no data.</summary>
    public static double PathFresnelV(Func<double, double, double?> elev,
        double lat1, double lon1, double h1AglM,
        double lat2, double lon2, double h2AglM,
        double freqMhz = 868.0, int samples = 24)
    {
        double? e1 = elev(lat1, lon1), e2 = elev(lat2, lon2);
        if (e1 == null || e2 == null) return -10;

        double dKm = HaversineKm(lat1, lon1, lat2, lon2);
        double dM = dKm * 1000.0;
        if (dM < 50) return -10; // effectively co-located

        double ht1 = e1.Value + h1AglM, ht2 = e2.Value + h2AglM;
        double lambda = 299.792458 / freqMhz; // m
        const double EffEarthM = 4.0 / 3.0 * 6371000.0;

        double vMax = -10;
        for (int s = 1; s < samples; s++)
        {
            double t = (double)s / samples;
            double? te = elev(lat1 + (lat2 - lat1) * t, lon1 + (lon2 - lon1) * t);
            if (te == null) continue;
            double d1 = dM * t, d2 = dM * (1 - t);
            double bulge = d1 * d2 / (2 * EffEarthM);
            double los = ht1 + (ht2 - ht1) * t;
            double hObs = te.Value + bulge - los;
            double v = hObs * Math.Sqrt(2 * dM / (lambda * d1 * d2));
            if (v > vMax) vMax = v;
        }
        return vMax;
    }

    /// <summary>EXCESS diffraction loss (dB) of the real terrain over the "typical
    /// link" baseline, where the ground runs straight between the two endpoints'
    /// ground elevations. The empirical path-loss model (fitted from real links) ALREADY
    /// contains the usual near-ground/Fresnel drag of a flat path — charging absolute
    /// obstruction would double-count it. Only genuine terrain in the way (a ridge
    /// between the endpoints) yields a positive value. 0 = clear or no data.</summary>
    public static double PathExcessLossDb(Func<double, double, double?> elev,
        double lat1, double lon1, double h1AglM,
        double lat2, double lon2, double h2AglM,
        double freqMhz = 868.0, int samples = 24)
    {
        double? e1 = elev(lat1, lon1), e2 = elev(lat2, lon2);
        if (e1 == null || e2 == null) return 0;

        double dKm = HaversineKm(lat1, lon1, lat2, lon2);
        double dM = dKm * 1000.0;
        if (dM < 50) return 0;

        double ht1 = e1.Value + h1AglM, ht2 = e2.Value + h2AglM;
        double lambda = 299.792458 / freqMhz;
        const double EffEarthM = 4.0 / 3.0 * 6371000.0;

        double vReal = -10, vBase = -10;
        for (int s = 1; s < samples; s++)
        {
            double t = (double)s / samples;
            double? te = elev(lat1 + (lat2 - lat1) * t, lon1 + (lon2 - lon1) * t);
            if (te == null) continue;
            double d1 = dM * t, d2 = dM * (1 - t);
            double bulge = d1 * d2 / (2 * EffEarthM);
            double los = ht1 + (ht2 - ht1) * t;
            double k = Math.Sqrt(2 * dM / (lambda * d1 * d2));

            double vR = (te.Value + bulge - los) * k;
            if (vR > vReal) vReal = vR;

            double baseGround = e1.Value + (e2.Value - e1.Value) * t; // straight ground line
            double vB = (baseGround + bulge - los) * k;
            if (vB > vBase) vBase = vB;
        }
        return Math.Max(0, KnifeEdgeLossDb(vReal) - KnifeEdgeLossDb(vBase));
    }

    /// <summary>Haversine distance in km.</summary>
    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Half-cell position error (km) of a precision_bits-truncated Meshtastic
    /// position: 2^(32−bits) · 1e-7° · 111.32 km/° / 2. 32 (or unknown/0) = exact;
    /// 16 bits ≈ ±0.37 km, 13 bits ≈ ±2.9 km.</summary>
    public static double PrecisionBitsToKm(int bits) =>
        bits is <= 0 or >= 32 ? 0 : Math.Pow(2, 32 - bits) * 1e-7 * 111.32 / 2.0;

    /// <summary>Path-loss model parameters: expected RSSI at 1 km and path-loss exponent.
    /// <see cref="Default"/> is the desk calibration (868 MHz, mixed terrain); a fitted
    /// calibration from the mesh's own links (see <see cref="FitCalibration"/>) replaces
    /// it once enough ground truth has been collected.</summary>
    /// <summary>Path-loss model + its residual spread. <paramref name="SigmaDb"/> is the
    /// RMSE of the fit residuals (dB) — it propagates into a multiplicative distance
    /// uncertainty via <see cref="DistanceFactor"/>. The default assumes ±6 dB.</summary>
    /// <summary><paramref name="Clamped"/>: the raw fit ran into the physical parameter
    /// bounds (n or P1km) — a strong sign the data does not support the model shape;
    /// the model dialog then recommends (and defaults to) the standard model.</summary>
    public sealed record Calibration(double P1Km, double Exponent, int Samples, double SigmaDb = 6.0, double R2 = 0, bool Clamped = false)
    {
        public static readonly Calibration Default = new(-90.0, 2.5, 0);
        public bool IsFitted => Samples > 0;
    }

    /// <summary>Multiplicative distance uncertainty of the model: ±SigmaDb of signal
    /// spread maps to a ×/÷ factor on distance. Never below 1.5 — LoRa links are never
    /// better than ±50 % distance accuracy from signal alone.</summary>
    public static double DistanceFactor(Calibration? cal = null)
    {
        var c = cal ?? Calibration.Default;
        return Math.Max(Math.Pow(10, c.SigmaDb / (10 * c.Exponent)), 1.5);
    }

    /// <summary>Signal → distance via log-distance path loss:
    /// d = 10^((P1km − RSSI) / (10·n)) km, clamped to 0.3–60 km. Inherently rough —
    /// terrain dominates LoRa links — which is why the map draws the anchor circles:
    /// they visualize the uncertainty instead of pretending precision.</summary>
    public static double SignalDistanceKm(double rssiEst, Calibration? cal = null)
    {
        var c = cal ?? Calibration.Default;
        return Math.Clamp(Math.Pow(10, (c.P1Km - rssiEst) / (10 * c.Exponent)), 0.3, 60.0);
    }

    /// <summary>Self-calibration from the mesh's own data: links where BOTH ends have a
    /// known position give (true distance, estimated RSSI) pairs. Ordinary least squares
    /// on RSSI = P1 − 10·n·log10(d) fits both parameters. Falls back to
    /// <see cref="Calibration.Default"/> when there are too few samples (&lt; 8), the
    /// distance spread is too narrow (max/min &lt; 3) or the fit is physically
    /// implausible; fitted values are clamped to sane RF ranges.</summary>
    public static Calibration FitCalibration(IReadOnlyList<(double DistKm, double Rssi)> samples)
    {
        var valid = samples.Where(s => s.DistKm >= 0.2 && s.DistKm <= 80).ToList();
        if (valid.Count < 8) return Calibration.Default;
        if (valid.Max(s => s.DistKm) / valid.Min(s => s.DistKm) < 3) return Calibration.Default;

        // OLS on y = a + b·x with x = log10(d), y = RSSI  →  P1 = a, n = −b/10
        var xs = valid.Select(s => Math.Log10(s.DistKm)).ToList();
        var ys = valid.Select(s => s.Rssi).ToList();
        double mx = xs.Average(), my = ys.Average();
        double sxx = xs.Sum(x => (x - mx) * (x - mx));
        if (sxx < 1e-9) return Calibration.Default;
        double sxy = xs.Zip(ys, (x, y) => (x - mx) * (y - my)).Sum();
        double b = sxy / sxx;
        double a = my - b * mx;

        if (b >= 0) return Calibration.Default; // signal must weaken with distance

        // Fit quality (R², residual spread) is REPORTED, not gated: the model-choice
        // dialog shows it with a recommendation and the user decides. Only mathematically
        // unusable fits (too few samples, no spread, positive slope) fall back silently.
        double ssTot = ys.Sum(v => (v - my) * (v - my));
        double ssRes = xs.Zip(ys, (x, v) => { double e = v - (a + b * x); return e * e; }).Sum();
        double r2 = ssTot > 1e-9 ? 1 - ssRes / ssTot : 0;

        double rmse = Math.Sqrt(ssRes / valid.Count);
        double nRaw = -b / 10.0;
        double n    = Math.Clamp(nRaw, 1.8, 4.5);
        double p1   = Math.Clamp(a, -110.0, -70.0);
        bool clamped = Math.Abs(n - nRaw) > 1e-9 || Math.Abs(p1 - a) > 1e-9;
        return new Calibration(p1, n, valid.Count, Math.Clamp(rmse, 3.0, 12.0), r2, clamped);
    }

    /// <summary>Weighted least-squares trilateration: minimizes Σ wᵢ·(|p−aᵢ|−dᵢ)² in a
    /// local planar km frame (closer anchors weigh more, w = 1/d), via damped
    /// normalized-gradient descent starting at the weighted centroid.</summary>
    public static (double Lat, double Lon) Trilaterate(IReadOnlyList<Anchor> anchors)
    {
        var r = Estimate(anchors);
        return (r.Lat, r.Lon);
    }

    /// <summary>Full estimate: best position, an uncertainty radius and — when the cost
    /// landscape has a second plausible region (classic two-circle ambiguity, one-sided
    /// anchor geometry) — an alternative candidate position.</summary>
    public sealed record EstimateResult(
        double Lat, double Lon, double UncertaintyKm, double? AltLat = null, double? AltLon = null)
    {
        public bool HasAlternative => AltLat.HasValue && AltLon.HasValue;
    }

    /// <summary>Geographic bounding box of Estimate's grid scan (each anchor padded by
    /// distance + position error) — preload the terrain grid over exactly this region.</summary>
    public static (double MinLat, double MinLon, double MaxLat, double MaxLon) SearchBounds(
        IReadOnlyList<Anchor> anchors)
    {
        double lat0 = anchors.Average(a => a.Lat);
        double kx = 111.320 * Math.Cos(lat0 * Math.PI / 180.0);
        double ky = 110.574;
        double minLat = anchors.Min(a => a.Lat - (a.DistKm + a.PosErrKm) / ky);
        double maxLat = anchors.Max(a => a.Lat + (a.DistKm + a.PosErrKm) / ky);
        double minLon = anchors.Min(a => a.Lon - (a.DistKm + a.PosErrKm) / kx);
        double maxLon = anchors.Max(a => a.Lon + (a.DistKm + a.PosErrKm) / kx);
        return (minLat, minLon, maxLat, maxLon);
    }

    /// <summary>Diffraction loss is capped: SRTM-90 terrain analysis is too coarse to
    /// justify more than ~25 dB of claimed obstruction loss for a link that evidently
    /// exists (we measured its signal).</summary>
    public const double MaxDiffractionDb = 25.0;

    public static EstimateResult Estimate(IReadOnlyList<Anchor> anchors, TerrainContext? terrain = null)
    {
        if (anchors.Count == 0) throw new ArgumentException("no anchors");
        if (anchors.Count == 1)
            return new EstimateResult(anchors[0].Lat, anchors[0].Lon,
                anchors[0].DistKm + anchors[0].PosErrKm);

        double lat0 = anchors.Average(a => a.Lat);
        double kx = 111.320 * Math.Cos(lat0 * Math.PI / 180.0);
        double ky = 110.574;
        double refLat = anchors[0].Lat, refLon = anchors[0].Lon;

        var pts = anchors.Select(a => (
            X: (a.Lon - refLon) * kx,
            Y: (a.Lat - refLat) * ky,
            D: a.DistKm,
            E: Math.Max(a.PosErrKm, 0),
            // 1-σ error of the distance estimate itself: explicit from the model's dB
            // spread, else 30 % of the distance (never below 0.3 km)
            S: a.SigmaKm > 0 ? a.SigmaKm : Math.Max(0.3 * a.DistKm, 0.3),
            W: Math.Max(a.WeightScale, 0.01))).ToList();

        // Huber threshold (in σ units): residuals beyond it count linearly, so ONE
        // inconsistent anchor (model garbage, unknown relay, wrong position) can no
        // longer dominate the quadratic cost and drag the estimate kilometers away.
        const double HuberK = 1.5;

        double F(double px, double py)
        {
            double sum = 0;
            double cellLat = refLat + py / ky, cellLon = refLon + px / kx;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                double r = Math.Sqrt((px - p.X) * (px - p.X) + (py - p.Y) * (py - p.Y));

                // Terrain: a path with a ridge in the way needs extra (diffraction)
                // loss, so the SAME measured signal maps to a LARGER model distance —
                // scale r into signal-distance space before comparing with D. Only the
                // EXCESS over a flat path counts (the fitted model already contains the
                // usual near-ground drag). Skip the reported-position prior (D≈0).
                if (terrain != null && p.D >= 0.05)
                {
                    double loss = Math.Min(PathExcessLossDb(terrain.ElevationAt,
                        cellLat, cellLon, terrain.TargetHeightM,
                        anchors[i].Lat, anchors[i].Lon, terrain.AnchorHeightM[i],
                        terrain.FreqMhz), MaxDiffractionDb);
                    if (loss > 0) r *= Math.Pow(10, loss / (10 * terrain.Exponent));
                }

                // Dead zone of the anchor's own position error: within ±E no penalty —
                // a truncated anchor position must not drag the estimate around.
                // Then normalize by the distance estimate's own σ and apply Huber.
                double e = Math.Max(Math.Abs(r - p.D) - p.E, 0) / p.S;
                double c = e <= HuberK ? e * e : HuberK * (2 * e - HuberK);
                sum += p.W * c;
            }
            return sum;
        }

        (double X, double Y, double F) Descend(double sx, double sy)
        {
            double x = sx, y = sy, f = F(sx, sy);
            double step = Math.Max(pts.Max(p => p.D) * 0.25, 1.0);
            for (int it = 0; it < 300 && step > 0.002; it++)
            {
                const double h = 0.01;
                double gx = (F(x + h, y) - F(x - h, y)) / (2 * h);
                double gy = (F(x, y + h) - F(x, y - h)) / (2 * h);
                double gl = Math.Sqrt(gx * gx + gy * gy);
                if (gl < 1e-9) break;
                double nx = x - step * gx / gl, ny = y - step * gy / gl;
                double nf = F(nx, ny);
                if (nf < f) { x = nx; y = ny; f = nf; }
                else step *= 0.5;
            }
            return (x, y, f);
        }

        // Coarse cost-landscape scan over the plausible region (each anchor ± distance ± pos. error)
        double minX = pts.Min(p => p.X - p.D - p.E), maxX = pts.Max(p => p.X + p.D + p.E);
        double minY = pts.Min(p => p.Y - p.D - p.E), maxY = pts.Max(p => p.Y + p.D + p.E);
        const int N = 81;
        double stepX = (maxX - minX) / (N - 1), stepY = (maxY - minY) / (N - 1);
        double gridStep = Math.Max(stepX, stepY);

        var cells = new List<(double X, double Y, double F)>(N * N);
        double bestCx = minX, bestCy = minY, bestCf = double.MaxValue;
        for (int iy = 0; iy < N; iy++)
            for (int ix = 0; ix < N; ix++)
            {
                double cx = minX + ix * stepX, cy = minY + iy * stepY;
                double cf = F(cx, cy);
                cells.Add((cx, cy, cf));
                if (cf < bestCf) { bestCf = cf; bestCx = cx; bestCy = cy; }
            }

        var best = Descend(bestCx, bestCy);

        // Plausible region: everything whose cost is close to the optimum
        double threshold = best.F * 1.6 + 0.02;
        var accepted = cells.Where(c => c.F <= threshold).ToList();

        // Alternative candidate: best accepted cell clearly away from the main solution
        double Dist(double ax, double ay, double bx, double by)
            => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        double altMinDist = Math.Max(3.0, 4 * gridStep);
        (double X, double Y, double F)? alt = null;
        var altSeed = accepted
            .Where(c => Dist(c.X, c.Y, best.X, best.Y) > altMinDist && c.F <= best.F * 1.4 + 0.02)
            .OrderBy(c => c.F)
            .Cast<(double X, double Y, double F)?>()
            .FirstOrDefault();
        if (altSeed != null)
        {
            var refined = Descend(altSeed.Value.X, altSeed.Value.Y);
            if (Dist(refined.X, refined.Y, best.X, best.Y) > altMinDist)
                alt = refined;
        }

        // Uncertainty: 90th-percentile distance of the MAIN cluster's accepted cells
        // (cells nearer to the alternative belong to that cluster, not the main one)
        var mainDists = accepted
            .Where(c => alt == null ||
                        Dist(c.X, c.Y, best.X, best.Y) <= Dist(c.X, c.Y, alt.Value.X, alt.Value.Y))
            .Select(c => Dist(c.X, c.Y, best.X, best.Y))
            .OrderBy(d => d)
            .ToList();
        double uncertainty = mainDists.Count > 0
            ? Math.Max(mainDists[(int)((mainDists.Count - 1) * 0.9)], gridStep)
            : gridStep;

        return new EstimateResult(
            refLat + best.Y / ky, refLon + best.X / kx,
            uncertainty,
            alt.HasValue ? refLat + alt.Value.Y / ky : null,
            alt.HasValue ? refLon + alt.Value.X / kx : null);
    }
}
