using FanForge.Core;

namespace FanForge;

/// <summary>
/// VivoBook ATKACPI: read RPM and select supported ASUS thermal profiles.
/// This is NOT a direct fan-PWM/curve control backend.
/// Native handles are method-local so concurrent probes and profile changes cannot close
/// one another's handles. Firmware calls themselves remain best-effort.
/// </summary>
internal sealed class AsusAcpiControlBackend : IFanControlBackend, IFanTelemetryProvider, IDisposable
{
    public string Name => "AsusACPI (ATKACPI)";
    private bool _disposed;

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
        int? target = FanPolicy.VivoBookProfileWireMode(request.Profile);
        if (target is null)
            return new ControlResult(false, "Perfil no admitido: solo Silencioso, Estándar y Rendimiento.");
        if (!AsusAcpiNative.TryOpen(out var handle))
            return new ControlResult(false, "No se pudo abrir ATKACPI.");

        try
        {
            int before = AsusAcpiNative.DeviceGet(handle, AsusAcpiNative.VivoBookMode);
            if (before < 0)
                return new ControlResult(false, "VivoBookMode no disponible: sin escritura.");
            ct.ThrowIfCancellationRequested();
            int status = AsusAcpiNative.DeviceSet(handle, AsusAcpiNative.VivoBookMode, target.Value);
            int after = AsusAcpiNative.DeviceGet(handle, AsusAcpiNative.VivoBookMode);
            bool verified = FanPolicy.VivoBookProfileConfirmed(after, target.Value);
            return new ControlResult(verified,
                $"Perfil {request.Profile}: raw {before} -> {after}, status={status}, lectura confirmada={verified}. No es PWM fijo.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ControlResult(false, "Error de comunicación ATKACPI: " + ex.Message);
        }
        finally { AsusAcpiNative.Close(handle); }
    }

    private static int SafeRead(IntPtr handle, uint endpoint)
    {
        try { return AsusAcpiNative.DeviceGet(handle, endpoint); }
        catch { return -65536; }
    }

    public Task<ControlResult> RestoreAutomaticAsync(CancellationToken cancellationToken) =>
        ApplyAsync(new FanControlRequest(FanProfile.Standard, null, null), cancellationToken);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AsusAcpiControlBackend));
    }

    public void Dispose() => _disposed = true;
}
