namespace FanForge.Core;

/// <summary>A single read-only snapshot of the cooling system.</summary>
public sealed record FanReading(int[] Rpms, int? Temperature);

/// <summary>
/// Low-level, serialized fan backend. Implementations may talk to the OEM library
/// (read/write) or be fakes used by automated tests. No implementation is allowed to
/// expose raw EC access or to write below <see cref="FanPolicy.MinimumPercent"/>.
/// </summary>
public interface IFanBackend : IDisposable
{
    int FanCount { get; }
    FanReading Read();
    void Apply(int[] percent);
    void Restore();
}

/// <summary>Events surfaced while supervising an active manual session.</summary>
public enum ControlEvent
{
    None = 0,
    Reapplied,
    ConfirmedRaised,
    NotConfirmed,
    PreventiveRestore
}
