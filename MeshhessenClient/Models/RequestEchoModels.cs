using Meshtastic.Protobufs;

namespace MeshhessenClient.Models;

/// <summary>Kind of a directed request another station sent to our node ("Funk-Echo").</summary>
public enum RequestEchoType { NodeInfo, Position, Telemetry, Traceroute, Admin }

/// <summary>Honest "will our node answer?" derivation from LOCAL knowledge only — the
/// firmware's actual reply is invisible to the client (not mirrored to the phone), so this
/// states what the configuration implies, never a confirmed transmission.</summary>
public static class RequestEchoExpectation
{
    /// <summary>Codes stored in request_echo.expected:
    /// "yes" = firmware will answer; "prec:&lt;bits&gt;" = position reply coarsened to the
    /// channel's precision; "no-pos" = no position available, nothing to send;
    /// "sensor" = depends on attached sensors (unknown); "auth" = only with admin
    /// authorization on our node.</summary>
    public static string Compute(RequestEchoType type, string? detail,
        bool hasOwnPosition, uint channelPrecisionBits)
    {
        switch (type)
        {
            case RequestEchoType.NodeInfo:
            case RequestEchoType.Traceroute:
                return "yes";                       // modules always respond
            case RequestEchoType.Position:
                if (!hasOwnPosition) return "no-pos";
                return channelPrecisionBits is > 0 and < 32
                    ? $"prec:{channelPrecisionBits}"
                    : "yes";
            case RequestEchoType.Telemetry:
                return detail is "Device" or "LocalStats" ? "yes" : "sensor";
            case RequestEchoType.Admin:
            default:
                return "auth";
        }
    }
}

/// <summary>A detected incoming request addressed to our node.</summary>
public record RequestEchoEvent(
    uint FromId,
    RequestEchoType Type,
    DateTime TimestampUtc,
    int? Hops,
    bool ViaMqtt,
    uint Channel,
    uint PacketId,
    List<uint>? RouteTowards,  // traceroute only: relays the request travelled through
    string? Detail = null      // telemetry only: requested variant ("Device", "Environment", …)
);

/// <summary>
/// Pure classification of incoming packets into Funk-Echo events. The firmware forwards
/// every received packet to the client (MeshService "Forwarding to phone"), so directed
/// requests reliably reach us — but their auto-responses do not, hence no "answered" state.
/// </summary>
public static class RequestEchoClassifier
{
    /// <summary>Classify a decoded packet; null = not a request aimed at us.</summary>
    public static RequestEchoType? Classify(uint myNodeId, MeshPacket packet)
    {
        if (myNodeId == 0 || packet is null) return null;
        if (packet.To != myNodeId) return null;                       // broadcasts & overheard → out
        if (packet.From == 0 || packet.From == myNodeId) return null; // own/VN-injected packets → out
        if (packet.PayloadVariantCase != MeshPacket.PayloadVariantOneofCase.Decoded) return null;

        var data = packet.Decoded;
        // Replies to OUR requests carry request_id — a request never does.
        if (data.RequestId != 0) return null;

        return (int)data.Portnum switch
        {
            // Classic info requests need the explicit want_response flag (periodic
            // broadcasts never reach here anyway: to == ^all).
            4  when data.WantResponse => RequestEchoType.NodeInfo,
            3  when data.WantResponse => RequestEchoType.Position,
            67 when data.WantResponse => RequestEchoType.Telemetry,
            70 when data.WantResponse => RequestEchoType.Traceroute,
            // Any foreign admin packet aimed at us is noteworthy, flag or not.
            6  => RequestEchoType.Admin,
            _  => null,
        };
    }

    /// <summary>For traceroute requests: the relays the request came through (route towards us).</summary>
    public static List<uint>? ExtractRoute(MeshPacket packet)
    {
        try
        {
            if (packet.PayloadVariantCase != MeshPacket.PayloadVariantOneofCase.Decoded) return null;
            if ((int)packet.Decoded.Portnum != 70) return null;
            var disc = RouteDiscovery.Parser.ParseFrom(packet.Decoded.Payload);
            return disc.Route.Count > 0 ? disc.Route.ToList() : null;
        }
        catch { return null; }
    }

    /// <summary>For telemetry requests: which variant is being asked for. The request
    /// payload is a Telemetry protobuf whose set variant names the wanted metric group
    /// (that's how our own RequestNodeInfoAsync asks, and how the firmware's telemetry
    /// modules decide whether to answer). Null = unknown/empty payload.</summary>
    public static string? ExtractTelemetryVariant(MeshPacket packet)
    {
        try
        {
            if (packet.PayloadVariantCase != MeshPacket.PayloadVariantOneofCase.Decoded) return null;
            if ((int)packet.Decoded.Portnum != 67) return null;
            var t = Telemetry.Parser.ParseFrom(packet.Decoded.Payload);
            return t.VariantCase switch
            {
                Telemetry.VariantOneofCase.DeviceMetrics      => "Device",
                Telemetry.VariantOneofCase.EnvironmentMetrics => "Environment",
                Telemetry.VariantOneofCase.AirQualityMetrics  => "AirQuality",
                Telemetry.VariantOneofCase.PowerMetrics       => "Power",
                Telemetry.VariantOneofCase.LocalStats         => "LocalStats",
                Telemetry.VariantOneofCase.HostMetrics        => "Host",
                Telemetry.VariantOneofCase.HealthMetrics      => "Health",
                _ => null,
            };
        }
        catch { return null; }
    }

    /// <summary>Hop count from the packet header (null = unknown, same rule as packet_rx).</summary>
    public static int? HopsOf(MeshPacket packet) =>
        (packet.HopStart == 0 || packet.HopLimit > packet.HopStart)
            ? null
            : (int)(packet.HopStart - packet.HopLimit);
}
