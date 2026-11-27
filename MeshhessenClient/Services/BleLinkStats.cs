using System;
using System.Threading;

namespace MeshhessenClient.Services;

/// <summary>Which connection-interval policy the Bluetooth link runs with (user setting "schonender Modus").</summary>
public enum BleLinkMode
{
    /// <summary>Fastest interval Windows offers while config and node list load, released afterwards.</summary>
    Fast,
    /// <summary>Balanced interval from the start — slower download, less load on a weak radio path.</summary>
    Gentle
}

/// <summary>Where the link is in its life: setting up, loading config/nodes, or steady traffic.</summary>
public enum BleLinkPhase { Connecting, Init, Steady }

/// <summary>What one FROMRADIO read returned.</summary>
public enum BleReadOutcome
{
    /// <summary>A FromRadio packet.</summary>
    Data,
    /// <summary>Zero bytes: the node has nothing queued.</summary>
    Empty,
    /// <summary>GATT error other than "unreachable" (protocol error, access denied, exception).</summary>
    Failed,
    /// <summary>Windows reports the device as unreachable — the link is probably gone.</summary>
    Unreachable
}

/// <summary>Why a drain (read-until-empty run) was started.</summary>
public enum BleDrainTrigger
{
    /// <summary>Right after connecting, in case packets are already queued.</summary>
    Initial,
    /// <summary>FROMNUM notification from the node.</summary>
    Notify,
    /// <summary>We just wrote to TORADIO; the node answers config requests and admin messages on FROMRADIO.</summary>
    Write,
    /// <summary>Safety poll — nothing announced, we look anyway.</summary>
    Poll
}

/// <summary>
/// Counters for one Bluetooth session. Bug reports about Bluetooth ("less reliable than Android") came without any
/// numbers because the BLE details were only logged with the debug flag on. These counters are always kept, land in
/// the log every few minutes and on disconnect, and feed the header tooltip. Thread-safe for one writer per
/// counter plus any number of readers (Interlocked); the descriptive fields are written by the connect sequence.
/// </summary>
public sealed class BleLinkStats
{
    private long _readsTotal, _readsEmpty, _readsData, _bytesIn, _readFailures;
    private long _notifies, _drainsNotify, _drainsPoll, _drainsWrite, _drainsInitial;
    private long _writes, _writeFailures;

    public DateTime? ConnectedUtc { get; set; }
    public BleLinkMode Mode { get; set; } = BleLinkMode.Fast;
    public BleLinkPhase Phase { get; set; } = BleLinkPhase.Connecting;
    public bool NotifySubscribed { get; set; }
    public TimeSpan PollInterval { get; set; }
    /// <summary>Last preferred-connection-parameters request and what Windows said to it, e.g. "ThroughputOptimized: Success".</summary>
    public string ConnParamsRequest { get; set; } = "";
    /// <summary>Negotiated connection interval in ms (Windows 11 22H2 and newer report it; older Windows: null).</summary>
    public int? IntervalMs { get; set; }
    public int? LinkTimeoutMs { get; set; }
    public int? Latency { get; set; }
    /// <summary>Advertising signal strength heard while connecting; Windows has no RSSI for a connected device.</summary>
    public int? RssiDbm { get; set; }
    public int AdvertisementsHeard { get; set; }

    public void CountRead(BleReadOutcome outcome, int bytes)
    {
        Interlocked.Increment(ref _readsTotal);
        switch (outcome)
        {
            case BleReadOutcome.Data:
                Interlocked.Increment(ref _readsData);
                Interlocked.Add(ref _bytesIn, bytes);
                break;
            case BleReadOutcome.Empty:
                Interlocked.Increment(ref _readsEmpty);
                break;
            default:
                Interlocked.Increment(ref _readFailures);
                break;
        }
    }

    public void CountNotify() => Interlocked.Increment(ref _notifies);

    public void CountDrain(BleDrainTrigger trigger)
    {
        switch (trigger)
        {
            case BleDrainTrigger.Notify:  Interlocked.Increment(ref _drainsNotify);  break;
            case BleDrainTrigger.Write:   Interlocked.Increment(ref _drainsWrite);   break;
            case BleDrainTrigger.Poll:    Interlocked.Increment(ref _drainsPoll);    break;
            default:                      Interlocked.Increment(ref _drainsInitial); break;
        }
    }

    public void CountWrite(bool ok)
    {
        Interlocked.Increment(ref _writes);
        if (!ok) Interlocked.Increment(ref _writeFailures);
    }

    public BleLinkStatsSnapshot Snapshot(DateTime nowUtc) => new(
        Uptime:          ConnectedUtc is { } c ? nowUtc - c : TimeSpan.Zero,
        Mode:            Mode,
        Phase:           Phase,
        NotifySubscribed: NotifySubscribed,
        PollInterval:    PollInterval,
        ConnParamsRequest: ConnParamsRequest,
        IntervalMs:      IntervalMs,
        LinkTimeoutMs:   LinkTimeoutMs,
        Latency:         Latency,
        RssiDbm:         RssiDbm,
        AdvertisementsHeard: AdvertisementsHeard,
        ReadsTotal:      Interlocked.Read(ref _readsTotal),
        ReadsEmpty:      Interlocked.Read(ref _readsEmpty),
        ReadsData:       Interlocked.Read(ref _readsData),
        BytesIn:         Interlocked.Read(ref _bytesIn),
        ReadFailures:    Interlocked.Read(ref _readFailures),
        Notifies:        Interlocked.Read(ref _notifies),
        DrainsNotify:    Interlocked.Read(ref _drainsNotify),
        DrainsWrite:     Interlocked.Read(ref _drainsWrite),
        DrainsPoll:      Interlocked.Read(ref _drainsPoll),
        DrainsInitial:   Interlocked.Read(ref _drainsInitial),
        Writes:          Interlocked.Read(ref _writes),
        WriteFailures:   Interlocked.Read(ref _writeFailures));
}

/// <summary>Immutable copy of <see cref="BleLinkStats"/> for logging and the UI.</summary>
public sealed record BleLinkStatsSnapshot(
    TimeSpan Uptime,
    BleLinkMode Mode,
    BleLinkPhase Phase,
    bool NotifySubscribed,
    TimeSpan PollInterval,
    string ConnParamsRequest,
    int? IntervalMs,
    int? LinkTimeoutMs,
    int? Latency,
    int? RssiDbm,
    int AdvertisementsHeard,
    long ReadsTotal,
    long ReadsEmpty,
    long ReadsData,
    long BytesIn,
    long ReadFailures,
    long Notifies,
    long DrainsNotify,
    long DrainsWrite,
    long DrainsPoll,
    long DrainsInitial,
    long Writes,
    long WriteFailures)
{
    /// <summary>Share of reads that came back empty, 0–100. 0 when nothing was read yet.</summary>
    public int EmptyReadPercent => ReadsTotal == 0 ? 0 : (int)Math.Round(100.0 * ReadsEmpty / ReadsTotal);

    /// <summary>One English log line — the shape a bug report can be grepped for.</summary>
    public string ToLogLine()
    {
        string interval = IntervalMs is { } i ? $"{i} ms" : "n/a";
        string rssi = RssiDbm is { } r ? $"{r} dBm" : "n/a";
        string poll = PollInterval.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return $"[BLE] Link stats: up {Uptime:hh\\:mm\\:ss}, mode {Mode.ToString().ToLowerInvariant()}/{Phase.ToString().ToLowerInvariant()}, " +
               $"interval {interval}, connect RSSI {rssi}, reads {ReadsTotal} ({EmptyReadPercent} % empty, {ReadsData} with data, " +
               $"{ReadFailures} failed, {BytesIn} bytes), notifies {Notifies} (subscribed: {(NotifySubscribed ? "yes" : "no")}), " +
               $"drains {DrainsNotify} notify / {DrainsWrite} write / {DrainsPoll} poll@{poll}s / {DrainsInitial} initial, " +
               $"writes {Writes} ({WriteFailures} failed), params request [{ConnParamsRequest}]";
    }
}
