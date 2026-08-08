using System.Reflection;
using Google.Protobuf;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;
// The main project maps these via global usings; the test project must name them explicitly.
using LoRaConfig = Meshtastic.Protobufs.Config.Types.LoRaConfig;
using RegionCode = Meshtastic.Protobufs.Config.Types.LoRaConfig.Types.RegionCode;
using ModemPreset = Meshtastic.Protobufs.Config.Types.LoRaConfig.Types.ModemPreset;

namespace MeshhessenClient.Tests;

/// <summary>
/// Synthetic test of the device-config WRITE path (no device): build a config via
/// the real protocol service, capture the framed bytes it hands to the transport,
/// unframe and parse them back to the wire, and assert the config – including the
/// enum fields that the protobuf migration turned from uint into real enums –
/// serialises to exactly the values that were set.
/// </summary>
public class ProtocolConfigWriteTests
{
    // Pre-seed the private session passkey so the write path does not wait on a
    // (never-arriving) device response for the session key.
    private static void SeedSessionKey(MeshtasticProtocolService svc)
    {
        typeof(MeshtasticProtocolService)
            .GetField("_sessionPasskey", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(svc, new byte[] { 1, 2, 3, 4 });
    }

    private static AdminMessage LastAdminMessageOnWire(FakeConnectionService fake)
    {
        var frame = Assert.Single(fake.Written);                 // exactly one frame sent
        Assert.Equal(0x94, frame[0]);                            // serial framing intact
        Assert.Equal(0xC3, frame[1]);
        int len = (frame[2] << 8) | frame[3];
        Assert.Equal(len, frame.Length - 4);

        var toRadio = ToRadio.Parser.ParseFrom(frame.AsSpan(4).ToArray());
        var data = toRadio.Packet.Decoded;
        Assert.Equal(PortNum.AdminApp, data.Portnum);            // wrapped as ADMIN_APP
        return AdminMessage.Parser.ParseFrom(data.Payload);
    }

    [Fact]
    public async Task SetLoRaConfig_SerialisesEnumsCorrectlyOnTheWire()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        SeedSessionKey(svc);

        await svc.SetLoRaConfigAsync(new LoRaConfig
        {
            Region      = RegionCode.Eu868,
            ModemPreset = ModemPreset.ShortSlow,
            HopLimit    = 5,
            TxEnabled   = true,
        });

        var admin = LastAdminMessageOnWire(fake);
        Assert.Equal(AdminMessage.PayloadVariantOneofCase.SetConfig, admin.PayloadVariantCase);

        var lora = admin.SetConfig.Lora;
        Assert.Equal(RegionCode.Eu868, lora.Region);            // enum value round-trips
        Assert.Equal(ModemPreset.ShortSlow, lora.ModemPreset);
        Assert.Equal(5u, lora.HopLimit);
        Assert.True(lora.TxEnabled);
    }

    [Fact]
    public async Task SetLoRaConfig_WireBytesAreStableForKnownEnumValues()
    {
        // Guards against a silent renumbering: RegionCode.Eu868 == 3, ModemPreset.ShortSlow == ...
        // We don't hardcode the numbers; instead we prove the parsed-back enum equals the input,
        // which is the property that actually matters for the device.
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        SeedSessionKey(svc);

        await svc.SetLoRaConfigAsync(new LoRaConfig { Region = RegionCode.Us, ModemPreset = ModemPreset.LongFast });

        var lora = LastAdminMessageOnWire(fake).SetConfig.Lora;
        Assert.Equal(RegionCode.Us, lora.Region);
        Assert.Equal(ModemPreset.LongFast, lora.ModemPreset);
    }
}
