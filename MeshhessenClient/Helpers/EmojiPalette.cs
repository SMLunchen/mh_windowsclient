using System.Linq;

namespace MeshhessenClient.Helpers;

/// <summary>
/// Central emoji definitions, built from Unicode code points so the source stays
/// pure ASCII. A non-UTF-8 file save can therefore never mangle them into "??"
/// (which is exactly what happened to the old inline emoji literals).
/// </summary>
public static class EmojiPalette
{
    private static string Cp(params int[] cps) => string.Concat(cps.Select(char.ConvertFromUtf32));

    /// <summary>32 quick reaction emojis (4 rows × 8) for the tap-back picker.</summary>
    public static readonly string[] Reactions =
    {
        Cp(0x1F44D), Cp(0x1F44E), Cp(0x2764, 0xFE0F), Cp(0x1F602), Cp(0x1F62E), Cp(0x1F622), Cp(0x1F621), Cp(0x1F389),
        Cp(0x1F64F), Cp(0x1F44F), Cp(0x1F525), Cp(0x1F4AF), Cp(0x2705), Cp(0x274C), Cp(0x2B50), Cp(0x2753),
        Cp(0x1F600), Cp(0x1F605), Cp(0x1F60D), Cp(0x1F914), Cp(0x1F60E), Cp(0x1F973), Cp(0x1F634), Cp(0x1F92F),
        Cp(0x1F440), Cp(0x1F4AA), Cp(0x1F680), Cp(0x26A1), Cp(0x1F4CD), Cp(0x1F4E1), Cp(0x1F50B), Cp(0x1F6F0, 0xFE0F),
    };
}
