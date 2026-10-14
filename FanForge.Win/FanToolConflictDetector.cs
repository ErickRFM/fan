using System.Diagnostics;
using FanForge.Core;

namespace FanForge;

/// <summary>Enumerates running processes and reports OEM tools that can conflict with FanForge.</summary>
internal static class FanToolConflictDetector
{
    public static IReadOnlyList<string> Running()
    {
        var names = new List<string>();
        foreach (Process process in Process.GetProcesses())
        {
            try { names.Add(process.ProcessName); }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        return ControlConflicts.Detect(names);
    }
}
