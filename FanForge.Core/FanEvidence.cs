using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FanForge.Core;

/// <summary>
/// One read-only sample of the fan-control environment: what a backend reported for capabilities, RPM
/// and temperatures at a point in time. Pure data with no write intent — an evidence sheet can be
/// produced on any machine, including one where manual control is blocked.
/// </summary>
public sealed record FanEvidenceObservation(
    DateTimeOffset Timestamp,
    string Model,
    string Bios,
    string Backend,
    CapabilityLevel PwmCpu,
    CapabilityLevel PwmGpu,
    bool TelemetryAvailable,
    int? CpuRpm,
    int? GpuRpm,
    int? CpuCelsius,
    int? GpuCelsius,
    string Profile,
    ReadingQuality Quality);

/// <summary>
/// Outcome of the physical validation protocol. Only <see cref="Verified"/> permits treating a
/// per-fan PWM channel as operational; every weaker state keeps manual control disabled.
/// </summary>
public enum PhysicalControlVerdict
{
    /// <summary>No backend exposes any control path; there is nothing to validate.</summary>
    Blocked,

    /// <summary>A control path is exposed but the RPM response has not been confirmed twice.</summary>
    InsufficientEvidence,

    /// <summary>The RPM responded, but safe automatic restore is not yet demonstrated.</summary>
    RpmResponded,

    /// <summary>RPM response confirmed twice AND automatic restore (incl. watchdog/resume) verified.</summary>
    Verified
}

/// <summary>
/// Formats fan-control evidence as a deterministic CSV sheet plus a JSON manifest, and turns the
/// acceptance criteria of the physical protocol into an explicit verdict. Pure and side-effect free so
/// the exact layout and the decision rule can be unit tested without a fan, a driver or a disk.
/// </summary>
public static class FanEvidence
{
    public const string CsvHeader =
        "timestamp_utc,model,bios,backend,pwm_cpu,pwm_gpu,telemetry," +
        "cpu_rpm,gpu_rpm,cpu_celsius,gpu_celsius,profile,quality";

    /// <summary>Independent readings required before an RPM response counts as a physical confirmation.</summary>
    public const int MinimumConfirmations = 2;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>True when any backend exposes something beyond <see cref="CapabilityLevel.Unsupported"/>.</summary>
    public static bool ControlPathExposed(IEnumerable<FanEvidenceObservation> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var row in rows)
        {
            if (row.PwmCpu != CapabilityLevel.Unsupported || row.PwmGpu != CapabilityLevel.Unsupported)
            {
                return true;
            }
        }

        return false;
    }

    public static string CsvRow(FanEvidenceObservation o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return string.Join(',',
            o.Timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            TelemetryCsv.Escape(o.Model),
            TelemetryCsv.Escape(o.Bios),
            TelemetryCsv.Escape(o.Backend),
            o.PwmCpu,
            o.PwmGpu,
            o.TelemetryAvailable ? "true" : "false",
            Num(o.CpuRpm),
            Num(o.GpuRpm),
            Num(o.CpuCelsius),
            Num(o.GpuCelsius),
            TelemetryCsv.Escape(o.Profile),
            o.Quality);
    }

    public static string Csv(IEnumerable<FanEvidenceObservation> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var builder = new StringBuilder();
        builder.Append(CsvHeader).Append('\n');
        foreach (var row in rows)
        {
            builder.Append(CsvRow(row)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// JSON manifest: the distinct backends seen and the decision verdict. It is a summary companion to
    /// the CSV rows, not a per-sample dump, so it stays small for a support attachment.
    /// </summary>
    public static string ToJson(IEnumerable<FanEvidenceObservation> rows, PhysicalControlVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var document = new EvidenceDocument(
            DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            FirstNonEmpty(rows, r => r.Model),
            FirstNonEmpty(rows, r => r.Bios),
            rows.Count(),
            ControlPathExposed(rows),
            verdict.ToString(),
            DistinctBackends(rows));
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    /// <summary>
    /// Decision rule from the acceptance criteria: a channel is only <see cref="PhysicalControlVerdict.Verified"/>
    /// when it is exposed, the RPM response was confirmed in at least two independent readings, and the
    /// automatic restore was demonstrated (normal restore, watchdog recovery and resume). Anything less
    /// never reaches Verified.
    /// </summary>
    public static PhysicalControlVerdict Evaluate(
        bool controlPathExposed,
        int confirmedReadings,
        bool restoreVerified,
        bool watchdogVerified,
        bool resumeVerified)
    {
        if (!controlPathExposed)
        {
            return PhysicalControlVerdict.Blocked;
        }

        if (confirmedReadings < MinimumConfirmations)
        {
            return PhysicalControlVerdict.InsufficientEvidence;
        }

        return restoreVerified && watchdogVerified && resumeVerified
            ? PhysicalControlVerdict.Verified
            : PhysicalControlVerdict.RpmResponded;
    }

    public static PhysicalControlVerdict Evaluate(
        IEnumerable<FanEvidenceObservation> rows,
        int confirmedReadings,
        bool restoreVerified,
        bool watchdogVerified,
        bool resumeVerified) =>
        Evaluate(ControlPathExposed(rows), confirmedReadings, restoreVerified, watchdogVerified, resumeVerified);

    /// <summary>First non-empty, non-"unknown" value for a field, so the manifest names the machine.</summary>
    private static string FirstNonEmpty(IEnumerable<FanEvidenceObservation> rows, Func<FanEvidenceObservation, string> select)
    {
        foreach (var row in rows)
        {
            string value = select(row);
            if (!string.IsNullOrWhiteSpace(value) && value != "unknown")
            {
                return value;
            }
        }

        return "unknown";
    }

    private static IReadOnlyList<BackendDocument> DistinctBackends(IEnumerable<FanEvidenceObservation> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BackendDocument>();
        foreach (var row in rows)
        {
            if (!seen.Add(row.Backend))
            {
                continue;
            }

            result.Add(new BackendDocument(
                row.Backend,
                row.PwmCpu.ToString(),
                row.PwmGpu.ToString(),
                row.TelemetryAvailable));
        }

        return result;
    }

    private static string FirstValue(IEnumerable<FanEvidenceObservation> rows, Func<FanEvidenceObservation, string> pick)
    {
        foreach (var row in rows)
        {
            string value = pick(row);
            if (!string.IsNullOrWhiteSpace(value) && value != "unknown")
            {
                return value;
            }
        }

        return "unknown";
    }

    private static string Num(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private sealed record EvidenceDocument(
        [property: JsonPropertyName("generated_utc")] string GeneratedUtc,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("bios")] string Bios,
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("control_path_exposed")] bool ControlPathExposed,
        [property: JsonPropertyName("verdict")] string Verdict,
        [property: JsonPropertyName("backends")] IReadOnlyList<BackendDocument> Backends);

    private sealed record BackendDocument(
        [property: JsonPropertyName("backend")] string Backend,
        [property: JsonPropertyName("pwm_cpu")] string PwmCpu,
        [property: JsonPropertyName("pwm_gpu")] string PwmGpu,
        [property: JsonPropertyName("telemetry_available")] bool TelemetryAvailable);
}
