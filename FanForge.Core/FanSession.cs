namespace FanForge.Core;

/// <summary>
/// Hardware-independent supervisor around an <see cref="IFanBackend"/>. Owns the manual
/// state machine: re-applies PWM to keep the controller alive, detects a confirmed RPM
/// response, and performs a preventive restoration when readings stall. Fully testable
/// with a fake backend and a deterministic <see cref="TimeProvider"/>.
/// </summary>
public sealed class FanSession : IDisposable
{
    public static readonly TimeSpan DefaultReapply = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan DefaultPreventive = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan DefaultConfirm = TimeSpan.FromSeconds(10);

    private readonly IFanBackend _backend;
    private readonly TimeProvider _time;
    private readonly TimeSpan _reapplyAfter;
    private readonly TimeSpan _preventiveAfter;
    private readonly TimeSpan _confirmAfter;

    private int[]? _targets;
    private int[]? _baseline;
    private DateTimeOffset _changedAt;
    private DateTimeOffset _lastWrite;
    private bool _reappliedSinceEvaluate;
    private bool _disposed;

    public FanSession(
        IFanBackend backend,
        TimeProvider? time = null,
        TimeSpan? reapplyAfter = null,
        TimeSpan? preventiveAfter = null,
        TimeSpan? confirmAfter = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _time = time ?? TimeProvider.System;
        _reapplyAfter = reapplyAfter ?? DefaultReapply;
        _preventiveAfter = preventiveAfter ?? DefaultPreventive;
        _confirmAfter = confirmAfter ?? DefaultConfirm;
        _changedAt = _lastWrite = _time.GetUtcNow();
    }

    public int FanCount => _backend.FanCount;
    public bool Manual => _targets != null;

    /// <summary>Reads a snapshot, refreshing the manual PWM if it is about to expire.</summary>
    public FanReading Read()
    {
        ThrowIfDisposed();
        if (_targets != null && _time.GetUtcNow() - _lastWrite > _reapplyAfter)
        {
            try
            {
                _backend.Apply(_targets);
                _lastWrite = _time.GetUtcNow();
                _reappliedSinceEvaluate = true;
            }
            catch
            {
                Restore();
                throw;
            }
        }
        return _backend.Read();
    }

    /// <summary>Requests manual PWM (percent). <paramref name="baseline"/> enables confirmation.</summary>
    public void Apply(int[] percent, int[]? baseline = null)
    {
        ThrowIfDisposed();
        if (percent is null || percent.Length != FanCount)
            throw new ArgumentException("Provide exactly one PWM target per detected fan.", nameof(percent));
        foreach (var value in percent) _ = FanPolicy.Duty(value);

        _backend.Apply(percent);
        _targets = (int[])percent.Clone();
        _baseline = baseline is null ? null : (int[])baseline.Clone();
        _changedAt = _lastWrite = _time.GetUtcNow();
        _reappliedSinceEvaluate = false;
    }

    /// <summary>Interprets a snapshot after <see cref="Read"/> to drive the manual state machine.</summary>
    public ControlEvent Evaluate(FanReading reading)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(reading);

        if (_reappliedSinceEvaluate)
        {
            _reappliedSinceEvaluate = false;
            return ControlEvent.Reapplied;
        }
        if (_targets is null) return ControlEvent.None;

        var now = _time.GetUtcNow();
        if (FanPolicy.NeedsPreventiveRestore(true, now - _changedAt, _preventiveAfter, reading.Rpms))
        {
            Restore();
            return ControlEvent.PreventiveRestore;
        }
        if (_baseline != null && now - _changedAt > _confirmAfter)
        {
            bool raised = FanPolicy.AnyIncreased(_baseline, reading.Rpms);
            _baseline = null;
            return raised ? ControlEvent.ConfirmedRaised : ControlEvent.NotConfirmed;
        }
        return ControlEvent.None;
    }

    public void Restore()
    {
        ThrowIfDisposed();
        _targets = null;
        _baseline = null;
        _backend.Restore();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_targets != null)
            {
                _targets = null;
                try { _backend.Restore(); } catch { /* best effort before shutdown */ }
            }
        }
        finally
        {
            try { _backend.Dispose(); } catch { /* native shutdown is best effort */ }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FanSession));
    }
}
