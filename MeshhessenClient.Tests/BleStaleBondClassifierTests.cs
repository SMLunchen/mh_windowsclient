using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// Community reports after 1.7.3 ("the client unpairs my node without asking"): the connect removed the Windows bond
/// whenever the service discovery failed on a bonded device — including "Unreachable", which a rebooting or distant
/// node returns, and it did so inside every reconnect attempt. The classifier is the one place that decides what a
/// failed discovery means; removing a bond is the user's decision in every case.
/// </summary>
public class BleStaleBondClassifierTests
{
    [Fact]
    public void Service_found_wins_regardless_of_pairing()
    {
        Assert.Equal(BleBondVerdict.ServiceFound, BleStaleBondClassifier.Classify(true, BleGattStatus.Success, 1));
        Assert.Equal(BleBondVerdict.ServiceFound, BleStaleBondClassifier.Classify(false, BleGattStatus.Success, 2));
    }

    [Fact]
    public void Unreachable_is_never_a_bond_problem()
    {
        // The exact case of the reconnect storm: node rebooting after a settings save, first attempt 5 s later.
        Assert.Equal(BleBondVerdict.Unreachable, BleStaleBondClassifier.Classify(true, BleGattStatus.Unreachable, 0));
        Assert.Equal(BleBondVerdict.Unreachable, BleStaleBondClassifier.Classify(false, BleGattStatus.Unreachable, 0));
    }

    [Theory]
    [InlineData(BleGattStatus.Success)]        // node answered with a table that lacks the Meshtastic service
    [InlineData(BleGattStatus.ProtocolError)]  // ATT error on the bonded link
    [InlineData(BleGattStatus.AccessDenied)]   // stored keys refused
    public void Paired_device_that_answered_without_our_service_is_a_stale_bond(BleGattStatus status)
    {
        Assert.Equal(BleBondVerdict.StaleBond, BleStaleBondClassifier.Classify(true, status, 0));
    }

    [Theory]
    [InlineData(BleGattStatus.Success)]
    [InlineData(BleGattStatus.ProtocolError)]
    [InlineData(BleGattStatus.AccessDenied)]
    public void Unpaired_device_without_our_service_has_nothing_to_unpair(BleGattStatus status)
    {
        Assert.Equal(BleBondVerdict.NoService, BleStaleBondClassifier.Classify(false, status, 0));
    }

    [Fact]
    public void Evidence_names_the_cached_table_only_when_it_was_stale()
    {
        var withCache = BleStaleBondClassifier.DescribeEvidence(BleGattStatus.Success, 3, cachedTableStale: true);
        Assert.StartsWith("Windows' cached service table", withCache);
        Assert.Contains("3 service(s)", withCache);

        var liveOnly = BleStaleBondClassifier.DescribeEvidence(BleGattStatus.AccessDenied, 0, cachedTableStale: false);
        Assert.DoesNotContain("cached", liveOnly);
        Assert.Contains("denied", liveOnly);
    }

    [Fact]
    public void Exceptions_carry_the_evidence_for_the_user_dialog()
    {
        var ex = new BleStaleBondException("live service discovery failed with a protocol error on the bonded link");
        Assert.Equal("live service discovery failed with a protocol error on the bonded link", ex.Evidence);
        Assert.Contains("stale", ex.Message);
        Assert.IsAssignableFrom<Exception>(new BleUnreachableException("n/a"));
    }
}
