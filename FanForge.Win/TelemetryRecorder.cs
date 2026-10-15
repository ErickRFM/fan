using System.Globalization;
using FanForge.Core;

namespace FanForge;

/// <summary>
/// Appends telemetry samples to a timestamped CSV under <c>%LocalAppData%\FanForge\logs</c> for the
/// reproducible thermal comparison. Write failures never propagate: logging must not disturb control.
/// </summary>
internal sealed class TelemetryRecorder : IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _writer;

    public TelemetryRecorder(string? directory = null, DateTimeOffset? now = null)
    {
        Directory = directory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FanForge", "logs");
        System.IO.Directory.CreateDirectory(Directory);
        FilePath = System.IO.Path.Combine(Directory,
            "telemetry-" + (now ?? DateTimeOffset.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
        _writer = new StreamWriter(FilePath, append: false) { AutoFlush = true };
        _writer.WriteLine(TelemetryCsv.Header);
    }

    public string Directory { get; }
    public string FilePath { get; }

    public void Write(HistoryPoint point, string? profile, bool? profileConfirmed, string? note = null)
    {
        lock (_gate)
        {
            try { _writer?.WriteLine(TelemetryCsv.Row(point, profile, profileConfirmed, note)); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _writer?.Dispose(); } catch (IOException) { }
            _writer = null;
        }
    }
}
