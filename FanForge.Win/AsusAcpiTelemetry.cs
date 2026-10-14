using FanForge.Core;

namespace FanForge;

internal sealed class AsusAcpiTelemetry : IFanTelemetryProvider, IDisposable
{
    public string Name => "AsusACPI (ATKACPI)";
    private IntPtr _handle;
    private bool _disposed;

    public async Task<FanCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var caps = new FanCapabilities(
                TelemetryState.Unverified,
                Supports(AsusAcpiNative.CPU_Fan),
                Supports(AsusAcpiNative.GPU_Fan),
                false,
                Supports(AsusAcpiNative.VivoBookMode),
                false,
                false,
                true);
            return caps with { TelemetryState = TelemetryState.Supported };
        }, cancellationToken);
    }

    private bool Supports(uint id)
    {
        try
        {
            if (!Open()) return false;
            var v = AsusAcpiNative.DeviceGet(_handle, id);
            Close();
            return v >= 0;
        }
        catch { Close(); return false; }
    }

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

    public async Task<TelemetrySnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var rpms = new List<FanRpm>(2);
            if (Open())
            {
                int cpuTemp, gpuTemp;
                try
                {
                    rpms.Add(new FanRpm(FanType.Cpu, DecodeRpm(AsusAcpiNative.DeviceGet(_handle, AsusAcpiNative.CPU_Fan))));
                    rpms.Add(new FanRpm(FanType.Gpu, DecodeRpm(AsusAcpiNative.DeviceGet(_handle, AsusAcpiNative.GPU_Fan))));
                    cpuTemp = DecodeTemp(AsusAcpiNative.DeviceGet(_handle, AsusAcpiNative.Temp_CPU));
                    gpuTemp = DecodeTemp(AsusAcpiNative.DeviceGet(_handle, AsusAcpiNative.Temp_GPU));
                }
                finally { Close(); }
                return new TelemetrySnapshot(rpms,
                    new TemperatureReading(cpuTemp < 0 ? null : cpuTemp, gpuTemp < 0 ? null : gpuTemp),
                    DateTimeOffset.UtcNow);
            }
            return new TelemetrySnapshot(rpms, new TemperatureReading(null, null), DateTimeOffset.UtcNow);
        }, cancellationToken);
    }

    /// <summary>Decodes an ATKACPI temperature in degrees Celsius; out-of-range values are invalid.</summary>
    internal static int DecodeTemp(int raw) => FanPolicy.AcpiTemperature(raw);

    /// <summary>Decodes an ATKACPI fan value. Raw unit is 100 RPM; values &gt;120 or empty-negative are invalid.</summary>
    internal static int DecodeRpm(int raw) => FanPolicy.AcpiFanRpm(raw);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
