using FanForge.Core;

namespace FanForge;

public sealed class FanBackendManager : IDisposable
{
    private readonly IFanTelemetryProvider[] _telemetry;
    private readonly List<IFanControlBackend> _controls = new();

    public FanBackendManager(IEnumerable<IFanTelemetryProvider> telemetry, IEnumerable<IFanControlBackend>? controls = null)
    {
        _telemetry = telemetry.ToArray();
        if (controls != null) _controls.AddRange(controls);
    }

    public async Task<FanCapabilities> GetBestCapabilitiesAsync(CancellationToken ct)
    {
        foreach (var p in _telemetry)
        {
            try
            {
                var c = await p.GetCapabilitiesAsync(ct);
                if (c.TelemetryState == TelemetryState.Supported || c.TelemetryState == TelemetryState.ReadOnly)
                    return c;
            }
            catch { }
        }
        return new FanCapabilities(TelemetryState.Unverified, false, false, false, false, false, false, false);
    }

    /// <summary>
    /// Reads every provider and merges the results: the first provider that yields fan RPMs
    /// wins for RPMs, while temperatures are taken from whichever provider supplies them.
    /// </summary>
    public async Task<TelemetrySnapshot> ReadBestAsync(CancellationToken ct)
    {
        var snapshots = new List<TelemetrySnapshot>(_telemetry.Length);
        foreach (var p in _telemetry)
        {
            try { snapshots.Add(await p.ReadAsync(ct)); }
            catch { }
        }
        return TelemetryMerge.Merge(snapshots);
    }

    public IEnumerable<string> TelemetryProviders => _telemetry.Select(t => t.Name);
    public IEnumerable<string> ControlProviders => _controls.Select(c => c.Name);

    public void Dispose()
    {
        foreach (var t in _telemetry) (t as IDisposable)?.Dispose();
        foreach (var c in _controls) (c as IDisposable)?.Dispose();
    }
}
