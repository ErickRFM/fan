namespace FanForge.Core;

/// <summary>
/// Orchestrates one or more <see cref="IFanSpeedBackend"/> instances behind a single, serialized
/// control channel.
/// </summary>
/// <remarks>
/// The engine separates detection, verification, request, response and measured RPM. A write is only
/// attempted through a backend whose direct-PWM capability is <see cref="CapabilityLevel.Verified"/>,
/// and a command is only ever presented as completed once the RPM response has been confirmed. On
/// hardware where no verified path exists (as on the M3500QC today) every request resolves to
/// <see cref="FanSpeedState.Unsupported"/> instead of a false success.
/// </remarks>
public sealed class FanSpeedEngine : IDisposable
{
    private readonly IReadOnlyList<IFanSpeedBackend> _backends;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<FanSpeedAuditEntry> _audit = new();

    private IReadOnlyList<FanSpeedCapabilities>? _capabilities;
    private FanTarget? _manualTarget;
    private int _manualPercent;
    private int? _baselineCpuRpm;
    private int? _baselineGpuRpm;
    private FanSpeedState _state = FanSpeedState.AvailableUnverified;

    public FanSpeedEngine(IEnumerable<IFanSpeedBackend> backends, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(backends);
        _backends = backends.Where(b => b is not null).ToArray();
        _time = time ?? TimeProvider.System;
    }

    public FanSpeedState State => _state;
    public bool Manual => _manualTarget is not null;
    public FanTarget? ManualTarget => _manualTarget;
    public int ManualPercent => _manualPercent;
    public IReadOnlyList<FanSpeedAuditEntry> Audit => _audit;
    public IReadOnlyList<IFanSpeedBackend> Backends => _backends;
    public IReadOnlyList<FanSpeedCapabilities> Capabilities =>
        _capabilities ?? Array.Empty<FanSpeedCapabilities>();

    private DateTimeOffset Now => _time.GetUtcNow();

    /// <summary>Probes every backend and records the reported capabilities.</summary>
    public async Task<IReadOnlyList<FanSpeedCapabilities>> DetectAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<FanSpeedCapabilities>();
        foreach (var backend in _backends)
        {
            try
            {
                list.Add(await backend.DetectAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                list.Add(FanSpeedCapabilities.None(backend.Name, ex.Message));
            }
        }

        _capabilities = list;
        if (!Manual)
        {
            _state = list.Any(c => c.PwmDirect == CapabilityLevel.Verified)
                ? FanSpeedState.Ready
                : FanSpeedState.AvailableUnverified;
        }

        return list;
    }

    /// <summary>Returns the first fully valid reading across backends, or the best partial one.</summary>
    public async Task<FanSpeedReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        FanSpeedReading? best = null;
        foreach (var backend in _backends)
        {
            FanSpeedReading reading;
            try
            {
                reading = await backend.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                reading = FanSpeedReading.Invalid($"{backend.Name}: {ex.Message}", Now);
            }

            if (reading.Quality == ReadingQuality.Ok)
            {
                return reading;
            }

            best ??= reading;
        }

        return best ?? FanSpeedReading.Invalid("none", Now);
    }

    /// <summary>
    /// Requests a manual speed. Only a backend whose PWM path is Verified can be used; the command is
    /// serialized and the result is never reported as completed until confirmed.
    /// </summary>
    public async Task<FanSpeedCommandResult> SetSpeedAsync(
        FanTarget target,
        int percent,
        CancellationToken cancellationToken = default)
    {
        if (percent < FanPolicy.MinimumPercent || percent > FanPolicy.MaximumPercent)
        {
            return new FanSpeedCommandResult(
                FanSpeedState.Failed,
                $"El control manual solo admite {FanPolicy.MinimumPercent}%-{FanPolicy.MaximumPercent}%.",
                percent);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var backend = await FindApplyBackendAsync(cancellationToken).ConfigureAwait(false);
            if (backend is null)
            {
                _state = FanSpeedState.Unsupported;
                Record(backend: "none", _state, "Ningún backend verificado para control PWM.");
                return new FanSpeedCommandResult(
                    FanSpeedState.Unsupported,
                    "Control PWM no soportado o no verificado en este equipo.",
                    percent);
            }

            var baseline = await SafeReadAsync(backend, cancellationToken).ConfigureAwait(false);
            if (baseline.Quality == ReadingQuality.Invalid)
            {
                _state = FanSpeedState.Failed;
                Record(backend.Name, _state, "Lecturas incoherentes; se cancela la escritura.");
                return new FanSpeedCommandResult(
                    FanSpeedState.Failed,
                    "Lecturas incoherentes: se cancela la escritura por seguridad.",
                    percent);
            }

            _state = FanSpeedState.Applying;
            Record(backend.Name, _state, $"Aplicando {percent}% a {target}.");

            FanSpeedCommandResult result;
            try
            {
                result = await backend.SetSpeedAsync(target, percent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _state = FanSpeedState.Failed;
                Record(backend.Name, _state, ex.Message);
                return new FanSpeedCommandResult(FanSpeedState.Failed, ex.Message, percent);
            }

            if (!result.Accepted)
            {
                _state = result.State == FanSpeedState.Unsupported
                    ? FanSpeedState.Unsupported
                    : FanSpeedState.Failed;
                Record(backend.Name, _state, result.Message);
                return new FanSpeedCommandResult(_state, result.Message, percent, result.ObservedRpm);
            }

            _manualTarget = target;
            _manualPercent = percent;
            _baselineCpuRpm = baseline.CpuRpm;
            _baselineGpuRpm = baseline.GpuRpm;
            _state = result.Confirmed ? FanSpeedState.Verified : FanSpeedState.AppliedUnverified;
            Record(backend.Name, _state, result.Confirmed
                ? "Cambio confirmado por el backend."
                : $"Aceptado por {backend.Name}; pendiente de confirmación física.");

            return new FanSpeedCommandResult(
                _state,
                result.Confirmed
                    ? "Cambio confirmado."
                    : "Solicitud aceptada por el controlador; RPM aún sin confirmar.",
                percent,
                result.ObservedRpm);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Re-reads RPM and promotes to Verified only on a clear, measured increase.</summary>
    public async Task<FanSpeedCommandResult> ConfirmAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_manualTarget is null)
            {
                return new FanSpeedCommandResult(FanSpeedState.Ready, "No hay control manual activo.");
            }

            var backend = await FindApplyBackendAsync(cancellationToken).ConfigureAwait(false)
                ?? _backends.FirstOrDefault();
            if (backend is null)
            {
                return new FanSpeedCommandResult(FanSpeedState.Unsupported, "Sin backend disponible.");
            }

            var now = await SafeReadAsync(backend, cancellationToken).ConfigureAwait(false);
            bool confirmed = ConfirmedFor(_manualTarget.Value, now);
            _state = confirmed ? FanSpeedState.Verified : FanSpeedState.AppliedUnverified;
            Record(backend.Name, _state, confirmed
                ? "Respuesta de RPM confirmada."
                : "Sin respuesta clara de RPM; permanece sin confirmar.");

            return new FanSpeedCommandResult(
                _state,
                confirmed ? "RPM confirmadas." : "Aceptado, pero las RPM no confirman el cambio.",
                _manualPercent,
                confirmed ? ObservedFor(_manualTarget.Value, now) : null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Restores automatic (BIOS/OEM) control across every backend and clears manual state.</summary>
    public async Task<FanSpeedCommandResult> RestoreAutomaticAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state = FanSpeedState.Restoring;
            Record("engine", _state, "Restaurando control automático.");

            bool any = false;
            foreach (var backend in _backends)
            {
                try
                {
                    var result = await backend.RestoreAutomaticAsync(cancellationToken).ConfigureAwait(false);
                    any |= result.Accepted || result.State == FanSpeedState.Restored;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // A backend that cannot restore must not block the others.
                }
            }

            _manualTarget = null;
            _manualPercent = 0;
            _baselineCpuRpm = null;
            _baselineGpuRpm = null;
            _state = any ? FanSpeedState.Restored : FanSpeedState.Unsupported;
            Record("engine", _state, any ? "Control automático restaurado." : "Sin canal de restauración.");

            return new FanSpeedCommandResult(
                _state,
                any ? "Control automático restaurado." : "Ningún backend pudo restaurar el control automático.",
                null,
                null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Safety pass: when manual control is active but the fan reading is unusable, trigger a
    /// preventive restoration. Intended to be called by the watchdog/UI timer.
    /// </summary>
    public async Task<FanSpeedCommandResult?> EvaluateSafetyAsync(CancellationToken cancellationToken = default)
    {
        if (!Manual)
        {
            return null;
        }

        var reading = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (reading.Quality == ReadingQuality.Invalid)
        {
            Record(reading.Source, FanSpeedState.Restoring, "Lectura inválida: restauración preventiva.");
            return await RestoreAutomaticAsync(cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<IFanSpeedBackend?> FindApplyBackendAsync(CancellationToken cancellationToken)
    {
        var caps = _capabilities ?? await DetectAsync(cancellationToken).ConfigureAwait(false);
        foreach (var backend in _backends)
        {
            var capability = caps.FirstOrDefault(c => c.Backend == backend.Name);
            if (capability?.PwmDirect == CapabilityLevel.Verified)
            {
                return backend;
            }
        }

        return null;
    }

    private static async Task<FanSpeedReading> SafeReadAsync(
        IFanSpeedBackend backend,
        CancellationToken cancellationToken)
    {
        try
        {
            return await backend.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return FanSpeedReading.Invalid(backend.Name, DateTimeOffset.UtcNow);
        }
    }

    private bool ConfirmedFor(FanTarget target, FanSpeedReading reading)
    {
        bool cpu = FanPolicy.Increased(_baselineCpuRpm ?? -1, reading.CpuRpm ?? -1);
        bool gpu = FanPolicy.Increased(_baselineGpuRpm ?? -1, reading.GpuRpm ?? -1);
        return target switch
        {
            FanTarget.Cpu => cpu,
            FanTarget.Gpu => gpu,
            _ => cpu || gpu
        };
    }

    private static int? ObservedFor(FanTarget target, FanSpeedReading reading) => target switch
    {
        FanTarget.Cpu => reading.CpuRpm,
        FanTarget.Gpu => reading.GpuRpm,
        _ => reading.CpuRpm ?? reading.GpuRpm
    };

    private void Record(string backend, FanSpeedState state, string message) =>
        _audit.Add(new FanSpeedAuditEntry(Now, state, backend, message));

    /// <summary>Releases the gate and disposes any backends this engine owns.</summary>
    public void Dispose()
    {
        foreach (var backend in _backends)
        {
            (backend as IDisposable)?.Dispose();
        }

        _gate.Dispose();
    }
}
