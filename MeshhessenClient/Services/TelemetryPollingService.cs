using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace MeshhessenClient.Services;

/// <summary>
/// App-wide scheduler that actively polls selected nodes for telemetry they don't
/// broadcast on their own (LocalStats, environment/device metrics, …). Managed from the
/// telemetry dashboard, but runs as long as the app is connected — the collected data
/// feeds the telemetry DB (dashboard widgets, telemetry window, map overlays).
///
/// Mesh etiquette limits (enforced here, not just in the UI): at most
/// <see cref="MaxNodes"/> distinct nodes, LocalStats at most once per hour per node,
/// everything else at most every 30 minutes. One request per scheduler tick (60 s),
/// so polls never burst.
/// </summary>
public sealed class TelemetryPollingService : IDisposable
{
    public static TelemetryPollingService? Instance { get; private set; }

    public const int MaxNodes = 4;

    /// <summary>Pollable request types (names = <see cref="MeshtasticProtocolService.InfoRequestType"/> members).</summary>
    public static readonly string[] AllowedTypes =
        { "LocalStats", "EnvironmentMetrics", "DeviceMetrics", "AirQualityMetrics", "PowerMetrics" };

    public static int MinIntervalMinutes(string type) => type == "LocalStats" ? 60 : 30;

    public sealed class PollEntry
    {
        [JsonPropertyName("nodeId")]          public uint   NodeId { get; set; }
        [JsonPropertyName("type")]            public string Type { get; set; } = "LocalStats";
        [JsonPropertyName("intervalMinutes")] public int    IntervalMinutes { get; set; } = 60;
        // Persisted so a client restart doesn't re-poll everything at once
        [JsonPropertyName("lastSentUtc")]     public DateTime LastSentUtc { get; set; }
    }

    public sealed class PollPlan
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("entries")] public List<PollEntry> Entries { get; set; } = new();
    }

    private static readonly string PlanFile =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "telemetry_polling.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly object _lock = new();
    private PollPlan _plan = new();
    private MeshtasticProtocolService? _protocol;
    private readonly DispatcherTimer _timer;
    private bool _sending;

    public TelemetryPollingService()
    {
        Load();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
        Instance = this;
    }

    /// <summary>Called whenever the main window (re)creates its protocol service.</summary>
    public void AttachProtocol(MeshtasticProtocolService? protocol)
    {
        lock (_lock) _protocol = protocol;
    }

    public PollPlan GetPlanSnapshot()
    {
        lock (_lock)
        {
            return new PollPlan
            {
                Enabled = _plan.Enabled,
                Entries = _plan.Entries.Select(e => new PollEntry
                {
                    NodeId = e.NodeId, Type = e.Type,
                    IntervalMinutes = e.IntervalMinutes, LastSentUtc = e.LastSentUtc,
                }).ToList(),
            };
        }
    }

    /// <summary>Replaces the plan after validating/normalizing it: unknown types dropped,
    /// duplicates (node+type) collapsed, intervals clamped to the per-type minimum, node
    /// cap enforced. Last-sent timestamps of unchanged entries are preserved. Returns an
    /// error key when the node cap is exceeded, otherwise null.</summary>
    public string? TryUpdatePlan(bool enabled, IEnumerable<PollEntry> entries)
    {
        var cleaned = new List<PollEntry>();
        foreach (var e in entries)
        {
            if (e.NodeId == 0 || !AllowedTypes.Contains(e.Type)) continue;
            if (cleaned.Any(x => x.NodeId == e.NodeId && x.Type == e.Type)) continue;
            cleaned.Add(new PollEntry
            {
                NodeId = e.NodeId,
                Type = e.Type,
                IntervalMinutes = Math.Max(e.IntervalMinutes, MinIntervalMinutes(e.Type)),
            });
        }

        if (cleaned.Select(e => e.NodeId).Distinct().Count() > MaxNodes)
            return "StrDashPollErrMaxNodes";

        lock (_lock)
        {
            foreach (var e in cleaned)
            {
                var old = _plan.Entries.FirstOrDefault(x => x.NodeId == e.NodeId && x.Type == e.Type);
                if (old != null) e.LastSentUtc = old.LastSentUtc;
            }
            _plan = new PollPlan { Enabled = enabled, Entries = cleaned };
            Save_NoLock();
        }
        return null;
    }

    private async Task TickAsync()
    {
        if (_sending) return;

        MeshtasticProtocolService? proto;
        PollEntry? due;
        lock (_lock)
        {
            proto = _protocol;
            if (proto == null || !_plan.Enabled || _plan.Entries.Count == 0) return;
            if (proto.GetMyNodeId() == 0) return; // not connected

            var now = DateTime.UtcNow;
            due = _plan.Entries
                .Where(e => now - e.LastSentUtc >= TimeSpan.FromMinutes(e.IntervalMinutes))
                .OrderBy(e => e.LastSentUtc)      // most overdue first; one per tick
                .FirstOrDefault();
            if (due == null) return;
            due.LastSentUtc = now;                // marked even if the send fails → no retry hammering
            Save_NoLock();
        }

        _sending = true;
        try
        {
            var type = Enum.Parse<MeshtasticProtocolService.InfoRequestType>(due.Type);
            await proto.RequestNodeInfoAsync(due.NodeId, type);
            Logger.WriteLine($"[Polling] {due.Type} requested from !{due.NodeId:x8} (every {due.IntervalMinutes} min)");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[Polling] request failed: {ex.Message}");
        }
        finally { _sending = false; }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(PlanFile))
                _plan = JsonSerializer.Deserialize<PollPlan>(File.ReadAllText(PlanFile), JsonOpts) ?? new();
        }
        catch { _plan = new PollPlan(); }
    }

    private void Save_NoLock()
    {
        try { File.WriteAllText(PlanFile, JsonSerializer.Serialize(_plan, JsonOpts)); }
        catch (Exception ex) { Logger.WriteLine($"[Polling] save failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        _timer.Stop();
        if (Instance == this) Instance = null;
    }
}
