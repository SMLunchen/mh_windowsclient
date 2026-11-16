using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// Auto-reconnect pacing (bug report 2026-11-14: a weak Bluetooth path, reconnect every 5 s, full node-list
/// download each time, drop again — 66 of 140 log entries were the loop itself).
/// </summary>
public class ReconnectBackoffTests
{
    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(2, 5)]     // a reboot needs ~5 s anyway
    [InlineData(3, 15)]    // storm threshold: give the link a breather
    [InlineData(4, 25)]
    [InlineData(7, 55)]
    [InlineData(8, 60)]    // capped
    [InlineData(50, 60)]
    public void Initial_delay_grows_with_recent_drops_and_caps_at_60s(int recentDrops, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ReconnectBackoff.InitialDelay(recentDrops));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(5, 3)]     // five quick tries cover a device reboot
    [InlineData(6, 6)]
    [InlineData(7, 12)]
    [InlineData(8, 24)]
    [InlineData(9, 48)]
    [InlineData(10, 60)]   // capped
    [InlineData(15, 60)]
    public void Retry_delay_is_quick_first_then_doubles_and_caps(int failedAttempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ReconnectBackoff.RetryDelay(failedAttempt));
    }

    [Fact]
    public void Whole_loop_spans_minutes_not_the_old_45_seconds()
    {
        var total = TimeSpan.Zero;
        for (int a = 1; a < ReconnectBackoff.MaxAttempts; a++) total += ReconnectBackoff.RetryDelay(a);
        Assert.InRange(total.TotalMinutes, 6, 8);          // 5×3 + 6+12+24+48 + 5×60 = 405 s
    }
}
