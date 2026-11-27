using System.Globalization;

namespace MeshhessenClient.Models;

/// <summary>
/// Sort keys and filter predicates for the node list that follow what the columns SHOW. Issue #83: the SNR
/// column displays <see cref="NodeInfo.SnrDisplay"/> ("2 Hops", "MQTT", "7,5 / MQTT" or the value) and RSSI shows
/// <see cref="NodeInfo.RssiDisplay"/> (empty for relayed/MQTT nodes), but the sort still used the raw strings —
/// a node showing "3 Hops" was placed by the SNR of its last relayed packet, so the visible order looked random.
/// Both columns use the same groups now: direct link with a value, relayed by hop count, MQTT, unknown.
/// Numbers are parsed culture-tolerant: values are formatted with the thread's culture and the UI may run under
/// another one ("12,5" vs "12.5").
/// </summary>
public static class NodeSortKeys
{
    /// <summary>Parses "12,5", "12.5", "-87", "85%", "7,5 dB", "160,2km", "450m". False for "-", empty, labels.</summary>
    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || text == "-") return false;
        var cleaned = text.Replace("%", "").Replace("dBm", "").Replace("dB", "").Replace("km", "")
                          .Replace("m", "").Replace('−', '-').Trim();
        if (cleaned.Length == 0) return false;
        if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return true;
        if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
        return double.TryParse(cleaned.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Distance in km from the display string ("450m" → 0.45, "12,3km" → 12.3); null when unknown.</summary>
    public static double? DistanceKm(string? distance)
    {
        if (!TryParseNumber(distance, out var v)) return null;
        bool metres = distance!.TrimEnd().EndsWith("m", StringComparison.Ordinal) && !distance.TrimEnd().EndsWith("km", StringComparison.Ordinal);
        return metres ? v / 1000.0 : v;
    }

    /// <summary>"Direct link": our own radio heard the node itself — last packet with 0 hops (unknown hop count
    /// counts as local, like the SNR column does) and not via MQTT. The firmware-2.8 "heard on current LoRa" flag
    /// is NOT a hop criterion (it is set for relayed packets too) and is deliberately not used here.</summary>
    public static bool IsDirectLink(NodeInfo n) => !n.IsViaMqtt && n.HopsToReach is null or 0;

    private static bool HasValue(string? s) => TryParseNumber(s, out var v) && Math.Abs(v) >= 0.01;

    /// <summary>Group shared by the SNR and RSSI columns, in display order: 0 direct link with a value,
    /// 1 relayed ("N Hops"), 2 MQTT with an earlier direct SNR ("x / MQTT"), 3 MQTT only, 4 nothing known.</summary>
    private static int LinkGroup(NodeInfo n, string? value)
    {
        if (n.IsViaMqtt) return n.DirectNeighborSnr.HasValue ? 2 : 3;
        if (n.HopsToReach > 0) return 1;
        return HasValue(value) ? 0 : 4;
    }

    /// <summary>Value inside the group: the measurement itself, or for relayed nodes "fewer hops is better"
    /// (−hops), so a descending sort puts the best link first in every group.</summary>
    private static double LinkKey(NodeInfo n, string? value)
    {
        if (n.IsViaMqtt) return n.DirectNeighborSnr ?? 0;
        if (n.HopsToReach > 0) return -n.HopsToReach.Value;
        return TryParseNumber(value, out var v) ? v : 0;
    }

    public static int SnrGroup(NodeInfo n) => LinkGroup(n, n.Snr);
    public static double SnrKey(NodeInfo n) => LinkKey(n, n.Snr);

    public static int RssiGroup(NodeInfo n) => LinkGroup(n, n.Rssi);
    public static double RssiKey(NodeInfo n) => LinkKey(n, n.Rssi);

    public static int BatteryGroup(NodeInfo n) => TryParseNumber(n.Battery, out _) ? 0 : 1;
    public static double BatteryKey(NodeInfo n) => TryParseNumber(n.Battery, out var v) ? v : 0;

    public static int DistanceGroup(NodeInfo n) => DistanceKm(n.Distance).HasValue ? 0 : 1;
    public static double DistanceKey(NodeInfo n) => DistanceKm(n.Distance) ?? 0;
}
