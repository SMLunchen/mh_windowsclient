using System.Globalization;
using MeshhessenClient.Models;

namespace MeshhessenClient.Tests;

/// <summary>
/// Issue #83 ("sorting by SNR and RSSI no longer works"): the columns show SnrDisplay / RssiDisplay (hop labels,
/// "MQTT", or the value) while the sort parsed the raw strings. The keys now follow the display, both columns share
/// the same groups, and numbers parse under either decimal separator. The "direct links only" filter uses the
/// same notion of "direct" as the columns.
/// </summary>
public class NodeSortKeysTests
{
    private static NodeInfo Direct(string snr, string rssi = "-87", int? hops = 0) =>
        new() { ShortName = "D", Snr = snr, Rssi = rssi, HopsToReach = hops };

    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("-10,8", -10.8)]
    [InlineData("-87", -87)]
    [InlineData("85%", 85)]
    [InlineData("7,5 dB", 7.5)]
    [InlineData("−6", -6)]          // U+2212 minus
    public void Numbers_parse_under_either_decimal_separator(string text, double expected)
    {
        Assert.True(NodeSortKeys.TryParseNumber(text, out var v));
        Assert.Equal(expected, v, 3);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("")]
    [InlineData("MQTT")]
    [InlineData("2 Hops")]
    public void Labels_and_placeholders_are_not_numbers(string text)
    {
        Assert.False(NodeSortKeys.TryParseNumber(text, out _));
    }

    [Fact]
    public void Numbers_parse_the_same_with_an_english_thread_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.True(NodeSortKeys.TryParseNumber("12,5", out var de));
            Assert.True(NodeSortKeys.TryParseNumber("12.5", out var en));
            Assert.Equal(12.5, de, 3);
            Assert.Equal(12.5, en, 3);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Theory]
    [InlineData("450m", 0.45)]
    [InlineData("12,3km", 12.3)]
    [InlineData("12.3km", 12.3)]
    public void Distance_converts_metres_to_kilometres(string text, double km)
    {
        Assert.Equal(km, NodeSortKeys.DistanceKm(text)!.Value, 3);
    }

    [Fact]
    public void Distance_placeholder_is_unknown()
    {
        Assert.Null(NodeSortKeys.DistanceKm("-"));
    }

    [Fact]
    public void Snr_groups_follow_the_column_display()
    {
        Assert.Equal(0, NodeSortKeys.SnrGroup(Direct("12,5")));                                   // value shown
        Assert.Equal(0, NodeSortKeys.SnrGroup(Direct("12,5", hops: null)));                       // unknown hops = treated as local
        Assert.Equal(1, NodeSortKeys.SnrGroup(Direct("12,5", hops: 2)));                          // column shows "2 Hops"
        Assert.Equal(2, NodeSortKeys.SnrGroup(new NodeInfo { IsViaMqtt = true, DirectNeighborSnr = 7.5f })); // "7,5 / MQTT"
        Assert.Equal(3, NodeSortKeys.SnrGroup(new NodeInfo { IsViaMqtt = true, Snr = "9,0" }));   // "MQTT" — the raw SNR is irrelevant
        Assert.Equal(4, NodeSortKeys.SnrGroup(Direct("-")));
    }

    [Fact]
    public void Descending_snr_sort_puts_the_best_link_first_in_every_group()
    {
        var nodes = new[]
        {
            Direct("3,0"),                                  // direct, weaker
            Direct("12,5"),                                 // direct, strongest
            Direct("20,0", hops: 3),                        // relayed 3 hops (raw SNR high but meaningless)
            Direct("1,0", hops: 1),                         // relayed 1 hop
            new NodeInfo { IsViaMqtt = true, DirectNeighborSnr = 7.5f, ShortName = "M1" },
            new NodeInfo { IsViaMqtt = true, ShortName = "M2" },
            Direct("-"),
        };
        var sorted = nodes.OrderBy(NodeSortKeys.SnrGroup).ThenByDescending(NodeSortKeys.SnrKey).ToList();

        Assert.Equal("12,5", sorted[0].Snr);
        Assert.Equal("3,0", sorted[1].Snr);
        Assert.Equal(1, sorted[2].HopsToReach);             // fewer hops before more hops
        Assert.Equal(3, sorted[3].HopsToReach);
        Assert.Equal("M1", sorted[4].ShortName);
        Assert.Equal("M2", sorted[5].ShortName);
        Assert.Equal("-", sorted[6].Snr);
    }

    [Fact]
    public void Rssi_uses_the_same_groups_as_snr_and_orders_relayed_nodes_by_hops()
    {
        Assert.Equal(0, NodeSortKeys.RssiGroup(Direct("1", "-87")));
        Assert.Equal(0, NodeSortKeys.RssiGroup(Direct("1", "-87", hops: null)));
        Assert.Equal(1, NodeSortKeys.RssiGroup(Direct("1", "-87", hops: 2)));          // RssiDisplay is empty for relayed nodes
        Assert.Equal(3, NodeSortKeys.RssiGroup(new NodeInfo { IsViaMqtt = true, Rssi = "-60" }));
        Assert.Equal(4, NodeSortKeys.RssiGroup(Direct("1", "-")));
        Assert.Equal(-87, NodeSortKeys.RssiKey(Direct("1", "-87")));

        // Screenshot from the report: relayed nodes were ordered by their hidden raw RSSI — now by hop count.
        var relayed = new[] { Direct("1", "-119", hops: 2), Direct("1", "-90", hops: 4), Direct("1", "-125", hops: 1) };
        var sorted = relayed.OrderBy(NodeSortKeys.RssiGroup).ThenByDescending(NodeSortKeys.RssiKey).Select(n => n.HopsToReach).ToList();
        Assert.Equal(new int?[] { 1, 2, 4 }, sorted);
    }

    [Fact]
    public void Direct_link_filter_means_zero_hops_and_not_mqtt_like_the_snr_column()
    {
        Assert.True(NodeSortKeys.IsDirectLink(new NodeInfo { HopsToReach = 0 }));
        Assert.True(NodeSortKeys.IsDirectLink(new NodeInfo { HopsToReach = null }));                 // unknown = local, as SnrDisplay
        Assert.False(NodeSortKeys.IsDirectLink(new NodeInfo { HopsToReach = 0, IsViaMqtt = true }));
        Assert.False(NodeSortKeys.IsDirectLink(new NodeInfo { HopsToReach = 1 }));
        Assert.False(NodeSortKeys.IsDirectLink(new NodeInfo { HopsToReach = 4, HeardOnCurrentLora = true }));   // 📶 is not "0 hops"
        Assert.False(NodeSortKeys.IsDirectLink(new NodeInfo { HopsToReach = 2, DirectNeighborAt = DateTime.Now }));
    }
}
