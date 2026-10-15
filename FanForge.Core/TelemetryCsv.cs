using System.Globalization;

namespace FanForge.Core;

/// <summary>
/// Formats telemetry samples as CSV. Pure and side-effect free so the exact column layout used for
/// the reproducible thermal comparison can be unit tested without a disk or hardware.
/// A confirmed profile change (read-back) is kept in its own column and never conflated with the
/// observed RPM in the RPM columns: a confirmed write is not a measured speed increase.
/// </summary>
public static class TelemetryCsv
{
    public const string Header =
        "timestamp_utc,cpu_rpm,gpu_rpm,cpu_celsius,gpu_celsius,profile,profile_confirmed,note";

    public static string Row(HistoryPoint point, string? profile, bool? profileConfirmed, string? note = null) =>
        Row(point.Timestamp, point.CpuRpm, point.GpuRpm, point.CpuCelsius, point.GpuCelsius,
            profile, profileConfirmed, note);

    public static string Row(
        DateTimeOffset timestampUtc,
        int? cpuRpm, int? gpuRpm, int? cpuCelsius, int? gpuCelsius,
        string? profile, bool? profileConfirmed, string? note)
    {
        string profileText = string.IsNullOrWhiteSpace(profile) ? "unknown" : profile.Trim();
        string confirmedText = profileConfirmed is null ? "" : (profileConfirmed.Value ? "true" : "false");
        return string.Join(',',
            Escape(timestampUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)),
            Number(cpuRpm), Number(gpuRpm), Number(cpuCelsius), Number(gpuCelsius),
            Escape(profileText), confirmedText, Escape(note));
    }

    private static string Number(int? value) =>
        value is int v ? v.ToString(CultureInfo.InvariantCulture) : "";

    /// <summary>RFC-4180 style escaping: quotes fields containing a comma, quote or newline.</summary>
    public static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        return field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;
    }

    /// <summary>A confirmed profile write is recorded distinctly from an unverified one.</summary>
    public static string ProfileChangeNote(FanProfile profile, bool confirmed) =>
        (confirmed ? "profile_change_confirmed=" : "profile_change_unverified=") + profile;

    /// <summary>Records whether an actual RPM increase was measured, not merely requested.</summary>
    public static string ObservedNote(bool increased) =>
        increased ? "rpm_increase_observed" : "rpm_increase_not_observed";
}
