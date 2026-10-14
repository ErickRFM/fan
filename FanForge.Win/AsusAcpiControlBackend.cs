using FanForge.Core;

namespace FanForge;

/// <summary>
/// Read/write backend over ASUS ATKACPI. On VivoBook-class hardware only performance-mode
/// switching is exposed; dedicated fan curves and direct PWM are reported unsupported.
/// All writes are verified by reading the endpoint back.
/// </summary>
internal sealed class AsusAcpiControlBackend : IFanControlBackend, IFanTelemetryProvider, IDisposable
{
    public string Name => "AsusACPI (ATKACPI)";

    // G-Helper internal performance bases.
    const int Balanced = 0;
    const int Turbo = 1;
    const int Silent = 2;

    private IntPtr _handle;
    private bool _disposed;

    public async Task<FanCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            bool vivo = DeviceGet(AsusAcpiNative.VivoBookMode) >= 0;
            bool cpuFan = DeviceGet(AsusAcpiNative.CPU_Fan) >= 0;
            bool gpuFan = DeviceGet(AsusAcpiNative.GPU_Fan) >= 0;
            bool curves = DeviceGet(AsusAcpiNative.DevsCPUFanCurve) >= 0;
            return new FanCapabilities(
                TelemetryState.ReadOnly,
                cpuFan,
                gpuFan,
                false,
                vivo,
                curves,
                false,
                false);
        }, cancellationToken);
    }

    public Task<TelemetrySnapshot> ReadAsync(CancellationToken cancellationToken) =>
        ReadCoreAsync(cancellationToken);

    private async Task<TelemetrySnapshot> ReadCoreAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var rpms = new List<FanRpm>(2);
            if (Open())
            {
                try
                {
                    rpms.Add(new FanRpm(FanType.Cpu, AsusAcpiTelemetry.DecodeRpm(AsusAcpiNative.DeviceGet(_handle, AsusAcpiNative.CPU_Fan))));
                    rpms.Add(new FanRpm(FanType.Gpu, AsusAcpiTelemetry.DecodeRpm(AsusAcpiNative.DeviceGet(_handle, AsusAcpiNative.GPU_Fan))));
                }
                finally { Close(); }
            }
            return new TelemetrySnapshot(rpms, new TemperatureReading(null, null), DateTimeOffset.UtcNow);
        }, cancellationToken);
    }

    public Task<ControlResult> ApplyAsync(FanControlRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ApplyCore(request), cancellationToken);

    private ControlResult ApplyCore(FanControlRequest request)
    {
        if (request.PwmPercentages is { Count: > 0 } || request.FanCurves is { Count: > 0 })
            return new ControlResult(false, "Curvas PWM no soportadas por ATKACPI en este modelo.");

        int? target = MapProfile(request.Profile);
        if (target is null)
            return new ControlResult(true, "Perfil Auto: sin escritura (controlado por ASUS).");

        if (!Open())
            return new ControlResult(false, "No se pudo abrir ATKACPI.");

        try
        {
            int before = AsusAcpiNative.DeviceGet(_handle, AsusAcpiNative.VivoBookMode);
            int status = AsusAcpiNative.DeviceSet(_handle, AsusAcpiNative.VivoBookMode, Swap(target.Value));
            int after = AsusAcpiNative.DeviceGet(_handle, AsusAcpiNative.VivoBookMode);
            bool changed = LowMode(after) == Swap(target.Value);
            return new ControlResult(changed,
                $"Perfil {request.Profile}: raw {before} -> {after}, status={status}, verificado={changed}");
        }
        finally { Close(); }
    }

    public Task<ControlResult> RestoreAutomaticAsync(CancellationToken cancellationToken) =>
        ApplyAsync(new FanControlRequest(FanProfile.Standard, null, null), cancellationToken);

    static int? MapProfile(FanProfile profile) => profile switch
    {
        FanProfile.Auto => null,
        FanProfile.Whisper => Silent,
        FanProfile.Standard => Balanced,
        FanProfile.Performance => Turbo,
        FanProfile.Max => Turbo,
        FanProfile.Manual => null,
        _ => null
    };

    // Device encoding is inverted for Turbo/Silent relative to internal bases.
    static int Swap(int mode) => FanPolicy.VivoBookWireMode(mode);

    static int LowMode(int raw) => raw & 0xFFFF;

    bool Open()
    {
        if (_handle != IntPtr.Zero) return true;
        return AsusAcpiNative.TryOpen(out _handle);
    }

    void Close()
    {
        if (_handle == IntPtr.Zero) return;
        AsusAcpiNative.Close(_handle);
        _handle = IntPtr.Zero;
    }

    int DeviceGet(uint id)
    {
        if (!Open()) return -1;
        try { return AsusAcpiNative.DeviceGet(_handle, id); }
        catch { Close(); return -1; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
