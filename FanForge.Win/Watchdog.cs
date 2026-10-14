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
        try
        {
            using var hardware = new AsusHardware();
            hardware.Restore();
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "FanForge-watchdog.log");
                File.AppendAllText(path, DateTimeOffset.Now.ToString("u") + " " + ex + Environment.NewLine);
            }
            catch { }
            return 2;
        }
    }
}
