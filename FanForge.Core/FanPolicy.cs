namespace FanForge.Core;

/// <summary>Rules that can be tested without touching the embedded controller.</summary>
public static class FanPolicy
{
    public const int MinimumPercent = 70;
    public const int MaximumPercent = 100;
    public const int MaxFans = 6;

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
