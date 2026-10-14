using FanForge.Core;

// Simulated / policy tests. No physical fan writes happen here: the backend is a fake.
int passed = 0;
void Check(bool result, string name)
{
    if (!result) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, name); return; }
    throw new Exception("FAILED (no exception): " + name);
}

// ---- Policy: model guard ----
Check(FanPolicy.SupportedModel("VivoBook_ASUSLaptop M3500QC_M3500QC"), "M3500QC allowed");
Check(FanPolicy.SupportedModel("m3500qc"), "case insensitive");
Check(!FanPolicy.SupportedModel("M3500Q"), "other model denied");
Check(!FanPolicy.SupportedModel(null), "null denied");

// ---- Policy: fan count ----
Check(FanPolicy.ValidFanCount(1) && FanPolicy.ValidFanCount(6), "valid counts");
Check(!FanPolicy.ValidFanCount(0) && !FanPolicy.ValidFanCount(7), "invalid counts");

// ---- Policy: percent -> duty ----
Check(FanPolicy.Duty(70) == 179, "70 percent pwm");
Check(FanPolicy.Duty(80) == 204, "80 percent pwm");
Check(FanPolicy.Duty(100) == 255, "100 percent pwm");
foreach (int pct in new[] { -1, 0, 1, 50, 69, 101, 255 })
    Throws<ArgumentOutOfRangeException>(() => _ = FanPolicy.Duty(pct), "reject " + pct);

// ---- Policy: RPM confirmation ----
Check(FanPolicy.Increased(1000, 1200), "RPM increase");
Check(!FanPolicy.Increased(1000, 1150), "insufficient increase");
Check(!FanPolicy.Increased(1000, 0), "stopped fan");
Check(!FanPolicy.Increased(-1, 3500), "invalid baseline");
Check(FanPolicy.AnyIncreased(new[] { 1000, 1000 }, new[] { 1010, 1400 }), "any increased");
Check(!FanPolicy.AnyIncreased(new[] { 1000, 1000 }, new[] { 1010, 1100 }), "none increased");

// ---- Policy: preventive restore ----
Check(FanPolicy.NeedsPreventiveRestore(true, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(8), new[] { 1200, 0 }),
    "preventive on stalled fan");
Check(!FanPolicy.NeedsPreventiveRestore(true, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8), new[] { 1200, 0 }),
    "no preventive inside grace");
Check(!FanPolicy.NeedsPreventiveRestore(false, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(8), new[] { 0 }),
    "no preventive in auto");
Check(!FanPolicy.NeedsPreventiveRestore(true, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(8), new[] { 1200, 1300 }),
    "no preventive when spinning");

// ---- Policy: newest OEM library wins (stale DriverStore folders lose) ----
var newest = FanPolicy.SelectNewestDll(new[]
{
    new DllCandidate(@"C:\old\AsusWinIO64.dll", new Version(1, 1, 2, 0)),
    new DllCandidate(@"C:\new\AsusWinIO64.dll", new Version(1, 1, 4, 0)),
    new DllCandidate(@"C:\mid\AsusWinIO64.dll", new Version(1, 1, 3, 0)),
});
Check(newest?.Path == @"C:\new\AsusWinIO64.dll", "newest dll selected");
Check(FanPolicy.SelectNewestDll(Array.Empty<DllCandidate>()) is null, "empty dll set");
Check(FanPolicy.SelectNewestDll(new[] { new DllCandidate("", new Version(9, 9)) }) is null, "blank dll path ignored");

// ---- Policy: ATKACPI decode ----
Check(FanPolicy.AcpiFanRpm(53) == 5300, "acpi fan raw 53 -> 5300");
Check(FanPolicy.AcpiFanRpm(0) == 0, "acpi fan zero rpm");
Check(FanPolicy.AcpiFanRpm(120) == 12000, "acpi fan max raw");
Check(FanPolicy.AcpiFanRpm(121) == -1, "acpi fan over max invalid");
Check(FanPolicy.AcpiFanRpm(-65536) == -1, "acpi fan empty status invalid");
Check(FanPolicy.AcpiTemperature(45) == 45, "acpi temp valid");
Check(FanPolicy.AcpiTemperature(0) == 0, "acpi temp zero valid");
Check(FanPolicy.AcpiTemperature(-65536) == -1, "acpi temp unsupported invalid");
Check(FanPolicy.AcpiTemperature(200) == -1, "acpi temp over max invalid");
Check(FanPolicy.VivoBookWireMode(1) == 2 && FanPolicy.VivoBookWireMode(2) == 1, "vivobook turbo/silent swap");
Check(FanPolicy.VivoBookWireMode(0) == 0, "vivobook balanced unchanged");

// ---- Session: manual state machine with a mock controller ----
var time = new ManualTime();
var backend = new FakeBackend(2, new[] { 1000, 1000 });
using (var session = new FanSession(backend, time, reapplyAfter: TimeSpan.FromSeconds(4),
           preventiveAfter: TimeSpan.FromSeconds(8), confirmAfter: TimeSpan.FromSeconds(10)))
{
    Check(!session.Manual, "starts in auto mode");
    _ = session.Read();
    Check(backend.ApplyCalls == 0, "auto read never writes pwm");

    Throws<ArgumentException>(() => session.Apply(new[] { 80 }), "reject wrong target count");
    Throws<ArgumentOutOfRangeException>(() => session.Apply(new[] { 50, 80 }), "reject out-of-range percent");

    session.Apply(new[] { 80, 100 }, new[] { 1000, 1000 });
    Check(session.Manual && backend.ApplyCalls == 1, "manual apply reaches backend");

    time.Advance(TimeSpan.FromSeconds(5));
    _ = session.Read();
    Check(backend.ApplyCalls == 2, "pwm re-applied after interval");
    Check(session.Evaluate(new FanReading(new[] { 1200, 1500 }, 40)) == ControlEvent.Reapplied,
        "reapply event surfaced");

    backend.Rpms = new[] { 1300, 1900 };
    time.Advance(TimeSpan.FromSeconds(6));
    Check(session.Evaluate(new FanReading(backend.Rpms, 40)) == ControlEvent.ConfirmedRaised,
        "confirmed rpm increase");

    session.Apply(new[] { 70, 70 }, new[] { 1300, 1900 });
    backend.Rpms = new[] { 1350, 1950 };
    time.Advance(TimeSpan.FromSeconds(11));
    Check(session.Evaluate(new FanReading(backend.Rpms, 40)) == ControlEvent.NotConfirmed,
        "reports not confirmed when firmware ignores");
}

// ---- Session: preventive restore on a stalled fan ----
var stallTime = new ManualTime();
var stallBackend = new FakeBackend(1, new[] { 900 });
using (var session = new FanSession(stallBackend, stallTime, reapplyAfter: TimeSpan.FromHours(1),
           preventiveAfter: TimeSpan.FromSeconds(8), confirmAfter: TimeSpan.FromSeconds(10)))
{
    session.Apply(new[] { 100 });
    stallBackend.Rpms = new[] { 0 };
    stallTime.Advance(TimeSpan.FromSeconds(9));
    Check(session.Evaluate(new FanReading(stallBackend.Rpms, 40)) == ControlEvent.PreventiveRestore,
        "preventive restore on stall");
    Check(!session.Manual && stallBackend.RestoreCalls >= 1, "stall restored to BIOS");
}

// ---- Session: OEM failure during re-apply restores BIOS and propagates ----
var failTime = new ManualTime();
var failBackend = new FakeBackend(1, new[] { 900 });
using (var session = new FanSession(failBackend, failTime, reapplyAfter: TimeSpan.FromSeconds(4),
           preventiveAfter: TimeSpan.FromSeconds(8), confirmAfter: TimeSpan.FromSeconds(10)))
{
    session.Apply(new[] { 80 });
    failBackend.ThrowOnApply = true;
    failTime.Advance(TimeSpan.FromSeconds(5));
    Throws<InvalidOperationException>(() => session.Read(), "reapply failure propagates");
    Check(!session.Manual && failBackend.RestoreCalls >= 1, "reapply failure restores BIOS");
}

// ---- Session: dispose restores and disposes the backend exactly once ----
var disposeBackend = new FakeBackend(1, new[] { 900 });
var disposeSession = new FanSession(disposeBackend, new ManualTime());
disposeSession.Apply(new[] { 90 });
disposeSession.Dispose();
Check(disposeBackend.Disposed && disposeBackend.RestoreCalls >= 1, "dispose restores and disposes backend");
disposeSession.Dispose();
Check(disposeBackend.RestoreCalls == 1, "dispose is idempotent");

Console.WriteLine("Passed: " + passed);

// ---- Test doubles (simulated controller; never touches real hardware) ----
sealed class FakeBackend : IFanBackend
{
    public FakeBackend(int fans, int[]? rpms = null)
    {
        FanCount = fans;
        Rpms = rpms ?? new int[fans];
    }

    public int FanCount { get; }
    public int[] Rpms { get; set; }
    public int? Temperature { get; set; } = 42;
    public int ApplyCalls { get; private set; }
    public int RestoreCalls { get; private set; }
    public bool ThrowOnApply { get; set; }
    public bool Disposed { get; private set; }

    public FanReading Read() => new((int[])Rpms.Clone(), Temperature);

    public void Apply(int[] percent)
    {
        if (ThrowOnApply) throw new InvalidOperationException("simulated OEM failure");
        ApplyCalls++;
    }

    public void Restore() => RestoreCalls++;

    public void Dispose() => Disposed = true;
}

sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now += delta;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => throw new NotSupportedException();
}
