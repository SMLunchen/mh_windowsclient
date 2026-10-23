using MeshhessenClient.Services;
using Xunit;
using ModemPreset = Meshtastic.Protobufs.Config.Types.LoRaConfig.Types.ModemPreset;

namespace MeshhessenClient.Tests;

// Airtime estimation for the Funk-Echo "response airtime" receipt.
public class LoRaAirtimeTests
{
    [Fact]
    public void Preset_table_matches_firmware_values()
    {
        Assert.Equal((11, 250.0, 5), LoRaAirtime.ForPreset(ModemPreset.LongFast));
        Assert.Equal((12, 125.0, 8), LoRaAirtime.ForPreset(ModemPreset.LongSlow));
        Assert.Equal(( 8, 250.0, 5), LoRaAirtime.ForPreset(ModemPreset.ShortSlow));
        Assert.Equal(( 7, 250.0, 5), LoRaAirtime.ForPreset(ModemPreset.ShortFast));
    }

    [Fact]
    public void Airtime_grows_with_payload_and_spreading_factor()
    {
        double small = LoRaAirtime.PacketAirtimeMs(32, 11, 250, 5);
        double big   = LoRaAirtime.PacketAirtimeMs(96, 11, 250, 5);
        Assert.True(big > small);

        double sf7  = LoRaAirtime.PacketAirtimeMs(64, 7, 250, 5);
        double sf11 = LoRaAirtime.PacketAirtimeMs(64, 11, 250, 5);
        Assert.True(sf11 > sf7 * 5, $"SF11 ({sf11:F0} ms) should dwarf SF7 ({sf7:F0} ms)");
    }

    [Fact]
    public void LongFast_64_bytes_lands_in_plausible_range()
    {
        // SF11 / 250 kHz / CR4-5, 16-symbol preamble: a 64-byte packet is a few hundred ms
        double ms = LoRaAirtime.PacketAirtimeMs(64, 11, 250, 5);
        Assert.InRange(ms, 300, 800);
    }

    [Fact]
    public void Low_data_rate_optimization_kicks_in_above_16ms_symbols()
    {
        // SF12 / 125 kHz → 32.8 ms symbols → DE=1 (longer than naive formula without DE)
        double withOpt = LoRaAirtime.PacketAirtimeMs(64, 12, 125, 5);
        Assert.InRange(withOpt, 1500, 5000);
    }

    [Fact]
    public void Response_airtime_counts_only_certain_replies()
    {
        Assert.NotNull(LoRaAirtime.ResponseAirtimeMs("NodeInfo", null, 0, "yes", ModemPreset.LongFast));
        Assert.NotNull(LoRaAirtime.ResponseAirtimeMs("Position", null, 0, "prec:13", ModemPreset.LongFast));
        Assert.Null(LoRaAirtime.ResponseAirtimeMs("Position", null, 0, "no-pos", ModemPreset.LongFast));
        Assert.Null(LoRaAirtime.ResponseAirtimeMs("Telemetry", "Environment", 0, "sensor", ModemPreset.LongFast));
        Assert.Null(LoRaAirtime.ResponseAirtimeMs("Admin", null, 0, "auth", ModemPreset.LongFast));
    }

    [Fact]
    public void Traceroute_response_grows_with_route_length()
    {
        double direct = LoRaAirtime.ResponseAirtimeMs("Traceroute", null, 0, "yes", ModemPreset.LongFast)!.Value;
        double relayed = LoRaAirtime.ResponseAirtimeMs("Traceroute", null, 3, "yes", ModemPreset.LongFast)!.Value;
        Assert.True(relayed > direct);
    }
}
