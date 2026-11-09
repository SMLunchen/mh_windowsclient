using System.Reflection;
using MeshhessenClient.Services;
using Meshtastic.Protobufs;
// The main project maps these via global usings; the test project must name them explicitly.
using ModemPreset = Meshtastic.Protobufs.Config.Types.LoRaConfig.Types.ModemPreset;

namespace MeshhessenClient.Tests;

/// <summary>
/// Tests for the protobuf-v2.8 integration round: AEAD channel crypto, UTF-8 name limits
/// with firmware gating, soil/water + lightning telemetry extraction, the completed
/// airtime preset table and the node-status persistence.
/// </summary>
public class ProtobufV28FeatureTests
{
    // ── ChannelAead (AES-CCM, 12-byte tag, firmware CryptoEngine::encryptPacketCCM) ──

    private static readonly byte[] Key32 = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] Plain = System.Text.Encoding.UTF8.GetBytes("Moin Meshhessen! 73");

    [Fact]
    public void Aead_roundtrip_restores_plaintext_and_appends_12_byte_tag()
    {
        var cipher = ChannelAead.Encrypt(Key32, 0x11223344, 0x55667788, 0xABCDEF, Plain);
        Assert.NotNull(cipher);
        Assert.Equal(Plain.Length + ChannelAead.TagSize, cipher!.Length);

        var back = ChannelAead.Decrypt(Key32, 0x11223344, 0x55667788, 0xABCDEF, cipher);
        Assert.Equal(Plain, back);
    }

    [Fact]
    public void Aead_roundtrip_works_with_aes128_key()
    {
        var key16 = Key32.Take(16).ToArray();
        var cipher = ChannelAead.Encrypt(key16, 1, 2, 3, Plain)!;
        Assert.Equal(Plain, ChannelAead.Decrypt(key16, 1, 2, 3, cipher));
    }

    [Fact]
    public void Aead_detects_tampering_and_wrong_context()
    {
        var cipher = ChannelAead.Encrypt(Key32, 0x11223344, 0x55667788, 0xABCDEF, Plain)!;

        var flipped = (byte[])cipher.Clone();
        flipped[3] ^= 0x01;
        Assert.Null(ChannelAead.Decrypt(Key32, 0x11223344, 0x55667788, 0xABCDEF, flipped)); // bit-flip

        Assert.Null(ChannelAead.Decrypt(Key32, 0x11223344, 0x99999999, 0xABCDEF, cipher)); // toNode in AAD
        Assert.Null(ChannelAead.Decrypt(Key32, 0x99999999, 0x55667788, 0xABCDEF, cipher)); // fromNode in nonce+AAD
        Assert.Null(ChannelAead.Decrypt(Key32, 0x11223344, 0x55667788, 0xABCDEE, cipher)); // packetId in nonce
        Assert.Null(ChannelAead.Decrypt(Key32, 1, 2, 3, new byte[ChannelAead.TagSize]));   // too short
    }

    // ── FirmwareCaps (version gating for 2.8 features) ──────────────────────────────

    [Theory]
    [InlineData("2.7.15.abcdef", 2, 7, 15)]
    [InlineData("2.8.0", 2, 8, 0)]
    [InlineData("2.8", 2, 8, 0)]
    public void Firmware_version_parses_numeric_prefix(string fw, int maj, int min, int patch)
    {
        var v = FirmwareCaps.Parse(fw);
        Assert.NotNull(v);
        Assert.Equal((maj, min, patch), (v!.Major, v.Minor, v.Build));
    }

    [Theory]
    [InlineData("2.8.0", true)]
    [InlineData("2.8.2.c0ffee", true)]
    [InlineData("3.0.1", true)]
    [InlineData("2.7.20", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("garbage", false)]
    public void AtLeast_2_8_gates_correctly(string? fw, bool expected)
        => Assert.Equal(expected, FirmwareCaps.AtLeast(fw, 2, 8));

    // ── Utf8Text (byte limits, firmware clamps long names to 24 bytes since 2.8) ────

    [Fact]
    public void Long_name_clamp_cuts_at_24_utf8_bytes_without_splitting_umlauts()
    {
        var name = "Hüttenfunk Südhessen";   // 20 chars, 2× 'ü' à 2 bytes → 22 bytes: fits
        Assert.Equal(22, Utf8Text.ByteCount(name));
        Assert.Equal(name, Utf8Text.Clamp(name, Utf8Text.LongNameMaxBytes));

        // +" e.V." → 27 bytes; the 24-byte cut lands after " e" (2 more ASCII bytes)
        var clamped = Utf8Text.Clamp(name + " e.V.", Utf8Text.LongNameMaxBytes);
        Assert.Equal(name + " e", clamped);
        Assert.Equal(Utf8Text.LongNameMaxBytes, Utf8Text.ByteCount(clamped));
    }

    [Fact]
    public void Short_name_limit_is_4_bytes_so_two_umlauts_fit()
    {
        Assert.Equal("üü", Utf8Text.Clamp("üüü", Utf8Text.ShortNameMaxBytes));
        Assert.Equal("MH73", Utf8Text.Clamp("MH73X", Utf8Text.ShortNameMaxBytes));
    }

    // ── Telemetry extraction: lightning (AS3935) and soil/water probes ──────────────

    [Fact]
    public void Lightning_fields_are_extracted_with_presence_semantics()
    {
        var em = new EnvironmentMetrics { LightningStrikeCount1H = 0, LightningDistanceKm = 12.5f };
        var d = EnvironmentMetricInfo.Extract(em);

        // A reported 0-strike sample is real data (charts must come back down)
        Assert.Equal(0, d["lightning_1h"]);
        Assert.Equal(12.5, d["lightning_km"], 3);

        // Without the sensor nothing is extracted
        Assert.False(EnvironmentMetricInfo.Extract(new EnvironmentMetrics()).ContainsKey("lightning_1h"));
    }

    [Fact]
    public void Soil_water_metrics_extract_by_presence()
    {
        var sw = new SoilWaterMetrics { SoilPh = 6.8f, ElectricalConductivity = 1.2f, Nitrogen = 42f };
        var d = EnvironmentMetricInfo.ExtractSoilWater(sw);

        Assert.Equal(3, d.Count);
        Assert.Equal(6.8, d["soil_ph"], 3);
        Assert.Equal(1.2, d["conductivity"], 3);
        Assert.Equal(42, d["nitrogen"], 3);
        Assert.False(d.ContainsKey("water_ph"));

        // Every extracted key must resolve in the registry (otherwise the insert drops it)
        foreach (var key in EnvironmentMetricInfo.ExtractSoilWater(FullSoilWater()).Keys)
            Assert.NotNull(EnvironmentMetricInfo.ByKey(key));
    }

    private static SoilWaterMetrics FullSoilWater() => new()
    {
        SoilPh = 1, Ph = 2, ElectricalConductivity = 3, Salinity = 4, Nitrogen = 5,
        Phosphorus = 6, Potassium = 7, DissolvedOxygen = 8, Orp = 9,
        ChemicalOxygenDemand = 10, Turbidity = 11, Nitrate = 12, Ammonium = 13,
        BiochemicalOxygenDemand = 14, SolarIrradiance = 15,
    };

    // ── LoRaAirtime: every non-deprecated firmware preset has parameters ────────────

    [Theory]
    [InlineData(ModemPreset.LongTurbo,   11, 500.0, 8)]
    [InlineData(ModemPreset.MediumTurbo,  9, 500.0, 5)]
    [InlineData(ModemPreset.LiteFast,     9, 125.0, 5)]
    [InlineData(ModemPreset.LiteSlow,    10, 125.0, 5)]
    [InlineData(ModemPreset.NarrowFast,   7,  62.5, 6)]
    [InlineData(ModemPreset.NarrowSlow,   8,  62.5, 6)]
    [InlineData(ModemPreset.TinyFast,     7,  15.6, 5)]
    [InlineData(ModemPreset.TinySlow,     8,  15.6, 6)]
    public void New_presets_match_firmware_modemPresetToParams(ModemPreset preset, int sf, double bw, int cr)
    {
        var p = LoRaAirtime.ForPreset(preset);
        Assert.NotNull(p);
        Assert.Equal((sf, bw, cr), p!.Value);
    }

    // ── Authenticated acks (FW 2.8): flag-only bubble display ──────────────────────

    [Fact]
    public void Ack_proof_flags_but_never_blocks_delivery()
    {
        var msg = new MeshhessenClient.Models.MessageItem { IsOwnMessage = true };

        msg.AckProofStatus = 1; // VALID
        msg.Status = MeshhessenClient.Models.SendState.Delivered;
        Assert.Contains("\U0001F6E1", msg.StatusGlyph);          // shield appended
        Assert.Equal("#25D366", msg.StatusColorHex);             // still green

        msg.AckProofStatus = 2; // INVALID (forged)
        Assert.Contains("‼", msg.StatusGlyph);              // ‼ appended
        Assert.Equal("#E53935", msg.StatusColorHex);             // red flag
        Assert.Equal(MeshhessenClient.Models.SendState.Delivered, msg.Status); // delivery untouched

        msg.AckProofStatus = 0; // ABSENT (pre-2.8) — plain double check
        Assert.Equal("✓✓", msg.StatusGlyph);
    }

    [Fact]
    public async Task Set_display_mirror_false_is_still_sent_on_the_wire()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        typeof(MeshtasticProtocolService)
            .GetField("_sessionPasskey", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(svc, new byte[] { 1, 2, 3, 4 });

        // false is meaningful for this verb (stop mirroring) — the oneof must be present
        await svc.SetDisplayMirrorAsync(false);

        var frame = Assert.Single(fake.Written);
        var toRadio = ToRadio.Parser.ParseFrom(frame.AsSpan(4).ToArray());
        var admin = AdminMessage.Parser.ParseFrom(toRadio.Packet.Decoded.Payload);
        Assert.Equal(AdminMessage.PayloadVariantOneofCase.SetDisplayMirror, admin.PayloadVariantCase);
        Assert.False(admin.SetDisplayMirror);
    }

    // ── Radiation display preference (µSv/h, 1 µSv/h = 100 µR/h) ───────────────────

    [Fact]
    public void Radiation_formats_as_microsievert_when_preference_is_set()
    {
        try
        {
            EnvironmentMetricInfo.DisplayRadiationInSievert = false;
            Assert.StartsWith("15", EnvironmentMetricInfo.Format("radiation", 15.0));
            Assert.EndsWith("µR/h", EnvironmentMetricInfo.Format("radiation", 15.0));

            EnvironmentMetricInfo.DisplayRadiationInSievert = true;
            var s = EnvironmentMetricInfo.Format("radiation", 15.0);
            Assert.EndsWith("µSv/h", s);
            Assert.StartsWith(0.15.ToString("F2", System.Globalization.CultureInfo.CurrentCulture), s);

            // Other metrics are untouched by the preference
            Assert.EndsWith("°C", EnvironmentMetricInfo.Format("temperature", 21.0));
        }
        finally
        {
            EnvironmentMetricInfo.DisplayRadiationInSievert = false; // static — reset for other tests
        }
    }

    // ── Node status persistence (StatusMessage module, port 36) ─────────────────────

    [Fact]
    public void Node_status_upsert_get_and_clear_roundtrip()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mh_status_test_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new TelemetryDatabaseService(dbPath);
            db.UpsertNodeStatus(0x100, "Im Urlaub bis 20.11.");
            db.UpsertNodeStatus(0x200, "Solar, Akku schwach");
            db.UpsertNodeStatus(0x100, "Wieder da");       // upsert replaces
            db.UpsertNodeStatus(0x200, "");                 // empty clears

            var statuses = db.GetNodeStatuses();
            Assert.Equal("Wieder da", statuses[0x100]);
            Assert.False(statuses.ContainsKey(0x200));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }
}
