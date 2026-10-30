using System.Reflection;
using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;
// The main project maps these via global usings; the test project must name them explicitly.
using ChannelRole = Meshtastic.Protobufs.Channel.Types.Role;

namespace MeshhessenClient.Tests;

/// <summary>
/// Regression tests for issue #69: saving uplink/downlink/position precision in the node
/// configuration wiped channel names on the device, because the set_channel write was
/// rebuilt from UI state instead of cloning the device's own channel proto. set_channel
/// REPLACES the whole channel — every field left out of the write is erased.
/// </summary>
public class ChannelEditTests
{
    private static Channel DeviceChannel() => new()
    {
        Index = 2,
        Role  = ChannelRole.Secondary,
        Settings = new ChannelSettings
        {
            Name            = "mesh hessen",
            Id              = 1234,
            Psk             = ByteString.CopyFrom(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }),
            UplinkEnabled   = false,
            DownlinkEnabled = false,
            ModuleSettings  = new ModuleSettings { PositionPrecision = 32 },
        },
    };

    [Fact]
    public void Edit_preserves_name_psk_id_and_mute()
    {
        var raw = DeviceChannel();

        var ch = ChannelEdit.WithUplinkDownlinkPrecision(raw, uplink: true, downlink: true, positionPrecision: 13);

        // Edited fields applied
        Assert.True(ch.Settings.UplinkEnabled);
        Assert.True(ch.Settings.DownlinkEnabled);
        Assert.Equal(13u, ch.Settings.ModuleSettings.PositionPrecision);

        // Everything else untouched — this is what #69 lost
        Assert.Equal("mesh hessen", ch.Settings.Name);
        Assert.Equal(1234u, ch.Settings.Id);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, ch.Settings.Psk.ToByteArray());
        Assert.Equal(2, ch.Index);
        Assert.Equal(ChannelRole.Secondary, ch.Role);

        // The source proto is a clone, not mutated in place
        Assert.False(raw.Settings.UplinkEnabled);
        Assert.Equal(32u, raw.Settings.ModuleSettings.PositionPrecision);
    }

    [Fact]
    public void Edit_tolerates_missing_settings_and_module_settings()
    {
        var ch = ChannelEdit.WithUplinkDownlinkPrecision(
            new Channel { Index = 0, Role = ChannelRole.Primary },
            uplink: true, downlink: false, positionPrecision: 16);

        Assert.True(ch.Settings.UplinkEnabled);
        Assert.False(ch.Settings.DownlinkEnabled);
        Assert.Equal(16u, ch.Settings.ModuleSettings.PositionPrecision);
    }

    [Fact]
    public async Task Channel_save_keeps_name_on_the_wire()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);

        // Pre-seed session key and the raw-channel cache as a config download would
        typeof(MeshtasticProtocolService)
            .GetField("_sessionPasskey", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(svc, new byte[] { 1, 2, 3, 4 });
        var temp = (List<Channel>)typeof(MeshtasticProtocolService)
            .GetField("_tempChannels", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc)!;
        temp.Add(DeviceChannel());

        // The NodeConfigWindow save path: raw proto → partial edit → set_channel
        var raw = svc.GetRawChannel(2);
        Assert.NotNull(raw);
        await svc.UpdateChannelUplinkDownlinkAsync(
            ChannelEdit.WithUplinkDownlinkPrecision(raw!, uplink: true, downlink: true, positionPrecision: 13));

        // Unframe and parse what actually went to the device
        var frame = Assert.Single(fake.Written);
        Assert.Equal(0x94, frame[0]);
        Assert.Equal(0xC3, frame[1]);
        var toRadio = ToRadio.Parser.ParseFrom(frame.AsSpan(4).ToArray());
        var admin = AdminMessage.Parser.ParseFrom(toRadio.Packet.Decoded.Payload);

        Assert.Equal(AdminMessage.PayloadVariantOneofCase.SetChannel, admin.PayloadVariantCase);
        var wire = admin.SetChannel;
        Assert.Equal("mesh hessen", wire.Settings.Name);
        Assert.Equal(1234u, wire.Settings.Id);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, wire.Settings.Psk.ToByteArray());
        Assert.True(wire.Settings.UplinkEnabled);
        Assert.True(wire.Settings.DownlinkEnabled);
        Assert.Equal(13u, wire.Settings.ModuleSettings.PositionPrecision);
    }
}
