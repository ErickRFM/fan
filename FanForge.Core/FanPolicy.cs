namespace FanForge.Core;

/// <summary>Rules that can be tested without touching the embedded controller.</summary>
public static class FanPolicy
{
    public const int MinimumPercent = 70;
    public const int MaximumPercent = 100;
    public const int MaxFans = 6;

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
}
