namespace MeshhessenClient.Services;

/// <summary>
/// Capability gating by connected-device firmware version. Version strings look like
/// "2.7.15.abcdef" — only the numeric prefix counts.
/// </summary>
public static class FirmwareCaps
{
    public static Version? Parse(string? firmwareVersion)
    {
        if (string.IsNullOrWhiteSpace(firmwareVersion)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(firmwareVersion.Trim(), @"^(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        int major = int.Parse(m.Groups[1].Value);
        int minor = int.Parse(m.Groups[2].Value);
        int patch = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        return new Version(major, minor, patch);
    }

    public static bool AtLeast(string? firmwareVersion, int major, int minor) =>
        Parse(firmwareVersion) is { } v && (v.Major > major || (v.Major == major && v.Minor >= minor));
}
