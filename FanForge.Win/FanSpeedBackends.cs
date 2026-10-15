using System.Management;
using FanForge.Core;

namespace FanForge;

/// <summary>
/// Factory that assembles the FanSpeed backends in preference order. Every real backend reports PWM as
/// unsupported unless it can prove otherwise, so the engine stays gated on the M3500QC.
/// </summary>
internal static class FanSpeedBackends
{
    public static IReadOnlyList<IFanSpeedBackend> CreateDefault(IEnumerable<IFanSpeedBackend>? extra = null)
    {
        var backends = new List<IFanSpeedBackend>
        {
            new AtkAcpiFanSpeedBackend(),
            new WmiFanSpeedBackend(),
            new HealthyTableFanSpeedBackend(),
            new PawnIoFanSpeedBackend(),
        };
        if (extra is not null)
        {
            backends.AddRange(extra);
        }

        return backends;
    }
}

/// <summary>
/// ATKACPI backend: read RPM and select ASUS thermal profiles. No PWM/curve write path is physically
/// verified on this model (the curve endpoints reject writes), so both stay Unsupported.
/// </summary>
internal sealed class AtkAcpiFanSpeedBackend : IFanSpeedBackend
{
    public string Name => "AsusACPI (ATKACPI)";

    public Task<FanSpeedCapabilities> DetectAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AsusAcpiNative.TryOpen(out var handle))
            {
                return FanSpeedCapabilities.None("AsusACPI (ATKACPI)", "No se pudo abrir ATKACPI.");
            }

            try
            {
                bool cpu = SafeRead(handle, AsusAcpiNative.CPU_Fan) >= 0;
                bool gpu = SafeRead(handle, AsusAcpiNative.GPU_Fan) >= 0;
                bool modes = FanPolicy.SupportedModel(AsusHardware.Model())
                    && SafeRead(handle, AsusAcpiNative.VivoBookMode) >= 0;

                return new FanSpeedCapabilities(
                    "AsusACPI (ATKACPI)",
                    CapabilityLevel.Unsupported,
                    CapabilityLevel.Unsupported,
                    SupportsProfiles: modes,
                    TelemetryAvailable: cpu || gpu,
                    Note: "RPM y perfiles ASUS; PWM/curvas no verificados.");
            }
            finally { AsusAcpiNative.Close(handle); }
        }, cancellationToken);

    public Task<FanSpeedReading> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? cpu = null, gpu = null, cpuTemp = null, gpuTemp = null;
            string profile = "unknown";
            if (AsusAcpiNative.TryOpen(out var handle))
            {
                try
                {
                    int cpuValue = FanPolicy.AcpiFanRpm(SafeRead(handle, AsusAcpiNative.CPU_Fan));
                    int gpuValue = FanPolicy.AcpiFanRpm(SafeRead(handle, AsusAcpiNative.GPU_Fan));
                    if (cpuValue >= 0) cpu = cpuValue;
                    if (gpuValue >= 0) gpu = gpuValue;

                    int cpuC = FanPolicy.AcpiTemperature(SafeRead(handle, AsusAcpiNative.Temp_CPU));
                    int gpuC = FanPolicy.AcpiTemperature(SafeRead(handle, AsusAcpiNative.Temp_GPU));
                    if (cpuC >= 0) cpuTemp = cpuC;
                    if (gpuC >= 0) gpuTemp = gpuC;

                    int modeWire = SafeRead(handle, AsusAcpiNative.VivoBookMode);
                    if (modeWire >= 0) profile = FanPolicy.ProfileName(modeWire & 0xFFFF);
                }
                finally { AsusAcpiNative.Close(handle); }
            }

            var quality = (cpu, gpu) switch
            {
                (not null, not null) => ReadingQuality.Ok,
                (not null, null) or (null, not null) => ReadingQuality.Partial,
                _ => ReadingQuality.Invalid
            };
            return new FanSpeedReading(cpu, gpu, cpuTemp, gpuTemp, profile, Name, quality, DateTimeOffset.UtcNow);
        }, cancellationToken);

    public Task<FanSpeedCommandResult> SetSpeedAsync(
        FanTarget target,
        int percent,
        CancellationToken cancellationToken) =>
        Task.FromResult(new FanSpeedCommandResult(
            FanSpeedState.Unsupported,
            "ATKACPI no ofrece PWM/curvas verificadas en este modelo.",
            percent));

    public Task<FanSpeedCommandResult> RestoreAutomaticAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new FanSpeedCommandResult(
            FanSpeedState.Unsupported,
            "Sin PWM manual ATKACPI; el perfil ASUS se gestiona por separado."));

    private static int SafeRead(IntPtr handle, uint endpoint)
    {
        try { return AsusAcpiNative.DeviceGet(handle, endpoint); }
        catch { return -65536; }
    }
}

/// <summary>
/// WMI backend for the ASUS ACPI fan channel (root\wmi:AsusAtkWmi_WMNB, method DSTS). Values carry a
/// status flag decoded by <see cref="AsusWmiCodec"/>. The class signature is hardware-specific and
/// untested here, so this is fully defensive and read-only; PWM stays Unsupported.
/// </summary>
internal sealed class WmiFanSpeedBackend : IFanSpeedBackend
{
    private const uint CpuFan = 0x00110013;
    private const uint GpuFan = 0x00110014;

    public string Name => "WMI (AsusAtkWmi_WMNB)";

    public Task<FanSpeedCapabilities> DetectAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool present = Query(CpuFan) is not null || Query(GpuFan) is not null;
            return new FanSpeedCapabilities(
                Name,
                CapabilityLevel.Unsupported,
                CapabilityLevel.Unsupported,
                SupportsProfiles: false,
                TelemetryAvailable: present,
                Note: "Solo telemetría si la clase WMI responde; sin verificar en hardware.");
        }, cancellationToken);

    public Task<FanSpeedReading> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? cpu = Query(CpuFan);
            int? gpu = Query(GpuFan);
            var quality = (cpu, gpu) switch
            {
                (not null, not null) => ReadingQuality.Ok,
                (not null, null) or (null, not null) => ReadingQuality.Partial,
                _ => ReadingQuality.Invalid
            };
            return new FanSpeedReading(cpu, gpu, null, null, "unknown", Name, quality, DateTimeOffset.UtcNow);
        }, cancellationToken);

    public Task<FanSpeedCommandResult> SetSpeedAsync(
        FanTarget target,
        int percent,
        CancellationToken cancellationToken) =>
        Task.FromResult(new FanSpeedCommandResult(
            FanSpeedState.Unsupported,
            "WMI solo expone telemetría; no se escribe.",
            percent));

    public Task<FanSpeedCommandResult> RestoreAutomaticAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new FanSpeedCommandResult(FanSpeedState.Unsupported, "WMI no ofrece restauración."));

    private static int? Query(uint deviceId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AsusAtkWmi_WMNB");
            foreach (var instance in searcher.Get())
            {
                using var mo = (ManagementObject)instance;
                if (InvokeFan(mo, deviceId) is uint raw)
                {
                    return AsusWmiCodec.DecodeFanRpm(raw);
                }
            }
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        catch (Exception) { }

        return null;
    }

    private static uint? InvokeFan(ManagementObject mo, uint deviceId)
    {
        try
        {
            object[] args = { deviceId };
            object? result = mo.InvokeMethod("DSTS", args);
            if (ToUInt(result) is uint direct)
            {
                return direct;
            }

            if (args.Length > 0 && ToUInt(args[0]) is uint mutated && mutated != deviceId)
            {
                return mutated;
            }
        }
        catch (ManagementException) { }
        catch (Exception) { }

        return null;
    }

    private static uint? ToUInt(object? value)
    {
        if (value is null) return null;
        try { return Convert.ToUInt32(value); }
        catch { return null; }
    }
}

/// <summary>
/// OEM HealthyTable (AsusWinIO64.dll) backend. Historically the only fixed-PWM path, but on the
/// M3500QC firmware <c>HealthyTable_FanCounts()</c> returns -1, so it is Unsupported. Even when the OEM
/// path is detected it is never auto-promoted to Verified: writing requires an explicit, hardware-
/// validated opt-in that no build enables by default.
/// </summary>
internal sealed class HealthyTableFanSpeedBackend : IFanSpeedBackend, IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly bool _writeEnabled;
    private AsusHardware? _hardware;
    private DateTimeOffset _failedProbeAt = DateTimeOffset.MinValue;

    public HealthyTableFanSpeedBackend(bool writeEnabled = false) => _writeEnabled = writeEnabled;

    public string Name => "AsusWinIO64 (HealthyTable)";

    public Task<FanSpeedCapabilities> DetectAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hardware = Acquire();
            if (hardware is null)
            {
                return new FanSpeedCapabilities(
                    Name,
                    CapabilityLevel.Unsupported,
                    CapabilityLevel.Unsupported,
                    SupportsProfiles: false,
                    TelemetryAvailable: false,
                    Note: "HealthyTable no disponible (FanCounts inválido o DLL ausente).");
            }

            return new FanSpeedCapabilities(
                Name,
                CapabilityLevel.Unverified,
                CapabilityLevel.Unsupported,
                SupportsProfiles: false,
                TelemetryAvailable: true,
                Note: "Ruta OEM detectada pero sin verificar; escritura bloqueada.");
        }, cancellationToken);

    public Task<FanSpeedReading> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hardware = Acquire();
            if (hardware is null)
            {
                return FanSpeedReading.Invalid(Name, DateTimeOffset.UtcNow);
            }

            try
            {
                FanReading reading = hardware.Read();
                int? cpu = reading.Rpms.Length > 0 && reading.Rpms[0] >= 0 ? reading.Rpms[0] : null;
                int? gpu = reading.Rpms.Length > 1 && reading.Rpms[1] >= 0 ? reading.Rpms[1] : null;
                int? temp = reading.Temperature is int t && t >= 0 ? t : null;
                var quality = cpu is not null || gpu is not null ? ReadingQuality.Ok : ReadingQuality.Invalid;
                return new FanSpeedReading(cpu, gpu, temp, null, "unknown", Name, quality, DateTimeOffset.UtcNow);
            }
            catch
            {
                return FanSpeedReading.Invalid(Name, DateTimeOffset.UtcNow);
            }
        }, cancellationToken);

    public Task<FanSpeedCommandResult> SetSpeedAsync(
        FanTarget target,
        int percent,
        CancellationToken cancellationToken)
    {
        if (!_writeEnabled)
        {
            return Task.FromResult(new FanSpeedCommandResult(
                FanSpeedState.Unsupported,
                "PWM HealthyTable no verificado: escritura bloqueada en este equipo."));
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hardware = Acquire();
            if (hardware is null)
            {
                return new FanSpeedCommandResult(FanSpeedState.Unsupported, "HealthyTable no disponible.", percent);
            }

            try
            {
                int[] values = new int[hardware.FanCount];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = target switch
                    {
                        FanTarget.Cpu => i == 0 ? percent : 0,
                        FanTarget.Gpu => i == 1 ? percent : 0,
                        _ => percent
                    };
                }

                hardware.Apply(values);
                return new FanSpeedCommandResult(FanSpeedState.AppliedUnverified, "PWM solicitado al OEM.", percent);
            }
            catch (Exception ex)
            {
                return new FanSpeedCommandResult(FanSpeedState.Failed, ex.Message, percent);
            }
        }, cancellationToken);
    }

    public Task<FanSpeedCommandResult> RestoreAutomaticAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hardware = Acquire();
            if (hardware is null)
            {
                return new FanSpeedCommandResult(FanSpeedState.Unsupported, "HealthyTable no disponible.");
            }

            try
            {
                hardware.Restore();
                return new FanSpeedCommandResult(FanSpeedState.Restored, "PWM devuelto a BIOS.");
            }
            catch (Exception ex)
            {
                return new FanSpeedCommandResult(FanSpeedState.Failed, ex.Message);
            }
        }, cancellationToken);

    /// <summary>
    /// Returns a cached OEM handle, constructing it once. A failed construction is retried only after a
    /// cooldown so a missing driver does not trigger repeated, expensive signature scans.
    /// </summary>
    private AsusHardware? Acquire()
    {
        lock (_gate)
        {
            if (_hardware is not null)
            {
                return _hardware;
            }

            if (DateTimeOffset.UtcNow - _failedProbeAt < RetryDelay)
            {
                return null;
            }

            try
            {
                _hardware = new AsusHardware();
                return _hardware;
            }
            catch
            {
                _failedProbeAt = DateTimeOffset.UtcNow;
                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _hardware?.Dispose();
            _hardware = null;
        }
    }
}

/// <summary>
/// PawnIO placeholder. PawnIO (a signed kernel driver) is used by other tools for CPU MSR/SMU access,
/// not for fan PWM, and this project ships no validated fan module for it. It is exposed as a disabled,
/// read-only stub so the engine can list it without ever offering a write path.
/// </summary>
internal sealed class PawnIoFanSpeedBackend : IFanSpeedBackend
{
    public string Name => "PawnIO (deshabilitado)";

    public Task<FanSpeedCapabilities> DetectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new FanSpeedCapabilities(
            Name,
            CapabilityLevel.Unsupported,
            CapabilityLevel.Unsupported,
            SupportsProfiles: false,
            TelemetryAvailable: false,
            Note: "Sin módulo de ventilador validado; PawnIO no controla PWM. Deshabilitado."));

    public Task<FanSpeedReading> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(FanSpeedReading.Invalid(Name, DateTimeOffset.UtcNow));

    public Task<FanSpeedCommandResult> SetSpeedAsync(
        FanTarget target,
        int percent,
        CancellationToken cancellationToken) =>
        Task.FromResult(new FanSpeedCommandResult(
            FanSpeedState.Unsupported,
            "PawnIO no se usa para control de ventiladores."));

    public Task<FanSpeedCommandResult> RestoreAutomaticAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new FanSpeedCommandResult(FanSpeedState.Unsupported, "PawnIO deshabilitado."));
}
