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
        try
        {
            Console.WriteLine("FanForge probe (solo lectura; no escribe PWM)");
            Console.WriteLine("Modelo: " + AsusHardware.Model());
            Console.WriteLine("BIOS: " + AsusHardware.BiosVersion());
            string? dll = AsusHardware.FindOfficialDll();
            Console.WriteLine("DLL OEM: " + (dll ?? "no encontrada"));
            using var hardware = new AsusHardware();
            Console.WriteLine("Ventiladores detectados: " + hardware.FanCount);
            FanReading reading = hardware.Read();
            Console.WriteLine("RPM: " + string.Join(" / ", reading.Rpms));
            Console.WriteLine("CPU: " + (reading.Temperature is int t ? t + " °C" : "sin lectura OEM"));
            Console.WriteLine("Probe completado; control manual NO activado.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Probe falló: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    private static int RestoreNow()
    {
        AttachConsole();
        try
        {
            using var hardware = new AsusHardware();
            hardware.Restore();
            Console.WriteLine("Restauración solicitada: modo automático ASUS.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("No se pudo restaurar: " + ex.Message);
            return 2;
        }
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
