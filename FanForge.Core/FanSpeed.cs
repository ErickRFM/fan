namespace FanForge.Core;

/// <summary>Which cooling fan(s) a command targets.</summary>
public enum FanTarget
{
    Cpu,
    Gpu,
    Both
}

/// <summary>
/// Distinguishes what a backend merely exposes from what has actually been verified. A capability is
/// only ever promoted to <see cref="Verified"/> after a confirmed physical response; anything less is
/// never treated as an operational control path.
/// </summary>
public enum CapabilityLevel
{
    Unsupported,
    Detected,
    Unverified,
    Verified
}

/// <summary>Full lifecycle of a manual fan-speed command, from availability to confirmation.</summary>
public enum FanSpeedState
{
    Unsupported,
    AvailableUnverified,
    Ready,
    Applying,
    AppliedUnverified,
    Verified,
    Restoring,
    Restored,
    Failed
}

public enum ReadingQuality
{
    Ok,
    Partial,
    Invalid
}

/// <summary>
/// What a backend reports it can do. Detected is "the endpoint responded", Verified is "the operation
/// was physically confirmed"; the engine never treats Detected as sufficient to write.
/// </summary>
public sealed record FanSpeedCapabilities(
    string Backend,
    CapabilityLevel PwmDirect,
    CapabilityLevel FanCurves,
    bool SupportsProfiles,
    bool TelemetryAvailable,
    string? Note = null)
{
    public static FanSpeedCapabilities None(string backend, string? note = null) =>
        new(backend, CapabilityLevel.Unsupported, CapabilityLevel.Unsupported, false, false, note);
}

/// <summary>
/// A single telemetry reading. RPM and temperatures are nullable so an unreadable value can never be
/// confused with a real 0; <see cref="Quality"/> records how complete the reading is and
/// <see cref="Source"/> records which provider produced it.
/// </summary>
public sealed record FanSpeedReading(
    int? CpuRpm,
    int? GpuRpm,
    int? CpuCelsius,
    int? GpuCelsius,
    string Profile,
    string Source,
    ReadingQuality Quality,
    DateTimeOffset Timestamp)
{
    public static FanSpeedReading Invalid(string source, DateTimeOffset timestamp) =>
        new(null, null, null, null, "unknown", source, ReadingQuality.Invalid, timestamp);
}

/// <summary>
/// Result of a command. <see cref="Accepted"/> means the controller acknowledged the request;
/// <see cref="Confirmed"/> means the change was physically observed. Only a Confirmed result may be
/// presented as completed.
/// </summary>
public sealed record FanSpeedCommandResult(
    FanSpeedState State,
    string Message,
    int? RequestedPercent = null,
    int? ObservedRpm = null)
{
    public bool Accepted => State is FanSpeedState.AppliedUnverified or FanSpeedState.Verified;
    public bool Confirmed => State == FanSpeedState.Verified;
}

/// <summary>One auditable transition of the engine.</summary>
public sealed record FanSpeedAuditEntry(
    DateTimeOffset Timestamp,
    FanSpeedState State,
    string Backend,
    string Message);

/// <summary>
/// Contract every FanSpeed backend implements. Implementations must never report success for an
/// operation they did not actually apply, and must return Unsupported rather than pretend.
/// </summary>
public interface IFanSpeedBackend
{
    string Name { get; }

    Task<FanSpeedCapabilities> DetectAsync(CancellationToken cancellationToken);

    Task<FanSpeedReading> ReadAsync(CancellationToken cancellationToken);

    Task<FanSpeedCommandResult> SetSpeedAsync(
        FanTarget target,
        int percent,
        CancellationToken cancellationToken);

    Task<FanSpeedCommandResult> RestoreAutomaticAsync(CancellationToken cancellationToken);
}
