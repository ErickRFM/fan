using FanForge.Core;

namespace FanForge;

/// <summary>
/// Persists the captured original ASUS profile next to the CLI log so that an unexpected close can
/// be repaired later by <c>--restore</c> or the watchdog. All disk errors are swallowed: losing the
/// sidecar record must never break the in-memory safety guarantees.
/// </summary>
internal sealed class FileProfileStore : IOriginalProfileStore
{
    public static string FilePath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FanForge", "original-profile.txt");

    public int? Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return null;
            if (OriginalProfileCodec.TryDecode(File.ReadAllText(path), out int wireMode)) return wireMode;
            File.Delete(path);
            return null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Save(int wireMode)
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, OriginalProfileCodec.Encode(wireMode, DateTimeOffset.UtcNow));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
