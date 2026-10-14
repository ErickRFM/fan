namespace FanForge.Core;

public enum TelemetryState
{
    Supported,
    ReadOnly,
    Unsupported,
    DriverError,
    Unverified
}

public enum FanType
{
    Cpu,
    Gpu,
    Mid
}

public enum FanProfile
{
    Auto,
    Whisper,
    Standard,
    Performance,
    Manual,
    Max
}

public sealed record FanRpm(FanType Type, int Value)
{
    public static FanRpm Invalid(FanType type) => new(type, -1);
}

public sealed record TemperatureReading(int? CpuCelsius, int? GpuCelsius);

public sealed record TelemetrySnapshot(
    IReadOnlyList<FanRpm> Rpms,
    TemperatureReading Temperatures,
    DateTimeOffset Timestamp);

public sealed record FanCapabilities(
    TelemetryState TelemetryState,
    bool SupportsCpuFan,
    bool SupportsGpuFan,
    bool SupportsMidFan,
    bool SupportsProfiles,
    bool SupportsFanCurves,
    bool SupportsPwmDirect,
    bool SupportsRestore);

public sealed record FanControlRequest(
    FanProfile Profile,
    IReadOnlyDictionary<FanType, int>? PwmPercentages,
    IReadOnlyDictionary<FanType, byte[]>? FanCurves);

public sealed record ControlResult(bool Success, string? Message);

/// <summary>Hardware-independent combination of telemetry from several providers.</summary>
public static class TelemetryMerge
{
    public static readonly TelemetrySnapshot Empty =
        new(Array.Empty<FanRpm>(), new TemperatureReading(null, null), DateTimeOffset.MinValue);

    /// <summary>
    /// RPMs come from the first snapshot that carries any valid value; each temperature is
    /// taken from the first snapshot that supplies it. Invalid RPMs (-1) are dropped.
    /// </summary>
    public static TelemetrySnapshot Merge(IEnumerable<TelemetrySnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var rpms = new List<FanRpm>();
        int? cpu = null, gpu = null;
        foreach (var snap in snapshots)
        {
            if (snap is null) continue;
            if (rpms.Count == 0)
                rpms.AddRange(snap.Rpms.Where(r => r.Value >= 0));
            cpu ??= snap.Temperatures.CpuCelsius;
            gpu ??= snap.Temperatures.GpuCelsius;
        }
        return new TelemetrySnapshot(rpms, new TemperatureReading(cpu, gpu), DateTimeOffset.UtcNow);
    }
}

/// <summary>Read-only telemetry provider (RPM, temperatures) independent from control.</summary>
public interface IFanTelemetryProvider
{
    string Name { get; }
    Task<FanCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken);
    Task<TelemetrySnapshot> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Fan control backend. Can support profiles/curves/PWM if hardware allows.</summary>
public interface IFanControlBackend
{
    string Name { get; }
    Task<FanCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken);
    Task<ControlResult> ApplyAsync(FanControlRequest request, CancellationToken cancellationToken);
    Task<ControlResult> RestoreAutomaticAsync(CancellationToken cancellationToken);
}
