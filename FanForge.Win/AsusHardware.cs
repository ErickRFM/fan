using FanForge.Core;

namespace FanForge;

internal sealed record FanReading(int[] Rpms, int? Temperature);

/// <summary>Serializes native I/O; restores BIOS fan mode on errors/disposal.</summary>
internal sealed class AsusHardware : IDisposable
{
    private readonly object _gate = new();
    private bool _initialized;
    private bool _disposed;
    private int[]? _targets;
    private DateTime _lastWrite = DateTime.MinValue;
    public int FanCount { get; }

    public static string Model()
    {
        // Registry/WMI manufacturer metadata may vary by BIOS version.
        var model = Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName", null)?.ToString();
        return string.IsNullOrWhiteSpace(model) ? "Unknown" : model;
    }

    public static string? FindOfficialDll()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "DriverStore", "FileRepository");
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateDirectories(root, "asussci*.inf_amd64_*")
                .Select(p => Path.Combine(p, "ASUSSystemAnalysis", "AsusWinIO64.dll"))
                .FirstOrDefault(File.Exists);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public AsusHardware()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Windows x64 required.");
        if (!FanPolicy.SupportedModel(Model()))
            throw new NotSupportedException("Only confirmed model identifier M3500QC is allowed.");
        string dll = FindOfficialDll() ?? throw new FileNotFoundException(
            "ASUS System Analysis / AsusWinIO64.dll not found in DriverStore.");
        AsusNative.Load(dll);
        AsusNative.InitializeWinIo();
        _initialized = true;
        try
        {
            int count = AsusNative.HealthyTable_FanCounts();
            if (!FanPolicy.ValidFanCount(count))
                throw new NotSupportedException("Invalid fan count returned by ASUS: " + count);
            FanCount = count;
        }
        catch { Dispose(); throw; }
    }

    public bool Manual { get { lock (_gate) return _targets != null; } }

    public FanReading Read()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_targets != null && DateTime.UtcNow - _lastWrite > TimeSpan.FromSeconds(4))
            {
                try { Send(_targets); }
                catch { TryRestore(); throw; }
            }
            var rpms = new int[FanCount];
            for (byte i = 0; i < FanCount; i++)
            {
                AsusNative.HealthyTable_SetFanIndex(i);
                rpms[i] = AsusNative.HealthyTable_FanRPM();
            }
            int? temp = null;
            try
            {
                ulong v = AsusNative.Thermal_Read_Cpu_Temperature();
                if (v >= 5 && v <= 115) temp = (int)v;
            }
            catch (EntryPointNotFoundException) { }
            return new FanReading(rpms, temp);
        }
    }

    public void Apply(int[] requested)
    {
        if (requested == null || requested.Length != FanCount)
            throw new ArgumentException("Provide exactly one PWM target per detected fan.");
        foreach (var p in requested) _ = FanPolicy.Duty(p);
        lock (_gate)
        {
            ThrowIfDisposed();
            try
            {
                Send(requested);
                _targets = (int[])requested.Clone();
            }
            catch { TryRestore(); throw; }
        }
    }

    private void Send(int[] values)
    {
        for (byte i = 0; i < values.Length; i++)
        {
            AsusNative.HealthyTable_SetFanIndex(i);
            AsusNative.HealthyTable_SetFanTestMode(1);
            AsusNative.HealthyTable_SetFanPwmDuty(FanPolicy.Duty(values[i]));
            Thread.Sleep(20);
        }
        _lastWrite = DateTime.UtcNow;
    }

    private void TryRestore()
    {
        // Does not throw away restoration errors when explicitly invoked via Restore().
        _targets = null;
        for (byte i = 0; i < FanCount; i++)
        {
            try
            {
                AsusNative.HealthyTable_SetFanIndex(i);
                AsusNative.HealthyTable_SetFanTestMode(0);
                AsusNative.HealthyTable_SetFanPwmDuty(0);
            }
            catch { /* best-effort for every fan */ }
        }
    }

    public void Restore()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _targets = null;
            List<Exception> failures = new();
            for (byte i = 0; i < FanCount; i++)
            {
                try
                {
                    AsusNative.HealthyTable_SetFanIndex(i);
                    AsusNative.HealthyTable_SetFanTestMode(0);
                    AsusNative.HealthyTable_SetFanPwmDuty(0);
                }
                catch (Exception ex) { failures.Add(ex); }
            }
            if (failures.Count > 0)
                throw new AggregateException("BIOS restoration had errors.", failures);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AsusHardware));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_targets != null) TryRestore();
            if (_initialized) { try { AsusNative.ShutdownWinIo(); } catch { } }
            _disposed = true;
        }
    }
}
