using System;

namespace MeshhessenClient.Services;

/// <summary>GATT status of the service discovery, decoupled from WinRT for tests.</summary>
public enum BleGattStatus { Success, Unreachable, ProtocolError, AccessDenied }

/// <summary>What the service discovery on a Bluetooth node tells us about the Windows bond.</summary>
public enum BleBondVerdict
{
    /// <summary>The Meshtastic service is there — connect.</summary>
    ServiceFound,
    /// <summary>Windows cannot reach the node right now (off, out of range, rebooting). Says nothing about the bond.</summary>
    Unreachable,
    /// <summary>The node answered, but Windows' stored bond does not fit it any more (service table without the
    /// Meshtastic service, protocol error or access denied on a bonded device). Only the user may decide to unpair.</summary>
    StaleBond,
    /// <summary>Not bonded and no service — a different kind of device, or a node without the Meshtastic service.</summary>
    NoService
}

/// <summary>
/// Decides whether a failed service discovery is a reason to even ask about the Windows pairing. Until 1.7.3.4 the
/// client removed the pairing on its own whenever the discovery failed on a bonded device — including the status
/// "Unreachable", which a node that is rebooting or out of range returns. Inside the reconnect loop that meant: first
/// failed attempt → device gone from the Windows list → every further attempt opened Windows' pairing prompt. Reports
/// from the community ("the client unpairs my node without asking") describe exactly that. Rules now: Unreachable is
/// never a bond problem; a bond is only called stale when the node answered and the answer does not fit the stored
/// bond; and removing it is always the user's decision (the callers ask).
/// </summary>
public static class BleStaleBondClassifier
{
    public static BleBondVerdict Classify(bool isPaired, BleGattStatus status, int serviceCount)
    {
        if (status == BleGattStatus.Success && serviceCount > 0) return BleBondVerdict.ServiceFound;
        if (status == BleGattStatus.Unreachable) return BleBondVerdict.Unreachable;
        if (!isPaired) return BleBondVerdict.NoService;
        return BleBondVerdict.StaleBond;
    }

    /// <summary>English evidence line for the log and the user dialog.</summary>
    public static string DescribeEvidence(BleGattStatus status, int serviceCount, bool cachedTableStale)
    {
        string live = status switch
        {
            BleGattStatus.Success      => $"live service discovery answered with {serviceCount} service(s) but without the Meshtastic service",
            BleGattStatus.ProtocolError => "live service discovery failed with a protocol error on the bonded link",
            BleGattStatus.AccessDenied  => "Windows denied access on the bonded link (stored keys no longer fit the node)",
            _                           => $"live service discovery returned {status}",
        };
        return cachedTableStale
            ? $"Windows' cached service table for this bond has no Meshtastic service; {live}"
            : live;
    }
}

/// <summary>Thrown by the Bluetooth connect when the stored Windows bond no longer fits the node. The caller decides
/// (with the user) whether to remove the pairing; the service never does that on its own.</summary>
public sealed class BleStaleBondException : Exception
{
    public string Evidence { get; }
    public BleStaleBondException(string evidence) : base("Bluetooth pairing looks stale: " + evidence) => Evidence = evidence;
}

/// <summary>Thrown by the Bluetooth connect when Windows reports the node unreachable. Plain retry material.</summary>
public sealed class BleUnreachableException : Exception
{
    public BleUnreachableException(string message) : base(message) { }
}
