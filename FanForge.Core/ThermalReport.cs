using System.Globalization;
using System.Text;

namespace FanForge.Core;

/// <summary>A named, coloured series for the SVG charts.</summary>
public sealed record ChartSeries(string Name, string Color, IReadOnlyList<double?> Values);

/// <summary>
/// Renders a <see cref="ThermalAnalysisResult"/> as a self-contained HTML report (with inline SVG
/// charts) and a machine-readable statistics CSV. Pure string generation so the report can be tested
/// without a browser or a disk.
/// </summary>
public static class ThermalReport
{
    private static readonly string[] ProfileOrder = { "Whisper", "Standard", "Performance" };

    public static string Html(
        ThermalAnalysisResult result,
        IReadOnlyList<TelemetrySample> samples,
        string title = "FanForge — Análisis térmico",
        DateTimeOffset? generatedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(samples);

        var sb = new StringBuilder();
        string generated = (generatedUtc ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("u", CultureInfo.InvariantCulture);

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"es\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>").Append(Escape(title)).AppendLine("</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;background:#0e131e;color:#e8f2fc;margin:24px}");
        sb.AppendLine("h1{color:#45d4c4;font-size:22px}h2{color:#89b4e0;font-size:16px;margin-top:28px}");
        sb.AppendLine("table{border-collapse:collapse;margin:8px 0}th,td{border:1px solid #2b3d51;padding:6px 10px;font-size:13px}");
        sb.AppendLine("th{background:#1b2637;color:#9fc0e8}td.num{text-align:right;font-variant-numeric:tabular-nums}");
        sb.AppendLine(".caveat{background:#3a2f14;color:#f0b45a;padding:10px 14px;border-left:4px solid #f0b45a;margin:12px 0}");
        sb.AppendLine(".muted{color:#a5b7ca;font-size:12px}svg{background:#131c29;border:1px solid #2b3d51;margin:6px 0}");
        sb.AppendLine("</style></head><body>");

        sb.Append("<h1>").Append(H(title)).Append("</h1>");
        sb.Append("<p class=\"muted\">Generado: ").Append(H(generated)).Append(" · Muestra: ")
          .Append(samples.Count).Append(" filas · Duración: ").Append(H(Duration(samples))).Append("</p>");

        var sourceCounts = samples.GroupBy(s => s.Source).Select(g => $"{g.Key}={g.Count()}");
        sb.Append("<p class=\"muted\">Fuentes: ").Append(H(string.Join(", ", sourceCounts))).Append("</p>");

        AppendQuality(sb, result.Quality);
        AppendOverall(sb, result);
        AppendCorrelation(sb, result);
        AppendProfiles(sb, result);
        AppendCharts(sb, samples);

        sb.Append("<div class=\"caveat\">").Append(H(result.CorrelationNote)).Append("</div>");
        sb.Append("<p class=\"muted\">Informe experimental de FanForge. No demuestra causalidad ni compatibilidad oficial de ASUS.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendQuality(StringBuilder sb, DataQuality q)
    {
        sb.Append("<h2>Calidad de datos</h2><table>");
        Row(sb, "Filas leídas", q.TotalRows);
        Row(sb, "Filas válidas", q.ValidRows);
        Row(sb, "Valores inválidos", q.InvalidValues);
        Row(sb, "Filas sin fecha", q.MissingTimestamps);
        Row(sb, "Lagunas de datos", q.Gaps.Count);
        sb.Append("</table>");
        if (q.Issues.Count > 0)
        {
            sb.Append("<ul class=\"muted\">");
            foreach (string issue in q.Issues) sb.Append("<li>").Append(H(issue)).Append("</li>");
            sb.Append("</ul>");
        }
        if (q.Gaps.Count > 0)
        {
            sb.Append("<table><tr><th>Desde</th><th>Hasta</th><th>Duración</th></tr>");
            foreach (var gap in q.Gaps.Take(40))
                sb.Append("<tr><td class=\"num\">").Append(H(gap.From.ToString("u"))).Append("</td><td class=\"num\">")
                  .Append(H(gap.To.ToString("u"))).Append("</td><td class=\"num\">")
                  .Append(gap.Length.ToString(@"hh\:mm\:ss")).Append("</td></tr>");
            sb.Append("</table>");
        }
    }

    private static void AppendOverall(StringBuilder sb, ThermalAnalysisResult r)
    {
        sb.Append("<h2>Estadísticas globales (solo valores válidos)</h2><table>");
        sb.Append("<tr><th>Serie</th><th>N</th><th>Mín</th><th>Máx</th><th>Media</th><th>Mediana</th><th>P90</th><th>P95</th></tr>");
        StatRow(sb, "RPM CPU", r.CpuRpm);
        StatRow(sb, "RPM GPU", r.GpuRpm);
        StatRow(sb, "°C CPU", r.CpuCelsius);
        StatRow(sb, "°C GPU", r.GpuCelsius);
        sb.Append("</table>");
    }

    private static void AppendCorrelation(StringBuilder sb, ThermalAnalysisResult r)
    {
        sb.Append("<h2>RPM frente a temperatura (CPU)</h2>");
        sb.Append("<p>Correlación de Pearson temperatura–RPM: <strong>")
          .Append(r.TemperatureRpmCorrelation is double c ? c.ToString("0.000", CultureInfo.InvariantCulture) : "no calculable")
          .Append("</strong> <span class=\"muted\">(asociación, no causalidad)</span></p>");
        if (r.AverageCpuRpmByTemperature.Count > 0)
        {
            sb.Append("<table><tr><th>°C CPU</th><th>RPM CPU medio</th></tr>");
            foreach (var (temp, rpm) in r.AverageCpuRpmByTemperature.Take(40))
                sb.Append("<tr><td class=\"num\">").Append(temp).Append("</td><td class=\"num\">")
                  .Append(rpm.ToString("0.0", CultureInfo.InvariantCulture)).Append("</td></tr>");
            sb.Append("</table>");
        }
    }

    private static void AppendProfiles(StringBuilder sb, ThermalAnalysisResult r)
    {
        sb.Append("<h2>Comparación por perfil ASUS</h2><table>");
        sb.Append("<tr><th>Perfil</th><th>RPM CPU (med/min/máx/p90)</th><th>RPM GPU (med/min/máx/p90)</th><th>°C CPU (med/máx)</th></tr>");
        foreach (var p in r.Profiles)
        {
            sb.Append("<tr><td>").Append(H(p.Profile)).Append("</td>");
            Cell(sb, p.CpuRpm, withP90: true);
            Cell(sb, p.GpuRpm, withP90: true);
            sb.Append("<td class=\"num\">").Append(p.CpuCelsius.HasData ? p.CpuCelsius.Average.ToString("0.0", CultureInfo.InvariantCulture) : "-")
              .Append(" / ").Append(p.CpuCelsius.HasData ? p.CpuCelsius.Max.ToString("0", CultureInfo.InvariantCulture) : "-").Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        sb.Append("<p class=\"muted\">Whisper/Standard/Performance se comparan solo por sus medias: " +
                  "las diferencias también dependen de la carga de trabajo y la temperatura ambiente.</p>");
    }

    private static void AppendCharts(StringBuilder sb, IReadOnlyList<TelemetrySample> samples)
    {
        sb.Append("<h2>Gráficas comparativas</h2>");
        sb.Append(Chart("RPM por tiempo", "RPM", new[]
        {
            new ChartSeries("CPU", "#45d4c4", samples.Select(s => (double?)s.CpuRpm).ToList()),
            new ChartSeries("GPU", "#f0b45a", samples.Select(s => (double?)s.GpuRpm).ToList())
        }));
        sb.Append(Chart("Temperatura por tiempo", "°C", new[]
        {
            new ChartSeries("CPU", "#e26a6a", samples.Select(s => (double?)s.CpuCelsius).ToList()),
            new ChartSeries("GPU", "#5aa9f0", samples.Select(s => (double?)s.GpuCelsius).ToList())
        }));
    }

    private static void Row(StringBuilder sb, string label, int value) =>
        sb.Append("<tr><td>").Append(H(label)).Append("</td><td class=\"num\">").Append(value).Append("</td></tr>");

    private static void StatRow(StringBuilder sb, string label, Stat s)
    {
        sb.Append("<tr><td>").Append(H(label)).Append("</td><td class=\"num\">").Append(s.Count).Append("</td>");
        Cell(sb, s, withP90: true, skipCount: true);
        sb.Append("</tr>");
    }

    private static void Cell(StringBuilder sb, Stat s, bool withP90, bool skipCount = false)
    {
        if (!skipCount)
            sb.Append("<td class=\"num\">").Append(s.HasData ? s.Average.ToString("0", CultureInfo.InvariantCulture) : "-")
              .Append(" / ").Append(s.HasData ? s.Min.ToString("0", CultureInfo.InvariantCulture) : "-")
              .Append(" / ").Append(s.HasData ? s.Max.ToString("0", CultureInfo.InvariantCulture) : "-")
              .Append(" / ").Append(s.HasData ? s.P90.ToString("0", CultureInfo.InvariantCulture) : "-").Append("</td>");
        else
            sb.Append("<td class=\"num\">").Append(s.HasData ? s.Min.ToString("0", CultureInfo.InvariantCulture) : "-")
              .Append(" / ").Append(s.HasData ? s.Max.ToString("0", CultureInfo.InvariantCulture) : "-")
              .Append(" / ").Append(s.HasData ? s.Average.ToString("0.0", CultureInfo.InvariantCulture) : "-")
              .Append(" / ").Append(s.HasData ? s.Median.ToString("0", CultureInfo.InvariantCulture) : "-")
              .Append(" / ").Append(s.HasData ? s.P90.ToString("0", CultureInfo.InvariantCulture) : "-")
              .Append(" / ").Append(s.HasData ? s.P95.ToString("0", CultureInfo.InvariantCulture) : "-").Append("</td>");
    }

    /// <summary>Builds a self-contained SVG line chart; null values split the line into segments.</summary>
    public static string Chart(string title, string unit, IReadOnlyList<ChartSeries> series, int width = 900, int height = 220)
    {
        const int left = 56, right = 12, top = 26, bottom = 24;
        int plotW = width - left - right;
        int plotH = height - top - bottom;

        double min = double.MaxValue, max = double.MinValue;
        int maxLen = 1;
        foreach (var s in series)
        {
            maxLen = Math.Max(maxLen, s.Values.Count);
            foreach (var v in s.Values)
            {
                if (v is not double d) continue;
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }
        if (min > max) { min = 0; max = 1; }
        if (max - min < 1) max = min + 1;

        double X(int i) => maxLen <= 1 ? left + plotW / 2.0 : left + plotW * i / (double)(maxLen - 1);
        double Y(double v) => top + plotH - (v - min) / (max - min) * plotH;

        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{width}\" height=\"{height}\" role=\"img\" aria-label=\"{H(title)}\">");
        sb.Append($"<text x=\"{left}\" y=\"16\" fill=\"#9fc0e8\" font-size=\"13\">{H(title)} ({H(unit)})</text>");
        sb.Append($"<line x1=\"{left}\" y1=\"{top}\" x2=\"{left}\" y2=\"{top + plotH}\" stroke=\"#2b3d51\"/>");
        sb.Append($"<line x1=\"{left}\" y1=\"{top + plotH}\" x2=\"{left + plotW}\" y2=\"{top + plotH}\" stroke=\"#2b3d51\"/>");
        sb.Append($"<text x=\"6\" y=\"{top + 8}\" fill=\"#a5b7ca\" font-size=\"11\">{max:0}</text>");
        sb.Append($"<text x=\"6\" y=\"{top + plotH}\" fill=\"#a5b7ca\" font-size=\"11\">{min:0}</text>");

        int legendX = left + 8;
        foreach (var s in series)
        {
            sb.Append($"<rect x=\"{legendX}\" y=\"6\" width=\"10\" height=\"10\" fill=\"{s.Color}\"/>");
            sb.Append($"<text x=\"{legendX + 14}\" y=\"15\" fill=\"#e8f2fc\" font-size=\"11\">{H(s.Name)}</text>");
            legendX += 84;

            var path = new StringBuilder();
            bool open = false;
            for (int i = 0; i < s.Values.Count; i++)
            {
                if (s.Values[i] is not double v) { open = false; continue; }
                path.Append(open ? 'L' : 'M').Append(X(i).ToString("0.#", CultureInfo.InvariantCulture))
                    .Append(' ').Append(Y(v).ToString("0.#", CultureInfo.InvariantCulture));
                open = true;
            }
            sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{s.Color}\" stroke-width=\"1.5\"/>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Machine-readable statistics export (one row per series/profile).</summary>
    public static string StatsCsv(ThermalAnalysisResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scope,metric,count,min,max,average,median,p90,p95");
        Add("overall", "cpu_rpm", r.CpuRpm);
        Add("overall", "gpu_rpm", r.GpuRpm);
        Add("overall", "cpu_celsius", r.CpuCelsius);
        Add("overall", "gpu_celsius", r.GpuCelsius);
        foreach (var p in r.Profiles)
        {
            Add(Escape(p.Profile), "cpu_rpm", p.CpuRpm);
            Add(Escape(p.Profile), "gpu_rpm", p.GpuRpm);
            Add(Escape(p.Profile), "cpu_celsius", p.CpuCelsius);
        }
        sb.AppendLine("correlation,temperature_rpm,," + (r.TemperatureRpmCorrelation?.ToString("0.000", CultureInfo.InvariantCulture) ?? ""));
        return sb.ToString();

        void Add(string scope, string metric, Stat s) =>
            sb.AppendLine(string.Join(',', scope, metric, s.Count,
                N(s.HasData ? s.Min : null), N(s.HasData ? s.Max : null),
                s.HasData ? s.Average.ToString("0.0", CultureInfo.InvariantCulture) : "",
                s.HasData ? s.Median.ToString("0", CultureInfo.InvariantCulture) : "",
                s.HasData ? s.P90.ToString("0", CultureInfo.InvariantCulture) : "",
                s.HasData ? s.P95.ToString("0", CultureInfo.InvariantCulture) : ""));
    }

    private static string N(double? v) => v?.ToString("0", CultureInfo.InvariantCulture) ?? "";

    private static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        return field.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }

    private static string H(string? s) =>
        (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string Duration(IReadOnlyList<TelemetrySample> samples) =>
        samples.Count < 2 ? "0s" : (samples[^1].Timestamp - samples[0].Timestamp).ToString(@"hh\:mm\:ss");
}
