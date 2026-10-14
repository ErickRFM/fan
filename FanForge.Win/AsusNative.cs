using System.Runtime.InteropServices;

namespace FanForge;

/// <summary>
/// ASUS System Analysis exports. Proprietary DLL must be installed on the user's PC.
/// Never downloads or bundles a kernel driver, nor accesses raw EC registers.
/// </summary>
internal static class AsusNative
{
    private const string Dll = "AsusWinIO64.dll";
    private static readonly object Gate = new();
    private static nint _handle;
    private static bool _resolverInstalled;

    public static void Load(string path)
    {
        lock (Gate)
        {
            if (_handle != 0) return;
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
                throw new FileNotFoundException("ASUS System Analysis DLL missing", path);
            var handle = NativeLibrary.Load(path);
            try
            {
                foreach (var symbol in new[] {
                    "InitializeWinIo", "ShutdownWinIo", "HealthyTable_FanCounts",
                    "HealthyTable_SetFanIndex", "HealthyTable_FanRPM",
                    "HealthyTable_SetFanTestMode", "HealthyTable_SetFanPwmDuty" })
                    if (!NativeLibrary.TryGetExport(handle, symbol, out _))
                        throw new EntryPointNotFoundException("Missing ASUS export: " + symbol);
                if (!_resolverInstalled)
                {
                    NativeLibrary.SetDllImportResolver(typeof(AsusNative).Assembly, Resolve);
                    _resolverInstalled = true;
                }
                _handle = handle;
            }
            catch
            {
                NativeLibrary.Free(handle);
                throw;
            }
        }
    }

    private static nint Resolve(string name, System.Reflection.Assembly assembly, DllImportSearchPath? path) =>
        string.Equals(name, Dll, StringComparison.OrdinalIgnoreCase) ? _handle : 0;

    [DllImport(Dll, ExactSpelling = true)] internal static extern void InitializeWinIo();
    [DllImport(Dll, ExactSpelling = true)] internal static extern void ShutdownWinIo();
    [DllImport(Dll, ExactSpelling = true)] internal static extern int HealthyTable_FanCounts();
    [DllImport(Dll, ExactSpelling = true)] internal static extern void HealthyTable_SetFanIndex(byte index);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int HealthyTable_FanRPM();
    // Native test-mode argument is a one-byte char; explicitly marshal one byte.
    [DllImport(Dll, ExactSpelling = true)] internal static extern void HealthyTable_SetFanTestMode(byte mode);
    [DllImport(Dll, ExactSpelling = true)] internal static extern void HealthyTable_SetFanPwmDuty(short duty);
    [DllImport(Dll, ExactSpelling = true)] internal static extern ulong Thermal_Read_Cpu_Temperature();
}
