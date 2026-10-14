namespace FanForge.Core;

/// <summary>Health of the separate watchdog observer process, as seen by the UI process.</summary>
public enum WatchdogHealth
{
    NotStarted,
    Active,
    Lost
}

/// <summary>
/// Bidirectional watchdog rules. The observer restores BIOS when the UI dies; the UI must
/// also notice a lost observer and hand control back safely instead of holding manual PWM.
/// Pure so the decision is testable without processes.
/// </summary>
public static class WatchdogPolicy
{
    public static WatchdogHealth Assess(bool started, bool exited) =>
        !started ? WatchdogHealth.NotStarted : exited ? WatchdogHealth.Lost : WatchdogHealth.Active;

    /// <summary>Manual PWM must not continue unsupervised: a lost observer forces a restore.</summary>
    public static bool ShouldRestoreOnLost(WatchdogHealth health, bool manualActive) =>
        health == WatchdogHealth.Lost && manualActive;

    public static string Describe(WatchdogHealth health, int? pid = null) => health switch
    {
        WatchdogHealth.Active => pid is int value ? "Watchdog: activo (PID " + value + ")" : "Watchdog: activo",
        WatchdogHealth.Lost => "Watchdog: PERDIDO",
        _ => "Watchdog: inactivo"
    };
}
