namespace MeshhessenClient.Services;

/// <summary>
/// Pure, testable math: condenses a node's recent readings of one metric into a single
/// representative value plus a recency confidence (0..1).
///
/// Older readings count less toward the value (exponential decay, <see cref="ValueHalfLifeHours"/>);
/// a node whose newest reading is itself old gets a lower confidence
/// (<see cref="RecencyHalfLifeHours"/>) so a sensor that stopped reporting stops dominating.
/// The value is NOT normalised here — colouring maps the real value to a calibrated scale.
/// </summary>
public static class EnvironmentHeatmapBuilder
{
    public const double ValueHalfLifeHours   = 12.0;
    public const double RecencyHalfLifeHours = 24.0;

    public readonly record struct Reading(double Value, double Recency);

    /// <summary><paramref name="samples"/> = (ageHours ≥ 0, value) of one node's readings.
    /// Returns null when empty.</summary>
    public static Reading? Representative(IReadOnlyList<(double ageHours, double value)> samples)
    {
        if (samples == null || samples.Count == 0) return null;

        double lambdaV = Math.Log(2) / ValueHalfLifeHours;
        double wsum = 0, vsum = 0, newestAge = double.MaxValue;
        foreach (var (age, value) in samples)
        {
            double a = age > 0 ? age : 0;
            double w = Math.Exp(-lambdaV * a);
            wsum += w;
            vsum += w * value;
            if (a < newestAge) newestAge = a;
        }
        if (wsum <= 0) return null;

        double recency = Math.Exp(-(Math.Log(2) / RecencyHalfLifeHours) * newestAge);
        return new Reading(vsum / wsum, Math.Clamp(recency, 0, 1));
    }
}
