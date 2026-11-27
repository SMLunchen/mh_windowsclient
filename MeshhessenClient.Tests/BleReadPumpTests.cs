using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// The FROMRADIO reader. Until 1.7.3.4 the client polled the characteristic in a 1 ms loop for the whole session,
/// with two more readers on top; the firmware (and Android, and the Python CLI) expect reads only after a FROMNUM
/// notification or a write, until the node returns an empty packet. These tests pin that contract down without WinRT.
/// </summary>
public class BleReadPumpTests
{
    private static readonly byte[] A = { 1, 2, 3 };
    private static readonly byte[] B = { 4, 5 };

    /// <summary>Scripted FROMRADIO: hands out the queued results, then "empty" forever.</summary>
    private sealed class ScriptedRadio
    {
        public readonly Queue<BleReadResult> Script = new();
        public int Reads;
        public Task<BleReadResult> ReadOnce(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(Script.Count > 0 ? Script.Dequeue() : BleReadResult.Empty);
        }
    }

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task Drain_reads_until_the_node_returns_empty_and_delivers_in_order()
    {
        var radio = new ScriptedRadio();
        radio.Script.Enqueue(BleReadResult.Of(A));
        radio.Script.Enqueue(BleReadResult.Of(B));
        var got = new List<byte[]>();
        var stats = new BleLinkStats();
        var pump = new BleReadPump(radio.ReadOnce, got.Add, stats, TimeSpan.FromSeconds(10), delay: NoDelay);

        Assert.True(await pump.DrainAsync(BleDrainTrigger.Notify, CancellationToken.None));

        Assert.Equal(new[] { A, B }, got);
        Assert.Equal(3, radio.Reads);                   // A, B, empty
        var s = stats.Snapshot(DateTime.UtcNow);
        Assert.Equal(3, s.ReadsTotal);
        Assert.Equal(2, s.ReadsData);
        Assert.Equal(1, s.ReadsEmpty);
        Assert.Equal(5, s.BytesIn);
        Assert.Equal(1, s.DrainsNotify);
    }

    [Fact]
    public async Task Notify_drain_stops_at_the_first_empty_read()
    {
        var radio = new ScriptedRadio();
        var pump = new BleReadPump(radio.ReadOnce, _ => { }, new BleLinkStats(), TimeSpan.FromSeconds(10), delay: NoDelay);

        await pump.DrainAsync(BleDrainTrigger.Notify, CancellationToken.None);
        Assert.Equal(1, radio.Reads);

        await pump.DrainAsync(BleDrainTrigger.Poll, CancellationToken.None);
        Assert.Equal(2, radio.Reads);
    }

    [Fact]
    public async Task Drain_after_a_write_retries_empty_reads_like_the_python_cli()
    {
        // nRF52: the config request may not be processed yet when the first read arrives → "empty" although data
        // is about to appear. Python retries 5× 100 ms; we retry 3×.
        var radio = new ScriptedRadio();
        radio.Script.Enqueue(BleReadResult.Empty);
        radio.Script.Enqueue(BleReadResult.Empty);
        radio.Script.Enqueue(BleReadResult.Of(A));
        var got = new List<byte[]>();
        int delays = 0;
        var pump = new BleReadPump(radio.ReadOnce, got.Add, new BleLinkStats(), TimeSpan.FromSeconds(10),
                                   delay: (_, _) => { delays++; return Task.CompletedTask; });

        await pump.DrainAsync(BleDrainTrigger.Write, CancellationToken.None);

        Assert.Single(got);
        Assert.Equal(4, radio.Reads);                   // empty, empty, A, empty (no retry after data)
        Assert.Equal(2, delays);
    }

    [Fact]
    public async Task Write_drain_gives_up_after_the_retry_budget()
    {
        var radio = new ScriptedRadio();              // empty forever
        var pump = new BleReadPump(radio.ReadOnce, _ => { }, new BleLinkStats(), TimeSpan.FromSeconds(10), delay: NoDelay);

        await pump.DrainAsync(BleDrainTrigger.Write, CancellationToken.None);

        Assert.Equal(1 + BleReadPump.EmptyRetriesAfterWrite, radio.Reads);
    }

    [Fact]
    public async Task Three_consecutive_failures_declare_the_link_lost_exactly_once()
    {
        var radio = new ScriptedRadio();
        radio.Script.Enqueue(BleReadResult.Unreachable("u1"));
        radio.Script.Enqueue(BleReadResult.Failed("f2"));
        radio.Script.Enqueue(BleReadResult.Unreachable("u3"));
        var lost = new List<string>();
        var stats = new BleLinkStats();
        var pump = new BleReadPump(radio.ReadOnce, _ => { }, stats, TimeSpan.FromSeconds(10), delay: NoDelay);
        pump.LinkLost += lost.Add;

        Assert.True(await pump.DrainAsync(BleDrainTrigger.Poll, CancellationToken.None));   // 1st failure: wait for next trigger
        Assert.True(await pump.DrainAsync(BleDrainTrigger.Poll, CancellationToken.None));   // 2nd
        Assert.False(await pump.DrainAsync(BleDrainTrigger.Poll, CancellationToken.None));  // 3rd → lost
        Assert.False(await pump.DrainAsync(BleDrainTrigger.Poll, CancellationToken.None));  // stays lost, no second event

        Assert.Single(lost);
        Assert.Contains("3 consecutive", lost[0]);
        Assert.Equal(3, stats.Snapshot(DateTime.UtcNow).ReadFailures);
    }

    [Fact]
    public async Task A_successful_read_resets_the_failure_counter()
    {
        var radio = new ScriptedRadio();
        radio.Script.Enqueue(BleReadResult.Failed("f1"));
        radio.Script.Enqueue(BleReadResult.Failed("f2"));
        radio.Script.Enqueue(BleReadResult.Of(A));       // recovers
        radio.Script.Enqueue(BleReadResult.Empty);
        radio.Script.Enqueue(BleReadResult.Failed("f3"));
        radio.Script.Enqueue(BleReadResult.Failed("f4"));
        var lost = 0;
        var pump = new BleReadPump(radio.ReadOnce, _ => { }, new BleLinkStats(), TimeSpan.FromSeconds(10), delay: NoDelay);
        pump.LinkLost += _ => lost++;

        for (int i = 0; i < 5; i++)
            await pump.DrainAsync(BleDrainTrigger.Poll, CancellationToken.None);

        Assert.Equal(0, lost);
        Assert.Equal(2, pump.ConsecutiveFailures);
    }

    [Fact]
    public async Task Exceptions_from_the_read_count_as_failures_not_crashes()
    {
        int n = 0;
        Task<BleReadResult> Boom(CancellationToken _) { n++; throw new InvalidOperationException("disposed"); }
        var lost = 0;
        var pump = new BleReadPump(Boom, _ => { }, new BleLinkStats(), TimeSpan.FromSeconds(10), delay: NoDelay);
        pump.LinkLost += _ => lost++;

        for (int i = 0; i < 3; i++)
            await pump.DrainAsync(BleDrainTrigger.Poll, CancellationToken.None);

        Assert.Equal(3, n);
        Assert.Equal(1, lost);
    }

    [Fact]
    public async Task Run_loop_wakes_on_signal_and_polls_on_its_own_when_nothing_is_announced()
    {
        var radio = new ScriptedRadio();
        radio.Script.Enqueue(BleReadResult.Of(A));
        var got = new List<byte[]>();
        var stats = new BleLinkStats();
        var pump = new BleReadPump(radio.ReadOnce, got.Add, stats, TimeSpan.FromMilliseconds(40), delay: NoDelay);
        using var cts = new CancellationTokenSource();
        var run = pump.RunAsync(cts.Token);

        pump.Signal(BleDrainTrigger.Notify);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (got.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.Single(got);

        // No further signal: the safety poll must keep looking on its own.
        while (stats.Snapshot(DateTime.UtcNow).DrainsPoll < 2 && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(stats.Snapshot(DateTime.UtcNow).DrainsPoll >= 2);

        cts.Cancel();
        await run;                                    // cancellation ends the loop without throwing
    }

    [Fact]
    public async Task Signals_before_the_pump_wakes_collapse_into_one_drain_with_the_strongest_trigger()
    {
        var radio = new ScriptedRadio();
        var stats = new BleLinkStats();
        var pump = new BleReadPump(radio.ReadOnce, _ => { }, stats, TimeSpan.FromSeconds(10), delay: NoDelay);
        using var cts = new CancellationTokenSource();

        pump.Signal(BleDrainTrigger.Notify);
        pump.Signal(BleDrainTrigger.Write);           // write beats notify: it brings the empty-read retries
        pump.Signal(BleDrainTrigger.Notify);
        var run = pump.RunAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (radio.Reads < 1 + BleReadPump.EmptyRetriesAfterWrite && DateTime.UtcNow < deadline) await Task.Delay(10);
        await Task.Delay(50);
        cts.Cancel();
        await run;

        var s = stats.Snapshot(DateTime.UtcNow);
        Assert.Equal(1, s.DrainsWrite);
        Assert.Equal(0, s.DrainsNotify);
        Assert.Equal(1 + BleReadPump.EmptyRetriesAfterWrite, radio.Reads);
    }

    [Fact]
    public void Link_stats_log_line_carries_the_numbers_a_bug_report_needs()
    {
        var stats = new BleLinkStats
        {
            ConnectedUtc = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc),
            Mode = BleLinkMode.Gentle, Phase = BleLinkPhase.Steady, NotifySubscribed = true,
            PollInterval = TimeSpan.FromMilliseconds(1500), ConnParamsRequest = "Balanced: Success → released",
            IntervalMs = 45, RssiDbm = -78,
        };
        stats.CountRead(BleReadOutcome.Data, 120);
        stats.CountRead(BleReadOutcome.Empty, 0);
        stats.CountRead(BleReadOutcome.Empty, 0);
        stats.CountRead(BleReadOutcome.Empty, 0);
        stats.CountRead(BleReadOutcome.Unreachable, 0);
        stats.CountNotify();
        stats.CountDrain(BleDrainTrigger.Notify);
        stats.CountDrain(BleDrainTrigger.Poll);
        stats.CountWrite(true);
        stats.CountWrite(false);

        var s = stats.Snapshot(new DateTime(2026, 11, 27, 11, 2, 3, DateTimeKind.Utc));
        Assert.Equal(60, s.EmptyReadPercent);          // 3 of 5
        var line = s.ToLogLine();
        Assert.Contains("up 01:02:03", line);
        Assert.Contains("mode gentle/steady", line);
        Assert.Contains("interval 45 ms", line);
        Assert.Contains("connect RSSI -78 dBm", line);
        Assert.Contains("reads 5 (60 % empty, 1 with data, 1 failed, 120 bytes)", line);
        Assert.Contains("notifies 1 (subscribed: yes)", line);
        Assert.Contains("drains 1 notify / 0 write / 1 poll@1.5s", line);
        Assert.Contains("writes 2 (1 failed)", line);
        Assert.Contains("[Balanced: Success → released]", line);
    }

    [Fact]
    public void Empty_percent_is_zero_before_the_first_read()
    {
        Assert.Equal(0, new BleLinkStats().Snapshot(DateTime.UtcNow).EmptyReadPercent);
    }
}
