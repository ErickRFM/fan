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

        if (args.Length == 1 && args[0] == "--diagnose-driver")
            return DiagnoseDriver();

        if (args.Length == 2 && args[0] == "--set-profile")
            return SetProfile(args[1]);

        if (args.Length >= 2 && args[0] == "--log-telemetry" && int.TryParse(args[1], out int logSeconds))
            return LogTelemetry(logSeconds, args.Length >= 3 ? args[2] : null);

        if (args.Length >= 2 && args[0] == "--analyze")
            return Analyze(args.Skip(1).ToArray());

        if (args.Length == 1 && args[0] == "--fan-backends")
            return FanBackendsReport();

        if (args.Length == 1 && args[0] == "--fan-capabilities")
            return FanCapabilitiesSummary();

        if (args.Length == 1 && args[0] == "--fan-read")
            return FanRead();

        if (args.Length == 1 && args[0] == "--fan-recovery-status")
            return FanRecoveryStatus();

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
            FanCapabilities controlCaps;
            using (var control = new AsusAcpiControlBackend())
                controlCaps = control.GetCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Report("Control: perfiles=" + controlCaps.SupportsProfiles + " curvas=" + controlCaps.SupportsFanCurves +
                   " pwm=" + controlCaps.SupportsPwmDirect);
            if (AsusAcpiNative.TryOpen(out var rawHandle))
            {
                try
                {
                    ReportRaw(rawHandle, "CPU_Fan(0x00110013)", AsusAcpiNative.CPU_Fan);
                    ReportRaw(rawHandle, "GPU_Fan(0x00110014)", AsusAcpiNative.GPU_Fan);
                    ReportRaw(rawHandle, "VivoBookMode(0x00110019)", AsusAcpiNative.VivoBookMode);
                    ReportRaw(rawHandle, "Temp_CPU(0x00120094)", AsusAcpiNative.Temp_CPU);
                    ReportRaw(rawHandle, "Temp_GPU(0x00120097)", AsusAcpiNative.Temp_GPU);
                    ReportRaw(rawHandle, "PerformanceMode(0x00120075)", AsusAcpiNative.PerformanceMode);
                    ReportRaw(rawHandle, "DevsCPUFanCurve(0x00110024)", AsusAcpiNative.DevsCPUFanCurve);
                    ReportRaw(rawHandle, "DevsGPUFanCurve(0x00110025)", AsusAcpiNative.DevsGPUFanCurve);
                }
                finally { AsusAcpiNative.Close(rawHandle); }
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

    /// <summary>Read-only ASUS driver/service diagnosis; never starts, stops or reconfigures anything.</summary>
    private static int DiagnoseDriver()
    {
        AttachConsole();
        Report("FanForge --diagnose-driver (solo lectura; no modifica drivers)");
        try
        {
            foreach (var line in DriverDiagnostics.Describe()) Report(line);
            Report("El control manual PWM requiere que la cadena OEM (AsusSAIO/System Control Interface) " +
                   "esté operativa. FanForge no modifica drivers ni el EC.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("diagnose-driver fallo: " + ex.GetType().Name + ": " + ex.Message);
            Report("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    /// <summary>Reads one raw endpoint, reporting per-method failures instead of aborting.</summary>
    private static void ReportRaw(IntPtr handle, string label, uint deviceId)
    {
        try { Report("Raw " + label + "=" + AsusAcpiNative.DeviceGet(handle, deviceId)); }
        catch (Exception ex) { Report("Raw " + label + "= ERROR " + ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>Applies an ASUS performance profile via ATKACPI (verified by read-back).</summary>
    private static int SetProfile(string name)
    {
        AttachConsole();
        Report("FanForge --set-profile " + name);
        if (!Enum.TryParse<FanProfile>(name, ignoreCase: true, out var profile))
        {
            Report("Perfil invalido. Usa: Whisper, Standard, Performance.");
            return 3;
        }
        if (FanPolicy.VivoBookProfileWireMode(profile) is null)
        {
            Report("Perfil no soportado por este modelo. Usa: Whisper, Standard, Performance.");
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

    /// <summary>
    /// Best-effort repair: returns manual PWM to BIOS (may be unavailable) and restores the ASUS
    /// profile captured before FanForge changed it (recovered from the persisted record if needed).
    /// </summary>
    /// <summary>
    /// Read-only CSV sampler: records RPM/temperature/profile/timestamps at ~1 Hz for the given number
    /// of seconds. It never writes PWM or changes the profile, so it is safe for the thermal comparison.
    /// </summary>
    private static int LogTelemetry(int seconds, string? directory)
    {
        AttachConsole();
        seconds = Math.Clamp(seconds, 1, 600);
        Report("FanForge --log-telemetry (solo lectura) durante " + seconds + "s");
        try
        {
            using var manager = new FanBackendManager(
                new IFanTelemetryProvider[] { new AsusAcpiTelemetry(), new AcpiThermalZoneTelemetry() },
                Array.Empty<IFanControlBackend>());
            using var recorder = new TelemetryRecorder(directory);
            var channel = new AsusAcpiProfileChannel();
            int? wire = FanPolicy.SupportedModel(AsusHardware.Model()) ? channel.ReadWireMode() : null;
            string profile = wire is int w ? FanPolicy.ProfileName(w) : "unknown";
            Report("Perfil activo: " + profile + " (wire " + (wire?.ToString() ?? "n/a") + ")");
            Report("Archivo: " + recorder.FilePath);

            var end = DateTimeOffset.UtcNow.AddSeconds(seconds);
            int rows = 0;
            while (DateTimeOffset.UtcNow < end)
            {
                var snapshot = manager.ReadBestAsync(CancellationToken.None).GetAwaiter().GetResult();
                var point = TelemetryHistory.FromSnapshot(snapshot);
                recorder.Write(point, profile, wire is not null);
                rows++;
                Thread.Sleep(1000);
            }
            Report($"CSV escrito: {rows} filas. Perfil confirmado por lectura: {wire is not null}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("log-telemetry fallo: " + ex.GetType().Name + ": " + ex.Message);
            Report("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    /// <summary>
    /// Offline thermal analyzer: reads FanForge CSV logs (and optional ProArt exports), computes
    /// statistics, and writes an HTML report and a CSV of the computed statistics. Never touches hardware.
    /// </summary>
    private static int Analyze(string[] args)
    {
        AttachConsole();
        Report("FanForge --analyze (solo lectura de archivos)");
        var fanForge = new List<string>();
        var proart = new List<string>();
        string? outDir = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--proart" when i + 1 < args.Length: proart.Add(args[++i]); break;
                default: fanForge.Add(args[i]); break;
            }
        }

        var samples = new List<TelemetrySample>();
        int files = 0;
        foreach (var (paths, source) in new[] { (fanForge, TelemetrySource.FanForge), (proart, TelemetrySource.ProArt) })
        {
            foreach (string raw in Expand(paths))
            {
                try
                {
                    var parsed = TelemetryCsvParser.Parse(File.ReadAllText(raw), source);
                    samples.AddRange(parsed.Samples);
                    files++;
                    Report($"  {Path.GetFileName(raw)} [{source}] -> {parsed.Samples.Count} filas válidas" +
                           (parsed.Quality.Issues.Count > 0 ? " · " + string.Join(" ", parsed.Quality.Issues) : string.Empty));
                }
                catch (IOException ex) { Report("AVISO: no se pudo leer " + raw + ": " + ex.Message); }
            }
        }

        if (files == 0 || samples.Count == 0)
        {
            Report("No hay datos analizables. Uso: --analyze <csv|dir> [--proart <csv>] [--out <dir>]");
            return 2;
        }

        samples.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        var result = ThermalAnalyzer.Analyze(samples);
        string directory = outDir ?? ResolveOutputDirectory(fanForge);
        Directory.CreateDirectory(directory);
        string htmlPath = Path.Combine(directory, "thermal-report.html");
        string csvPath = Path.Combine(directory, "thermal-stats.csv");
        File.WriteAllText(htmlPath, ThermalReport.Html(result, samples));
        File.WriteAllText(csvPath, ThermalReport.StatsCsv(result));

        Report("RPM CPU: " + DescribeStat(result.CpuRpm));
        Report("RPM GPU: " + DescribeStat(result.GpuRpm));
        Report("Perfiles: " + string.Join(", ", result.Profiles.Select(p =>
            $"{p.Profile}={(p.CpuRpm.HasData ? p.CpuRpm.Average.ToString("0") + " RPM" : "sin datos")}")));
        Report("Correlacion temp-RPM: " + (result.TemperatureRpmCorrelation?.ToString("0.000") ?? "no calculable") +
               " (asociacion, no causalidad)");
        Report("Informe HTML: " + htmlPath);
        Report("Estadisticas CSV: " + csvPath);
        return 0;

        List<string> Expand(List<string> paths)
        {
            var list = new List<string>();
            foreach (string p in paths)
            {
                if (Directory.Exists(p)) list.AddRange(Directory.GetFiles(p, "*.csv"));
                else if (File.Exists(p)) list.Add(p);
                else Report("AVISO: no existe " + p);
            }
            return list;
        }
    }

    private static string DescribeStat(Stat s) => s.HasData
        ? $"min={s.Min:0} max={s.Max:0} media={s.Average:0.0} p90={s.P90:0} p95={s.P95:0}"
        : "sin datos";

    private static string ResolveOutputDirectory(List<string> fanForgePaths)
    {
        foreach (string p in fanForgePaths)
        {
            string? dir = Directory.Exists(p) ? p : Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
        }
        return Environment.CurrentDirectory;
    }

    /// <summary>Read-only: lists every FanSpeed backend and its reported capabilities.</summary>
    private static int FanBackendsReport()
    {
        AttachConsole();
        Report("FanForge --fan-backends (solo lectura; no escribe PWM)");
        try
        {
            using var engine = new FanSpeedEngine(FanSpeedBackends.CreateDefault());
            var caps = engine.DetectAsync(CancellationToken.None).GetAwaiter().GetResult();
            foreach (var c in caps)
            {
                Report($"- {c.Backend}: pwm={c.PwmDirect} curvas={c.FanCurves} " +
                       $"perfiles={c.SupportsProfiles} telemetria={c.TelemetryAvailable}" +
                       (c.Note is null ? string.Empty : " · " + c.Note));
            }
            Report("Estado del motor: " + engine.State);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("fan-backends fallo: " + ex.GetType().Name + ": " + ex.Message);
            Report("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    /// <summary>Read-only: summarizes whether any verified PWM control path exists.</summary>
    private static int FanCapabilitiesSummary()
    {
        AttachConsole();
        Report("FanForge --fan-capabilities (solo lectura)");
        try
        {
            using var engine = new FanSpeedEngine(FanSpeedBackends.CreateDefault());
            var caps = engine.DetectAsync(CancellationToken.None).GetAwaiter().GetResult();
            var verified = caps.FirstOrDefault(c => c.PwmDirect == CapabilityLevel.Verified);
            bool anyTelemetry = caps.Any(c => c.TelemetryAvailable);
            Report("PWM directo verificado: " + (verified is not null ? "sí (" + verified.Backend + ")" : "no"));
            Report("Telemetría disponible: " + (anyTelemetry ? "sí" : "no"));
            Report("Estado del motor: " + engine.State);
            Report("Control manual: " + (engine.Manual ? "activo" : "inactivo") +
                   " (requiere PWM verificado; en M3500QC el firmware no lo expone).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("fan-capabilities fallo: " + ex.GetType().Name + ": " + ex.Message);
            Report("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    /// <summary>Read-only: prints one reading per backend, tolerating per-backend failures.</summary>
    private static int FanRead()
    {
        AttachConsole();
        Report("FanForge --fan-read (solo lectura; no escribe PWM)");
        try
        {
            using var engine = new FanSpeedEngine(FanSpeedBackends.CreateDefault());
            foreach (var backend in engine.Backends)
            {
                try
                {
                    var r = backend.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Report($"- {backend.Name}: CPU={r.CpuRpm?.ToString() ?? "n/a"} RPM " +
                           $"GPU={r.GpuRpm?.ToString() ?? "n/a"} RPM " +
                           $"CPU={r.CpuCelsius?.ToString() ?? "n/a"}°C GPU={r.GpuCelsius?.ToString() ?? "n/a"}°C " +
                           $"perfil={r.Profile} calidad={r.Quality}");
                }
                catch (Exception ex)
                {
                    Report($"- {backend.Name}: ERROR {ex.GetType().Name}: {ex.Message}");
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("fan-read fallo: " + ex.GetType().Name + ": " + ex.Message);
            Report("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    /// <summary>Read-only: shows the persisted recovery record and the current ASUS profile.</summary>
    private static int FanRecoveryStatus()
    {
        AttachConsole();
        Report("FanForge --fan-recovery-status (solo lectura)");
        try
        {
            var store = new FileProfileStore();
            int? original = store.Load();
            Report("Archivo de recuperación: " + FileProfileStore.FilePath);
            Report(original is int w
                ? "Perfil original guardado: " + FanPolicy.ProfileName(w) + " (wire " + w + ")"
                : "Sin perfil original pendiente de restaurar.");

            int? current = FanPolicy.SupportedModel(AsusHardware.Model())
                ? new AsusAcpiProfileChannel().ReadWireMode()
                : null;
            Report(current is int c
                ? "Perfil actual: " + FanPolicy.ProfileName(c) + " (wire " + c + ")"
                : "Perfil actual: desconocido o no soportado.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("fan-recovery-status fallo: " + ex.GetType().Name + ": " + ex.Message);
            Report("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    private static int RestoreNow()
    {
        AttachConsole();
        Report("FanForge --restore");
        int failures = 0;
        try
        {
            using var hardware = new AsusHardware();
            hardware.Restore();
            Report("PWM manual devuelto a BIOS (si estaba activo).");
        }
        catch (Exception ex)
        {
            Report("PWM: no restaurado (normal si HealthyTable no está disponible): " + ex.Message);
        }
        try
        {
            using var control = AsusAcpiControlBackend.ForRecovery();
            var result = control.RestoreAutomaticAsync(CancellationToken.None).GetAwaiter().GetResult();
            Report("Perfil ASUS: " + (result.Success ? "OK: " : "AVISO: ") + result.Message);
        }
        catch (Exception ex)
        {
            failures++;
            Report("Perfil ASUS: ERROR " + ex.GetType().Name + ": " + ex.Message);
        }
        return failures == 0 ? 0 : 2;
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
