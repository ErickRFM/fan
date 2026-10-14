using System.Diagnostics;
using FanForge.Core;

namespace FanForge;

/// <summary>Serialized native I/O around the OEM library. No manual-session logic lives here.</summary>
internal sealed class AsusHardware : IFanBackend
{
    private readonly object _gate = new();
    private bool _disposed;

    public int FanCount { get; }

    public static string Model()
    {
        // Registry/WMI manufacturer metadata may vary by BIOS version.
        var model = Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName", null)?.ToString();
        return string.IsNullOrWhiteSpace(model) ? "Unknown" : model;
    }

    public static string BiosVersion()
    {
        var version = Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "BIOSVersion", null)?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "desconocida" : version;
    }

    /// <summary>
    /// Locates the OEM library and returns the highest-version match. DriverStore keeps
    /// stale copies after driver updates, so the folder order is not trustworthy.
    /// </summary>
    public static string? FindOfficialDll()
    {
        var candidates = new List<DllCandidate>();
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string repository = Path.Combine(windows, "System32", "DriverStore", "FileRepository");
        const string relative = @"ASUSSystemAnalysis\AsusWinIO64.dll";
        foreach (var pattern in new[] { "asussci*.inf_amd64_*", "asus*.inf_amd64_*" })
            Scan(repository, pattern, relative, candidates);
        Add(Path.Combine(windows, "System32", "AsusWinIO64.dll"), candidates);
        return FanPolicy.SelectNewestDll(candidates)?.Path;
    }

    private static void Scan(string root, string pattern, string relative, List<DllCandidate> candidates)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, pattern))
                Add(Path.Combine(dir, relative), candidates);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Add(string dll, List<DllCandidate> candidates)
    {
        if (!File.Exists(dll)) return;
        var version = Version.TryParse(FileVersionInfo.GetVersionInfo(dll).FileVersion, out var parsed)
            ? parsed
            : new Version(0, 0);
        candidates.Add(new DllCandidate(dll, version));
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
        try
        {
            int count = AsusNative.HealthyTable_FanCounts();
            if (count < 1)
                throw new NotSupportedException(
                    "ASUS no devolvió un conteo de ventiladores válido (" + count + "). " +
                    "Suele indicar que el driver ASUS (AsusSAIO / ASUS System Control Interface) no expone datos. " +
                    "Comprueba que el Diagnóstico de ventilador de MyASUS funcione, reinicia Windows y, " +
                    "si persiste, revierte el driver ASUS. FanForge no modifica drivers ni el EC.");
            if (!FanPolicy.ValidFanCount(count))
                throw new NotSupportedException("Número de ventiladores inesperado devuelto por ASUS: " + count);
            FanCount = count;
        }
        catch
        {
            try { AsusNative.ShutdownWinIo(); } catch { }
            throw;
        }
    }

    public FanReading Read()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var rpms = new int[FanCount];
            for (byte i = 0; i < FanCount; i++)
            {
                AsusNative.HealthyTable_SetFanIndex(i);
                rpms[i] = AsusNative.HealthyTable_FanRPM();
            }
            int? temp = null;
            try
            {
                ulong value = AsusNative.Thermal_Read_Cpu_Temperature();
                if (value >= 5 && value <= 115) temp = (int)value;
            }
            catch (EntryPointNotFoundException) { }
            return new FanReading(rpms, temp);
        }
    }

    public void Apply(int[] percent)
    {
        if (percent is null || percent.Length != FanCount)
            throw new ArgumentException("Provide exactly one PWM target per detected fan.", nameof(percent));
        foreach (var value in percent) _ = FanPolicy.Duty(value);
        lock (_gate)
        {
            ThrowIfDisposed();
            Send(percent);
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
    }

    public void Restore()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
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
            _disposed = true;
            try { AsusNative.ShutdownWinIo(); } catch { }
        }
    }
}
