using MeshhessenClient.Models;

namespace MeshhessenClient.Tests;

/// <summary>
/// RouteDiscovery semantics (firmware TraceRouteModule): snr_towards[i] is how route[i] heard the previous
/// hop, the last entry is the destination's reading; snr_back[i] is how route_back[i] heard the previous hop
/// on the way back, the last entry is what our own radio measured on the reply. The window shows both legs
/// since issue #85 (the return leg was missing).
/// </summary>
public class TraceroutePathTests
{
    private static TracerouteResult Result(uint me, uint dest,
        List<uint>? fwd = null, List<int>? snrTowards = null,
        List<uint>? back = null, List<int>? snrBack = null) => new()
    {
        SourceNodeId = me, DestinationNodeId = dest,
        RouteForward = fwd ?? new(), SnrTowards = snrTowards ?? new(),
        RouteBack = back ?? new(), SnrBack = snrBack ?? new(),
    };

    [Fact]
    public void Direct_trace_has_one_link_each_way()
    {
        var r = Result(1, 2, snrTowards: new() { 41 }, snrBack: new() { -12 });

        Assert.Equal(new[] { new TracerouteLink(1, null), new TracerouteLink(2, 41) }, TraceroutePath.Forward(r));
        Assert.Equal(new[] { new TracerouteLink(2, null), new TracerouteLink(1, -12) }, TraceroutePath.Back(r));
    }

    [Fact]
    public void Relays_get_the_snr_they_heard_the_previous_hop_with()
    {
        var r = Result(1, 2,
            fwd: new() { 3, 4 }, snrTowards: new() { 10, 20, 30 },
            back: new() { 4 },   snrBack:    new() { 5, 6 });

        Assert.Equal(
            new[] { new TracerouteLink(1, null), new TracerouteLink(3, 10), new TracerouteLink(4, 20), new TracerouteLink(2, 30) },
            TraceroutePath.Forward(r));
        Assert.Equal(
            new[] { new TracerouteLink(2, null), new TracerouteLink(4, 5), new TracerouteLink(1, 6) },
            TraceroutePath.Back(r));
    }

    [Fact]
    public void Missing_snrs_stay_null_instead_of_shifting()
    {
        // Pre-2.3 firmware: relays listed, no SNRs at all
        var r = Result(1, 2, fwd: new() { 3 }, back: new() { 4 });

        Assert.Equal(new[] { new TracerouteLink(1, null), new TracerouteLink(3, null), new TracerouteLink(2, null) }, TraceroutePath.Forward(r));
        Assert.Equal(new[] { new TracerouteLink(2, null), new TracerouteLink(4, null), new TracerouteLink(1, null) }, TraceroutePath.Back(r));
    }

    [Fact]
    public void No_return_leg_at_all_gives_an_empty_back_list()
    {
        var r = Result(1, 2, snrTowards: new() { 41 });

        Assert.Empty(TraceroutePath.Back(r));
        Assert.Equal(2, TraceroutePath.Forward(r).Count);
    }

    [Fact]
    public void Only_our_own_reading_on_a_direct_reply_is_the_back_leg()
    {
        // Direct reply: route_back empty, snr_back holds exactly our radio's reading of the reply.
        var r = Result(0x75fb6baa, 0x9c942a1d, snrTowards: new() { 41 }, snrBack: new() { 25 });

        var back = TraceroutePath.Back(r);
        Assert.Equal(2, back.Count);
        Assert.Equal(0x9c942a1du, back[0].NodeId);
        Assert.Null(back[0].SnrScaled);
        Assert.Equal(0x75fb6baau, back[1].NodeId);
        Assert.Equal(25, back[1].SnrScaled);
    }
}
