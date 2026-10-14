namespace FanForge.Core;

/// <summary>A single sampled point of telemetry, with only the values that were readable.</summary>
public readonly record struct HistoryPoint(
    DateTimeOffset Timestamp,
    int? CpuCelsius,
    int? GpuCelsius,
    int? CpuRpm,
    int? GpuRpm);

/// <summary>
/// Bounded, hardware-independent ring buffer of telemetry samples. Keeps the newest
/// <see cref="Capacity"/> points in chronological order for charting and diagnostics.
/// </summary>
public sealed class TelemetryHistory
{
    private readonly Queue<HistoryPoint> _points;

    public TelemetryHistory(int capacity = 180)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _points = new Queue<HistoryPoint>(capacity);
    }

    public int Capacity { get; }

    public int Count => _points.Count;

    public IReadOnlyList<HistoryPoint> Points => _points.ToArray();

    public void Add(TelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _points.Enqueue(ToPoint(snapshot));
        while (_points.Count > Capacity) _points.Dequeue();
    }

    public HistoryPoint? Latest => _points.Count == 0 ? null : _points.Last();

    public void Clear() => _points.Clear();

    private static HistoryPoint ToPoint(TelemetrySnapshot snapshot)
    {
        int? cpuRpm = null, gpuRpm = null;
        foreach (var rpm in snapshot.Rpms)
        {
            if (rpm.Value < 0) continue;
            switch (rpm.Type)
            {
                case FanType.Cpu: cpuRpm = rpm.Value; break;
                case FanType.Gpu: gpuRpm = rpm.Value; break;
            }
        }
        return new HistoryPoint(
            snapshot.Timestamp,
            snapshot.Temperatures.CpuCelsius,
            snapshot.Temperatures.GpuCelsius,
            cpuRpm,
            gpuRpm);
    }
}
