namespace MeshhessenClient.Services;

/// <summary>
/// LoRa airtime estimation (Semtech SX127x formula) for the Funk-Echo "response airtime"
/// receipt. Preset parameters mirror the firmware's modemPresetToParams() in
/// src/mesh/MeshRadio.h (non-wideLora values, i.e. EU868); preamble is 16 symbols and the
/// over-the-air Meshtastic header is 16 bytes (RadioInterface.h).
/// </summary>
public static class LoRaAirtime
{
    public const int OtaHeaderBytes = 16;        // MESHTASTIC_HEADER_LENGTH
    public const int PreambleSymbols = 16;       // RadioInterface.preambleLength

    /// <summary>SF / bandwidth / coding-rate denominator per modem preset (non-wide LoRa).
    /// Null = unknown preset.</summary>
    public static (int Sf, double BwKhz, int Cr)? ForPreset(ModemPreset preset) => preset switch
    {
        ModemPreset.LongFast     => (11, 250.0, 5),
        ModemPreset.LongSlow     => (12, 125.0, 8),
        ModemPreset.LongModerate => (11, 125.0, 8),
        ModemPreset.MediumSlow   => (10, 250.0, 5),
        ModemPreset.MediumFast   => ( 9, 250.0, 5),
        ModemPreset.ShortSlow    => ( 8, 250.0, 5),
        ModemPreset.ShortFast    => ( 7, 250.0, 5),
        ModemPreset.ShortTurbo   => ( 7, 500.0, 5),
        _ => null,
    };

    /// <summary>Airtime of one LoRa packet in milliseconds (explicit header, CRC on).
    /// Low-data-rate optimization switches on automatically above 16 ms symbol time,
    /// matching common radio drivers.</summary>
    public static double PacketAirtimeMs(int payloadBytes, int sf, double bwKhz, int crDenominator,
        int preambleSymbols = PreambleSymbols)
    {
        double tSymMs = Math.Pow(2, sf) / bwKhz; // bwKhz in kHz → ms per symbol
        bool lowDataRateOpt = tSymMs > 16.0;

        double preambleMs = (preambleSymbols + 4.25) * tSymMs;

        int de = lowDataRateOpt ? 1 : 0;
        int crBits = Math.Clamp(crDenominator, 5, 8) - 4;   // 1..4
        double num = 8 * payloadBytes - 4 * sf + 28 + 16;   // explicit header, CRC on
        double payloadSymbols = 8 + Math.Max(Math.Ceiling(num / (4 * (sf - 2 * de))) * (crBits + 4), 0);

        return preambleMs + payloadSymbols * tSymMs;
    }

    /// <summary>Rough OTA size (bytes incl. 16-byte header) of the firmware's auto-response
    /// to a given request type — deliberately conservative averages, used only for the
    /// "≈ response airtime" receipt.</summary>
    public static int EstimateResponseBytes(string type, string? detail, int routeHops) => type switch
    {
        "NodeInfo"   => OtaHeaderBytes + 50,                        // User proto (names, id, key)
        "Position"   => OtaHeaderBytes + 30,
        "Telemetry"  => OtaHeaderBytes + (detail == "LocalStats" ? 45 : 32),
        // RouteDiscovery grows with the path: ids (4 B) + SNR (1 B) per hop, both directions
        "Traceroute" => OtaHeaderBytes + 12 + 6 * Math.Max(routeHops + 1, 1),
        _            => OtaHeaderBytes + 30,
    };

    /// <summary>Estimated airtime of the expected response. Only certain responses count
    /// ("yes" / "prec:…") — sensor-dependent or auth-gated requests return null so the
    /// receipt never overstates what our node transmits.</summary>
    public static double? ResponseAirtimeMs(string type, string? detail, int routeHops,
        string? expectedCode, ModemPreset preset)
    {
        bool certain = expectedCode == "yes" || (expectedCode?.StartsWith("prec:") ?? false);
        if (!certain) return null;
        if (ForPreset(preset) is not { } p) return null;
        int bytes = EstimateResponseBytes(type, detail, routeHops);
        return PacketAirtimeMs(bytes, p.Sf, p.BwKhz, p.Cr);
    }
}
