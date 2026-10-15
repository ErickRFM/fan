using System.Globalization;
using System.Text;

namespace FanForge.Core;

/// <summary>Result of parsing a telemetry CSV: the readable samples plus a data-quality report.</summary>
public sealed record ParsedTelemetry(IReadOnlyList<TelemetrySample> Samples, DataQuality Quality);

/// <summary>RPM and temperature statistics for a single ASUS profile.</summary>
public sealed record ProfileSummary(string Profile, Stat CpuRpm, Stat GpuRpm, Stat CpuCelsius, Stat GpuCelsius);

/// <summary>
/// Descriptive analysis of a telemetry log. A high temperature/RPM correlation is reported as an
/// association only: fan speed also follows hysteresis, power draw and firmware limits, so it is
/// never presented as proof that temperature alone caused the observed speed.
/// </summary>
public sealed record ThermalAnalysisResult(
    DataQuality Quality,
    Stat CpuRpm,
    Stat GpuRpm,
    Stat CpuCelsius,
    Stat GpuCelsius,
    IReadOnlyList<ProfileSummary> Profiles,
    IReadOnlyDictionary<int, double> AverageCpuRpmByTemperature,
    double? TemperatureRpmCorrelation,
    string CorrelationNote,
    TimeSpan Duration);

/// <summary>
/// Parses FanForge telemetry CSVs and manually exported ProArt readings. Pure and disk-free so the
/// column mapping, invalid-value handling and gap detection can be unit tested without hardware.
/// </summary>
public static class TelemetryCsvParser
{
    private static readonly string[] TimestampAliases = { "timestamp", "timestamp_utc", "time", "hora", "fecha", "datetime" };
    private static readonly string[] CpuRpmAliases = { "cpu_rpm", "cpu_fan", "cpufan", "rpm_cpu", "rpm" };
    private static readonly string[] GpuRpmAliases = { "gpu_rpm", "gpu_fan", "gpufan", "rpm_gpu" };
    private static readonly string[] CpuCelsiusAliases = { "cpu_celsius", "cpu_temp", "cpu_temperature", "temp_cpu", "temperature", "temp", "temperatura", "cpu" };
    private static readonly string[] GpuCelsiusAliases = { "gpu_celsius", "gpu_temp", "gpu_temperature", "temp_gpu", "gpu" };
    private static readonly string[] ProfileAliases = { "profile", "mode", "perfil", "modo" };
    private static readonly string[] ConfirmedAliases = { "profile_confirmed", "confirmed", "confirmado" };
    private static readonly string[] NoteAliases = { "note", "nota" };

    public static readonly TimeSpan DefaultGapThreshold = TimeSpan.FromSeconds(5);

    public static ParsedTelemetry Parse(string? text, TelemetrySource source, TimeSpan? gapThreshold = null)
    {
        var rows = Tokenize(text ?? string.Empty);
        var map = rows.Count > 0 && LooksLikeHeader(rows[0]) ? BuildColumnMap(rows[0]) : DefaultColumnMap();
        int start = rows.Count > 0 && LooksLikeHeader(rows[0]) ? 1 : 0;
        map.TryAdd("timestamp", 0);

        var samples = new List<TelemetrySample>();
        int total = 0, invalid = 0, missingTimestamps = 0;

        for (int i = start; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace)) continue;
            total++;

            if (!TryParseTimestamp(Field(row, map, "timestamp"), out DateTimeOffset timestamp))
            {
                missingTimestamps++;
                continue;
            }

            int? cpuRpm = ParseReading(Field(row, map, "cpu_rpm"), 0, 20000, ref invalid);
            int? gpuRpm = ParseReading(Field(row, map, "gpu_rpm"), 0, 20000, ref invalid);
            int? cpuC = ParseReading(Field(row, map, "cpu_celsius"), -20, 150, ref invalid);
            int? gpuC = ParseReading(Field(row, map, "gpu_celsius"), -20, 150, ref invalid);

            samples.Add(new TelemetrySample(
                timestamp, cpuRpm, gpuRpm, cpuC, gpuC,
                NormalizeProfile(Field(row, map, "profile")),
                ParseBool(Field(row, map, "profile_confirmed")),
                NullIfBlank(Field(row, map, "note")),
                source));
        }

        var gaps = FindGaps(samples, gapThreshold ?? DefaultGapThreshold);
        var issues = new List<string>();
        if (missingTimestamps > 0) issues.Add($"{missingTimestamps} fila(s) sin fecha válida (descartadas).");
        if (invalid > 0) issues.Add($"{invalid} valor(es) fuera de rango o no numéricos (registrados como vacío).");
        if (gaps.Count > 0) issues.Add($"{gaps.Count} laguna(s) de datos detectada(s).");
        if (samples.Count == 0) issues.Add("No hay filas válidas en el archivo.");

        return new ParsedTelemetry(samples, new DataQuality(total, samples.Count, invalid, missingTimestamps, gaps, issues));
    }

    private static IReadOnlyList<TimeGap> FindGaps(IReadOnlyList<TelemetrySample> samples, TimeSpan threshold)
    {
        var gaps = new List<TimeGap>();
        for (int i = 1; i < samples.Count; i++)
        {
            var delta = samples[i].Timestamp - samples[i - 1].Timestamp;
            if (delta > threshold) gaps.Add(new TimeGap(samples[i - 1].Timestamp, samples[i].Timestamp, delta));
        }
        return gaps;
    }

    private static int? ParseReading(string? text, int min, int max, ref int invalid)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
        {
            invalid++;
            return null;
        }
        return value;
    }

    private static bool? ParseBool(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string v = text.Trim();
        if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0" || v.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static bool TryParseTimestamp(string? text, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
    }

    /// <summary>Collapses the free-text profile column onto the three ASUS profiles when recognisable.</summary>
    public static string NormalizeProfile(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "unknown";
        string v = raw.Trim();
        string lower = v.ToLowerInvariant();
        if (lower is "whisper" or "silent" or "silencio" or "silencioso") return "Whisper";
        if (lower is "standard" or "estándar" or "estandar" or "balanced" or "equilibrado") return "Standard";
        if (lower is "performance" or "rendimiento" or "turbo") return "Performance";
        return v;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();

    private static string Field(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> map, string key) =>
        map.TryGetValue(key, out int index) && index >= 0 && index < row.Count ? row[index] : string.Empty;

    private static Dictionary<string, int> DefaultColumnMap() => new()
    {
        ["timestamp"] = 0, ["cpu_rpm"] = 1, ["gpu_rpm"] = 2, ["cpu_celsius"] = 3,
        ["gpu_celsius"] = 4, ["profile"] = 5, ["profile_confirmed"] = 6, ["note"] = 7
    };

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> header)
    {
        var map = new Dictionary<string, int>();
        for (int i = 0; i < header.Count; i++)
        {
            string name = header[i].Trim().ToLowerInvariant().Replace(" ", "_");
            void Map(string[] aliases, string key)
            {
                if (!map.ContainsKey(key) && aliases.Contains(name)) map[key] = i;
            }
            Map(TimestampAliases, "timestamp");
            Map(CpuRpmAliases, "cpu_rpm");
            Map(GpuRpmAliases, "gpu_rpm");
            Map(CpuCelsiusAliases, "cpu_celsius");
            Map(GpuCelsiusAliases, "gpu_celsius");
            Map(ProfileAliases, "profile");
            Map(ConfirmedAliases, "profile_confirmed");
            Map(NoteAliases, "note");
        }
        return map;
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> first)
    {
        foreach (string field in first)
        {
            string v = field.Trim().ToLowerInvariant().Replace(" ", "_");
            if (TimestampAliases.Contains(v) || v.Contains("timestamp") || v is "time" or "hora") return true;
        }
        return false;
    }

    /// <summary>RFC-4180 style tokenizer: honours quotes and embedded commas/newlines.</summary>
    internal static List<List<string>> Tokenize(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, any = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(c);
            }
            else if (c == '"') { quoted = true; any = true; }
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); any = true; }
            else if (c == '\r') { }
            else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new List<string>(); any = false; }
            else { field.Append(c); any = true; }
        }
        if (any || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}

/// <summary>Computes the comparative thermal analysis from parsed samples.</summary>
public static class ThermalAnalyzer
{
    public const string CorrelationNote =
        "Correlación ≠ causalidad: las RPM dependen también de histéresis, potencia y límites térmicos del firmware.";

    public static ThermalAnalysisResult Analyze(
        IReadOnlyList<TelemetrySample> samples, DataQuality? quality = null, TimeSpan? gapThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        quality ??= new DataQuality(
            samples.Count, samples.Count, 0, 0,
            FindGaps(samples, gapThreshold ?? TelemetryCsvParser.DefaultGapThreshold),
            Array.Empty<string>());

        return new ThermalAnalysisResult(
            quality,
            Stat.From(samples.Where(s => s.CpuRpm is not null).Select(s => s.CpuRpm!.Value)),
            Stat.From(samples.Where(s => s.GpuRpm is not null).Select(s => s.GpuRpm!.Value)),
            Stat.From(samples.Where(s => s.CpuCelsius is not null).Select(s => s.CpuCelsius!.Value)),
            Stat.From(samples.Where(s => s.GpuCelsius is not null).Select(s => s.GpuCelsius!.Value)),
            Summarize(samples),
            AverageCpuRpmByTemperature(samples),
            Pearson(samples),
            CorrelationNote,
            Duration(samples));
    }

    private static IReadOnlyList<TimeGap> FindGaps(IReadOnlyList<TelemetrySample> samples, TimeSpan threshold)
    {
        var gaps = new List<TimeGap>();
        for (int i = 1; i < samples.Count; i++)
        {
            var delta = samples[i].Timestamp - samples[i - 1].Timestamp;
            if (delta > threshold) gaps.Add(new TimeGap(samples[i - 1].Timestamp, samples[i].Timestamp, delta));
        }
        return gaps;
    }

    private static TimeSpan Duration(IReadOnlyList<TelemetrySample> samples) =>
        samples.Count < 2 ? TimeSpan.Zero : samples[^1].Timestamp - samples[0].Timestamp;

    /// <summary>Per-profile statistics, ordered Whisper, Standard, Performance, then the rest.</summary>
    public static IReadOnlyList<ProfileSummary> Summarize(IReadOnlyList<TelemetrySample> samples)
    {
        string[] order = { "Whisper", "Standard", "Performance" };
        var groups = samples.GroupBy(s => s.Profile)
            .ToDictionary(g => g.Key, g => g.ToList());
        var ordered = order.Where(groups.ContainsKey)
            .Concat(groups.Keys.Where(k => !order.Contains(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

        var result = new List<ProfileSummary>();
        foreach (string profile in ordered)
        {
            var set = groups[profile];
            result.Add(new ProfileSummary(
                profile,
                Stat.From(set.Where(s => s.CpuRpm is not null).Select(s => s.CpuRpm!.Value)),
                Stat.From(set.Where(s => s.GpuRpm is not null).Select(s => s.GpuRpm!.Value)),
                Stat.From(set.Where(s => s.CpuCelsius is not null).Select(s => s.CpuCelsius!.Value)),
                Stat.From(set.Where(s => s.GpuCelsius is not null).Select(s => s.GpuCelsius!.Value))));
        }
        return result;
    }

    /// <summary>Average CPU RPM at each whole-degree CPU temperature (only rows with both values).</summary>
    public static IReadOnlyDictionary<int, double> AverageCpuRpmByTemperature(IReadOnlyList<TelemetrySample> samples)
    {
        var buckets = new SortedDictionary<int, List<int>>();
        foreach (var s in samples)
        {
            if (s.CpuCelsius is int t && s.CpuRpm is int rpm)
            {
                if (!buckets.TryGetValue(t, out var list)) buckets[t] = list = new List<int>();
                list.Add(rpm);
            }
        }
        var result = new Dictionary<int, double>();
        foreach (var (temp, rpms) in buckets) result[temp] = Math.Round(rpms.Average(), 1);
        return result;
    }

    /// <summary>Pearson correlation between CPU temperature and CPU RPM; null when undefined.</summary>
    public static double? Pearson(IReadOnlyList<TelemetrySample> samples)
    {
        var paired = samples.Where(s => s.CpuCelsius is not null && s.CpuRpm is not null)
            .Select(s => ((double)s.CpuCelsius!.Value, (double)s.CpuRpm!.Value)).ToList();
        if (paired.Count < 2) return null;

        double meanX = paired.Average(p => p.Item1);
        double meanY = paired.Average(p => p.Item2);
        double sumXY = 0, sumXX = 0, sumYY = 0;
        foreach (var (x, y) in paired)
        {
            double dx = x - meanX, dy = y - meanY;
            sumXY += dx * dy; sumXX += dx * dx; sumYY += dy * dy;
        }
        if (sumXX == 0 || sumYY == 0) return null;
        return Math.Round(sumXY / Math.Sqrt(sumXX * sumYY), 3);
    }
}
