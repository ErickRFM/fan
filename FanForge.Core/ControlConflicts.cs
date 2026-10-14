namespace FanForge.Core;

/// <summary>
/// Recognises OEM utilities that can fight FanForge for fan control.
/// Pure string logic so it can be tested without enumerating real processes.
/// </summary>
public static class ControlConflicts
{
    /// <summary>Executable base names (without extension) of known ASUS fan/thermal managers.</summary>
    public static readonly IReadOnlyList<string> KnownTools = new[]
    {
        "MyASUS",
        "MyASUSSystemDiagnosis",
        "ArmouryCrate",
        "ArmouryCrateService",
        "ArmouryCrate.UserSessionHelper",
        "AsusOptimization",
        "ASUSOptimization",
        "GHelper",
        "G-Helper"
    };

    /// <summary>True when a process name matches a known competing tool (case-insensitive, .exe optional).</summary>
    public static bool IsConflict(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        string name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        foreach (string known in KnownTools)
            if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Distinct known tools present in the given process names, de-duplicated and ordered.</summary>
    public static IReadOnlyList<string> Detect(IEnumerable<string?> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? name in processNames)
        {
            if (!IsConflict(name)) continue;
            string trimmed = name!.Trim();
            found.Add(trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed);
        }
        return found.ToList();
    }

    /// <summary>Human-readable advisory; empty when nothing conflicts.</summary>
    public static string Advisory(IReadOnlyList<string> running)
    {
        ArgumentNullException.ThrowIfNull(running);
        if (running.Count == 0) return string.Empty;
        return "Herramienta(s) ASUS en ejecución que pueden competir por el control: " +
               string.Join(", ", running) +
               ". Ciérralas para un control fiable de los ventiladores.";
    }
}
