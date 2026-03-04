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
    /// <summary>Night Ø – Day Ø: negative = drops overnight (discharge), positive = rises (solar charging)</summary>
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

// ── Signal analysis models ─────────────────────────────────────────────────────

public enum LedState { NoData, Good, Warning, Alert }

public class NeighborTrend
{
    public uint   NeighborId   { get; set; }
    public string NeighborName { get; set; } = string.Empty;
    public float  ShortSlope   { get; set; }  // dB/h in short window (negative = falling)
    public float  LongSlope    { get; set; }  // dB/day in long window
    public int    PointCount   { get; set; }
}

public class SignalAnalysisResult
{
    // LED 1: multiple neighbors simultaneously falling short-term → weather effect
    public LedState WeatherLed         { get; set; }
    public int      DecliningNeighbors { get; set; }
    public int      TotalNeighbors     { get; set; }

    // LED 2: all/most neighbors falling long-term → own antenna problem
    public LedState AntennaLed         { get; set; }
    public float    AvgLongSlope       { get; set; }

    // LED 3: exactly one neighbor falling while others stable → neighbor problem
    public LedState NeighborLed            { get; set; }
    public uint?    ProblemNeighborId      { get; set; }
    public string   ProblemNeighborName    { get; set; } = string.Empty;

    // LED 4: path stability from traceroute data
    public LedState PathLed            { get; set; }
    public float    HopCost            { get; set; }
    public float    RouteChangeRate    { get; set; }

    public List<NeighborTrend> Trends  { get; set; } = new();
}

public class MeshHealthScore
{
    public float    Score              { get; set; }  // 0–100, higher = better
    public LedState State              { get; set; }
    public float    AvgPathCost        { get; set; }
    public float    RouteChangeRate    { get; set; }
    /// <summary>Historical baseline: average packets/hour during daytime (NOAA).</summary>
    public float    DayRxPerHour       { get; set; }
    /// <summary>Historical baseline: average packets/hour during nighttime (NOAA).</summary>
    public float    NightRxPerHour     { get; set; }
    /// <summary>Actual packets/hour in the current short window (1–2 h).</summary>
    public float    CurrentRxPerHour   { get; set; }
    /// <summary>currentRx / expectedRx (day or night baseline), 0–1. 1 = on par with baseline.</summary>
    public float    RxScore            { get; set; }
    public float    ChannelUtilization { get; set; }
    public string   Summary            { get; set; } = string.Empty;
}
