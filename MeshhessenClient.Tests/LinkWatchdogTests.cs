using Google.Protobuf;
using Meshtastic.Protobufs;
using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// Bug report 2026-11-09: serial/TCP connections went silent after ~2 h. The firmware closes its client
/// API 15 min after the last ToRadio it received; the client now heartbeats every 5 min and treats
/// missing heartbeat replies as a dead link. These tests pin the watchdog policy and the wire format.
/// </summary>
public class LinkWatchdogTests
{
    private static readonly DateTime T0 = new(2026, 11, 9, 17, 0, 0, DateTimeKind.Utc);
    private static DateTime Min(double m) => T0.AddMinutes(m);

    [Fact]
    public void Device_that_answers_heartbeats_is_stale_after_two_unanswered_probes()
    {
        var w = new LinkWatchdog();
        w.OnFrameReceived(Min(0), false);
        w.OnHeartbeatSent(Min(5));
        w.OnFrameReceived(Min(5.01), isQueueStatus: true);   // reply → device supports probes
        Assert.True(w.DeviceAnswersHeartbeats);

        w.OnHeartbeatSent(Min(10));                          // unanswered
        Assert.False(w.CheckStale(Min(15)));                 // one miss is tolerated
        w.OnHeartbeatSent(Min(15));                          // second unanswered
        Assert.True(w.CheckStale(Min(20)));
        Assert.False(w.CheckStale(Min(25)));                 // reported once per silence

        w.OnFrameReceived(Min(26), false);                   // traffic again → armed anew
        w.OnHeartbeatSent(Min(30));
        w.OnHeartbeatSent(Min(35));
        Assert.True(w.CheckStale(Min(40)));
    }

    [Fact]
    public void Device_that_never_answers_only_counts_long_total_silence()
    {
        var w = new LinkWatchdog();
        w.OnFrameReceived(Min(0), false);
        for (int i = 1; i <= 8; i++) w.OnHeartbeatSent(Min(5 * i));  // 40 min of unanswered probes
        Assert.False(w.DeviceAnswersHeartbeats);
        Assert.False(w.CheckStale(Min(40)));                 // quiet mesh on old firmware is not an error
        Assert.True(w.CheckStale(Min(46)));                  // but 45+ min without any frame is
    }

    [Fact]
    public void QueueStatus_long_after_heartbeat_does_not_count_as_reply()
    {
        var w = new LinkWatchdog();
        w.OnHeartbeatSent(Min(0));
        w.OnFrameReceived(Min(3), isQueueStatus: true);      // > 1 min later: unrelated
        Assert.False(w.DeviceAnswersHeartbeats);
        Assert.False(w.CheckStale(Min(10)));
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var w = new LinkWatchdog();
        w.OnHeartbeatSent(Min(0));
        w.OnFrameReceived(Min(0.1), true);
        w.Reset();
        Assert.Null(w.LastFrameUtc);
        Assert.False(w.DeviceAnswersHeartbeats);
        Assert.False(w.CheckStale(Min(100)));                // nothing received yet → no verdict
    }

    [Fact]
    public async Task Heartbeat_is_a_plain_keepalive_on_the_wire()
    {
        var fake = new FakeConnectionService(ConnectionType.Serial);
        var svc = new MeshtasticProtocolService(fake);
        await svc.SendHeartbeatAsync();

        var frame = Assert.Single(fake.Written);
        Assert.Equal(0x94, frame[0]);
        Assert.Equal(0xC3, frame[1]);
        var toRadio = ToRadio.Parser.ParseFrom(frame.AsSpan(4).ToArray());
        Assert.Equal(ToRadio.PayloadVariantOneofCase.Heartbeat, toRadio.PayloadVariantCase);
        Assert.Equal(0u, toRadio.Heartbeat.Nonce); // nonce 1 would trigger a NodeInfo broadcast on the mesh
    }

    [Fact]
    public void QueueStatus_frame_updates_last_frame_time()
    {
        var fake = new FakeConnectionService(ConnectionType.Serial);
        var svc = new MeshtasticProtocolService(fake);
        Assert.Null(svc.LastFrameUtc);
        var payload = new FromRadio { QueueStatus = new QueueStatus { Free = 10, Maxlen = 16 } }.ToByteArray();
        var frame = new byte[4 + payload.Length];
        frame[0] = 0x94; frame[1] = 0xC3; frame[2] = (byte)(payload.Length >> 8); frame[3] = (byte)(payload.Length & 0xFF);
        Array.Copy(payload, 0, frame, 4, payload.Length);
        fake.Receive(frame);
        Assert.NotNull(svc.LastFrameUtc);
        Assert.True((DateTime.UtcNow - svc.LastFrameUtc!.Value).TotalSeconds < 5);
    }
}
