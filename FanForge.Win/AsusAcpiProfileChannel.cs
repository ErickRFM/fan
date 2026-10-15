using FanForge.Core;

namespace FanForge;

/// <summary>
/// ATKACPI implementation of the VivoBook profile channel. Each call owns its native handle so a
/// concurrent probe cannot close another operation's handle. Reads/writes are best effort and any
/// firmware failure is reported as "not available" rather than an exception.
/// </summary>
internal sealed class AsusAcpiProfileChannel : IProfileChannel
{
    public int? ReadWireMode()
    {
        if (!AsusAcpiNative.TryOpen(out var handle)) return null;
        try
        {
            int raw = AsusAcpiNative.DeviceGet(handle, AsusAcpiNative.VivoBookMode);
            if (raw < 0) return null;
            int wire = raw & 0xFFFF;
            return wire is >= 0 and <= 2 ? wire : null;
        }
        catch { return null; }
        finally { AsusAcpiNative.Close(handle); }
    }

    public bool WriteWireMode(int wireMode)
    {
        if (wireMode is < 0 or > 2) return false;
        if (!AsusAcpiNative.TryOpen(out var handle)) return false;
        try
        {
            AsusAcpiNative.DeviceSet(handle, AsusAcpiNative.VivoBookMode, wireMode);
            int after = AsusAcpiNative.DeviceGet(handle, AsusAcpiNative.VivoBookMode);
            return FanPolicy.VivoBookProfileConfirmed(after, wireMode);
        }
        catch { return false; }
        finally { AsusAcpiNative.Close(handle); }
    }
}
