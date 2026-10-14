using FanForge.Core;

namespace FanForge;

/// <summary>
/// Read-only ATKACPI sensor provider. Each operation owns its native handle;
/// no handle can be closed by a concurrent capability probe or disposal.
/// </summary>
internal sealed class AsusAcpiTelemetry : IFanTelemetryProvider, IDisposable
{
    public string Name => "AsusACPI (ATKACPI)";
    private bool _disposed;

    public Task<FanCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!AsusAcpiNative.TryOpen(out var handle))
                return new FanCapabilities(TelemetryState.DriverError, false, false, false,
                    false, false, false, false);
            try
            {
                bool cpu = Supports(handle, AsusAcpiNative.CPU_Fan);
                bool gpu = Supports(handle, AsusAcpiNative.GPU_Fan);
                bool profile = Supports(handle, AsusAcpiNative.VivoBookMode);
                return new FanCapabilities(
                    cpu || gpu ? TelemetryState.ReadOnly : TelemetryState.Unsupported,
                    cpu, gpu, false, profile, false, false, false);
            }
            finally { AsusAcpiNative.Close(handle); }
        }, cancellationToken);

    private static bool Supports(IntPtr handle, uint id)
    {
        try { return AsusAcpiNative.DeviceGet(handle, id) >= 0; }
        catch { return false; }
    }

    public Task<TelemetrySnapshot> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            var rpms = new List<FanRpm>(2);
            int? cpuTemp = null, gpuTemp = null;
            if (AsusAcpiNative.TryOpen(out var handle))
            {
                try
                {
                    rpms.Add(new FanRpm(FanType.Cpu, DecodeRpm(AsusAcpiNative.DeviceGet(handle, AsusAcpiNative.CPU_Fan))));
                    rpms.Add(new FanRpm(FanType.Gpu, DecodeRpm(AsusAcpiNative.DeviceGet(handle, AsusAcpiNative.GPU_Fan))));
                    int cpu = DecodeTemp(AsusAcpiNative.DeviceGet(handle, AsusAcpiNative.Temp_CPU));
                    int gpu = DecodeTemp(AsusAcpiNative.DeviceGet(handle, AsusAcpiNative.Temp_GPU));
                    if (cpu >= 0) cpuTemp = cpu;
                    if (gpu >= 0) gpuTemp = gpu;
                }
                finally { AsusAcpiNative.Close(handle); }
            }
            return new TelemetrySnapshot(rpms, new TemperatureReading(cpuTemp, gpuTemp), DateTimeOffset.UtcNow);
        }, cancellationToken);

    internal static int DecodeTemp(int raw) => FanPolicy.AcpiTemperature(raw);
    internal static int DecodeRpm(int raw) => FanPolicy.AcpiFanRpm(raw);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AsusAcpiTelemetry));
    }

    public void Dispose() => _disposed = true;
}
