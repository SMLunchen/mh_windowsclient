namespace MeshhessenClient.Models;

// ── Aggregated stats returned by TelemetryDatabaseService queries ──────────────

public class SignalStats
{
    public int     Days            { get; set; }
    public int     TotalPackets   { get; set; }

    // SNR (rx_snr – empfangen von diesem Node)
    public float? DaySnrMedian    { get; set; }
    public float? NightSnrMedian  { get; set; }
    public float? SnrMin          { get; set; }
    public float? SnrMax          { get; set; }
    public float? SnrVariance     { get; set; }

    // RSSI
    public float? DayRssiMedian   { get; set; }
    public float? NightRssiMedian { get; set; }
    public float? RssiMin         { get; set; }
    public float? RssiMax         { get; set; }
}

public class PowerStats
{
    public int    Days             { get; set; }
    public int    TotalReadings    { get; set; }

    public float? DayBatteryAvg    { get; set; }
    public float? NightBatteryAvg  { get; set; }
    /// <summary>Day Ø – Night Ø, positive = drops overnight</summary>
    public float? NightBatteryDrop { get; set; }

    public float? DayVoltageAvg    { get; set; }
    public float? NightVoltageAvg  { get; set; }
    public float? NightVoltageDrop { get; set; }

    public float? VoltageMin       { get; set; }
    public float? VoltageMax       { get; set; }

    public long?  LastUptimeSeconds { get; set; }
}

public class AirtimeStats
{
    public int    Days                 { get; set; }

    public float? DayChannelUtilAvg    { get; set; }
    public float? NightChannelUtilAvg  { get; set; }
    public float? ChannelUtilMax       { get; set; }

    public float? DayAirTxUtilAvg      { get; set; }
    public float? NightAirTxUtilAvg    { get; set; }
    public float? AirTxUtilMax         { get; set; }
}

public class RoutingStats
{
    public int    Days            { get; set; }

    public float? AvgHops         { get; set; }
    public int?   MinHops         { get; set; }
    public int?   MaxHops         { get; set; }

    public int    TotalPackets    { get; set; }
    public int    AckRequested    { get; set; }
    public int    AckReceived     { get; set; }
    /// <summary>AckReceived / AckRequested, 0–1</summary>
    public float? AckSuccessRate  { get; set; }

    public int    UniqueFromNodes { get; set; }

    // Hop-count distribution: key = hop count, value = # packets
    public Dictionary<int, int> HopDistribution { get; set; } = new();
}

public class NeighborStats
{
    public uint   NeighborId      { get; set; }
    public string NeighborName    { get; set; } = string.Empty;

    public float? MedianRssi      { get; set; }
    public float? MedianSnr       { get; set; }
    public float  PacketsPerHour  { get; set; }
    public DateTime LastSeen      { get; set; }
    public int    TotalPackets    { get; set; }
}

public class TimeSeriesPoint
{
    public uint     NodeId    { get; set; }
    public DateTime Timestamp { get; set; }
    public double   Value     { get; set; }
    public string   Metric    { get; set; } = string.Empty;
}
