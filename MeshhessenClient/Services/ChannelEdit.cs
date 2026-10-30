using Meshtastic.Protobufs;

namespace MeshhessenClient.Services;

/// <summary>
/// Builds channel write protos. AdminMessage.set_channel REPLACES the whole channel on
/// the device, so a partial edit must start from the device's own proto and change only
/// the edited fields — a message rebuilt from UI state wiped the channel names (#69).
/// </summary>
public static class ChannelEdit
{
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
