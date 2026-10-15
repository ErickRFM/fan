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

// ---- Policy: OEM library version and signature verification ----
Check(OemLibraryPolicy.VersionSupported(new Version(3, 1, 0, 0)), "accepts modern oem version");
Check(!OemLibraryPolicy.VersionSupported(new Version(0, 0, 0, 0)), "rejects zero oem version");
Check(!OemLibraryPolicy.VersionSupported(null), "rejects null oem version");
Check(OemLibraryPolicy.TrustedSigner("CN=ASUSTeK Computer Inc."), "trusts asus signer");
Check(OemLibraryPolicy.TrustedSigner("CN=ASUS"), "trusts asus signer short");
Check(!OemLibraryPolicy.TrustedSigner("CN=Evil Corp"), "rejects foreign signer");
Check(!OemLibraryPolicy.TrustedSigner(""), "rejects empty signer");
Check(OemLibraryPolicy.TrustedSigner(null) == false, "rejects null signer");
Check(OemLibraryPolicy.Describe(new Version(3, 1, 0, 0), "CN=ASUSTeK Computer Inc.").Contains("firmante ASUS"), "describe names asus signer");
Check(OemLibraryPolicy.Describe(new Version(3, 1, 0, 0), "CN=Evil Corp").Contains("no autorizado"), "describe flags foreign signer");
Check(OemLibraryPolicy.Describe(new Version(0, 0, 0, 0), "CN=ASUS").Contains("no soportada"), "describe flags unsupported version");
Check(!OemLibraryPolicy.TrustedSigner("CN=Untrusted ASUS Drivers"), "reject signer containing ASUS as substring");
Check(!OemLibraryPolicy.TrustedSigner("O=ASUSTeK Computer Inc., CN=Fake ASUS LLC"), "reject fake ASUS CN");
Check(!OemLibraryPolicy.TrustedSigner("ASUSTeK"), "reject non-CN signer text");
Check(OemLibraryPolicy.MayLoad(new Version(1, 1, 4), "CN=ASUSTeK Computer Inc.", true), "load only validated signature");
Check(!OemLibraryPolicy.MayLoad(new Version(1, 1, 4), "CN=ASUSTeK Computer Inc.", false), "reject cryptographically invalid DLL");
Check(!OemLibraryPolicy.MayLoad(new Version(1, 1, 4), "CN=Fake ASUS Drivers", true), "reject different signer despite valid signature");
Check(!OemLibraryPolicy.MayLoad(new Version(0, 0, 0, 0), "CN=ASUS", true), "reject obsolete version even when signed");

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

// ---- Telemetry: provider merge ----
var acpi = new TelemetrySnapshot(
    new[] { new FanRpm(FanType.Cpu, 4400), new FanRpm(FanType.Gpu, 3800) },
    new TemperatureReading(null, null), DateTimeOffset.UnixEpoch);
var thermal = new TelemetrySnapshot(
    Array.Empty<FanRpm>(), new TemperatureReading(90, null), DateTimeOffset.UnixEpoch);
var merged = TelemetryMerge.Merge(new[] { acpi, thermal });
Check(merged.Rpms.Count == 2 && merged.Rpms[0].Value == 4400, "merge keeps first provider RPMs");
Check(merged.Temperatures.CpuCelsius == 90 && merged.Temperatures.GpuCelsius is null, "merge fills temp from second provider");

var staleWinIo = new TelemetrySnapshot(
    new[] { new FanRpm(FanType.Cpu, -1) }, new TemperatureReading(null, 70), DateTimeOffset.UnixEpoch);
var mergedFallback = TelemetryMerge.Merge(new[] { staleWinIo, acpi });
Check(mergedFallback.Rpms.Count == 2 && mergedFallback.Rpms[0].Value == 4400, "merge falls back past empty provider");
Check(mergedFallback.Temperatures.GpuCelsius == 70, "merge keeps earlier temperature");

Check(TelemetryMerge.Merge(Array.Empty<TelemetrySnapshot>()).Rpms.Count == 0, "merge of nothing is empty");
Check(TelemetryMerge.Merge(new[] { acpi }).Temperatures.CpuCelsius is null, "merge without temperature yields null");

// ---- Telemetry history: bounded ring buffer ----
var history = new TelemetryHistory(3);
for (int i = 0; i < 5; i++)
    history.Add(new TelemetrySnapshot(
        new[] { new FanRpm(FanType.Cpu, 1000 + i), new FanRpm(FanType.Gpu, -1) },
        new TemperatureReading(40 + i, null), DateTimeOffset.UnixEpoch.AddSeconds(i)));
Check(history.Count == 3, "history trims to capacity");
Check(history.Points[0].CpuRpm == 1002 && history.Points[2].CpuRpm == 1004, "history keeps newest in order");
Check(history.Points[0].CpuCelsius == 42, "history maps cpu temperature");
Check(history.Latest?.CpuRpm == 1004, "history latest is newest");
Check(history.Points.All(p => p.GpuRpm is null), "history drops invalid rpm");
var mixed = new TelemetryHistory(2);
mixed.Add(new TelemetrySnapshot(new[] { new FanRpm(FanType.Gpu, 3800) }, new TemperatureReading(null, 65), DateTimeOffset.UnixEpoch));
mixed.Add(new TelemetrySnapshot(Array.Empty<FanRpm>(), new TemperatureReading(null, null), DateTimeOffset.UnixEpoch));
Check(mixed.Latest?.GpuRpm is null && mixed.Points[0].GpuCelsius == 65, "history keeps only readable values");
mixed.Clear();
Check(mixed.Count == 0 && mixed.Latest is null, "history clear empties buffer");

// ---- VivoBook profile semantics (no fake MAX fan speed, no success on invalid sentinels) ----
Check(FanPolicy.VivoBookProfileWireMode(FanProfile.Whisper) == 1, "Whisper wire mode");
Check(FanPolicy.VivoBookProfileWireMode(FanProfile.Standard) == 0, "Standard wire mode");
Check(FanPolicy.VivoBookProfileWireMode(FanProfile.Performance) == 2, "Performance wire mode");
Check(FanPolicy.VivoBookProfileWireMode(FanProfile.Max) is null, "Max unsupported, not performance alias");
Check(FanPolicy.VivoBookProfileWireMode(FanProfile.Manual) is null, "manual unsupported on VivoBook ACPI");
Check(FanPolicy.VivoBookProfileWireMode(FanProfile.Auto) is null, "Auto requires explicit restore");
Check(FanPolicy.VivoBookProfileConfirmed(131074, 2), "positive profile readback verifies");
Check(FanPolicy.VivoBookProfileConfirmed(131073, 1), "whisper readback with 0x20000 flag verifies");
Check(FanPolicy.VivoBookProfileConfirmed(131072, 0), "standard readback with 0x20000 flag verifies");
Check(FanPolicy.VivoBookProfileConfirmed(0, 0), "valid balanced readback verifies");
Check(!FanPolicy.VivoBookProfileConfirmed(-65536, 0), "reject failed readback even for balanced");
Check(!FanPolicy.VivoBookProfileConfirmed(131073, 2), "reject mismatched readback");
Check(!FanPolicy.VivoBookProfileConfirmed(131075, 3), "reject unsupported profile 3");

// ---- Watchdog: bidirectional health ----
Check(WatchdogPolicy.Assess(false, false) == WatchdogHealth.NotStarted, "watchdog not started");
Check(WatchdogPolicy.Assess(true, false) == WatchdogHealth.Active, "watchdog active");
Check(WatchdogPolicy.Assess(true, true) == WatchdogHealth.Lost, "watchdog lost when exited");
Check(WatchdogPolicy.ShouldRestoreOnLost(WatchdogHealth.Lost, true), "restore when lost during manual");
Check(!WatchdogPolicy.ShouldRestoreOnLost(WatchdogHealth.Lost, false), "no restore when not manual");
Check(!WatchdogPolicy.ShouldRestoreOnLost(WatchdogHealth.Active, true), "no restore while active");
Check(!WatchdogPolicy.ShouldRestoreOnLost(WatchdogHealth.NotStarted, true), "no restore before start");
Check(WatchdogPolicy.Describe(WatchdogHealth.Active, 1234).Contains("1234"), "describe shows pid");
Check(WatchdogPolicy.Describe(WatchdogHealth.Lost) == "Watchdog: PERDIDO", "describe lost");

// ---- Control conflicts: known OEM tools ----
Check(ControlConflicts.IsConflict("MyASUS"), "detects MyASUS");
Check(ControlConflicts.IsConflict("myasus.exe"), "detects MyASUS exe, case-insensitive");
Check(ControlConflicts.IsConflict("G-Helper"), "detects G-Helper");
Check(!ControlConflicts.IsConflict("chrome"), "ignores unknown process");
Check(!ControlConflicts.IsConflict(""), "ignores empty name");
Check(!ControlConflicts.IsConflict(null), "ignores null name");
var conflicts = ControlConflicts.Detect(new[] { "explorer", "MyASUS", "myasus", "GHelper", null });
Check(conflicts.Count == 2 && conflicts.Contains("MyASUS") && conflicts.Contains("GHelper"), "dedupes and orders conflicts");
Check(ControlConflicts.Advisory(Array.Empty<string>()) == "", "empty advisory when clean");
Check(ControlConflicts.Advisory(conflicts).Contains("MyASUS"), "advisory names the tool");

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

// ---- Original profile codec ----
string encoded = OriginalProfileCodec.Encode(2, DateTimeOffset.UnixEpoch);
Check(OriginalProfileCodec.TryDecode(encoded, out int decoded) && decoded == 2, "codec round trip");
Check(OriginalProfileCodec.TryDecode(" 1|1|10 ", out int spaced) && spaced == 1, "codec trims whitespace");
Check(!OriginalProfileCodec.TryDecode(null, out _), "codec rejects null");
Check(!OriginalProfileCodec.TryDecode("", out _), "codec rejects empty");
Check(!OriginalProfileCodec.TryDecode("1|3|0", out _), "codec rejects out-of-range mode");
Check(!OriginalProfileCodec.TryDecode("2|1|0", out _), "codec rejects wrong version");
Check(!OriginalProfileCodec.TryDecode("1|1", out _), "codec rejects truncated record");
Throws<ArgumentOutOfRangeException>(() => OriginalProfileCodec.Encode(3, DateTimeOffset.UnixEpoch),
    "encode rejects bad mode");

// ---- Profile guard: capture original and restore the original (never assume Standard) ----
var channel = new FakeProfileChannel(wire: 2);
var store = new FakeProfileStore();
var guard = new ProfileGuard(channel, store);
Check(guard.Apply(FanProfile.Standard).Kind == ProfileOutcomeKind.Applied, "guard applies standard");
Check(channel.Wire == 0, "guard wrote standard wire 0");
Check(guard.OriginalWireMode == 2, "guard captured original wire 2");
Check(store.Value == 2, "guard persisted original wire 2");
Check(guard.Modified && channel.Writes.Count == 1, "guard marks modified after one write");
Check(guard.RestoreOriginal() is { Kind: ProfileOutcomeKind.Restored, Success: true } && channel.Wire == 2,
    "restore writes original 2, not standard");
Check(!guard.Modified && store.Value is null, "restore clears modified flag and persisted record");

// ---- Profile guard: no write when the target is already active ----
var alreadyChannel = new FakeProfileChannel(0);
var alreadyGuard = new ProfileGuard(alreadyChannel);
Check(alreadyGuard.Apply(FanProfile.Standard).Kind == ProfileOutcomeKind.Unchanged, "unchanged apply reported");
Check(alreadyChannel.Writes.Count == 0 && !alreadyGuard.Modified, "unchanged apply never writes");
Check(!alreadyGuard.HasCaptured, "unchanged apply captures nothing to restore");

// ---- Profile guard: unsupported profiles are refused before any I/O ----
var unsupportedChannel = new FakeProfileChannel(0);
Check(new ProfileGuard(unsupportedChannel).Apply(FanProfile.Max).Kind == ProfileOutcomeKind.ProfileUnsupported,
    "guard refuses Max");
Check(new ProfileGuard(unsupportedChannel).Apply(FanProfile.Manual).Kind == ProfileOutcomeKind.ProfileUnsupported,
    "guard refuses Manual");
Check(unsupportedChannel.Writes.Count == 0, "refused profiles never write");

// ---- Profile guard: a read error blocks modification (protects the unknown original) ----
var readFailChannel = new FakeProfileChannel(0) { ReadReturnsNull = true };
var readFailGuard = new ProfileGuard(readFailChannel, new FakeProfileStore());
Check(readFailGuard.Apply(FanProfile.Performance).Kind == ProfileOutcomeKind.OriginalUnavailable,
    "read failure blocks modification");
Check(readFailChannel.Writes.Count == 0 && !readFailGuard.Modified, "no write when original is unreadable");

// ---- Profile guard: a rejected write keeps the original and is not persisted ----
var rejectChannel = new FakeProfileChannel(0) { AcceptWrites = false };
var rejectStore = new FakeProfileStore();
var rejectGuard = new ProfileGuard(rejectChannel, rejectStore);
Check(rejectGuard.Apply(FanProfile.Performance).Kind == ProfileOutcomeKind.WriteRejected, "write rejection reported");
Check(!rejectGuard.Modified && rejectStore.Value is null && rejectChannel.Wire == 0,
    "rejected write does not change or persist state");

// ---- Profile guard: a failed restore keeps the persisted original for a later retry ----
var restoreFailChannel = new FakeProfileChannel(2);
var restoreFailStore = new FakeProfileStore();
var restoreFailGuard = new ProfileGuard(restoreFailChannel, restoreFailStore);
_ = restoreFailGuard.Apply(FanProfile.Standard);
restoreFailChannel.AcceptWrites = false;
Check(restoreFailGuard.RestoreOriginal().Kind == ProfileOutcomeKind.WriteRejected, "failed restore reported");
Check(restoreFailGuard.Modified && restoreFailStore.Value == 2, "failed restore keeps persisted original");

// ---- Profile guard: restore without a capture writes nothing ----
var uncapturedChannel = new FakeProfileChannel(0);
Check(new ProfileGuard(uncapturedChannel).RestoreOriginal().Kind == ProfileOutcomeKind.NotCaptured,
    "restore without capture refused");
Check(uncapturedChannel.Writes.Count == 0, "restore without capture never writes");

// ---- Profile guard: unexpected close is repaired from the persisted record ----
var crashStore = new FakeProfileStore { Value = 2 };
var crashChannel = new FakeProfileChannel(0); // firmware stayed on Standard after the crash
var recovery = new ProfileGuard(crashChannel, crashStore, recoverPersisted: true);
Check(recovery.OriginalWireMode == 2, "recovery loads persisted original");
Check(recovery.RestoreOriginal() is { Kind: ProfileOutcomeKind.Restored } && crashChannel.Wire == 2,
    "recovery restores original from disk");
Check(crashStore.Value is null, "recovery clears persisted record after success");
Check(new ProfileGuard(new FakeProfileChannel(1), new FakeProfileStore(), recoverPersisted: true)
        .RestoreOriginal().Kind == ProfileOutcomeKind.NotCaptured,
    "recovery without a record is a no-op");

// ---- Profile guard: interactive sessions must ignore a stale record from a past run ----
var staleStore = new FakeProfileStore { Value = 1 };
var staleChannel = new FakeProfileChannel(2); // user changed to Performance elsewhere
var interactive = new ProfileGuard(staleChannel, staleStore);
Check(interactive.OriginalWireMode is null, "interactive session ignores stale record");
Check(interactive.RestoreOriginal().Kind == ProfileOutcomeKind.NotCaptured, "no stale restore");

// ---- Telemetry CSV: layout keeps a confirmed profile change separate from measured RPM ----
Check(TelemetryCsv.Header.StartsWith("timestamp_utc,cpu_rpm,gpu_rpm,cpu_celsius,gpu_celsius"), "csv header");
Check(TelemetryCsv.Header.EndsWith("profile,profile_confirmed,note"), "csv header profile columns");
var csvPoint = new HistoryPoint(DateTimeOffset.UnixEpoch, 45, null, 4400, 3800);
string csvRow = TelemetryCsv.Row(csvPoint, "Performance", true);
Check(csvRow.StartsWith("1970-01-01T00:00:00.0000000Z,4400,3800,45,,"), "csv row values in order");
Check(csvRow.EndsWith("Performance,true,"), "csv row records confirmed profile");
string unknownRow = TelemetryCsv.Row(csvPoint, null, null);
Check(unknownRow.EndsWith("unknown,,"), "csv unknown profile with empty confirmed flag");
Check(TelemetryCsv.Row(csvPoint, "a,b", null, "x\"y").Contains("\"a,b\""), "csv escapes commas");
Check(TelemetryCsv.Row(csvPoint, "a,b", null, "x\"y").Contains("\"x\"\"y\""), "csv escapes quotes");
Check(TelemetryCsv.Escape("plain") == "plain" && TelemetryCsv.Escape(null) == "", "csv escape basics");
Check(TelemetryCsv.ProfileChangeNote(FanProfile.Standard, true).StartsWith("profile_change_confirmed"),
    "csv confirmed profile note");
Check(TelemetryCsv.ProfileChangeNote(FanProfile.Standard, false).StartsWith("profile_change_unverified"),
    "csv unverified profile note");
Check(TelemetryCsv.ObservedNote(true) == "rpm_increase_observed", "csv observed increase note");
Check(TelemetryCsv.ObservedNote(false) == "rpm_increase_not_observed", "csv observed no increase note");

// ---- Policy: wire mode to profile name ----
Check(FanPolicy.ProfileName(0) == "Standard" && FanPolicy.ProfileName(1) == "Whisper" &&
      FanPolicy.ProfileName(2) == "Performance", "profile names");
Check(FanPolicy.ProfileName(9) == "unknown", "unknown wire mode name");

// ---- History: snapshot projection is reusable ----
var projected = TelemetryHistory.FromSnapshot(new TelemetrySnapshot(
    new[] { new FanRpm(FanType.Cpu, 4400), new FanRpm(FanType.Gpu, -1) },
    new TemperatureReading(50, null), DateTimeOffset.UnixEpoch));
Check(projected.CpuRpm == 4400 && projected.GpuRpm is null && projected.CpuCelsius == 50, "FromSnapshot projects");

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

sealed class FakeProfileChannel : IProfileChannel
{
    private int _wire;
    public FakeProfileChannel(int wire) => _wire = wire;

    public bool ReadReturnsNull { get; set; }
    public bool AcceptWrites { get; set; } = true;
    public List<int> Writes { get; } = new();
    public int Wire => _wire;

    public int? ReadWireMode() => ReadReturnsNull ? null : _wire;

    public bool WriteWireMode(int wireMode)
    {
        if (!AcceptWrites) return false;
        Writes.Add(wireMode);
        _wire = wireMode;
        return true;
    }
}

sealed class FakeProfileStore : IOriginalProfileStore
{
    public int? Value { get; set; }
    public int? Load() => Value;
    public void Save(int wireMode) => Value = wireMode;
    public void Clear() => Value = null;
}
