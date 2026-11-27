using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeshhessenClient.Services;

/// <summary>Result of one FROMRADIO read, transport-agnostic so the pump can be tested without WinRT.</summary>
public readonly record struct BleReadResult(BleReadOutcome Outcome, byte[]? Data = null, string? Detail = null)
{
    public static BleReadResult Empty => new(BleReadOutcome.Empty);
    public static BleReadResult Of(byte[] data) => new(BleReadOutcome.Data, data);
    public static BleReadResult Failed(string detail) => new(BleReadOutcome.Failed, null, detail);
    public static BleReadResult Unreachable(string detail) => new(BleReadOutcome.Unreachable, null, detail);
}

/// <summary>
/// The one and only FROMRADIO reader of a Bluetooth link. Until 1.7.3.4 the client polled FROMRADIO in a loop with a
/// 1 ms pause for the whole session, with two more readers (FROMNUM handler, after every write) on top. The firmware
/// never meant that: on ESP32 every empty read parks the NimBLE task in a wait loop and wakes the main loop until it
/// reports "nothing there" (~60 such cycles per second at a 15 ms interval); Android and the Python CLI read only after
/// a FROMNUM notification or a write. This pump does the same: it sleeps until something signals it (notify, write,
/// connect), then reads until the node returns an empty packet. A slow safety poll covers a lost notification.
/// Repeated read failures mean the link is gone — the pump says so once and stops; the owner disconnects, which
/// triggers the normal reconnect.
/// </summary>
public sealed class BleReadPump
{
    public const int DefaultMaxConsecutiveFailures = 3;
    /// <summary>Empty reads right after a write (or the initial drain) are retried a few times 100 ms apart, like the
    /// Python CLI does: on nRF52 the config request may not have been processed by the main loop when the first read
    /// arrives, and the firmware then answers "empty" although data is about to appear.</summary>
    public const int EmptyRetriesAfterWrite = 3;
    public static readonly TimeSpan EmptyRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly Func<CancellationToken, Task<BleReadResult>> _readOnce;
    private readonly Action<byte[]> _onData;
    private readonly BleLinkStats _stats;
    private readonly TimeSpan _pollInterval;
    private readonly int _maxConsecutiveFailures;
    private readonly Action<string>? _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private int _pendingTrigger = (int)BleDrainTrigger.Poll;
    private int _consecutiveFailures;
    private int _linkLostRaised;

    /// <summary>Raised once when <see cref="DefaultMaxConsecutiveFailures"/> reads in a row failed. The pump stops after it.</summary>
    public event Action<string>? LinkLost;

    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    public BleReadPump(Func<CancellationToken, Task<BleReadResult>> readOnce, Action<byte[]> onData, BleLinkStats stats,
                       TimeSpan pollInterval, int maxConsecutiveFailures = DefaultMaxConsecutiveFailures,
                       Action<string>? log = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _readOnce = readOnce;
        _onData = onData;
        _stats = stats;
        _pollInterval = pollInterval;
        _maxConsecutiveFailures = Math.Max(1, maxConsecutiveFailures);
        _log = log;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Asks for a drain. Several signals before the pump wakes collapse into one; the strongest trigger wins
    /// (write beats notify beats poll) because it decides how patiently an empty read is retried.</summary>
    public void Signal(BleDrainTrigger trigger)
    {
        int rank = Rank(trigger);
        int cur;
        do
        {
            cur = Volatile.Read(ref _pendingTrigger);
            if (Rank((BleDrainTrigger)cur) >= rank && cur != (int)BleDrainTrigger.Poll) break;
        } while (Interlocked.CompareExchange(ref _pendingTrigger, (int)trigger, cur) != cur);
        try { _signal.Release(); } catch (SemaphoreFullException) { /* already pending */ }
    }

    private static int Rank(BleDrainTrigger t) => t switch
    {
        BleDrainTrigger.Write => 3, BleDrainTrigger.Initial => 3, BleDrainTrigger.Notify => 2, _ => 1
    };

    /// <summary>Runs until cancelled or until the link is declared lost.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                bool signaled = await _signal.WaitAsync(_pollInterval, ct);
                var trigger = BleDrainTrigger.Poll;
                if (signaled)
                    trigger = (BleDrainTrigger)Interlocked.Exchange(ref _pendingTrigger, (int)BleDrainTrigger.Poll);
                if (!await DrainAsync(trigger, ct))
                    return;
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Reads until the node returns an empty packet. False when the link was declared lost.</summary>
    public async Task<bool> DrainAsync(BleDrainTrigger trigger, CancellationToken ct)
    {
        if (Volatile.Read(ref _linkLostRaised) == 1) return false;   // declared dead — no more reads on this link
        _stats.CountDrain(trigger);
        int emptyRetries = trigger is BleDrainTrigger.Write or BleDrainTrigger.Initial ? EmptyRetriesAfterWrite : 0;
        while (!ct.IsCancellationRequested)
        {
            BleReadResult r;
            try { r = await _readOnce(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { r = BleReadResult.Failed($"{ex.GetType().Name}: {ex.Message}"); }

            switch (r.Outcome)
            {
                case BleReadOutcome.Data when r.Data is { Length: > 0 }:
                    Volatile.Write(ref _consecutiveFailures, 0);
                    _stats.CountRead(BleReadOutcome.Data, r.Data.Length);
                    emptyRetries = 0; // data arrived — the next empty read is a real end of queue
                    try { _onData(r.Data); }
                    catch (Exception ex) { _log?.Invoke($"[BLE] DataReceived handler failed: {ex.Message}"); }
                    continue;

                case BleReadOutcome.Data:
                case BleReadOutcome.Empty:
                    Volatile.Write(ref _consecutiveFailures, 0);
                    _stats.CountRead(BleReadOutcome.Empty, 0);
                    if (emptyRetries-- > 0)
                    {
                        await _delay(EmptyRetryDelay, ct);
                        continue;
                    }
                    return true;

                default:
                    _stats.CountRead(r.Outcome, 0);
                    int failures = Interlocked.Increment(ref _consecutiveFailures);
                    _log?.Invoke($"[BLE] Read failed ({failures}/{_maxConsecutiveFailures}): {r.Outcome}{(r.Detail != null ? " — " + r.Detail : "")}");
                    if (failures >= _maxConsecutiveFailures)
                    {
                        if (Interlocked.Exchange(ref _linkLostRaised, 1) == 0)
                            LinkLost?.Invoke($"{failures} consecutive read failures, last: {r.Outcome}{(r.Detail != null ? " (" + r.Detail + ")" : "")}");
                        return false;
                    }
                    return true; // wait for the next trigger or poll instead of hammering a dying link
            }
        }
        return true;
    }
}
