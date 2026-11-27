using Meshtastic.Protobufs;

namespace MeshhessenClient.Models;

/// <summary>
/// One decoded packet as our node heard it, reduced to what the live situation map ("Lagebild") needs:
/// who sent it to whom, how it travelled, and — for traceroutes — the path recorded in the payload.
/// Raised for every decoded packet from another node once the connection is initialised (the connect
/// backlog is replayed stale and is skipped).
/// </summary>
public sealed record PacketObservation(
    uint FromId,
    uint ToId,
    int Portnum,
    int? Hops,
    bool ViaMqtt,
    DateTime TimestampUtc,
    uint PacketId,
    bool WantResponse,
    uint RequestId,
    IReadOnlyList<uint>? Route,      // traceroute: relays towards the destination (request: so far; reply: full)
    IReadOnlyList<uint>? RouteBack)  // traceroute reply: relays on the way back to the requester
{
    public const uint Broadcast = 0xFFFFFFFF;

    public bool IsBroadcast  => ToId == Broadcast || ToId == 0;
    public bool IsTraceroute => Portnum == 70;
    /// <summary>Replies to a request carry request_id; requests never do.</summary>
    public bool IsReply => RequestId != 0;

    public bool IsAddressedTo(uint myNodeId) => myNodeId != 0 && ToId == myNodeId;

    /// <summary>A request aimed at us that the firmware will answer: the scope plays the sonar ping
    /// (sender → us → sender) for these. Admin packets count whether or not they ask for a reply.</summary>
    public bool IsRequestTo(uint myNodeId) =>
        IsAddressedTo(myNodeId) && !IsReply && (WantResponse || Portnum == 6);

    /// <summary>Build from a decoded mesh packet; null for undecoded packets or ones without a sender.</summary>
    public static PacketObservation? From(MeshPacket packet, DateTime nowUtc)
    {
        if (packet is null || packet.From == 0) return null;
        if (packet.PayloadVariantCase != MeshPacket.PayloadVariantOneofCase.Decoded) return null;
        var d = packet.Decoded;

        IReadOnlyList<uint>? route = null, back = null;
        if ((int)d.Portnum == 70)
        {
            try
            {
                var disc = RouteDiscovery.Parser.ParseFrom(d.Payload);
                if (disc.Route.Count > 0)     route = disc.Route.ToList();
                if (disc.RouteBack.Count > 0) back  = disc.RouteBack.ToList();
            }
            catch { /* malformed payload: no path to draw */ }
        }

        return new PacketObservation(packet.From, packet.To, (int)d.Portnum, RequestEchoClassifier.HopsOf(packet),
            packet.ViaMqtt, nowUtc, packet.Id, d.WantResponse, d.RequestId, route, back);
    }
}
