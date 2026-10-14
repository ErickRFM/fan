using System.Management;
using FanForge.Core;

namespace FanForge;

/// <summary>
/// Supplemental temperature provider using the ACPI thermal zone exposed through WMI
/// (root\WMI:MSAcpi_ThermalZoneTemperature). Reported as a coarse chassis/zone temperature,
/// not an exact CPU core reading. Requires elevation.
/// </summary>
internal sealed class AcpiThermalZoneTelemetry : IFanTelemetryProvider
{
    public string Name => "ACPI thermal zone (WMI)";

    public Task<FanCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new FanCapabilities(
            TelemetryState.ReadOnly,
            false, false, false,
            false,
            false,
            false,
            false));

    public Task<TelemetrySnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            int? cpu = null;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (var o in searcher.Get())
                {
                    using var mo = (ManagementObject)o;
                    if (mo["CurrentTemperature"] is not { } raw) continue;
                    int c = (int)(Convert.ToDouble(raw) / 10d - 273.15d);
                    if (c is >= 0 and <= FanPolicy.MaxAcpiTemperature && (cpu is null || c > cpu))
                        cpu = c;
                }
            }
            catch
            {
                // Access denied or unsupported: report nothing rather than guessing.
            }
            return new TelemetrySnapshot(Array.Empty<FanRpm>(), new TemperatureReading(cpu, null), DateTimeOffset.UtcNow);
        }, cancellationToken);
    }
}
