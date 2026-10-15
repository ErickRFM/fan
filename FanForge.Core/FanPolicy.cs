namespace FanForge.Core;

/// <summary>Rules that can be tested without touching the embedded controller.</summary>
public static class FanPolicy
{
    public const int MinimumPercent = 70;
    public const int MaximumPercent = 100;
    public const int MaxFans = 6;

    /// <summary>Highest valid ATKACPI fan raw value; the raw unit is 100 RPM.</summary>
    public const int MaxAcpiFanRaw = 120;
    /// <summary>Highest plausible ATKACPI temperature in degrees Celsius.</summary>
    public const int MaxAcpiTemperature = 110;

    /// <summary>Confirmed-only model guard. Never widen without physical validation.</summary>
    public static bool SupportedModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) &&
        model.Contains("M3500QC", StringComparison.OrdinalIgnoreCase);

    public static bool ValidFanCount(int count) => count >= 1 && count <= MaxFans;

    public static short Duty(int percent)
    {
        if (percent < MinimumPercent || percent > MaximumPercent)
            throw new ArgumentOutOfRangeException(nameof(percent), "Manual fan control is limited to 70%-100%.");
        return checked((short)Math.Round(percent * 255d / 100d, MidpointRounding.AwayFromZero));
    }

    public static bool Increased(int baseline, int observed, int delta = 150) =>
        baseline >= 0 && observed > 0 && delta >= 0 && (long)observed - baseline > delta;

    /// <summary>True when at least one fan shows a clear RPM increase versus baseline.</summary>
    public static bool AnyIncreased(IReadOnlyList<int> baseline, IReadOnlyList<int> observed, int delta = 150)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(observed);
        int count = Math.Min(baseline.Count, observed.Count);
        for (int i = 0; i < count; i++)
            if (Increased(baseline[i], observed[i], delta)) return true;
        return false;
    }

    /// <summary>
    /// A stalled (or invalid) reading while manual control has been active beyond the
    /// grace period must trigger a preventive restoration to BIOS.
    /// </summary>
    public static bool NeedsPreventiveRestore(bool manual, TimeSpan sinceChange, TimeSpan grace, IReadOnlyList<int> rpms)
    {
        if (!manual || sinceChange < grace) return false;
        ArgumentNullException.ThrowIfNull(rpms);
        for (int i = 0; i < rpms.Count; i++)
            if (rpms[i] <= 0) return true;
        return false;
    }

    /// <summary>
    /// Decodes an ATKACPI fan value returned by DeviceGet. The raw unit is 100 RPM; values
    /// above <see cref="MaxAcpiFanRaw"/> or an empty negative status are treated as invalid.
    /// </summary>
    public static int AcpiFanRpm(int raw)
    {
        int value = raw & 0xFFFF;
        if (value > MaxAcpiFanRaw || (value == 0 && raw < 0)) return -1;
        return value * 100;
    }

    /// <summary>Decodes an ATKACPI temperature; out-of-range values are treated as unavailable.</summary>
    public static int AcpiTemperature(int raw) =>
        raw is >= 0 and <= MaxAcpiTemperature ? raw : -1;

    /// <summary>
    /// ATKACPI VivoBook encodes Turbo/Silent inverted relative to the internal performance
    /// bases, so writing a mode requires swapping 1 and 2.
    /// </summary>
    public static int VivoBookWireMode(int baseMode) => baseMode switch { 1 => 2, 2 => 1, _ => baseMode };

    /// <summary>Only three VivoBook profiles are known on M3500QC. No separate MAX/PWM.</summary>
    public static int? VivoBookProfileWireMode(FanProfile profile) => profile switch
    {
        FanProfile.Whisper => 1,
        FanProfile.Standard => 0,
        FanProfile.Performance => 2,
        _ => null
    };

    /// <summary>Maps a wire mode back to its profile name for reporting (unknown stays explicit).</summary>
    public static string ProfileName(int wireMode) => wireMode switch
    {
        0 => "Standard",
        1 => "Whisper",
        2 => "Performance",
        _ => "unknown"
    };

    /// <summary>Rejects invalid API sentinel values even for balanced mode (0).</summary>
    public static bool VivoBookProfileConfirmed(int raw, int wireMode) =>
        raw >= 0 && wireMode is >= 0 and <= 2 && (raw & 0xFFFF) == wireMode;

    /// <summary>Selects the highest-version OEM library; stale DriverStore folders must never win.</summary>
    public static DllCandidate? SelectNewestDll(IEnumerable<DllCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        DllCandidate? best = null;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Path)) continue;
            if (best is null || candidate.Version > best.Value.Version) best = candidate;
        }
        return best;
    }
}

/// <summary>An OEM library found on disk together with its file version.</summary>
public readonly record struct DllCandidate(string Path, Version Version);
