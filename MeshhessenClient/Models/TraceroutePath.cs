namespace MeshhessenClient.Models;

/// <summary>One node on a traceroute leg with the SNR of the link INTO it (q4-scaled, null = not reported).</summary>
public readonly record struct TracerouteLink(uint NodeId, int? SnrScaled);

/// <summary>
/// Builds the two legs of a traceroute the way the firmware fills RouteDiscovery (TraceRouteModule::
/// alterReceivedProtobuf): towards the destination every relay appends its id and the SNR it heard the
/// previous hop with, the destination appends only its SNR; on the way back the same happens in
/// route_back/snr_back, and our own node appends the SNR it heard the last hop with (its id is omitted
/// because the reply is addressed to us). So snr_towards[i] belongs to route[i] (last entry: the
/// destination) and snr_back[i] to route_back[i] (last entry: us). The Android app shows both legs
/// ("Route traced toward destination" / "Route traced back to us"); the client showed only the first.
/// </summary>
public static class TraceroutePath
{
    /// <summary>Us → relays → destination.</summary>
    public static List<TracerouteLink> Forward(TracerouteResult r)
    {
        var list = new List<TracerouteLink> { new(r.SourceNodeId, null) };
        for (int i = 0; i < r.RouteForward.Count; i++)
            list.Add(new(r.RouteForward[i], i < r.SnrTowards.Count ? r.SnrTowards[i] : null));
        list.Add(new(r.DestinationNodeId,
            r.SnrTowards.Count > r.RouteForward.Count ? r.SnrTowards[r.RouteForward.Count] : null));
        return list;
    }

    /// <summary>Destination → relays → us. Empty when the reply carries no return leg at all (very old
    /// firmware); with return relays but no SNRs (pre-2.3 firmware) the hops are listed without values.</summary>
    public static List<TracerouteLink> Back(TracerouteResult r)
    {
        if (r.RouteBack.Count == 0 && r.SnrBack.Count == 0) return new();
        var list = new List<TracerouteLink> { new(r.DestinationNodeId, null) };
        for (int i = 0; i < r.RouteBack.Count; i++)
            list.Add(new(r.RouteBack[i], i < r.SnrBack.Count ? r.SnrBack[i] : null));
        list.Add(new(r.SourceNodeId,
            r.SnrBack.Count > r.RouteBack.Count ? r.SnrBack[r.RouteBack.Count] : null));
        return list;
    }
}
