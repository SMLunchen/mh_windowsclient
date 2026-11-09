using Meshtastic.Protobufs;

namespace MeshhessenClient.Services;

/// <summary>
/// Builds channel write protos. AdminMessage.set_channel REPLACES the whole channel on
/// the device, so a partial edit must start from the device's own proto and change only
/// the edited fields — a message rebuilt from UI state wiped the channel names (#69).
/// </summary>
public static class ChannelEdit
{
    /// <summary>Firmware limit: ChannelSettings.name is max_size:12 in nanopb = 11 UTF-8
    /// bytes + NUL. A longer name makes the device reject the whole set_channel write.</summary>
    public const int MaxNameBytes = 11;

    /// <summary>Truncate <paramref name="name"/> to at most <see cref="MaxNameBytes"/> UTF-8
    /// bytes without splitting a character (grapheme clusters stay whole).</summary>
    public static string ClampName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return name ?? "";
        var utf8 = System.Text.Encoding.UTF8;
        if (utf8.GetByteCount(name) <= MaxNameBytes) return name;

        var sb = new System.Text.StringBuilder();
        int bytes = 0;
        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(name);
        while (elements.MoveNext())
        {
            var el = (string)elements.Current;
            int b = utf8.GetByteCount(el);
            if (bytes + b > MaxNameBytes) break;
            sb.Append(el);
            bytes += b;
        }
        return sb.ToString();
    }

    /// <summary>Clone <paramref name="raw"/> and apply only uplink/downlink/position
    /// precision; name, PSK, id and every other settings field stay untouched.</summary>
    public static Channel WithUplinkDownlinkPrecision(Channel raw, bool uplink, bool downlink, uint positionPrecision)
    {
        var ch = raw.Clone();
        ch.Settings ??= new ChannelSettings();
        ch.Settings.UplinkEnabled   = uplink;
        ch.Settings.DownlinkEnabled = downlink;
        ch.Settings.ModuleSettings ??= new ModuleSettings();
        ch.Settings.ModuleSettings.PositionPrecision = positionPrecision;
        return ch;
    }
}
