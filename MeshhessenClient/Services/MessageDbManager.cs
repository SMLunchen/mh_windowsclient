using System.IO;
using MeshhessenClient.Models;

namespace MeshhessenClient.Services;

/// <summary>
/// High-level manager that owns all message DB files.
/// Channel messages  → messages/channel_{index}_{safename}.db  (one per channel)
/// Direct messages   → messages/dm.db  (shared, keyed by partner_id)
/// </summary>
public class MessageDbManager : IDisposable
{
    private readonly string _baseDir;
    private readonly Dictionary<string, MessageDatabaseService> _channelDbs = new();
    private MessageDatabaseService? _dmDb;
    private readonly object _lock = new();

    public MessageDbManager(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(baseDir);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));

    private string ChannelKey(int index, string name) => $"{index}_{SafeName(name)}";

    public string ChannelDbPath(int index, string name) =>
        Path.Combine(_baseDir, $"channel_{ChannelKey(index, name)}.db");

    // ── DB access ─────────────────────────────────────────────────────────────

    private MessageDatabaseService GetOrCreateChannelDb(int index, string name)
    {
        var key = ChannelKey(index, name);
        lock (_lock)
        {
            if (!_channelDbs.TryGetValue(key, out var svc))
            {
                svc = new MessageDatabaseService(ChannelDbPath(index, name));
                _channelDbs[key] = svc;
            }
            return svc;
        }
    }

    private MessageDatabaseService GetOrCreateDmDb()
    {
        lock (_lock)
        {
            if (_dmDb == null)
                _dmDb = new MessageDatabaseService(Path.Combine(_baseDir, "dm.db"));
            return _dmDb;
        }
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public void InsertChannelMessage(int channelIndex, string channelName, MessageItem msg)
    {
        try { GetOrCreateChannelDb(channelIndex, channelName).Insert(msg, channelIndex, channelName); }
        catch (Exception ex) { Logger.WriteLine($"[MsgDB] InsertChannel: {ex.Message}"); }
    }

    public void InsertDmMessage(uint partnerId, MessageItem msg)
    {
        try { GetOrCreateDmDb().Insert(msg, channelIndex: 0, channelName: string.Empty, partnerId: partnerId); }
        catch (Exception ex) { Logger.WriteLine($"[MsgDB] InsertDM: {ex.Message}"); }
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>Load last-24h messages from all channel_*.db files in the base directory.</summary>
    public List<MessageDbEntry> LoadAllChannelMessagesSince(long sinceUnixSeconds)
    {
        var result = new List<MessageDbEntry>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(_baseDir, "channel_*.db"))
            {
                // Open or re-use the cached service for this file
                var key = Path.GetFileNameWithoutExtension(path)["channel_".Length..];
                MessageDatabaseService svc;
                lock (_lock)
                {
                    if (!_channelDbs.TryGetValue(key, out svc!))
                    {
                        svc = new MessageDatabaseService(path);
                        _channelDbs[key] = svc;
                    }
                }
                result.AddRange(svc.LoadSince(sinceUnixSeconds));
            }
        }
        catch (Exception ex) { Logger.WriteLine($"[MsgDB] LoadAllChannels: {ex.Message}"); }
        return result;
    }

    public List<MessageDbEntry> LoadChannelMessagesBefore(int channelIndex, string channelName,
        long beforeUnixSeconds, int count)
    {
        try
        {
            // Only query if the file exists (don't create an empty DB just for a read)
            var path = ChannelDbPath(channelIndex, channelName);
            if (!File.Exists(path)) return new();
            return GetOrCreateChannelDb(channelIndex, channelName)
                       .LoadBefore(beforeUnixSeconds, count);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MsgDB] LoadChannelBefore: {ex.Message}");
            return new();
        }
    }

    public List<MessageDbEntry> LoadDmMessagesSince(long sinceUnixSeconds)
    {
        try   { return GetOrCreateDmDb().LoadSince(sinceUnixSeconds); }
        catch (Exception ex) { Logger.WriteLine($"[MsgDB] LoadDmSince: {ex.Message}"); return new(); }
    }

    public List<MessageDbEntry> LoadDmConversationBefore(uint partnerId,
        long beforeUnixSeconds, int count)
    {
        try   { return GetOrCreateDmDb().LoadBefore(beforeUnixSeconds, count, partnerId); }
        catch (Exception ex) { Logger.WriteLine($"[MsgDB] LoadDmBefore: {ex.Message}"); return new(); }
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    /// <summary>Clear one channel DB (optionally only entries older than N days).</summary>
    public void ClearChannelDb(int channelIndex, string channelName, int olderThanDays = 0)
    {
        try
        {
            var path = ChannelDbPath(channelIndex, channelName);
            if (!File.Exists(path)) return;
            var svc = GetOrCreateChannelDb(channelIndex, channelName);
            if (olderThanDays > 0) svc.ClearOlderThan(olderThanDays);
            else                   svc.ClearAll();
        }
        catch (Exception ex) { Logger.WriteLine($"[MsgDB] ClearChannel: {ex.Message}"); }
    }

    /// <summary>Clear all channel DBs in the base directory.</summary>
    public void ClearAllChannelDbs(int olderThanDays = 0)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(_baseDir, "channel_*.db"))
            {
                var key = Path.GetFileNameWithoutExtension(path)["channel_".Length..];
                MessageDatabaseService svc;
                lock (_lock)
                {
                    if (!_channelDbs.TryGetValue(key, out svc!))
                    {
                        svc = new MessageDatabaseService(path);
                        _channelDbs[key] = svc;
                    }
                }
                if (olderThanDays > 0) svc.ClearOlderThan(olderThanDays);
                else                   svc.ClearAll();
            }
        }
        catch (Exception ex) { Logger.WriteLine($"[MsgDB] ClearAllChannels: {ex.Message}"); }
    }

    public void ClearDmConversation(uint partnerId)
    {
        try   { GetOrCreateDmDb().ClearAll(partnerId); }
        catch (Exception ex) { Logger.WriteLine($"[MsgDB] ClearDm: {ex.Message}"); }
    }

    public void ClearAllDms()
    {
        try   { GetOrCreateDmDb().ClearAll(); }
        catch (Exception ex) { Logger.WriteLine($"[MsgDB] ClearAllDms: {ex.Message}"); }
    }

    /// <summary>Apply retention policy: delete all messages older than N days (0 = skip).</summary>
    public void ApplyRetention(int days)
    {
        if (days <= 0) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(_baseDir, "channel_*.db"))
            {
                var key = Path.GetFileNameWithoutExtension(path)["channel_".Length..];
                MessageDatabaseService svc;
                lock (_lock)
                {
                    if (!_channelDbs.TryGetValue(key, out svc!))
                    {
                        svc = new MessageDatabaseService(path);
                        _channelDbs[key] = svc;
                    }
                }
                svc.ClearOlderThan(days);
            }
            if (File.Exists(Path.Combine(_baseDir, "dm.db")))
                GetOrCreateDmDb().ClearOlderThan(days);
        }
        catch (Exception ex) { Logger.WriteLine($"[MsgDB] ApplyRetention: {ex.Message}"); }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var svc in _channelDbs.Values) svc.Dispose();
            _channelDbs.Clear();
            _dmDb?.Dispose();
            _dmDb = null;
        }
    }
}
