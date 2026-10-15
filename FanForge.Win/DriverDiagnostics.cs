using System.Management;

namespace FanForge;

/// <summary>
/// Read-only diagnostics for the ASUS kernel driver/service chain. It never starts, stops or
/// reconfigures a driver and never touches the EC; it only reports what Windows already reports.
/// </summary>
internal static class DriverDiagnostics
{
    private static readonly string[] Names = { "AsusSAIO", "AsusOptimization", "AsIO3", "AsIO2" };

    public static IReadOnlyList<string> Describe()
    {
        var lines = new List<string>();
        foreach (var name in Names) lines.Add(DescribeService(name));
        lines.AddRange(AsusDevices());
        return lines;
    }

    private static string DescribeService(string name)
    {
        try
        {
            string service = "SELECT Name, State, Status, StartMode FROM Win32_Service WHERE Name='" + name + "'";
            using var searcher = new ManagementObjectSearcher(service);
            foreach (var o in searcher.Get())
            {
                using var mo = (ManagementObject)o;
                return $"Servicio {name}: State={mo["State"]} Status={mo["Status"]} StartMode={mo["StartMode"]}";
            }
            string driver = "SELECT Name, State, Status, StartMode FROM Win32_SystemDriver WHERE Name='" + name + "'";
            using var drivers = new ManagementObjectSearcher(driver);
            foreach (var o in drivers.Get())
            {
                using var mo = (ManagementObject)o;
                return $"Driver {name}: State={mo["State"]} Status={mo["Status"]} StartMode={mo["StartMode"]}";
            }
            return $"Servicio/driver {name}: no instalado o no visible.";
        }
        catch (ManagementException ex)
        {
            return $"Servicio/driver {name}: consulta no disponible ({ex.ErrorCode}).";
        }
        catch (UnauthorizedAccessException)
        {
            return $"Servicio/driver {name}: acceso denegado (requiere elevación).";
        }
    }

    private static IEnumerable<string> AsusDevices()
    {
        var lines = new List<string>();
        try
        {
            const string query =
                "SELECT Name, Status, ConfigManagerErrorCode FROM Win32_PnPEntity " +
                "WHERE Name LIKE '%ASUS%' OR Name LIKE '%ATK%'";
            using var searcher = new ManagementObjectSearcher(query);
            foreach (var o in searcher.Get())
            {
                using var mo = (ManagementObject)o;
                lines.Add($"Dispositivo: {mo["Name"]} Status={mo["Status"]} Error={mo["ConfigManagerErrorCode"]}");
            }
            if (lines.Count == 0) lines.Add("Dispositivo ASUS/ATK: no enumerado por WMI.");
        }
        catch (ManagementException ex)
        {
            lines.Add("Dispositivo ASUS/ATK: consulta no disponible (" + ex.ErrorCode + ").");
        }
        catch (UnauthorizedAccessException)
        {
            lines.Add("Dispositivo ASUS/ATK: acceso denegado (requiere elevación).");
        }
        return lines;
    }
}
