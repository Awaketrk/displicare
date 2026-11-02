using System;

namespace Displicare.Tests
{
    /// <summary>Gamma ramp and software brightness math, in SDR and HDR.</summary>
    public static class RampTests
    {
        private static int Identity(int i) { return (i * 65535) / 255; }

        private static DisplayInfo Display(bool hardware, int brightness, bool hdr = false)
        {
            return new DisplayInfo
            {
                DeviceName = @"\\.\DISPLAY1",
                SupportsDdcCi = hardware,
                UseHardwareBrightness = hardware,
                UserBrightness = brightness,
                IsHdr = hdr
            };
        }

        private static ushort[] Build(DisplayInfo d, ushort[] baseline = null)
        {
            var output = new ushort[768];
            MonitorEngine.BuildRamp(d, baseline, output);
            return output;
        }

        private static ushort[] ScaledIdentity(double scale)
        {
            var r = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                ushort v = (ushort)Math.Round(Identity(i) * scale);
                r[i] = v; r[256 + i] = v; r[512 + i] = v;
            }
            return r;
        }

        [Test]
        public static void NeutralHardwareDisplayGetsIdentityRamp()
        {
            var ramp = Build(Display(hardware: true, brightness: 40));
            for (int i = 0; i < 256; i++)
            {
                Assert.Near(Identity(i), ramp[i], 1, "red[" + i + "]");
                Assert.Near(Identity(i), ramp[512 + i], 1, "blue[" + i + "]");
            }
        }

        [Test]
        public static void SoftwareBrightness100ReachesFullWhite()
        {
            var ramp = Build(Display(hardware: false, brightness: 100));
            Assert.Equal((ushort)65535, ramp[255], "red white");
            Assert.Equal((ushort)65535, ramp[511], "green white");
            Assert.Equal((ushort)65535, ramp[767], "blue white");
        }

        [Test]
        public static void SoftwareBrightnessScalesWhiteByCurve()
        {
            foreach (int b in new[] { 0, 15, 50, 80 })
            {
                var ramp = Build(Display(hardware: false, brightness: b));
                double expected = MonitorEngine.SoftwareBrightnessScale(b) * 65535;
                Assert.Near(expected, ramp[255], 2, "white at " + b + "%");
            }
        }

        [Test]
        public static void SoftwareBrightnessCurveIsMonotonicAndBounded()
        {
            double prev = 0;
            for (int b = 0; b <= 100; b++)
            {
                double s = MonitorEngine.SoftwareBrightnessScale(b);
                Assert.True(s >= prev, "scale must not fall at " + b + "%");
                if (b > 16) Assert.True(s > prev, "scale must rise above the SDR floor at " + b + "%");
                Assert.True(s > 0.5 && s <= 1.0, "scale out of range at " + b + "%: " + s);
                prev = s;
            }
            Assert.Near(1.0, MonitorEngine.SoftwareBrightnessScale(100), 1e-9, "scale at 100%");
        }

        [Test]
        public static void SdrAndHdrDimToTheSameLight()
        {
            // Regression (2.5.0): SDR's 30% floor made software dimming much brighter than the hardware slider
            foreach (int b in new[] { 20, 50, 80 })
                Assert.Near(MonitorEngine.SoftwareLuminance(b), Math.Pow(MonitorEngine.SoftwareBrightnessScale(b), 2.2), 1e-9, "light at " + b + "%");
            Assert.Near(0.22, Math.Pow(MonitorEngine.SoftwareBrightnessScale(0), 2.2), 1e-9, "SDR stops at the driver's 22%");
            Assert.Near(0.08, MonitorEngine.SoftwareLuminance(0), 1e-9, "HDR goes down to 8%");
        }

        [Test]
        public static void LiftedGammaLimitLetsSdrDimTo8Percent()
        {
            try
            {
                MonitorEngine.RampFullRange = true;
                Assert.Near(0.08, Math.Pow(MonitorEngine.SoftwareBrightnessScale(0), 2.2), 1e-9, "SDR reaches 8% light");
                var ramp = Build(Display(hardware: false, brightness: 0));
                Assert.True(ramp[255] < 65535 - 32760, "white goes past Windows' default limit");
                var warm = Display(hardware: true, brightness: 50);
                warm.ColorTemperature = 1500;
                Assert.True(Build(warm)[767] < 65535 - 32760, "very warm blue isn't cut short");
            }
            finally { MonitorEngine.RampFullRange = false; }
            Assert.True(Build(Display(hardware: false, brightness: 0))[255] >= 65535 - 32760, "back within the limit");
        }

        [Test]
        public static void GammaRangeBackupPutsBackWhatWasThere()
        {
            Assert.Equal(GammaRange.BackupAbsent, GammaRange.BackupOf(null), "no value before");
            Assert.Equal("1", GammaRange.BackupOf(1), "an old number");
            Assert.True(GammaRange.RestoreArguments(GammaRange.BackupAbsent).StartsWith("delete "), "absent: delete the value");
            Assert.True(GammaRange.RestoreArguments("1").EndsWith("/d 1 /f"), "old number written back");
            Assert.True(GammaRange.RestoreArguments(null) == null, "not ours (f.lux): left alone");
            Assert.True(GammaRange.RestoreArguments("junk") == null, "unreadable backup: left alone");
            Assert.True(GammaRange.RestoreArguments("99999") == null, "tampered backup: nothing out of range written elevated");
            Assert.True(GammaRange.RestoreArguments("-1") == null, "negative backup rejected");
            Assert.True(GammaRange.RestoreArguments("256") != null, "256 itself is a valid old value");
            Assert.True(GammaRange.SetArguments(256).Contains("GdiIcmGammaRange /t REG_DWORD /d 256"), "sets 256");
            Assert.True(GammaRange.IsUnlimited(256) && !GammaRange.IsUnlimited(255) && !GammaRange.IsUnlimited(null), "256 or more lifts it");
        }

        [Test]
        public static void AutoColorManagementIsNotHdr()
        {
            // Regression (2.5.0): the AORUS in SDR with Auto Color Management reports advanced color on (0x7) and got
            // the HDR curve, which barely dimmed white and crushed blacks
            Assert.True(!HdrHelper.IsHdrMode(0xD7, 1), "24H2: wide color SDR (ACM) is not HDR");
            Assert.True(HdrHelper.IsHdrMode(0xF3, 2), "24H2: HDR mode is HDR");
            Assert.True(!HdrHelper.IsHdrMode(0x13, 0), "24H2: plain SDR");
            Assert.True(!HdrHelper.IsHdrMode(0x7, -1), "older builds: enabled but wide color enforced (ACM)");
            Assert.True(HdrHelper.IsHdrMode(0x3, -1), "older builds: enabled, not enforced");
            Assert.True(!HdrHelper.IsHdrMode(0x1, -1), "older builds: supported but off");
        }

        [Test]
        public static void PqCurveRoundTripsAndMatchesTheStandard()
        {
            Assert.Near(0.7518, MonitorEngine.LightToPq(0.1), 1e-3, "1000 nits");
            Assert.Near(0.5081, MonitorEngine.LightToPq(0.01), 1e-3, "100 nits");
            Assert.Near(1.0, MonitorEngine.LightToPq(1.0), 1e-9, "10000 nits");
            for (int i = 0; i <= 20; i++)
            {
                double s = i / 20.0;
                Assert.Near(s, MonitorEngine.LightToPq(MonitorEngine.PqToLight(s)), 1e-9, "round trip at " + s);
            }
        }

        [Test]
        public static void HdrNeutralRampIsIdentity()
        {
            var ramp = Build(Display(hardware: false, brightness: 100, hdr: true));
            for (int i = 0; i < 768; i++) Assert.Near(Identity(i % 256), ramp[i], 1, "ramp[" + i + "]");
        }

        [Test]
        public static void HdrSoftwareDimmingScalesLightNotSignal()
        {
            // Scaling a PQ signal flattens contrast (washed out); the ramp must scale the light behind it instead
            foreach (int b in new[] { 0, 15, 50 })
            {
                var ramp = Build(Display(hardware: false, brightness: b, hdr: true));
                double want = MonitorEngine.SoftwareLuminance(b);
                for (int i = 64; i < 256; i += 16)
                {
                    double ratio = MonitorEngine.PqToLight(ramp[i] / 65535.0) / MonitorEngine.PqToLight(i / 255.0);
                    Assert.Near(want, ratio, want * 0.02, "light ratio at " + b + "%, step " + i);
                }
                Assert.True(ramp[255] < 65535 && ramp[255] > 65535 / 2, "white dimmed within the driver's limit at " + b + "%");
            }
        }

        [Test]
        public static void HdrWarmthIsAGainInLight()
        {
            // The SDR ramp scales the 2.2-encoded signal by KelvinToRgb, which is value^2.2 in light; HDR matches that
            var d = Display(hardware: true, brightness: 50, hdr: true);
            d.ColorTemperature = 3400;
            double kr, kg, kb;
            MonitorEngine.KelvinToRgb(3400, out kr, out kg, out kb);
            var ramp = Build(d);
            double inLight = MonitorEngine.PqToLight(200 / 255.0);
            Assert.Near(1.0, MonitorEngine.PqToLight(ramp[200] / 65535.0) / inLight, 0.02, "red stays full when warm");
            Assert.Near(Math.Pow(kb, 2.2), MonitorEngine.PqToLight(ramp[512 + 200] / 65535.0) / inLight, 0.02, "blue scaled in light");
            Assert.True(ramp[512 + 200] < ramp[256 + 200] && ramp[256 + 200] < ramp[200], "warm: b < g < r");
        }

        [Test]
        public static void HdrRampStillAppliesContrastAndGamma()
        {
            var d = Display(hardware: true, brightness: 50, hdr: true);
            d.Gamma = 1.5;
            var ramp = Build(d);
            Assert.True(ramp[128] > Identity(128) + 1000, "gamma 1.5 brightens mid-tones on the HDR ramp");
        }

        [Test]
        public static void RampsStayValidForExtremeSettings()
        {
            // Windows rejects ramps that aren't monotonic or stray more than 32760 from identity
            foreach (int kelvin in new[] { 2500, 3400, 6500, 10000 })
            foreach (double contrast in new[] { 0.5, 1.0, 1.8 })
            foreach (double gamma in new[] { 0.5, 1.0, 2.5 })
            foreach (int brightness in new[] { 0, 50, 100 })
            foreach (bool hardware in new[] { true, false })
            foreach (bool hdr in new[] { false, true })
            {
                var d = Display(hardware, brightness, hdr);
                d.ColorTemperature = kelvin; d.Contrast = contrast; d.Gamma = gamma;
                d.RedGain = 2.0; d.BlueGain = 0.1;
                var ramp = Build(d);
                string ctx = string.Format("{0}K c{1} g{2} b{3} hw{4} hdr{5}", kelvin, contrast, gamma, brightness, hardware, hdr);
                for (int ch = 0; ch < 3; ch++)
                for (int i = 0; i < 256; i++)
                {
                    int v = ramp[ch * 256 + i];
                    Assert.True(Math.Abs(v - Identity(i)) <= 32760, "deviation too large at " + ctx + " [" + ch + "," + i + "]");
                    if (i > 0) Assert.True(v >= ramp[ch * 256 + i - 1], "not monotonic at " + ctx + " [" + ch + "," + i + "]");
                }
            }
        }

        [Test]
        public static void BaselineCheckAcceptsIdentity()
        {
            Assert.True(MonitorEngine.IsNeutralBaseline(ScaledIdentity(1.0)), "identity ramp is a valid baseline");
        }

        [Test]
        public static void BaselineCheckRejectsDimmedLeftover()
        {
            // Regression: a 90% ramp left by a previous run was captured as "original", capping 100% at 90%
            Assert.True(!MonitorEngine.IsNeutralBaseline(ScaledIdentity(0.9)), "90% leftover must be rejected");
            Assert.True(!MonitorEngine.IsNeutralBaseline(null), "null");
            Assert.True(!MonitorEngine.IsNeutralBaseline(new ushort[10]), "wrong length");
        }

        [Test]
        public static void BaselineCheckRejectsWarmLeftover()
        {
            var warm = ScaledIdentity(1.0);
            for (int i = 0; i < 256; i++) warm[512 + i] = (ushort)(warm[512 + i] * 0.55); // 3400K-ish blue
            Assert.True(!MonitorEngine.IsNeutralBaseline(warm), "warm tinted leftover must be rejected");
        }

        [Test]
        public static void RejectedBaselineFallsBackToIdentity()
        {
            var ramp = Build(Display(hardware: true, brightness: 80), ScaledIdentity(0.9));
            Assert.Equal((ushort)65535, ramp[255], "white with a rejected 90% baseline");
        }

        private static DisplayInfo CurveDisplay()
        {
            // The hardware check's crash case: HDR, software 80%, contrast 1.2, gamma 1.3
            var d = Display(hardware: false, brightness: 80, hdr: true);
            d.Contrast = 1.2;
            d.Gamma = 1.3;
            return d;
        }

        [Test]
        public static void LeftoverCurveIsDiscardedAfterSettingsChange()
        {
            // Regression: after a crash with a contrast/gamma curve, a restart with different settings captured
            // the leftover as the baseline (it looks neutral) and kept it, even through a clean exit
            var identity = ScaledIdentity(1.0);
            var leftover = Build(CurveDisplay(), identity);
            Assert.True(MonitorEngine.IsNeutralBaseline(leftover), "the leftover passes the neutral check, so that alone can't catch it");

            ushort[] savedBaseline, savedApplied;
            Assert.True(MonitorEngine.TryDecodeRampState(MonitorEngine.EncodeRampState(identity, leftover), out savedBaseline, out savedApplied), "state round trip");

            var resolved = MonitorEngine.ResolveCapturedBaseline(leftover, savedBaseline, savedApplied);
            for (int i = 0; i < 768; i++) Assert.Equal(identity[i], resolved[i], "baseline[" + i + "]");

            // With neutral settings now, 100% must come out as the identity again, not the old curve
            var neutral = Build(Display(hardware: false, brightness: 100, hdr: true), resolved);
            Assert.Equal(Identity(128), neutral[128], "mid gray after restart");
        }

        [Test]
        public static void LeftoverResolvesToSavedCalibrationBaseline()
        {
            // An ICC calibration curve under our ramp comes back as it was, not as linear
            var calibration = ScaledIdentity(1.0);
            for (int i = 0; i < 256; i++) calibration[512 + i] = (ushort)Math.Round(Math.Pow(i / 255.0, 1.08) * 65535);
            Assert.True(MonitorEngine.IsNeutralBaseline(calibration), "test calibration must be a valid baseline");

            var leftover = Build(CurveDisplay(), calibration);
            var resolved = MonitorEngine.ResolveCapturedBaseline(leftover, calibration, leftover);
            for (int i = 0; i < 768; i++) Assert.Equal(calibration[i], resolved[i], "baseline[" + i + "]");
        }

        [Test]
        public static void RampThatIsNotOursIsKept()
        {
            // Another tool or a new calibration changed the ramp since we wrote it: not ours to discard
            var identity = ScaledIdentity(1.0);
            var ours = Build(CurveDisplay(), identity);
            var resolved = MonitorEngine.ResolveCapturedBaseline(identity, identity, ours);
            Assert.True(object.ReferenceEquals(identity, resolved), "captured ramp returned unchanged");

            ushort[] b, a;
            Assert.True(!MonitorEngine.TryDecodeRampState(new byte[100], out b, out a), "truncated state file rejected");
            Assert.True(!MonitorEngine.TryDecodeRampState(null, out b, out a), "missing state rejected");
        }

        [Test]
        public static void BacklightUsesTheMonitorsOwnRange()
        {
            Assert.Equal(40u, MonitorEngine.PercentToRaw(40, 0, 100), "0-100 (AORUS): unchanged");
            Assert.Equal(40, MonitorEngine.RawToPercent(40, 0, 100), "0-100 read back");
            Assert.Equal(25u, MonitorEngine.PercentToRaw(50, 0, 50), "0-50 monitor: 50% is 25");
            Assert.Equal(50, MonitorEngine.RawToPercent(25, 0, 50), "0-50 read back");
            Assert.Equal(255u, MonitorEngine.PercentToRaw(100, 0, 255), "0-255 at full");
            Assert.Equal(55u, MonitorEngine.PercentToRaw(50, 10, 100), "10-100: 50% is halfway, 55");
            Assert.Equal(0, MonitorEngine.RawToPercent(5, 10, 100), "below the minimum reads as 0%");
            Assert.Equal(100, MonitorEngine.RawToPercent(300, 0, 255), "above the maximum reads as 100%");
            Assert.Equal(70u, MonitorEngine.PercentToRaw(70, 0, 0), "no range reported: treat as 0-100");
            Assert.Equal(0u, MonitorEngine.PercentToRaw(-5, 0, 100), "clamped low");
            Assert.Equal(100u, MonitorEngine.PercentToRaw(150, 0, 100), "clamped high");
            for (int p = 0; p <= 100; p += 5)
                Assert.Equal(p, MonitorEngine.RawToPercent(MonitorEngine.PercentToRaw(p, 0, 255), 0, 255), "0-255 round trip at " + p + "%");
        }

        [Test]
        public static void ProcessNameOfFindsRunningProcess()
        {
            uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            Assert.Equal("Displicare.Tests", MainWindow.ProcessNameOf(self), "own process");
            Assert.True(MainWindow.ProcessNameOf(0) == null, "pid 0 (idle) can't be opened");
        }

        [Test]
        public static void OnlyShellEventsThatCanResetGammaReassert()
        {
            Assert.True(MainWindow.IsGammaRelevantShellEvent(4), "window activated (Alt+Tab)");
            Assert.True(MainWindow.IsGammaRelevantShellEvent(0x8004), "rude app activated (fullscreen game)");
            Assert.True(MainWindow.IsGammaRelevantShellEvent(1), "window created");
            Assert.True(MainWindow.IsGammaRelevantShellEvent(16), "monitor changed");
            Assert.True(!MainWindow.IsGammaRelevantShellEvent(6), "title redraw");
            Assert.True(!MainWindow.IsGammaRelevantShellEvent(0x8006), "taskbar flash");
            Assert.True(!MainWindow.IsGammaRelevantShellEvent(12), "media key");
            Assert.True(!MainWindow.IsGammaRelevantShellEvent(8), "keyboard language");
        }

        [Test]
        public static void BrightnessStepsToTheNextMultiple()
        {
            Assert.Equal(65, MainWindow.NextLevel(63, 5, 1), "63 up to 65");
            Assert.Equal(60, MainWindow.NextLevel(63, 5, -1), "63 down to 60");
            Assert.Equal(65, MainWindow.NextLevel(60, 5, 1), "60 up to 65");
            Assert.Equal(55, MainWindow.NextLevel(60, 5, -1), "60 down to 55");
            Assert.Equal(100, MainWindow.NextLevel(97, 10, 1), "capped at 100");
            Assert.Equal(100, MainWindow.NextLevel(100, 5, 1), "stays at 100");
            Assert.Equal(0, MainWindow.NextLevel(3, 10, -1), "down to 0");
            Assert.Equal(0, MainWindow.NextLevel(0, 5, -1), "stays at 0");
        }

        [Test]
        public static void BrightnessHotkeysParse()
        {
            uint mods, key;
            Assert.True(Displicare.Rooms.Hotkey.TryParse(MainWindow.BrightnessUpHotkey, out mods, out key), "up hotkey parses");
            Assert.Equal((uint)System.Windows.Forms.Keys.Up, key, "Up arrow");
            Assert.True(Displicare.Rooms.Hotkey.TryParse(MainWindow.BrightnessDownHotkey, out mods, out key), "down hotkey parses");
            Assert.Equal((uint)System.Windows.Forms.Keys.Down, key, "Down arrow");
            Assert.True(!new AppSettings().BrightnessHotkeys, "off by default");
        }

        [Test]
        public static void KelvinToRgbShape()
        {
            double r, g, b;
            MonitorEngine.KelvinToRgb(3400, out r, out g, out b);
            Assert.Near(1.0, r, 1e-9, "3400K red");
            Assert.True(b < g && g < r, "3400K should be warm (b < g < r)");

            MonitorEngine.KelvinToRgb(8500, out r, out g, out b);
            Assert.Near(1.0, b, 1e-9, "8500K blue");
            Assert.True(r < 1.0, "8500K should be cool (r < 1)");

            double prevBlue = -1;
            for (int k = 2000; k <= 6500; k += 250)
            {
                MonitorEngine.KelvinToRgb(k, out r, out g, out b);
                Assert.True(b >= prevBlue, "blue should rise with temperature at " + k + "K");
                prevBlue = b;
            }
        }
    }
}
