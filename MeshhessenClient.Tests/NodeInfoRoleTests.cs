using MeshhessenClient.Models;

namespace MeshhessenClient.Tests;

// Role arrives as the C# proto enum name ("ClientMute", "Router"); the display/
// infrastructure logic historically matched SCREAMING_SNAKE. These tests pin the
// normalization that makes both spellings work (GitHub issue #46 rework).
public class NodeInfoRoleTests
{
    [Theory]
    [InlineData("Client",        "Client")]
    [InlineData("CLIENT",        "Client")]
    [InlineData("ClientMute",    "Client Mute")]
    [InlineData("CLIENT_MUTE",   "Client Mute")]
    [InlineData("Router",        "Router")]
    [InlineData("ROUTER",        "Router")]
    [InlineData("RouterClient",  "Router+Client")]
    [InlineData("RouterLate",    "Router Late")]
    [InlineData("ROUTER_LATE",   "Router Late")]
    [InlineData("Repeater",      "Repeater")]
    [InlineData("Tracker",       "Tracker")]
    [InlineData("Sensor",        "Sensor")]
    [InlineData("Tak",           "TAK")]
    [InlineData("ClientHidden",  "Hidden")]
    [InlineData("LostAndFound",  "L&F")]
    [InlineData("ClientBase",    "Client Base")]
    [InlineData("CLIENT_BASE",   "Client Base")]
    public void RoleDisplay_handles_both_enum_name_and_screaming_snake(string role, string expected)
    {
        var node = new NodeInfo { Role = role };
        Assert.Equal(expected, node.RoleDisplay);
    }

    [Fact]
    public void RoleDisplay_empty_role_is_empty()
    {
        Assert.Equal(string.Empty, new NodeInfo().RoleDisplay);
    }

    [Fact]
    public void RoleDisplay_unknown_role_falls_through_to_raw_value()
    {
        // Future firmware roles must still render something sensible (raw, not normalized)
        Assert.Equal("SomeNewRole", new NodeInfo { Role = "SomeNewRole" }.RoleDisplay);
    }

    [Theory]
    [InlineData("Router",       true)]
    [InlineData("ROUTER",       true)]
    [InlineData("RouterClient", true)]
    [InlineData("Repeater",     true)]
    [InlineData("RouterLate",   true)]
    [InlineData("ROUTER_LATE",  true)]
    [InlineData("Client",       false)]
    [InlineData("ClientMute",   false)]
    [InlineData("",             false)]
    public void IsInfrastructure_matches_router_roles_in_both_spellings(string role, bool expected)
    {
        Assert.Equal(expected, new NodeInfo { Role = role }.IsInfrastructure);
    }
}
