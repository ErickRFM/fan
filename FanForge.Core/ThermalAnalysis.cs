using System.Globalization;

namespace FanForge.Core;

/// <summary>Where a telemetry sample came from, so imported manual readings stay distinguishable.</summary>
public enum TelemetrySource
{
    FanForge,
    ProArt
}

/// <summary>One parsed telemetry row. Only readable values are kept; unreadable ones are null.</summary>
public sealed record TelemetrySample(
    DateTimeOffset Timestamp,
    int? CpuRpm,
    int? GpuRpm,
    int? CpuCelsius,
    int? GpuCelsius,
    string Profile,
    bool? ProfileConfirmed,
    string? Note,
    TelemetrySource Source);

/// <summary>A gap between two consecutive samples that exceeds the expected cadence.</summary>
public sealed record TimeGap(DateTimeOffset From, DateTimeOffset To, TimeSpan Length);

/// <summary>How clean the parsed data is; every anomaly found during parsing is counted here.</summary>
public sealed record DataQuality(
    int TotalRows,
    int ValidRows,
    int InvalidValues,
    int MissingTimestamps,
    IReadOnlyList<TimeGap> Gaps,
    IReadOnlyList<string> Issues);

/// <summary>Descriptive statistics over a set of valid integer readings.</summary>
public sealed record Stat(int Count, double Min, double Max, double Average, double Median, double P90, double P95)
{
    public static readonly Stat Empty = new(0, 0, 0, 0, 0, 0, 0);

    public bool HasData => Count > 0;

    public static Stat From(IEnumerable<int> values)
    {
        var data = (values ?? throw new ArgumentNullException(nameof(values))).ToList();
        if (data.Count == 0) return Empty;
        data.Sort();
        return new Stat(
            data.Count,
            data[0],
            data[^1],
            Math.Round(data.Average(), 1),
            Percentile(data, 50),
            Percentile(data, 90),
            Percentile(data, 95));
    }

    /// <summary>Nearest-rank percentile over an ascending-sorted list; 0 when empty.</summary>
    public static double Percentile(IReadOnlyList<int> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        if (percentile <= 0) return sorted[0];
        if (percentile >= 100) return sorted[^1];
        int rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        if (rank < 0) rank = 0;
        if (rank >= sorted.Count) rank = sorted.Count - 1;
        return sorted[rank];
    }
}
