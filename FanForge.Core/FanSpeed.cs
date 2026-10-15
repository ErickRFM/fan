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

/// <summary>
/// Full lifecycle of a manual fan-speed command, from availability to confirmation. The numeric order
/// matters: <see cref="CapabilityLevel"/> combinations keep the lowest value, so an unusable fan is
/// never misread as controllable.
/// </summary>
public enum FanSpeedState
{
    /// <summary>No backend exposes any fan channel at all.</summary>
    Unsupported,

    /// <summary>An endpoint responded (e.g. telemetry) but exposes no control path.</summary>
    DetectedUnverified,

    /// <summary>A control path is exposed but not yet physically verified (never writable).</summary>
    Compatible,

    /// <summary>A verified control path exists and is ready to accept a request.</summary>
    Ready,

    /// <summary>A request is being applied.</summary>
    Applying,

    /// <summary>The controller acknowledged the request but the RPM response is unconfirmed.</summary>
    AcceptedUnverified,

    /// <summary>The physical response was confirmed by measured RPM.</summary>
    Verified,

    /// <summary>Automatic (BIOS/OEM) control is being restored.</summary>
    Restoring,

    /// <summary>Automatic control was restored.</summary>
    Restored,

    /// <summary>The operation failed; nothing is assumed to have been applied.</summary>
    Failed
}

public enum ReadingQuality
{
    Ok,
    Partial,
    Invalid
}

/// <summary>
/// Per-fan control capability. A model may expose CPU and GPU channels with different capabilities, so
/// they are tracked independently instead of being collapsed into one flag.
/// </summary>
public sealed record FanChannelCapabilities(CapabilityLevel Cpu, CapabilityLevel Gpu)
{
    public static FanChannelCapabilities None { get; } = new(CapabilityLevel.Unsupported, CapabilityLevel.Unsupported);

    public static FanChannelCapabilities From(CapabilityLevel both) => new(both, both);

    /// <summary>Capability for a single fan; <see cref="FanTarget.Both"/> requires both to be usable.</summary>
    public CapabilityLevel For(FanTarget target) => target switch
    {
        FanTarget.Cpu => Cpu,
        FanTarget.Gpu => Gpu,
        _ => Combine(Cpu, Gpu)
    };

    /// <summary>Both fans are controllable only when each one individually is.</summary>
    public static CapabilityLevel Combine(CapabilityLevel a, CapabilityLevel b) =>
        (a, b) switch
        {
            (CapabilityLevel.Verified, CapabilityLevel.Verified) => CapabilityLevel.Verified,
            (CapabilityLevel.Unsupported, _) or (_, CapabilityLevel.Unsupported) => CapabilityLevel.Unsupported,
            _ => (CapabilityLevel)Math.Min((int)a, (int)b)
        };
}

/// <summary>
/// What a backend reports it can do. Detected is "the endpoint responded", Verified is "the operation
/// was physically confirmed"; the engine never treats Detected as sufficient to write.
/// <see cref="PerFan"/> carries the per-fan breakdown when a model exposes fans unevenly.
/// </summary>
public sealed record FanSpeedCapabilities(
    string Backend,
    CapabilityLevel PwmDirect,
    CapabilityLevel FanCurves,
    bool SupportsProfiles,
    bool TelemetryAvailable,
    string? Note = null,
    FanChannelCapabilities? PerFan = null)
{
    /// <summary>CPU-channel PWM capability (falls back to the aggregate value).</summary>
    public CapabilityLevel CpuPwm => PerFan?.Cpu ?? PwmDirect;

    /// <summary>GPU-channel PWM capability (falls back to the aggregate value).</summary>
    public CapabilityLevel GpuPwm => PerFan?.Gpu ?? PwmDirect;

    /// <summary>PWM capability for a specific target.</summary>
    public CapabilityLevel PwmFor(FanTarget target) => PerFan?.For(target) ?? PwmDirect;

    /// <summary>True only when the requested fan(s) have a physically verified write path.</summary>
    public bool CanControl(FanTarget target) => PwmFor(target) == CapabilityLevel.Verified;

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
/// presented as completed. <see cref="RequestedPercent"/> is what was asked for; <see cref="ObservedRpm"/>
/// is what the tachometer actually reported — they are deliberately kept separate.
/// </summary>
public sealed record FanSpeedCommandResult(
    FanSpeedState State,
    string Message,
    int? RequestedPercent = null,
    int? ObservedRpm = null,
    FanTarget? Target = null)
{
    public bool Accepted => State is FanSpeedState.AcceptedUnverified or FanSpeedState.Verified;
    public bool Confirmed => State == FanSpeedState.Verified;
}

/// <summary>One auditable transition of the engine. Requested percent and observed RPM are recorded
/// separately so a request is never mistaken for a demonstrated physical change.</summary>
public sealed record FanSpeedAuditEntry(
    DateTimeOffset Timestamp,
    FanSpeedState State,
    string Backend,
    string Message,
    int? RequestedPercent = null,
    int? ObservedRpm = null);

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
