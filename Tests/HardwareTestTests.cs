using System.Collections.Generic;

namespace Displicare.Tests
{
    /// <summary>The pure parts of the in-app hardware test: planning, verdicts and backlight recovery.</summary>
    public static class HardwareTestTests
    {
        [Test]
        public static void PlanRunsHardwareCasesFirstOnDdcDisplays()
        {
            // Software cases must follow hardware ones, so the switch can be seen raising the backlight to 100
            var plan = HardwareTest.PlanFor(true);
            Assert.True(plan.Count == 5, "DDC/CI plan has 2 hardware + 3 software cases");
            Assert.True(plan[0].Hardware && plan[1].Hardware, "hardware cases first");
            for (int i = 2; i < plan.Count; i++) Assert.True(!plan[i].Hardware, "software case " + i);
            Assert.True(plan[1].Brightness < 100, "last hardware case leaves the backlight below 100");
        }

        [Test]
        public static void PlanSkipsHardwareCasesWithoutDdc()
        {
            var plan = HardwareTest.PlanFor(false);
            Assert.Equal(3, plan.Count, "software-only plan");
            foreach (var c in plan) Assert.True(!c.Hardware, c.Name + " is software");
        }

        [Test]
        public static void RampVerdictUsesDriverTolerance()
        {
            var a = new ushort[768];
            var b = new ushort[768];
            Assert.Equal(0, HardwareTest.MaxRampDifference(a, b), "identical");
            b[300] = 256;
            Assert.True(HardwareTest.RampPasses(HardwareTest.MaxRampDifference(a, b)), "256 is LUT quantization");
            b[300] = 257;
            Assert.True(!HardwareTest.RampPasses(HardwareTest.MaxRampDifference(a, b)), "257 fails");
            Assert.Equal(-1, HardwareTest.MaxRampDifference(a, null), "unreadable ramp");
            Assert.True(!HardwareTest.RampPasses(-1), "unreadable ramp fails");
        }

        [Test]
        public static void FilterFollowsTheSoftwareHdrDisplay()
        {
            var sdr = new DisplayInfo { DeviceName = "A", UseHardwareBrightness = false };
            var hdrHardware = new DisplayInfo { DeviceName = "B", IsHdr = true, SupportsDdcCi = true, UseHardwareBrightness = true };
            var hdrSoftware = new DisplayInfo { DeviceName = "C", IsHdr = true, UseHardwareBrightness = false };

            Assert.True(MonitorEngine.FilterSource(new List<DisplayInfo> { sdr, hdrHardware, hdrSoftware }) == hdrSoftware, "software HDR display wins");
            Assert.True(MonitorEngine.FilterSource(new List<DisplayInfo> { sdr, hdrHardware }) == hdrHardware, "else the first HDR display");
            Assert.True(MonitorEngine.FilterSource(new List<DisplayInfo> { sdr }) == null, "no HDR display: no filter");
        }

        [Test]
        public static void RecoveryFileParsingSkipsBadLines()
        {
            var saved = HardwareTest.ParseRecovery(new[]
            {
                @"\\.\DISPLAY1" + "\t55",
                @"\\.\DISPLAY2" + "\t101",   // out of range
                "garbage",
                @"\\.\DISPLAY3" + "\tabc",
                "",
                null
            });
            Assert.Equal(1, saved.Count, "only the valid line");
            Assert.Equal(55, saved[@"\\.\display1"], "lookup ignores case");
            Assert.Equal(0, HardwareTest.ParseRecovery(null).Count, "missing file");
        }

        [Test]
        public static void SummaryLineNamesTheOutcome()
        {
            Assert.Equal("Result: ALL PASSED (12 passed, 0 failed)", HardwareTest.SummaryLine(12, 0, false), "pass");
            Assert.Equal("Result: FAILURES (10 passed, 2 failed)", HardwareTest.SummaryLine(10, 2, false), "fail");
            Assert.Equal("Result: STOPPED (4 passed, 0 failed)", HardwareTest.SummaryLine(4, 0, true), "stopped");
        }
    }
}
