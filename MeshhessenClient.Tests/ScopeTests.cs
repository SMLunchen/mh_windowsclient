using Google.Protobuf;
using MeshhessenClient.Models;
using Meshtastic.Protobufs;

namespace MeshhessenClient.Tests;

/// <summary>Situation map ("Lagebild"): packet observations fed to it and the fixed-scale projection.</summary>
public class ScopeTests
{
    private const uint Me = 0x75fb6baa, Peer = 0x9c942a1d, R1 = 0x11111111, R2 = 0x22222222;

    private static MeshPacket Packet(uint from, uint to, PortNum port, ByteString? payload = null,
        bool wantResponse = false, uint requestId = 0, uint hopStart = 3, uint hopLimit = 3) => new()
    {
        From = from, To = to, Id = 42, HopStart = hopStart, HopLimit = hopLimit,
        Decoded = new Data { Portnum = port, Payload = payload ?? ByteString.Empty, WantResponse = wantResponse, RequestId = requestId },
    };

    // ── PacketObservation ────────────────────────────────────────────────────

    [Fact]
    public void Text_message_observation_carries_hops_and_addressing()
    {
        var obs = PacketObservation.From(Packet(Peer, 0xFFFFFFFF, PortNum.TextMessageApp, hopStart: 3, hopLimit: 1), DateTime.UtcNow)!;
        Assert.Equal(Peer, obs.FromId);
        Assert.True(obs.IsBroadcast);
        Assert.Equal(2, obs.Hops);
        Assert.False(obs.IsTraceroute);
        Assert.False(obs.IsRequestTo(Me));
        Assert.Null(obs.Route);
    }

    [Fact]
    public void Traceroute_request_lists_relays_so_far_and_counts_as_request_to_us()
    {
        var disc = new RouteDiscovery { Route = { R1, R2 } };
        var obs = PacketObservation.From(Packet(Peer, Me, PortNum.TracerouteApp, disc.ToByteString(), wantResponse: true), DateTime.UtcNow)!;
        Assert.True(obs.IsTraceroute);
        Assert.False(obs.IsReply);
        Assert.True(obs.IsRequestTo(Me));
        Assert.Equal(new uint[] { R1, R2 }, obs.Route);
        Assert.Null(obs.RouteBack);
    }

    [Fact]
    public void Traceroute_reply_lists_both_legs_and_is_no_request()
    {
        var disc = new RouteDiscovery { Route = { R1 }, RouteBack = { R2 }, SnrTowards = { 10, 20 }, SnrBack = { 5, 6 } };
        var obs = PacketObservation.From(Packet(Peer, Me, PortNum.TracerouteApp, disc.ToByteString(), requestId: 77), DateTime.UtcNow)!;
        Assert.True(obs.IsReply);
        Assert.False(obs.IsRequestTo(Me));
        Assert.Equal(new uint[] { R1 }, obs.Route);
        Assert.Equal(new uint[] { R2 }, obs.RouteBack);
    }

    [Fact]
    public void Admin_packet_to_us_is_a_request_even_without_want_response()
    {
        var obs = PacketObservation.From(Packet(Peer, Me, PortNum.AdminApp), DateTime.UtcNow)!;
        Assert.True(obs.IsRequestTo(Me));
        Assert.False(obs.IsRequestTo(0));   // unknown own id → never
    }

    [Fact]
    public void Undecoded_or_senderless_packets_give_no_observation()
    {
        Assert.Null(PacketObservation.From(new MeshPacket { From = Peer, To = Me, Encrypted = ByteString.CopyFrom(1, 2, 3) }, DateTime.UtcNow));
        Assert.Null(PacketObservation.From(Packet(0, Me, PortNum.TextMessageApp), DateTime.UtcNow));
    }

    [Fact]
    public void Malformed_traceroute_payload_still_yields_an_observation_without_a_path()
    {
        var obs = PacketObservation.From(Packet(Peer, Me, PortNum.TracerouteApp, ByteString.CopyFrom(0xFF, 0xFF, 0xFF)), DateTime.UtcNow);
        Assert.NotNull(obs);
        Assert.Null(obs!.Route);
    }

    // ── ScopeProjection ──────────────────────────────────────────────────────

    private static ScopeProjection Frankfurt(double radiusPx = 250) =>
        new(50.11, 8.68, 70, radiusPx, 300, 300);

    [Fact]
    public void Centre_maps_to_the_scope_centre()
    {
        var p = Frankfurt();
        var (x, y) = p.ToScope(50.11, 8.68);
        Assert.Equal(300, x, 6);
        Assert.Equal(300, y, 6);
    }

    [Fact]
    public void Seventy_km_lands_on_the_rim_and_north_is_up()
    {
        var p = Frankfurt();
        // ~70 km north: 70 / 111.2 degrees of latitude
        var (x, y) = p.ToScope(50.11 + 70 / 111.2, 8.68);
        Assert.Equal(300, x, 1);
        Assert.InRange(300 - y, p.RadiusPx * 0.97, p.RadiusPx * 1.03);   // Mercator stretch at 50° is ~1%
        // ~70 km east
        var (xe, ye) = p.ToScope(50.11, 8.68 + 70 / (111.32 * Math.Cos(50.11 * Math.PI / 180)));
        Assert.Equal(300, ye, 1);
        Assert.InRange(xe - 300, p.RadiusPx * 0.97, p.RadiusPx * 1.03);
    }

    [Fact]
    public void Zoom_is_the_coarsest_level_that_needs_little_upscaling()
    {
        Assert.Equal(8, ScopeProjection.ChooseZoom(50.11, 70, 250, 6, 11, 1.5));   // 1.40× upscale → ok
        Assert.Equal(9, ScopeProjection.ChooseZoom(50.11, 70, 400, 6, 11, 1.5));   // 2.24× would be too blurry
        Assert.Equal(11, ScopeProjection.ChooseZoom(50.11, 70, 5000, 6, 11, 1.5)); // capped
    }

    [Fact]
    public void Tile_range_covers_the_centre_tile_and_tiles_align_with_points()
    {
        var p = Frankfurt();
        var (x0, x1, y0, y1) = p.TileRange();
        var (wx, wy) = ScopeProjection.WorldPixel(50.11, 8.68, p.Zoom);
        int cxTile = (int)(wx / 256), cyTile = (int)(wy / 256);
        Assert.InRange(cxTile, x0, x1);
        Assert.InRange(cyTile, y0, y1);

        // The centre lies inside its tile's drawn rectangle
        var (tx, ty, size) = p.TileRect(cxTile, cyTile);
        Assert.InRange(300, tx, tx + size);
        Assert.InRange(300, ty, ty + size);
    }

    [Fact]
    public void Points_beyond_the_rim_are_clamped_onto_it()
    {
        var p = Frankfurt();
        var (x, y) = p.ToScope(50.11, 10.5);   // ~130 km east
        var (bx, by, outside) = p.ClampToRim(x, y, 10);
        Assert.True(outside);
        Assert.Equal(p.RadiusPx - 10, Math.Sqrt((bx - 300) * (bx - 300) + (by - 300) * (by - 300)), 3);
        Assert.Equal(300, by, 1);

        var inside = p.ClampToRim(320, 290);
        Assert.False(inside.Outside);
        Assert.Equal((320, 290), (inside.X, inside.Y));
    }

    [Fact]
    public void Haversine_distance_is_sane()
    {
        // Frankfurt → Mainz ≈ 32 km
        Assert.InRange(ScopeProjection.DistanceKm(50.11, 8.68, 49.99, 8.27), 30, 34);
        Assert.Equal(0, ScopeProjection.DistanceKm(50.11, 8.68, 50.11, 8.68), 6);
    }
}
