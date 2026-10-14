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
