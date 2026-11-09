namespace MeshhessenClient.Services;

/// <summary>
/// UTF-8 byte-limit helpers for strings written to the device. Protobuf string limits on
/// the wire are BYTES (nanopb max_size), not characters — an umlaut takes 2, an emoji up
/// to 4. Exceeding a limit makes the device reject the whole write (nanopb decode halt).
/// </summary>
public static class Utf8Text
{
    /// <summary>Long-name clamp enforced by firmware since 2.8 (NodeInfoLite max_size:25).</summary>
    public const int LongNameMaxBytes = 24;

    /// <summary>Historical User.long_name limit (max_size:40) still valid on pre-2.8 firmware.</summary>
    public const int LongNameMaxBytesLegacy = 39;

    /// <summary>User.short_name max_size:5 → 4 usable bytes (all firmware versions).</summary>
    public const int ShortNameMaxBytes = 4;

    public static int ByteCount(string? s) =>
        string.IsNullOrEmpty(s) ? 0 : System.Text.Encoding.UTF8.GetByteCount(s);

    /// <summary>Truncate <paramref name="s"/> to at most <paramref name="maxBytes"/> UTF-8
    /// bytes without splitting a character (grapheme clusters stay whole).</summary>
    public static string Clamp(string? s, int maxBytes)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        var utf8 = System.Text.Encoding.UTF8;
        if (utf8.GetByteCount(s) <= maxBytes) return s;

        var sb = new System.Text.StringBuilder();
        int bytes = 0;
        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(s);
        while (elements.MoveNext())
        {
            var el = (string)elements.Current;
            int b = utf8.GetByteCount(el);
            if (bytes + b > maxBytes) break;
            sb.Append(el);
            bytes += b;
        }
        return sb.ToString();
    }
}
