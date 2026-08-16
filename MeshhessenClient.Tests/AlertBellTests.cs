using MeshhessenClient.Helpers;

namespace MeshhessenClient.Tests;

public class AlertBellTests
{
    private const char Bel = (char)7; // ASCII BEL

    // The firmware's external-notification bell fires only when the message payload contains
    // the ASCII BEL control character (0x07) — the bell emoji is display-only. A regression once
    // dropped the 0x07 (and mangled the emoji to "??"), so the alert never triggered hardware.
    [Fact]
    public void AlertPrefix_ContainsAsciiBell()
    {
        Assert.Contains(Bel, EmojiPalette.AlertPrefix);
    }

    [Fact]
    public void AlertPrefix_ContainsBellEmoji_NotMangled()
    {
        Assert.Contains(EmojiPalette.Bell, EmojiPalette.AlertPrefix);
        Assert.DoesNotContain("??", EmojiPalette.AlertPrefix);
    }

    [Fact]
    public void BellChar_IsSingleAsciiBellByte()
    {
        Assert.Equal(Bel.ToString(), EmojiPalette.BellChar);
    }

    [Fact]
    public void Bell_IsTheBellCodepoint()
    {
        Assert.Equal(char.ConvertFromUtf32(0x1F514), EmojiPalette.Bell);
    }
}
