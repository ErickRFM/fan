namespace FanForge.Core;

/// <summary>
/// Minimal channel to the firmware VivoBook profile register. Implemented over ATKACPI in the
/// Windows layer and faked in policy tests. The wire mode is 0 (Standard), 1 (Silent) or
/// 2 (Performance); the raw firmware value carries a 0x20000 flag in its high bits.
/// </summary>
public interface IProfileChannel
{
    /// <summary>Reads the currently active wire mode, or null when it cannot be read.</summary>
    int? ReadWireMode();

    /// <summary>Writes the wire mode and confirms it by read-back. Returns false on any failure.</summary>
    bool WriteWireMode(int wireMode);
}

/// <summary>
/// Persists the captured original profile so an unexpected close can still be repaired on the next
/// launch or by the watchdog. File-backed in the Windows layer; faked in policy tests.
/// </summary>
public interface IOriginalProfileStore
{
    int? Load();
    void Save(int wireMode);
    void Clear();
}

/// <summary>Plain-text codec for the persisted original profile; tested without touching disk.</summary>
public static class OriginalProfileCodec
{
    public const int CurrentVersion = 1;
    private const char Separator = '|';

    public static string Encode(int wireMode, DateTimeOffset capturedUtc)
    {
        if (wireMode is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(wireMode), "Wire mode must be 0, 1 or 2.");
        return string.Join(Separator, CurrentVersion, wireMode, capturedUtc.ToUniversalTime().Ticks);
    }

    public static bool TryDecode(string? text, out int wireMode)
    {
        wireMode = -1;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(Separator);
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out int version) || version != CurrentVersion) return false;
        if (!int.TryParse(parts[1], out int mode) || mode is < 0 or > 2) return false;
        if (!long.TryParse(parts[2], out long ticks) || ticks < 0) return false;
        wireMode = mode;
        return true;
    }
}

public enum ProfileOutcomeKind
{
    Applied,
    Restored,
    Unchanged,
    ProfileUnsupported,
    OriginalUnavailable,
    WriteRejected,
    NotCaptured
}

public sealed record ProfileOutcome(ProfileOutcomeKind Kind, bool Success, string Message);

/// <summary>
/// Remembers the ASUS profile that was active before FanForge modified it and restores exactly that
/// profile later, never assuming Standard. The capture happens only when a real change is about to
/// be written, so an unreadable register blocks modification instead of leaving an unknown state.
/// </summary>
public sealed class ProfileGuard
{
    private readonly IProfileChannel _channel;
    private readonly IOriginalProfileStore? _store;
    private int? _original;

    /// <param name="recoverPersisted">
    /// When true (repair paths: <c>--restore</c>, watchdog) a previously persisted original is loaded
    /// so a crash that lost memory can still be repaired. Interactive sessions leave this false so a
    /// stale record from a past run can never overwrite a profile changed in another tool.
    /// </param>
    public ProfileGuard(IProfileChannel channel, IOriginalProfileStore? store = null, bool recoverPersisted = false)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _store = store;
        if (recoverPersisted && store?.Load() is int recovered && recovered is >= 0 and <= 2)
            _original = recovered;
    }

    public int? OriginalWireMode => _original;
    public bool Modified { get; private set; }
    public bool HasCaptured => _original is not null;

    /// <summary>Captures the original on the first real change, then applies and verifies the profile.</summary>
    public ProfileOutcome Apply(FanProfile profile)
    {
        int? target = FanPolicy.VivoBookProfileWireMode(profile);
        if (target is null)
            return new(ProfileOutcomeKind.ProfileUnsupported, false,
                "Perfil no admitido: solo Silencioso, Estándar y Rendimiento.");

        int? current = _channel.ReadWireMode();
        if (current is null)
            return new(ProfileOutcomeKind.OriginalUnavailable, false,
                "No se pudo leer el perfil activo; no se modifica para no perder el original.");

        if (current == target)
            return new(ProfileOutcomeKind.Unchanged, true,
                $"El perfil {profile} ya estaba activo (wire {target}); sin escritura.");

        _original ??= current;

        if (!_channel.WriteWireMode(target.Value))
            return new(ProfileOutcomeKind.WriteRejected, false,
                $"Escritura rechazada o sin confirmación; se conserva el perfil original (wire {_original}).");

        Modified = true;
        _store?.Save(_original.Value);
        return new(ProfileOutcomeKind.Applied, true,
            $"Perfil {profile} aplicado y confirmado (wire {current} -> {target}); original {_original} guardado.");
    }

    /// <summary>Restores the captured original profile (not necessarily Standard).</summary>
    public ProfileOutcome RestoreOriginal()
    {
        if (_original is null)
            return new(ProfileOutcomeKind.NotCaptured, false,
                "No se capturó el perfil original; no se escribe nada.");

        int? current = _channel.ReadWireMode();
        if (current is null)
            return new(ProfileOutcomeKind.OriginalUnavailable, false,
                "No se pudo leer el perfil activo; no se pudo confirmar la restauración.");

        if (current == _original)
        {
            Modified = false;
            _store?.Clear();
            return new(ProfileOutcomeKind.Unchanged, true,
                $"El perfil original (wire {_original}) ya estaba activo.");
        }

        if (!_channel.WriteWireMode(_original.Value))
            return new(ProfileOutcomeKind.WriteRejected, false,
                $"No se pudo restaurar el perfil original (wire {_original}).");

        Modified = false;
        _store?.Clear();
        return new(ProfileOutcomeKind.Restored, true,
            $"Perfil original restaurado (wire {current} -> {_original}).");
    }
}
