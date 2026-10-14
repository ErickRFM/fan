using FanForge.Core;
int passed = 0;
void Check(bool result, string name)
{
    if (!result) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}
Check(FanPolicy.SupportedModel("VivoBook_ASUSLaptop M3500QC_M3500QC"), "M3500QC allowed");
Check(FanPolicy.SupportedModel("m3500qc"), "case insensitive");
Check(!FanPolicy.SupportedModel("M3500Q"), "other model denied");
Check(!FanPolicy.SupportedModel(null), "null denied");
Check(FanPolicy.ValidFanCount(1) && FanPolicy.ValidFanCount(6), "valid counts");
Check(!FanPolicy.ValidFanCount(0) && !FanPolicy.ValidFanCount(7), "invalid counts");
Check(FanPolicy.Duty(70) == 179, "70 percent pwm");
Check(FanPolicy.Duty(80) == 204, "80 percent pwm");
Check(FanPolicy.Duty(100) == 255, "100 percent pwm");
foreach (int pct in new[] { -1, 0, 1, 50, 69, 101, 255 })
{
    bool rejected = false;
    try { _ = FanPolicy.Duty(pct); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "reject " + pct);
}
Check(FanPolicy.Increased(1000, 1200), "RPM increase");
Check(!FanPolicy.Increased(1000, 1150), "insufficient increase");
Check(!FanPolicy.Increased(1000, 0), "stopped fan");
Check(!FanPolicy.Increased(-1, 3500), "invalid baseline");
Console.WriteLine("Passed: " + passed);
