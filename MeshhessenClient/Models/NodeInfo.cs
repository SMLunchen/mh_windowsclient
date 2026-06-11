namespace MeshhessenClient.Models;

public class NodeInfo
{
    public string Name { get; set; } = "Unknown";
    public string ShortName { get; set; } = string.Empty;
    public string LongName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Distance { get; set; } = "-";
    public string Snr { get; set; } = "-";
    public string Rssi { get; set; } = "-";
    public string Battery { get; set; } = "-";
    public string LastSeen { get; set; } = "-";
    public DateTime? LastSeenDateTime { get; set; }
    public uint NodeId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? Altitude { get; set; }
    public float? GroundSpeed { get; set; }  // m/s
    public float? GroundTrack { get; set; }  // degrees 0–360 (0=North, clockwise)
    public string ColorHex { get; set; } = string.Empty;  // Empty = no color, otherwise #RRGGBB
    public string Note { get; set; } = string.Empty;      // User note
    public bool IsPinned { get; set; }                    // Pinned to top of node list
    public bool IsFavorite { get; set; }                  // Marked as favorite (synced with device)
    public string HardwareModel { get; set; } = string.Empty;
    public bool PkiKeyKnown { get; set; }
    public string PkiKeyIcon => PkiKeyKnown ? "🔑" : "";
    public string SignalTrendColor { get; set; } = string.Empty;  // "" | "#4CAF50" | "#FFC107" | "#F44336"

    // Live values for 3-dot node-list indicators (set when packets/telemetry arrive)
    public float? SnrValue     { get; set; }  // most recent rx_snr from this node
    public float? BatteryValue { get; set; }  // most recent battery_level (%) from this node

    // Direct-neighbor tracking (set whenever a 0-hop, non-MQTT packet is received)
    public DateTime? DirectNeighborAt  { get; set; }  // last time seen as 0-hop HF neighbour
    public float?    DirectNeighborSnr { get; set; }  // rx_snr of that last direct packet

    // Routing metadata from most recent received packet
    public bool IsViaMqtt   { get; set; }   // last packet arrived via MQTT
    public int? HopsToReach { get; set; }   // hop count of last received packet (null = unknown)

    // Device role and additional fields from proto
    public string Role { get; set; } = string.Empty;   // Config.DeviceConfig.Role name
    public float? BatteryVoltage { get; set; }          // from DeviceMetrics.voltage

    // Environment telemetry (updated on each EnvironmentMetrics packet)
    public float? Temperature         { get; set; }
    public float? RelativeHumidity    { get; set; }
    public float? BarometricPressure  { get; set; }

    // ── Tile-view computed helpers ──────────────────────────────────────
    public static bool ShowSignalColors { get; set; } = true;
    public static bool FancyColorful    { get; set; } = true;

    public bool IsInfrastructure =>
        Role is "ROUTER" or "ROUTER_CLIENT" or "REPEATER" or "ROUTER_LATE";

    public string TimeAgoDisplay => LastSeenDateTime switch
    {
        null                                                    => "-",
        DateTime dt when (DateTime.Now - dt).TotalSeconds < 60 => "gerade",
        DateTime dt when (DateTime.Now - dt).TotalMinutes < 60 => $"{(int)(DateTime.Now - dt).TotalMinutes} min",
        DateTime dt when (DateTime.Now - dt).TotalHours   < 24 => $"{(int)(DateTime.Now - dt).TotalHours} h",
        DateTime dt                                             => $"{(int)(DateTime.Now - dt).TotalDays} d",
    };

    public string PkiStatusIcon  => PkiKeyKnown ? "🔑" : "🔓";
    public string PkiStatusColor => PkiKeyKnown ? "#4CAF50" : "#FFC107";

    // Tile color: node color wins; SNR only for direct (0-hop) HF contacts
    public string TileColor =>
        !string.IsNullOrEmpty(ColorHex) ? ColorHex :
        (FancyColorful && ShowSignalColors && !IsViaMqtt &&
         HopsToReach is null or 0 && !string.IsNullOrEmpty(SignalQualityColor)) ? SignalQualityColor :
        "#546E7A";

    // Gradient color helpers: red (-20 dB) → yellow (0 dB) → green (+10 dB)
    private static string SignalGradient(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        int r, g, b;
        if (t < 0.5f) { float u = t * 2f;       r=(int)(244+(255-244)*u); g=(int)( 67+(193- 67)*u); b=(int)(54+(7 -54)*u); }
        else           { float u = (t-0.5f)*2f;  r=(int)(255+( 76-255)*u); g=(int)(193+(175-193)*u); b=(int)( 7+(80- 7)*u); }
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public string SnrGradientColor =>
        SnrValue.HasValue
            ? SignalGradient(Math.Clamp((SnrValue.Value + 20f) / 30f, 0f, 1f))
            : "#9E9E9E";

    public string RssiGradientColor
    {
        get
        {
            if (!int.TryParse(Rssi.Replace(" ", ""), out int rssi) || rssi == 0) return "#9E9E9E";
            return SignalGradient(Math.Clamp((rssi + 130f) / 50f, 0f, 1f));
        }
    }

    // True only when we have a confirmed direct 0-hop, non-MQTT contact
    public bool IsDirectContact => HopsToReach is 0 && !IsViaMqtt;

    // Link-quality (SNR/RSSI) is meaningful for our own reception of the node:
    // not relayed (multi-hop) and not via MQTT. Unknown hop count is treated as local.
    public bool ShowLinkQuality => (HopsToReach is null or 0) && !IsViaMqtt;

    public bool HasTelemetry => Temperature.HasValue || RelativeHumidity.HasValue || BarometricPressure.HasValue;
    // BatteryLevel > 100 in Meshtastic firmware signals external power
    public bool IsPowered    => BatteryValue.HasValue && BatteryValue > 100f;
    public bool HasBattery   => BatteryValue.HasValue;
    public bool HasDistance  => Distance != "-" && !string.IsNullOrEmpty(Distance);
    public bool HasAltitude  => Altitude.HasValue && Altitude != 0;
    public bool HasVoltage   => BatteryVoltage.HasValue && BatteryVoltage > 0f;
    public bool HasHops      => HopsToReach.HasValue;
    public bool HasSnr       => Snr != "-" && !string.IsNullOrEmpty(Snr);
    public bool HasRssi      => Rssi != "-" && !string.IsNullOrEmpty(Rssi);

    // Signal quality label for tile hop row
    public string SignalQualityLabel => SnrValue switch
    {
        null   => string.Empty,
        >= 0f  => "Good",
        >= -5f => "Fair",
        _      => "Weak"
    };

    public string AltitudeDisplay =>
        HasAltitude ? $"↑ {Altitude} m" : string.Empty;

    public string RoleDisplay => Role switch
    {
        "CLIENT"         => "Client",
        "CLIENT_MUTE"    => "Client Mute",
        "ROUTER"         => "Router",
        "ROUTER_CLIENT"  => "Router+Client",
        "REPEATER"       => "Repeater",
        "TRACKER"        => "Tracker",
        "SENSOR"         => "Sensor",
        "TAK"            => "TAK",
        "CLIENT_HIDDEN"  => "Hidden",
        "LOST_AND_FOUND" => "L&F",
        "ROUTER_LATE"    => "Router Late",
        "CLIENT_BASE"    => "Base",
        _                => Role,
    };

    // Avatar background: node colour → HF-only signal quality colour → neutral
    // MQTT-forwarded packets are excluded: their RxSnr is the gateway's SNR, not ours
    public string AvatarColor =>
        !string.IsNullOrEmpty(ColorHex) ? ColorHex :
        (ShowSignalColors && !IsViaMqtt && !string.IsNullOrEmpty(SignalQualityColor)) ? SignalQualityColor :
        "#546E7A";

    // Status dot colour based on how recently the node was last heard
    public string LastSeenStatusColor => LastSeenDateTime switch
    {
        null                                                          => "#9E9E9E",
        DateTime dt when (DateTime.Now - dt).TotalMinutes < 15       => "#4CAF50",
        DateTime dt when (DateTime.Now - dt).TotalHours   < 2        => "#8BC34A",
        DateTime dt when (DateTime.Now - dt).TotalHours   < 24       => "#FFC107",
        _                                                             => "#9E9E9E"
    };

    // Truncated ShortName for avatar circle (max 4 chars)
    public string AvatarLabel => ShortName?.Length > 4 ? ShortName[..4] : ShortName ?? "?";

    // SNR display (also shown in list view SNR column):
    //   MQTT + direct HF history → last 0-hop HF SNR + " / MQTT"
    //   MQTT only               → "MQTT"
    //   0-hop direct HF         → SNR value
    //   multi-hop HF            → "N Hops"  (SNR = last link only, not meaningful)
    //   unknown hops            → SNR value  (best effort, no hop data yet)
    public string SnrDisplay
    {
        get
        {
            if (IsViaMqtt)
                return DirectNeighborSnr.HasValue
                    ? $"{DirectNeighborSnr.Value:F1} / MQTT"
                    : "MQTT";
            if (HopsToReach > 0)
                return $"{HopsToReach} Hop{(HopsToReach == 1 ? "" : "s")}";
            return Snr;
        }
    }

    // RSSI shown whenever it's meaningful for OUR link to the node — i.e. not a
    // multi-hop relay and not MQTT. Mirrors SnrDisplay so SNR and RSSI appear
    // together (HopsToReach == null means hop count unknown, still treat as local).
    public string RssiDisplay =>
        (HopsToReach is null or 0) && !IsViaMqtt && HasRssi ? Rssi : string.Empty;

    public string SignalQualityColor => SnrValue switch
    {
        null   => string.Empty,
        >= 5f  => "#4CAF50",
        >= -5f => "#FFC107",
        _      => "#F44336"
    };

    public string BatteryStatusColor => BatteryValue switch
    {
        null    => string.Empty,
        >= 50f  => "#4CAF50",
        >= 20f  => "#FFC107",
        _       => "#F44336"
    };
}
