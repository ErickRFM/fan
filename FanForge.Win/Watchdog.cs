using System.Diagnostics;

namespace FanForge;

/// <summary>Separate observer process. Best effort, not a hardware-rated failsafe.</summary>
internal static class Watchdog
{
    public static Process Start()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("No executable path");
        using var current = Process.GetCurrentProcess();
        string args = "--watchdog " + current.Id + " " + current.StartTime.ToUniversalTime().Ticks;
        return Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory
        }) ?? throw new InvalidOperationException("Cannot launch watchdog.");
    }

    public static int Run(int pid, long ticks)
    {
        while (true)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited || p.StartTime.ToUniversalTime().Ticks != ticks) break;
            }
            catch (ArgumentException) { break; }
            catch (InvalidOperationException) { break; }
            catch (System.ComponentModel.Win32Exception) { break; }
            Thread.Sleep(1300);
        }
        bool failed = false;
        // 1) Return manual PWM to BIOS (best effort; HealthyTable may be unavailable).
        try
        {
            using var hardware = new AsusHardware();
            hardware.Restore();
        }
        catch (Exception ex) { Log("PWM", ex); } // HealthyTable being unavailable is expected here.
        // 2) Restore the ASUS profile captured before FanForge changed it, from the persisted record.
        try
        {
            using var control = AsusAcpiControlBackend.ForRecovery();
            var result = control.RestoreAutomaticAsync(CancellationToken.None).GetAwaiter().GetResult();
            Log("Perfil", null, result.Message);
        }
        catch (Exception ex) { failed = true; Log("Perfil", ex); }
        return failed ? 2 : 0;
    }

    private static bool Log(string scope, Exception? ex, string? note = null)
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FanForge-watchdog.log");
            string line = DateTimeOffset.Now.ToString("u") + " " + scope +
                (note is null ? "" : ": " + note) +
                (ex is null ? "" : ": " + ex) + Environment.NewLine;
            File.AppendAllText(path, line);
        }
        catch { }
        return ex is null;
    }
}
