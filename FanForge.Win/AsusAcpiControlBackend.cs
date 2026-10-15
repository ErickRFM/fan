using FanForge.Core;

namespace FanForge;

/// <summary>
/// VivoBook ATKACPI: read RPM and select supported ASUS thermal profiles.
/// This is NOT a direct fan-PWM/curve control backend.
/// The active profile is captured before the first change and restored exactly as it was; only three
/// profiles are known on M3500QC and writing is confirmed by read-back. Native handles are
/// method-local so concurrent probes and profile changes cannot close one another's handles.
/// </summary>
internal sealed class AsusAcpiControlBackend : IFanControlBackend, IFanTelemetryProvider, IDisposable
{
    public string Name => "AsusACPI (ATKACPI)";
    private readonly ProfileGuard _guard;
    private bool _disposed;

    public AsusAcpiControlBackend()
        : this(new ProfileGuard(new AsusAcpiProfileChannel(), new FileProfileStore())) { }

    private AsusAcpiControlBackend(ProfileGuard guard) => _guard = guard;

    /// <summary>
    /// Repair instance used by <c>--restore</c> and the watchdog: it recovers the original profile
    /// persisted before a crash so the firmware can be returned to the user's chosen mode.
    /// </summary>
    public static AsusAcpiControlBackend ForRecovery() =>
        new(new ProfileGuard(new AsusAcpiProfileChannel(), new FileProfileStore(), recoverPersisted: true));

    /// <summary>True when FanForge has changed the ASUS profile in this session.</summary>
    public bool ProfileModified => _guard.Modified;

    /// <summary>The captured original wire mode (0/1/2), or null when nothing was captured.</summary>
    public int? OriginalWireMode => _guard.OriginalWireMode;

    public Task<FanCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            bool model = FanPolicy.SupportedModel(AsusHardware.Model());
            if (!AsusAcpiNative.TryOpen(out var handle))
                return new FanCapabilities(TelemetryState.DriverError, false, false, false,
                    false, false, false, false);
            try
            {
                bool cpu = SafeRead(handle, AsusAcpiNative.CPU_Fan) >= 0;
                bool gpu = SafeRead(handle, AsusAcpiNative.GPU_Fan) >= 0;
                bool modes = model && SafeRead(handle, AsusAcpiNative.VivoBookMode) >= 0;
                // A readable curve endpoint is not proof that this implementation can write curves.
                return new FanCapabilities(
                    cpu || gpu ? TelemetryState.ReadOnly : TelemetryState.Unsupported,
                    cpu, gpu, false, modes, false, false, modes);
            }
            finally { AsusAcpiNative.Close(handle); }
        }, cancellationToken);

    public Task<TelemetrySnapshot> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            var rpm = new List<FanRpm>(2);
            if (AsusAcpiNative.TryOpen(out var handle))
            {
                try
                {
                    rpm.Add(new FanRpm(FanType.Cpu, AsusAcpiTelemetry.DecodeRpm(
                        SafeRead(handle, AsusAcpiNative.CPU_Fan))));
                    rpm.Add(new FanRpm(FanType.Gpu, AsusAcpiTelemetry.DecodeRpm(
                        SafeRead(handle, AsusAcpiNative.GPU_Fan))));
                }
                finally { AsusAcpiNative.Close(handle); }
            }
            return new TelemetrySnapshot(rpm, new TemperatureReading(null, null), DateTimeOffset.UtcNow);
        }, cancellationToken);

    public Task<ControlResult> ApplyAsync(FanControlRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ApplyCore(request, cancellationToken), cancellationToken);

    private ControlResult ApplyCore(FanControlRequest request, CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (request.PwmPercentages is { Count: > 0 } || request.FanCurves is { Count: > 0 })
            return new ControlResult(false, "Este backend ATKACPI no admite control PWM ni curvas.");
        if (!FanPolicy.SupportedModel(AsusHardware.Model()))
            return new ControlResult(false, "Escritura bloqueada: modelo diferente de M3500QC.");
        ProfileOutcome outcome = _guard.Apply(request.Profile);
        return new ControlResult(outcome.Success, outcome.Message + " No es PWM fijo.");
    }

    /// <summary>Returns control to the ASUS profile that was active before FanForge changed it.</summary>
    public Task<ControlResult> RestoreAutomaticAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!FanPolicy.SupportedModel(AsusHardware.Model()))
                return new ControlResult(false, "Escritura bloqueada: modelo diferente de M3500QC.");
            ProfileOutcome outcome = _guard.RestoreOriginal();
            return new ControlResult(outcome.Success, outcome.Message);
        }, cancellationToken);

    private static int SafeRead(IntPtr handle, uint endpoint)
    {
        try { return AsusAcpiNative.DeviceGet(handle, endpoint); }
        catch { return -65536; }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AsusAcpiControlBackend));
    }

    public void Dispose() => _disposed = true;
}
