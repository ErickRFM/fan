using System.Runtime.InteropServices;
using FanForge.Core;

namespace FanForge;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--watchdog" &&
            int.TryParse(args[1], out int pid) && long.TryParse(args[2], out long ticks))
            return Watchdog.Run(pid, ticks);

        if (args.Length == 1 && args[0] == "--restore")
            return RestoreNow();

        if (args.Length == 1 && args[0] == "--probe")
            return Probe();

        if (args.Length == 1 && args[0] == "--hardware-diagnose")
            return HardwareDiagnose();

        if (args.Length == 2 && args[0] == "--set-profile")
            return SetProfile(args[1]);

        using var mutex = new Mutex(true, @"Local\FanForge-M3500QC", out bool acquired);
        if (!acquired)
        {
            MessageBox.Show("FanForge ya está abierto.", "FanForge");
            return 1;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>Read-only diagnostic: never enables manual control. Requires elevation.</summary>
    private static int Probe()
    {
        AttachConsole();
        Report("FanForge probe (solo lectura; no escribe PWM)");
        try
        {
            Report("Modelo: " + AsusHardware.Model());
            Report("BIOS: " + AsusHardware.BiosVersion());
            string? dll = AsusHardware.FindOfficialDll();
            Report("DLL OEM: " + (dll ?? "no encontrada"));
            using var hardware = new AsusHardware();
            Report("Ventiladores detectados: " + hardware.FanCount);
            FanReading reading = hardware.Read();
            Report("RPM: " + string.Join(" / ", reading.Rpms));
            Report("CPU: " + (reading.Temperature is int t ? t + " °C" : "sin lectura OEM"));
            Report("Probe completado; control manual NO activado.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Probe falló: " + ex.GetType().Name + ": " + ex.Message);
            Report("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    /// <summary>Read-only multibackend diagnostic: probes ACPI/ATKACPI telemetry without writing.</summary>
    private static int HardwareDiagnose()
    {
        AttachConsole();
        Report("FanForge hardware-diagnose (read-only; no escribe PWM)");
        var conflicts = FanToolConflictDetector.Running();
        Report(conflicts.Count == 0
            ? "Conflictos: ninguna herramienta ASUS de control en ejecucion."
            : "Conflictos: " + string.Join(", ", conflicts) + " (pueden competir por el control).");
        try
        {
            using var manager = new FanBackendManager(
                new IFanTelemetryProvider[] { new AsusAcpiTelemetry(), new AcpiThermalZoneTelemetry() },
                Array.Empty<IFanControlBackend>());
            Report("Proveedores de telemetria: " + string.Join(", ", manager.TelemetryProviders));
            var caps = manager.GetBestCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Report("Estado: " + caps.TelemetryState);
            Report("CPU/GPU soportados: " + caps.SupportsCpuFan + "/" + caps.SupportsGpuFan);
            var snap = manager.ReadBestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Report("RPM: " + string.Join(" / ", snap.Rpms.Select(r => r.Type + "=" + r.Value)));
            Report("Temperaturas: CPU=" + (snap.Temperatures.CpuCelsius?.ToString() ?? "n/a") +
                   " GPU=" + (snap.Temperatures.GpuCelsius?.ToString() ?? "n/a"));
            using var control = new AsusAcpiControlBackend();
            var controlCaps = control.GetCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Report("Control: perfiles=" + controlCaps.SupportsProfiles + " curvas=" + controlCaps.SupportsFanCurves +
                   " pwm=" + controlCaps.SupportsPwmDirect);
            if (AsusAcpiNative.TryOpen(out var rawHandle))
            {
                Report("Raw Temp_CPU(0x00120094)=" + AsusAcpiNative.DeviceGet(rawHandle, AsusAcpiNative.Temp_CPU));
                Report("Raw Temp_GPU(0x00120097)=" + AsusAcpiNative.DeviceGet(rawHandle, AsusAcpiNative.Temp_GPU));
                Report("Raw CPU_Fan(0x00110013)=" + AsusAcpiNative.DeviceGet(rawHandle, AsusAcpiNative.CPU_Fan));
                Report("Raw GPU_Fan(0x00110014)=" + AsusAcpiNative.DeviceGet(rawHandle, AsusAcpiNative.GPU_Fan));
                Report("Raw VivoBookMode(0x00110019)=" + AsusAcpiNative.DeviceGet(rawHandle, AsusAcpiNative.VivoBookMode));
                Report("Raw PerformanceMode(0x00120075)=" + AsusAcpiNative.DeviceGet(rawHandle, AsusAcpiNative.PerformanceMode));
                Report("Raw DevsCPUFanCurve(0x00110024)=" + AsusAcpiNative.DeviceGet(rawHandle, AsusAcpiNative.DevsCPUFanCurve));
                Report("Raw DevsGPUFanCurve(0x00110025)=" + AsusAcpiNative.DeviceGet(rawHandle, AsusAcpiNative.DevsGPUFanCurve));
                AsusAcpiNative.Close(rawHandle);
            }
            Report("Hardware-diagnose completado; control manual NO activado.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("hardware-diagnose fallo: " + ex.GetType().Name + ": " + ex.Message);
            Report("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    /// <summary>Applies an ASUS performance profile via ATKACPI (verified by read-back).</summary>
    private static int SetProfile(string name)
    {
        AttachConsole();
        Report("FanForge --set-profile " + name);
        if (!Enum.TryParse<FanProfile>(name, ignoreCase: true, out var profile))
        {
            Report("Perfil invalido. Usa: Auto, Whisper, Standard, Performance, Max.");
            return 3;
        }
        try
        {
            using var control = new AsusAcpiControlBackend();
            var result = control.ApplyAsync(new FanControlRequest(profile, null, null), CancellationToken.None)
                .GetAwaiter().GetResult();
            Report((result.Success ? "OK: " : "FALLO: ") + result.Message);
            return result.Success ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("set-profile fallo: " + ex.GetType().Name + ": " + ex.Message);
            Report("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    private static int RestoreNow()
    {
        AttachConsole();
        Report("FanForge --restore");
        try
        {
            using var hardware = new AsusHardware();
            hardware.Restore();
            Report("Restauración solicitada: modo automático ASUS.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("No se pudo restaurar: " + ex.Message);
            Report("ERROR: " + ex.Message);
            return 2;
        }
    }

    /// <summary>Writes to console (when attached) and to a persistent CLI log for evidence.</summary>
    private static void Report(string line)
    {
        Console.WriteLine(line);
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanForge");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "fanforge-cli.log"),
                DateTimeOffset.Now.ToString("u") + " " + line + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    /// <summary>GUI-subsystem exes have no console; attach to the caller's when present.</summary>
    private static void AttachConsole()
    {
        try
        {
            if (AttachConsole(AttachParentProcess))
            {
                var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(stdout);
            }
        }
        catch (IOException) { }
    }
}
