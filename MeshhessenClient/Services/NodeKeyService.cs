using System.IO;
using System.Text;

namespace MeshhessenClient.Services;

public record NodeKeyEntry(
    uint NodeId,
    string ShortName,
    string LongName,
    string PublicKeyBase64,
    DateTime FirstSeen,
    DateTime LastSeen
);

public enum NodeKeyStatus { New, Known, Changed }

public class NodeKeyMismatchEventArgs : EventArgs
{
    public uint NodeId { get; init; }
    public string ShortName { get; init; } = "";
    public string OldKeyBase64 { get; init; } = "";
    public string NewKeyBase64 { get; init; } = "";
    /// <summary>Set to true in the handler to accept (overwrite) the new key.</summary>
    public bool Accept { get; set; }
}

public class NodeKeyService
{
    private readonly string _csvPath;
    private readonly Dictionary<uint, NodeKeyEntry> _entries = new();
    private readonly object _lock = new();

    public event EventHandler<NodeKeyMismatchEventArgs>? KeyMismatchDetected;

    public NodeKeyService(string csvPath)
    {
        _csvPath = csvPath;
        LoadAll();
    }

    // ── Read ────────────────────────────────────────────────────────────────

    private void LoadAll()
    {
        if (!File.Exists(_csvPath))
            return;

        try
        {
            foreach (var line in File.ReadAllLines(_csvPath, Encoding.UTF8))
            {
                if (line.StartsWith("NodeId") || string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(';');
                if (parts.Length < 6)
                    continue;

                if (!uint.TryParse(parts[0], out var nodeId))
                    continue;

                var entry = new NodeKeyEntry(
                    nodeId,
                    parts[1],
                    parts[2],
                    parts[3],
                    DateTime.TryParse(parts[4], out var fs) ? fs : DateTime.Now,
                    DateTime.TryParse(parts[5], out var ls) ? ls : DateTime.Now
                );

                _entries[nodeId] = entry;
            }

            Logger.WriteLine($"NodeKeyService: Loaded {_entries.Count} entries from {_csvPath}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"NodeKeyService: Error loading CSV: {ex.Message}");
        }
    }

    // ── Write ────────────────────────────────────────────────────────────────

    private void SaveAll()
    {
        try
        {
            var lines = new List<string> { "NodeId;ShortName;LongName;PublicKeyBase64;FirstSeen;LastSeen" };
            foreach (var e in _entries.Values)
            {
                lines.Add($"{e.NodeId};{e.ShortName};{e.LongName};{e.PublicKeyBase64};{e.FirstSeen:O};{e.LastSeen:O}");
            }
            File.WriteAllLines(_csvPath, lines, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"NodeKeyService: Error saving CSV: {ex.Message}");
        }
    }

    // ── Core logic ──────────────────────────────────────────────────────────

    /// <summary>
    /// Checks the stored key for <paramref name="nodeId"/> against <paramref name="publicKey"/>.
    /// Depending on <paramref name="action"/> the key is updated, a warning is logged, or
    /// a <see cref="KeyMismatchDetected"/> event is raised for the caller to decide.
    /// </summary>
    public NodeKeyStatus CheckAndUpdate(
        uint nodeId,
        string shortName,
        string longName,
        byte[] publicKey,
        PskMismatchAction action)
    {
        if (publicKey == null || publicKey.Length == 0)
            return NodeKeyStatus.Known;

        var newKeyB64 = Convert.ToBase64String(publicKey);

        lock (_lock)
        {
            if (!_entries.TryGetValue(nodeId, out var existing))
            {
                // New entry
                var entry = new NodeKeyEntry(nodeId, shortName, longName, newKeyB64, DateTime.Now, DateTime.Now);
                _entries[nodeId] = entry;
                SaveAll();
                Logger.WriteLine($"NodeKeyService: New key stored for !{nodeId:x8} ({shortName})");
                return NodeKeyStatus.New;
            }

            if (existing.PublicKeyBase64 == newKeyB64)
            {
                // Key unchanged — update LastSeen + names
                _entries[nodeId] = existing with
                {
                    ShortName = shortName,
                    LongName = longName,
                    LastSeen = DateTime.Now
                };
                SaveAll();
                return NodeKeyStatus.Known;
            }

            // Key changed!
            var oldKey = existing.PublicKeyBase64;
            Logger.WriteLine($"NodeKeyService: Key CHANGED for !{nodeId:x8} ({shortName}) — action={action}");

            switch (action)
            {
                case PskMismatchAction.Overwrite:
                    _entries[nodeId] = existing with
                    {
                        ShortName = shortName,
                        LongName = longName,
                        PublicKeyBase64 = newKeyB64,
                        LastSeen = DateTime.Now
                    };
                    SaveAll();
                    break;

                case PskMismatchAction.Warn:
                    // Only log, do not update
                    Logger.WriteLine($"  OLD: {oldKey}");
                    Logger.WriteLine($"  NEW: {newKeyB64}");
                    // Update LastSeen + names but keep old key
                    _entries[nodeId] = existing with
                    {
                        ShortName = shortName,
                        LongName = longName,
                        LastSeen = DateTime.Now
                    };
                    SaveAll();
                    break;

                case PskMismatchAction.Ask:
                    var args = new NodeKeyMismatchEventArgs
                    {
                        NodeId = nodeId,
                        ShortName = shortName,
                        OldKeyBase64 = oldKey,
                        NewKeyBase64 = newKeyB64
                    };
                    KeyMismatchDetected?.Invoke(this, args);
                    if (args.Accept)
                    {
                        _entries[nodeId] = existing with
                        {
                            ShortName = shortName,
                            LongName = longName,
                            PublicKeyBase64 = newKeyB64,
                            LastSeen = DateTime.Now
                        };
                    }
                    else
                    {
                        _entries[nodeId] = existing with
                        {
                            ShortName = shortName,
                            LongName = longName,
                            LastSeen = DateTime.Now
                        };
                    }
                    SaveAll();
                    break;
            }

            return NodeKeyStatus.Changed;
        }
    }

    /// <summary>Returns the stored public key (Base64) for a node, or null if not found.</summary>
    public string? GetPublicKey(uint nodeId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(nodeId, out var e) ? e.PublicKeyBase64 : null;
        }
    }
}
