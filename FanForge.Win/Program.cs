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
        {
            try
            {
                using var hw = new AsusHardware();
                hw.Restore();
                MessageBox.Show("Se solicitó el retorno al modo automático ASUS.", "FanForge");
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "FanForge: no se pudo restaurar",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }

        using var mutex = new Mutex(true, @"LocalFanForge-M3500QC", out bool acquired);
        if (!acquired)
        {
            MessageBox.Show("FanForge ya está abierto.", "FanForge");
            return 1;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
