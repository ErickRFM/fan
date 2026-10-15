namespace FanForge.Core;

/// <summary>
/// In-memory backend used for tests and demonstrations. It never touches hardware, so its capabilities
/// are explicitly labelled as simulated; it exists solely to exercise the engine without a device.
/// </summary>
public sealed class SimulatedFanSpeedBackend : IFanSpeedBackend
{
    private const int IdleRpm = 2000;

    private readonly bool _verifiedPwm;
    private readonly bool _autoConfirm;
    private int _cpuRpm = IdleRpm;
    private int _gpuRpm = IdleRpm;

    public SimulatedFanSpeedBackend(bool verifiedPwm = true, bool autoConfirm = true)
    {
        _verifiedPwm = verifiedPwm;
        _autoConfirm = autoConfirm;
    }

    public string Name => "simulado";

    public Task<FanSpeedCapabilities> DetectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new FanSpeedCapabilities(
            Name,
            _verifiedPwm ? CapabilityLevel.Verified : CapabilityLevel.Unsupported,
            CapabilityLevel.Unsupported,
            SupportsProfiles: false,
            TelemetryAvailable: true,
            Note: "Backend de demostración: no accede al hardware."));

    public Task<FanSpeedReading> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new FanSpeedReading(
            _cpuRpm,
            _gpuRpm,
            45,
            45,
            "simulado",
            Name,
            ReadingQuality.Ok,
            DateTimeOffset.UtcNow));

    public Task<FanSpeedCommandResult> SetSpeedAsync(
        FanTarget target,
        int percent,
        CancellationToken cancellationToken)
    {
        if (!_verifiedPwm)
        {
            return Task.FromResult(new FanSpeedCommandResult(
                FanSpeedState.Unsupported,
                "PWM simulado no disponible.",
                percent));
        }

        int rpm = percent * 50;
        if (target is FanTarget.Cpu or FanTarget.Both)
        {
            _cpuRpm = rpm;
        }

        if (target is FanTarget.Gpu or FanTarget.Both)
        {
            _gpuRpm = rpm;
        }

        return Task.FromResult(new FanSpeedCommandResult(
            _autoConfirm ? FanSpeedState.Verified : FanSpeedState.AppliedUnverified,
            _autoConfirm ? "Cambio simulado confirmado." : "Cambio simulado aceptado.",
            percent,
            rpm));
    }

    public Task<FanSpeedCommandResult> RestoreAutomaticAsync(CancellationToken cancellationToken)
    {
        _cpuRpm = IdleRpm;
        _gpuRpm = IdleRpm;
        return Task.FromResult(new FanSpeedCommandResult(
            FanSpeedState.Restored,
            "Control automático simulado restaurado."));
    }
}
