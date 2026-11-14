using System;
using System.Collections.Generic;

namespace Displicare.Tests
{
    /// <summary>Display changes and screen power: when the backlight is written again, and which screen is which.</summary>
    public static class DisplayEventTests
    {
        private static DisplayInfo Monitor(uint min, uint max, uint reported, int set)
        {
            var d = new DisplayInfo();
            d.SupportsDdcCi = true;
            d.UseHardwareBrightness = true;
            d.MinBrightness = min;
            d.MaxBrightness = max;
            d.CurrentBrightness = reported;
            d.UserBrightness = set;
            return d;
        }

        [Test]
        public static void BacklightIsWrittenAgainOnlyWhenScreensComeOn()
        {
            // Regression (2.5.3): a write during standby is stored but not shown, so Solar Sync left the panel at its old brightness
            const int unknown = -1, off = 0, on = 1, dimmed = 2;
            Assert.True(MainWindow.ScreensCameOn(off, on), "off to on");
            Assert.True(MainWindow.ScreensCameOn(off, dimmed), "off to dimmed");
            Assert.True(!MainWindow.ScreensCameOn(unknown, on), "the first report is only the current state");
            Assert.True(!MainWindow.ScreensCameOn(on, off), "going off");
            Assert.True(!MainWindow.ScreensCameOn(dimmed, on), "the screen never went dark");
            Assert.True(!MainWindow.ScreensCameOn(off, off), "still off");
        }

        [Test]
        public static void BacklightSetOnTheMonitorItselfIsLeftAlone()
        {
            // Writing again is for a monitor that reports our value but may not show it. One that reports another value was
            // changed with its own buttons: the first version of the rewrite put Displicare's back after every unlock
            Assert.True(MainWindow.BacklightHoldsOurValue(Monitor(0, 100, 15, 15)), "reports what we set");
            Assert.True(!MainWindow.BacklightHoldsOurValue(Monitor(0, 100, 40, 100)), "set to 40 on the monitor");
            Assert.True(MainWindow.BacklightHoldsOurValue(Monitor(0, 50, 16, 15)), "15% reads back as 16% on a 0-50 monitor: the same step");
            Assert.True(!MainWindow.BacklightHoldsOurValue(Monitor(0, 50, 20, 15)), "another step");

            var software = Monitor(0, 100, 100, 100);
            software.UseHardwareBrightness = false;
            Assert.True(!MainWindow.BacklightHoldsOurValue(software), "software dimming: the backlight isn't ours to write");
            var noDdc = Monitor(0, 100, 15, 15);
            noDdc.SupportsDdcCi = false;
            Assert.True(!MainWindow.BacklightHoldsOurValue(noDdc), "no DDC/CI");
            Assert.True(!MainWindow.BacklightHoldsOurValue(null), "no display");
        }

        [Test]
        public static void BacklightIsWrittenAgainTwoSixAndFifteenSecondsAfterAWake()
        {
            var seconds = MainWindow.BacklightRewriteSeconds;
            Assert.Equal(2, MainWindow.SecondsBeforeStep(seconds, 0), "first write");
            Assert.Equal(4, MainWindow.SecondsBeforeStep(seconds, 1), "second, 6 s after the wake");
            Assert.Equal(9, MainWindow.SecondsBeforeStep(seconds, 2), "third, 15 s after the wake");
            Assert.Equal(-1, MainWindow.SecondsBeforeStep(seconds, 3), "then it stops");
            Assert.Equal(-1, MainWindow.SecondsBeforeStep(seconds, -1), "no such step");
            Assert.Equal(-1, MainWindow.SecondsBeforeStep(null, 0), "no steps");
            Assert.Equal(1, MainWindow.SecondsBeforeStep(new[] { 1, 4 }, 0), "HDR recheck after 1 s");
            Assert.Equal(3, MainWindow.SecondsBeforeStep(new[] { 1, 4 }, 1), "and after 4 s");
        }

        [Test]
        public static void DisplayChangeRewritesTheBacklightOnlyWhenAScreenAppears()
        {
            // An HDR switch or a game changing the resolution keeps the same screens: no reason to write to every monitor
            Assert.True(!MainWindow.ScreenAppeared(new[] { "GBT3214" }, new[] { "GBT3214" }), "same screen");
            Assert.True(!MainWindow.ScreenAppeared(new[] { "GBT3214" }, new[] { "gbt3214" }), "same screen, another spelling");
            Assert.True(MainWindow.ScreenAppeared(new[] { "GBT3214" }, new[] { "GBT3214", "GSM82CD" }), "the TV came on");
            Assert.True(MainWindow.ScreenAppeared(new string[0], new[] { "GBT3214" }), "the monitor came back");
            Assert.True(!MainWindow.ScreenAppeared(new[] { "GBT3214", "GSM82CD" }, new[] { "GBT3214" }), "the TV went off");
            Assert.True(MainWindow.ScreenAppeared(new[] { "GBT3214" }, new[] { "GSM82CD" }), "another screen took over");
        }

        [Test]
        public static void SolarSyncWritesTheBacklightAgainEveryTenMinutes()
        {
            // A monitor switched off with its own button comes back without Windows noticing
            var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(!MainWindow.BacklightRewriteDue(now.AddMinutes(-9), now), "nine minutes: not yet");
            Assert.True(MainWindow.BacklightRewriteDue(now.AddMinutes(-10), now), "ten minutes");
            Assert.True(MainWindow.BacklightRewriteDue(now.AddHours(1), now), "the clock moved back");
        }

        [Test]
        public static void TwoMonitorsOfOneModelGetKeysOfTheirOwn()
        {
            // Regression (2.5.3): two identical monitors were one screen, so with sync off they shared settings and ramps
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal("GBT3214", MonitorEngine.UniqueMonitorKey("GBT3214", taken), "the first keeps the model");
            taken.Add("GBT3214");
            Assert.Equal("GBT3214#2", MonitorEngine.UniqueMonitorKey("GBT3214", taken), "the second");
            Assert.Equal("GBT3214#2", MonitorEngine.UniqueMonitorKey("gbt3214", taken).ToUpperInvariant(), "whatever the spelling");
            taken.Add("GBT3214#2");
            Assert.Equal("GBT3214#3", MonitorEngine.UniqueMonitorKey("GBT3214", taken), "the third");
            Assert.Equal("GSM82CD", MonitorEngine.UniqueMonitorKey("GSM82CD", taken), "another model is untouched");
            Assert.Equal(null, MonitorEngine.UniqueMonitorKey(null, taken), "no model");
        }
    }
}
