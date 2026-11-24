using System.IO;
using System.Text.Json;
using Google.Protobuf;
using Meshtastic.Protobufs;

namespace MeshhessenClient.Services;

/// <summary>A parsed device-configuration backup (the JSON written by <see cref="MeshtasticProtocolService.BuildConfigBackupJson"/>).</summary>
public sealed record ConfigBackup(
    string NodeIdHex,
    string FirmwareVersion,
    DateTime? ExportedAtUtc,
    IReadOnlyList<Config> Configs,
    IReadOnlyList<ModuleConfig> ModuleConfigs,
    IReadOnlyList<Channel> Channels,
    User? Owner)
{
    public bool HasSecurity => Configs.Any(c => c.PayloadVariantCase == Config.PayloadVariantOneofCase.Security);
    public int SectionCount => Configs.Count + ModuleConfigs.Count + Channels.Count + (Owner != null ? 1 : 0);
}

/// <summary>Reads the backup JSON back into protobuf messages. Tolerant of unknown fields so a file
/// written by a newer client (newer protobufs) still restores what this build understands.</summary>
public static class ConfigBackupFile
{
    private static readonly JsonParser Parser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    public static ConfigBackup Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("config", out _))
            throw new InvalidDataException("Not a Meshhessen configuration backup (no \"config\" section)");

        var nodeId = root.TryGetProperty("nodeId", out var n) && n.ValueKind == JsonValueKind.String ? (n.GetString() ?? "") : "";
        var fw = root.TryGetProperty("firmwareVersion", out var f) && f.ValueKind == JsonValueKind.String ? (f.GetString() ?? "") : "";
        DateTime? exported = null;
        if (root.TryGetProperty("exportedAt", out var e) && e.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(e.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            exported = dt.ToUniversalTime();

        var configs = ParseArray(root, "config", Config.Parser);
        var modules = ParseArray(root, "moduleConfig", ModuleConfig.Parser);
        var channels = ParseArray(root, "channels", Channel.Parser);
        User? owner = null;
        if (root.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.Object)
            owner = Parser.Parse<User>(o.GetRawText());

        return new ConfigBackup(nodeId.TrimStart('!'), fw, exported, configs, modules, channels, owner);
    }

    private static List<T> ParseArray<T>(JsonElement root, string name, MessageParser<T> _) where T : IMessage<T>, new()
    {
        var list = new List<T>();
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            list.Add(Parser.Parse<T>(el.GetRawText()));
        }
        return list;
    }

    /// <summary>Short human-readable inventory for the confirmation dialog.</summary>
    public static string Describe(ConfigBackup b)
    {
        var cfg = string.Join(", ", b.Configs.Select(c => c.PayloadVariantCase.ToString()));
        var mod = string.Join(", ", b.ModuleConfigs.Select(m => m.PayloadVariantCase.ToString()));
        var ch = string.Join(", ", b.Channels.OrderBy(c => c.Index).Select(c =>
            $"{c.Index}:{(string.IsNullOrEmpty(c.Settings?.Name) ? c.Role.ToString() : c.Settings!.Name)}"));
        return $"Config ({b.Configs.Count}): {cfg}\nModule ({b.ModuleConfigs.Count}): {mod}\nChannels ({b.Channels.Count}): {ch}" +
               (b.Owner != null ? $"\nOwner: {b.Owner.LongName} ({b.Owner.ShortName})" : "");
    }
}
