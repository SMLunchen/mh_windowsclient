using MeshhessenClient.Services;
using static MeshhessenClient.Services.TriangulationService;

namespace MeshhessenClient.Tests;

// Position estimation from RF neighbors: distance model + trilateration.
public class TriangulationTests
{
    private static double DistKm(double lat1, double lon1, double lat2, double lon2)
    {
        double kx = 111.320 * Math.Cos(((lat1 + lat2) / 2) * Math.PI / 180);
        double dx = (lon2 - lon1) * kx, dy = (lat2 - lat1) * 110.574;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    [Fact]
    public void Distance_model_is_monotonic_and_calibrated()
    {
        // −90 dBm ⇒ 1 km reference point
        Assert.Equal(1.0, SignalDistanceKm(-90), precision: 3);
        // weaker signal → farther
        Assert.True(SignalDistanceKm(-115) > SignalDistanceKm(-100));
        Assert.True(SignalDistanceKm(-100) > SignalDistanceKm(-85));
        // clamps
        Assert.Equal(0.3, SignalDistanceKm(-20), precision: 3);
        Assert.Equal(60.0, SignalDistanceKm(-170), precision: 3);
    }

    [Fact]
    public void Trilateration_recovers_position_from_exact_distances()
    {
        // True position near Alsfeld; three anchors around it
        (double Lat, double Lon) truth = (50.75, 9.27);
        var anchorPos = new (double Lat, double Lon)[]
        {
            (50.90, 9.10), (50.60, 9.20), (50.80, 9.55),
        };
        var anchors = anchorPos
            .Select(a => new Anchor(a.Lat, a.Lon, DistKm(a.Lat, a.Lon, truth.Lat, truth.Lon)))
            .ToList();

        var (lat, lon) = Trilaterate(anchors);
        Assert.True(DistKm(lat, lon, truth.Lat, truth.Lon) < 0.5,
            $"estimate off by {DistKm(lat, lon, truth.Lat, truth.Lon):F2} km");
    }

    [Fact]
    public void Trilateration_with_noisy_distances_stays_in_plausible_region()
    {
        (double Lat, double Lon) truth = (50.75, 9.27);
        var anchorPos = new (double Lat, double Lon)[]
        {
            (50.90, 9.10), (50.60, 9.20), (50.80, 9.55), (50.70, 9.00),
        };
        // ±40 % distance error (realistic for the RSSI model)
        var noise = new[] { 1.4, 0.7, 1.25, 0.8 };
        var anchors = anchorPos
            .Select((a, i) => new Anchor(a.Lat, a.Lon,
                DistKm(a.Lat, a.Lon, truth.Lat, truth.Lon) * noise[i]))
            .ToList();

        var (lat, lon) = Trilaterate(anchors);
        Assert.True(DistKm(lat, lon, truth.Lat, truth.Lon) < 8,
            $"estimate off by {DistKm(lat, lon, truth.Lat, truth.Lon):F2} km");
    }

    [Fact]
    public void Two_anchors_give_a_point_between_or_near_them()
    {
        var a = new Anchor(50.0, 9.0, 5);
        var b = new Anchor(50.0, 9.3, 5); // ~21 km apart, both claim 5 km → midpoint-ish
        var (lat, lon) = Trilaterate(new[] { a, b });
        Assert.InRange(lon, 9.0, 9.3);
        Assert.InRange(lat, 49.9, 50.1);
    }

    [Fact]
    public void Single_anchor_returns_anchor_position()
    {
        var (lat, lon) = Trilaterate(new[] { new Anchor(51.1, 8.8, 10) });
        Assert.Equal(51.1, lat, precision: 6);
        Assert.Equal(8.8, lon, precision: 6);
    }

    // ── Uncertainty + ambiguity (Estimate) ──────────────────────────────────

    [Fact]
    public void Surrounding_anchors_with_exact_distances_give_tight_unambiguous_estimate()
    {
        (double Lat, double Lon) truth = (50.75, 9.27);
        var anchorPos = new (double Lat, double Lon)[] { (50.90, 9.10), (50.60, 9.20), (50.80, 9.55) };
        var anchors = anchorPos
            .Select(a => new Anchor(a.Lat, a.Lon, DistKm(a.Lat, a.Lon, truth.Lat, truth.Lon)))
            .ToList();

        var est = Estimate(anchors);
        Assert.True(DistKm(est.Lat, est.Lon, truth.Lat, truth.Lon) < 0.6);
        Assert.True(est.UncertaintyKm < 2.5, $"uncertainty {est.UncertaintyKm:F1} km");
        Assert.False(est.HasAlternative);
    }

    [Fact]
    public void Colinear_anchors_report_the_mirror_position_as_alternative()
    {
        // The MHR4 case: all anchors on one line (no coverage on one side) — the mirror
        // position across that line fits the distances exactly as well.
        (double Lat, double Lon) truth = (50.08, 9.10);
        var anchorPos = new (double Lat, double Lon)[] { (50.0, 9.0), (50.0, 9.1), (50.0, 9.2) };
        var anchors = anchorPos
            .Select(a => new Anchor(a.Lat, a.Lon, DistKm(a.Lat, a.Lon, truth.Lat, truth.Lon)))
            .ToList();

        var est = Estimate(anchors);
        Assert.True(est.HasAlternative, "mirror ambiguity should be detected");
        // One of the two candidates is (near) the truth, the other its mirror at ~49.92
        double dMain = DistKm(est.Lat, est.Lon, truth.Lat, truth.Lon);
        double dAlt  = DistKm(est.AltLat!.Value, est.AltLon!.Value, truth.Lat, truth.Lon);
        Assert.True(Math.Min(dMain, dAlt) < 1.5, $"neither candidate near truth ({dMain:F1}/{dAlt:F1} km)");
        Assert.True(Math.Max(dMain, dAlt) > 10, "candidates should be on opposite sides of the line");
    }

    [Fact]
    public void Inconsistent_distances_increase_uncertainty()
    {
        (double Lat, double Lon) truth = (50.75, 9.27);
        var anchorPos = new (double Lat, double Lon)[] { (50.90, 9.10), (50.60, 9.20), (50.80, 9.55) };

        var exact = anchorPos
            .Select(a => new Anchor(a.Lat, a.Lon, DistKm(a.Lat, a.Lon, truth.Lat, truth.Lon)))
            .ToList();
        var noise = new[] { 1.6, 0.6, 1.3 };
        var noisy = anchorPos
            .Select((a, i) => new Anchor(a.Lat, a.Lon, DistKm(a.Lat, a.Lon, truth.Lat, truth.Lon) * noise[i]))
            .ToList();

        Assert.True(Estimate(noisy).UncertaintyKm > Estimate(exact).UncertaintyKm,
            "contradictory distances must widen the uncertainty region");
    }

    [Fact]
    public void Distance_factor_reflects_model_sigma_and_has_a_floor()
    {
        Assert.True(DistanceFactor() >= 1.5);
        var tight = new Calibration(-90, 2.5, 20, SigmaDb: 3);
        var loose = new Calibration(-90, 2.5, 20, SigmaDb: 10);
        Assert.True(DistanceFactor(loose) > DistanceFactor(tight));
    }

    // ── Trimmed median (robust per-link signal value) ────────────────────────

    [Fact]
    public void Trimmed_median_handles_empty_and_single()
    {
        Assert.Null(TrimmedMedian(Array.Empty<double>()));
        Assert.Equal(-7.5, TrimmedMedian(new[] { -7.5 })!.Value, precision: 6);
    }

    [Fact]
    public void Trimmed_median_is_the_median_for_small_samples()
    {
        // n < 20 → 5 % trim rounds to 0, plain median (order must not matter)
        Assert.Equal(5, TrimmedMedian(new double[] { 9, 1, 5 })!.Value, precision: 6);
        Assert.Equal(3, TrimmedMedian(new double[] { 4, 1, 2, 8 })!.Value, precision: 6); // even: mean of middle two
    }

    [Fact]
    public void Trimmed_median_discards_outliers()
    {
        // 20 samples around −95 with two wild outliers at the ends: the 5 % trim drops
        // one per end; the median must stay at the bulk either way.
        var values = Enumerable.Repeat(-95.0, 18).Concat(new[] { -160.0, -20.0 }).ToList();
        Assert.Equal(-95, TrimmedMedian(values)!.Value, precision: 6);
    }

    // ── Robust estimation & degenerate fits ──────────────────────────────────

    [Fact]
    public void One_outlier_anchor_cannot_drag_the_estimate()
    {
        // Two consistent 2 km circles tangent at their midpoint (~50.0, 8.028) plus one
        // wildly wrong anchor 26 km east whose circle claims 3 km. With quadratic cost
        // the outlier dominated and pulled the estimate kilometers east — the Huber
        // tails cap its influence, the consistent pair must win.
        var consistent = new List<Anchor>
        {
            new(50.0, 8.000, 2.0),
            new(50.0, 8.056, 2.0),
        };
        var withOutlier = new List<Anchor>(consistent) { new(50.0, 8.400, 3.0) };

        var clean = Estimate(consistent);
        var robust = Estimate(withOutlier);
        double kx = 111.32 * Math.Cos(50 * Math.PI / 180);
        double shiftKm = Math.Sqrt(
            Math.Pow((robust.Lat - clean.Lat) * 110.574, 2) +
            Math.Pow((robust.Lon - clean.Lon) * kx, 2));
        Assert.True(shiftKm < 1.5, $"outlier shifted the estimate by {shiftKm:0.0} km");
    }

    [Fact]
    public void Fit_running_into_parameter_bounds_is_flagged_as_clamped()
    {
        // Nearly distance-independent RSSI → raw slope n < 1.8 → clamped fit.
        var flat = new List<(double DistKm, double Rssi)>();
        for (int i = 0; i < 20; i++)
            flat.Add((0.5 + i * 0.8, -95 + (i % 2)));  // 16 km spread, ~1 dB wiggle
        var cal = FitCalibration(flat);
        if (cal.IsFitted) Assert.True(cal.Clamped, "degenerate fit must be flagged");

        // A clean n=2.5 synthetic stays unclamped.
        var good = new List<(double, double)>();
        for (int i = 0; i < 20; i++)
        {
            double d = 0.5 + i * 0.7;
            good.Add((d, -90 - 25 * Math.Log10(d)));
        }
        var calGood = FitCalibration(good);
        Assert.True(calGood.IsFitted);
        Assert.False(calGood.Clamped);
    }

    // ── Terrain: Fresnel/knife-edge path analysis ────────────────────────────

    [Fact]
    public void Knife_edge_loss_is_zero_when_clear_and_grows_with_v()
    {
        Assert.Equal(0, KnifeEdgeLossDb(-1), precision: 6);
        Assert.Equal(0, KnifeEdgeLossDb(-0.79), precision: 6);
        Assert.Equal(6.0, KnifeEdgeLossDb(0), 0.1);     // grazing LOS ≈ 6 dB
        Assert.True(KnifeEdgeLossDb(1) > KnifeEdgeLossDb(0));
        Assert.True(KnifeEdgeLossDb(3) > KnifeEdgeLossDb(1));
    }

    [Fact]
    public void Flat_terrain_has_no_excess_loss()
    {
        // The empirical model already contains the usual near-ground drag: a perfectly
        // flat 10 km path must contribute ZERO excess loss, even though its absolute
        // Fresnel zone is ground-obstructed at 2.5 m antenna height.
        double? Flat(double lat, double lon) => 100;
        double excess = PathExcessLossDb(Flat, 50.0, 8.0, 2.5, 50.0, 8.14, 2.5);
        Assert.Equal(0, excess, precision: 6);
        // sanity: the ABSOLUTE analysis does see the ground-dragged Fresnel zone
        Assert.True(PathFresnelV(Flat, 50.0, 8.0, 2.5, 50.0, 8.14, 2.5) > -0.78);
    }

    [Fact]
    public void Ridge_between_endpoints_yields_excess_loss()
    {
        // 200 m ridge mid-path between two 100 m endpoints → real terrain pokes far
        // above the straight baseline → positive excess diffraction loss.
        double? Ridge(double lat, double lon) =>
            lon is > 8.06 and < 8.08 ? 300 : 100;
        double excess = PathExcessLossDb(Ridge, 50.0, 8.0, 2.5, 50.0, 8.14, 2.5);
        Assert.True(excess > 6, $"expected clearly positive excess loss, got {excess:0.0} dB");
        Assert.Equal(0, PathExcessLossDb(Ridge, 50.0, 8.09, 2.5, 50.0, 8.14, 2.5), precision: 6);
    }

    [Fact]
    public void No_elevation_data_means_no_penalty()
    {
        Assert.Equal(0, PathExcessLossDb((_, _) => null, 50.0, 8.0, 2.5, 50.0, 8.14, 2.5), precision: 6);
    }

    [Fact]
    public void Terrain_breaks_the_mirror_ambiguity_toward_the_clear_side()
    {
        // Two anchors on a west–east line, target distance equal from both → classic
        // mirror ambiguity north/south. A 400 m plateau north of the line blocks the
        // northern candidate's paths — the estimate must land on the clear south side.
        var anchors = new List<Anchor>
        {
            new(50.0, 8.00, 6.0),
            new(50.0, 8.14, 6.0),
        };
        double? Plateau(double lat, double lon) => lat > 50.02 ? 400 : 100;
        var terrain = new TerrainContext
        {
            ElevationAt = Plateau,
            AnchorHeightM = new[] { 2.5, 2.5 },
            TargetHeightM = 2.5,
        };
        var est = Estimate(anchors, terrain);
        Assert.True(est.Lat < 50.0, $"estimate should be south of the anchor line, got {est.Lat:F4}");
    }

    // ── Expert mode: link-budget adjustment ──────────────────────────────────

    [Fact]
    public void Reference_link_budget_is_a_noop()
    {
        Assert.Equal(-100, AdjustedRssi(-100,
            ReferenceTxDbm, ReferenceAntennaDbi, ReferenceAntennaDbi), precision: 6);
    }

    [Fact]
    public void Stronger_transmitter_yields_larger_distance()
    {
        // TX end 30 dBm + 3 dBi vs reference 22 dBm + 1 dBi = 10 dB hotter link
        // → the same measured RSSI means a farther node
        double adjusted = AdjustedRssi(-100, 30, 3, ReferenceAntennaDbi);
        Assert.Equal(-110, adjusted, precision: 6);
        Assert.True(SignalDistanceKm(adjusted) > SignalDistanceKm(-100));
    }

    [Fact]
    public void Rx_antenna_gain_counts_like_tx_gain()
    {
        // 7 dBi on the RECEIVING end hears 6 dB better than the 1 dBi reference
        Assert.Equal(-106, AdjustedRssi(-100, 22, 1, 7), precision: 6);
        // the link budget is symmetric: 6 dB more TX antenna gives the same delta
        Assert.Equal(AdjustedRssi(-100, 22, 7, 1), AdjustedRssi(-100, 22, 1, 7), precision: 6);
    }

    [Fact]
    public void Hardware_presets_are_sensible()
    {
        Assert.Equal(22, TxPresetDbm("RAK4631"));
        Assert.Equal(27, TxPresetDbm("RAK_WISMESH_TAP"));
        Assert.Equal(21, TxPresetDbm("HELTEC_V3"));
        Assert.Equal(22, TxPresetDbm("TBEAM"));
        Assert.Equal(20, TxPresetDbm(null));
        Assert.Equal(20, TxPresetDbm("SOME_FUTURE_BOARD"));
    }

    [Fact]
    public void Dbm_to_watts_conversion()
    {
        Assert.Equal(0.5, DbmToWatts(27), precision: 2);
        Assert.Equal(1.0, DbmToWatts(30), precision: 2);
    }

    // ── Position precision (precision_bits) ─────────────────────────────────

    [Fact]
    public void Precision_bits_convert_to_sensible_km_radii()
    {
        Assert.Equal(0, PrecisionBitsToKm(32), precision: 6);
        Assert.Equal(0, PrecisionBitsToKm(0), precision: 6);   // unknown = treated exact
        Assert.InRange(PrecisionBitsToKm(16), 0.3, 0.45);      // ≈ ±0.37 km
        Assert.InRange(PrecisionBitsToKm(13), 2.5, 3.3);       // ≈ ±2.9 km
        Assert.True(PrecisionBitsToKm(11) > PrecisionBitsToKm(13));
    }

    [Fact]
    public void Anchor_position_error_dead_zone_absorbs_truncated_positions()
    {
        (double Lat, double Lon) truth = (50.75, 9.27);
        var exactPos = new (double Lat, double Lon)[] { (50.90, 9.10), (50.60, 9.20), (50.80, 9.55) };

        // Third anchor's reported position is displaced ~3 km (truncation), honest PosErrKm = 4
        var displaced = (Lat: 50.80 + 0.027, Lon: 9.55);
        List<Anchor> Build(double posErr) => new()
        {
            new Anchor(exactPos[0].Lat, exactPos[0].Lon, DistKm(exactPos[0].Lat, exactPos[0].Lon, truth.Lat, truth.Lon)),
            new Anchor(exactPos[1].Lat, exactPos[1].Lon, DistKm(exactPos[1].Lat, exactPos[1].Lon, truth.Lat, truth.Lon)),
            new Anchor(displaced.Lat, displaced.Lon, DistKm(exactPos[2].Lat, exactPos[2].Lon, truth.Lat, truth.Lon), posErr),
        };

        var withErr    = Estimate(Build(4));
        var withoutErr = Estimate(Build(0));

        double errWith    = DistKm(withErr.Lat, withErr.Lon, truth.Lat, truth.Lon);
        double errWithout = DistKm(withoutErr.Lat, withoutErr.Lon, truth.Lat, truth.Lon);
        Assert.True(errWith <= errWithout + 0.3,
            $"dead zone should not worsen the estimate ({errWith:F2} vs {errWithout:F2} km)");
        Assert.True(errWith < 2.5, $"estimate off by {errWith:F2} km despite dead zone");
    }

    [Fact]
    public void Reported_position_prior_pulls_estimate_toward_its_cell()
    {
        // Target reports a coarse position (cell ±3 km); two RF anchors claim distances
        // that contradict it (they would put the node ~15 km away). The prior must pull
        // the estimate decisively toward the reported cell.
        (double Lat, double Lon) reported = (50.75, 9.27);
        var rfOnly = new List<Anchor>
        {
            new(50.90, 9.10, 3.0),
            new(50.88, 9.40, 3.0),
        };
        var withPrior = new List<Anchor>(rfOnly)
        {
            new(reported.Lat, reported.Lon, 0, 3.0, WeightScale: 5), // the prior (hard info)
        };

        double dWithout = DistKm(Estimate(rfOnly).Lat, Estimate(rfOnly).Lon, reported.Lat, reported.Lon);
        var est = Estimate(withPrior);
        double dWith = DistKm(est.Lat, est.Lon, reported.Lat, reported.Lon);

        Assert.True(dWith < dWithout * 0.55,
            $"prior should at least halve the offset ({dWith:F1} vs {dWithout:F1} km)");
        Assert.True(dWith < 8, $"estimate {dWith:F1} km from the reported cell");
    }

    // ── Path-loss self-calibration ──────────────────────────────────────────

    private static List<(double DistKm, double Rssi)> Synth(double p1, double n, params double[] dists)
        => dists.Select(d => (d, p1 - 10 * n * Math.Log10(d))).ToList();

    [Fact]
    public void Calibration_recovers_known_model_parameters()
    {
        // Ground truth: P1km = −95 dBm, n = 3.0; clean samples over a wide spread
        var cal = FitCalibration(Synth(-95, 3.0, 0.5, 1, 2, 3, 5, 8, 12, 20, 30));
        Assert.True(cal.IsFitted);
        Assert.Equal(-95, cal.P1Km, precision: 1);
        Assert.Equal(3.0, cal.Exponent, precision: 2);
    }

    [Fact]
    public void Calibration_flags_noise_without_correlation_as_low_quality()
    {
        // Distance and RSSI uncorrelated (terrain-dominated garbage): the fit is either
        // mathematically unusable (positive slope → rejected) or returned with a LOW R²,
        // which makes the model-choice dialog recommend the default model.
        var rng = new Random(42);
        var samples = Enumerable.Range(0, 24)
            .Select(_ => (DistKm: 0.5 + rng.NextDouble() * 25, Rssi: -115 + rng.NextDouble() * 30))
            .ToList();
        var cal = FitCalibration(samples);
        Assert.True(!cal.IsFitted || cal.R2 < 0.2, $"garbage fit not flagged (R²={cal.R2:F2})");
    }

    [Fact]
    public void Clean_fit_reports_high_r2()
    {
        var cal = FitCalibration(Synth(-95, 3.0, 0.5, 1, 2, 3, 5, 8, 12, 20, 30));
        Assert.True(cal.IsFitted);
        Assert.True(cal.R2 > 0.95);
    }

    [Fact]
    public void Calibration_falls_back_on_thin_or_degenerate_data()
    {
        // Too few samples
        Assert.False(FitCalibration(Synth(-95, 3.0, 1, 2, 5)).IsFitted);
        // No distance spread (all ~5 km)
        Assert.False(FitCalibration(Synth(-95, 3.0, 5, 5.1, 5.2, 4.9, 5.05, 5.15, 4.95, 5.0)).IsFitted);
        // Physically implausible (signal getting STRONGER with distance)
        var inverted = Synth(-95, -2.0, 0.5, 1, 2, 3, 5, 8, 12, 20);
        Assert.False(FitCalibration(inverted).IsFitted);
    }

    [Fact]
    public void Calibration_clamps_extreme_fits_to_sane_rf_ranges()
    {
        // Absurdly steep model (n = 9) → exponent clamped to ≤ 4.5
        var cal = FitCalibration(Synth(-80, 9.0, 0.5, 1, 2, 3, 5, 8, 12, 20));
        Assert.True(cal.IsFitted);
        Assert.InRange(cal.Exponent, 1.8, 4.5);
        Assert.InRange(cal.P1Km, -110, -70);
    }

    [Fact]
    public void Fitted_calibration_changes_distance_estimates()
    {
        var cal = FitCalibration(Synth(-100, 3.0, 0.5, 1, 2, 3, 5, 8, 12, 20, 30));
        // At RSSI −100 the fitted model says 1 km; the default (−90/2.5) says ~2.5 km
        Assert.Equal(1.0, SignalDistanceKm(-100, cal), precision: 2);
        Assert.True(SignalDistanceKm(-100) > 2.0);
    }
}
