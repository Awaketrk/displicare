using System;

namespace Displicare.Tests
{
    /// <summary>Gamma ramp, software brightness and HDR color filter math.</summary>
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
                Assert.True(s > prev, "scale must increase at " + b + "%");
                Assert.True(s > 0.5 && s <= 1.0, "scale out of range at " + b + "%: " + s);
                prev = s;
            }
            Assert.Near(1.0, MonitorEngine.SoftwareBrightnessScale(100), 1e-9, "scale at 100%");
        }

        [Test]
        public static void HdrSoftwareModeDoesNotDimTheRamp()
        {
            // In HDR the gamma ramp is undefined, so dimming happens in the color filter instead
            var ramp = Build(Display(hardware: false, brightness: 15, hdr: true));
            Assert.Equal((ushort)65535, ramp[255], "white in HDR software mode");
            Assert.Near(Identity(128), ramp[128], 1, "mid-grey in HDR software mode");
        }

        [Test]
        public static void HdrRampLeavesWarmthAndGainsToTheFilter()
        {
            var d = Display(hardware: true, brightness: 50, hdr: true);
            d.ColorTemperature = 3400; d.BlueGain = 0.5;
            var ramp = Build(d);
            Assert.Equal((ushort)65535, ramp[767], "blue white stays full on the HDR ramp");
            Assert.Near(Identity(128), ramp[512 + 128], 1, "blue mid stays identity on the HDR ramp");
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
        public static void HdrFilterIsNeutralForDefaultHardwareDisplay()
        {
            float r, g, b;
            MonitorEngine.ComputeHdrFilter(Display(hardware: true, brightness: 15, hdr: true), out r, out g, out b);
            Assert.Near(1.0, r, 1e-6, "r"); Assert.Near(1.0, g, 1e-6, "g"); Assert.Near(1.0, b, 1e-6, "b");
        }

        [Test]
        public static void HdrFilterDimsEvenlyInSoftwareMode()
        {
            float r, g, b;
            MonitorEngine.ComputeHdrFilter(Display(hardware: false, brightness: 15, hdr: true), out r, out g, out b);
            Assert.Near(0.218, r, 1e-5, "r"); Assert.Near(0.218, g, 1e-5, "g"); Assert.Near(0.218, b, 1e-5, "b");
        }

        [Test]
        public static void HdrFilterWarmthMatchesSdrWhitePoint()
        {
            // The SDR ramp scales the encoded signal by KelvinToRgb; in linear light that's value^2.2
            var d = Display(hardware: true, brightness: 50, hdr: true);
            d.ColorTemperature = 3400;
            double kr, kg, kb;
            MonitorEngine.KelvinToRgb(3400, out kr, out kg, out kb);
            float r, g, b;
            MonitorEngine.ComputeHdrFilter(d, out r, out g, out b);
            Assert.Near(1.0, r, 1e-6, "red stays full when warm");
            Assert.Near(Math.Pow(kg, 2.2), g, 1e-5, "green in linear light");
            Assert.Near(Math.Pow(kb, 2.2), b, 1e-5, "blue in linear light");
            Assert.True(b < g && g < r, "warm: b < g < r");
        }

        [Test]
        public static void HdrFilterCombinesWarmthGainsAndDimming()
        {
            var d = Display(hardware: false, brightness: 15, hdr: true);
            d.ColorTemperature = 4500; d.RedGain = 2.0; d.GreenGain = 0.9;
            double kr, kg, kb;
            MonitorEngine.KelvinToRgb(4500, out kr, out kg, out kb);
            float r, g, b;
            MonitorEngine.ComputeHdrFilter(d, out r, out g, out b);
            Assert.Near(0.218, r, 1e-5, "red gain above 1 is capped at full, then dimmed");
            Assert.Near(Math.Pow(kg * 0.9, 2.2) * 0.218, g, 1e-5, "green: warmth × gain, linear, dimmed");
            Assert.Near(Math.Pow(kb, 2.2) * 0.218, b, 1e-5, "blue: warmth, linear, dimmed");
        }

        [Test]
        public static void HdrFilterScaleMatchesTunedCurve()
        {
            Assert.Near(0.08, MonitorEngine.HdrFilterScale(0), 1e-6, "0%");
            Assert.Near(0.218, MonitorEngine.HdrFilterScale(15), 1e-6, "15% (night floor)");
            Assert.Near(1.0, MonitorEngine.HdrFilterScale(100), 1e-6, "100%");
            Assert.True(MonitorEngine.HdrFilterScale(-20) >= 0.08 - 1e-6, "below 0% clamps");
            Assert.True(MonitorEngine.HdrFilterScale(150) <= 1.0 + 1e-6, "above 100% clamps");
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
            {
                var d = Display(hardware, brightness);
                d.ColorTemperature = kelvin; d.Contrast = contrast; d.Gamma = gamma;
                d.RedGain = 2.0; d.BlueGain = 0.1;
                var ramp = Build(d);
                string ctx = string.Format("{0}K c{1} g{2} b{3} hw{4}", kelvin, contrast, gamma, brightness, hardware);
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
