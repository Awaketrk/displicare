using System.Collections.Generic;
using System;
using System.Text;

namespace Displicare.Tests
{
    /// <summary>Settings JSON parsing and validation (no file or registry access).</summary>
    public static class ConfigTests
    {
        [Test]
        public static void UnusableJsonReturnsNull()
        {
            Assert.True(ConfigManager.Parse(null) == null, "null");
            Assert.True(ConfigManager.Parse("") == null, "empty");
            Assert.True(ConfigManager.Parse("{") == null, "truncated");
            Assert.True(ConfigManager.Parse("not json") == null, "garbage");
        }

        [Test]
        public static void ActivePresetComesFromTheValues()
        {
            var builtIn = MonitorEngine.GetPresets();
            var gaming = builtIn.Find(p => p.Name == "Gaming FPS");
            Assert.Equal("Gaming FPS", MainWindow.MatchPreset(gaming.ColorTemp, gaming.RedGain, gaming.GreenGain, gaming.BlueGain, gaming.Contrast, gaming.Gamma, 40, builtIn, null), "its values, at any brightness");
            Assert.True(MainWindow.MatchPreset(gaming.ColorTemp + 100, gaming.RedGain, gaming.GreenGain, gaming.BlueGain, gaming.Contrast, gaming.Gamma, 40, builtIn, null) == null, "warmth moved: no preset");
            Assert.True(MainWindow.MatchPreset(gaming.ColorTemp, gaming.RedGain, gaming.GreenGain, gaming.BlueGain, gaming.Contrast, gaming.Gamma + 0.1, 40, builtIn, null) == null, "gamma moved: no preset");
            Assert.Equal("Balanced", MainWindow.MatchPreset(6500, 1, 1, 1, 1, 1, 100, builtIn, null), "neutral is Balanced");

            var mine = new CustomPreset { Id = "x", Name = "Mine", Brightness = 60, ColorTemperature = 6500, RedGain = 1, GreenGain = 1, BlueGain = 1, Contrast = 1, Gamma = 1 };
            var custom = new List<CustomPreset> { mine };
            Assert.Equal("custom:x", MainWindow.MatchPreset(6500, 1, 1, 1, 1, 1, 60, builtIn, custom), "a custom preset wins with its brightness");
            Assert.Equal("Balanced", MainWindow.MatchPreset(6500, 1, 1, 1, 1, 1, 61, builtIn, custom), "other brightness: not the custom one");

            // Every built-in must fit the sliders, or it could never show as active
            foreach (var p in builtIn)
            {
                Assert.Equal(p.Name, MainWindow.MatchPreset(p.ColorTemp, p.RedGain, p.GreenGain, p.BlueGain, p.Contrast, p.Gamma, 50, builtIn, null), p.Name + " matches itself");
                Assert.True(p.ColorTemp >= 2500 && p.ColorTemp <= 10000, p.Name + " fits the warmth slider");
            }
        }

        [Test]
        public static void SyncOffNeverSelectsAllDisplays()
        {
            // Regression (2.5.3): after a restart sync off selected "All Displays", and the next slider move put one
            // screen's settings on every screen (that's how the LG got the AORUS's colors)
            var keys = new List<string> { "GBT3214", "GSM82CD" };
            Assert.Equal(0, MainWindow.PickDisplayEntry(keys, 0, "GSM82CD", true), "sync on: All");
            Assert.Equal(2, MainWindow.PickDisplayEntry(keys, 0, "GSM82CD", false), "sync off: keeps the selected screen");
            Assert.Equal(1, MainWindow.PickDisplayEntry(keys, 0, null, false), "after a restart: the main screen");
            Assert.Equal(2, MainWindow.PickDisplayEntry(keys, 1, "TCL9653", false), "selected screen gone: the main screen");
            Assert.Equal(1, MainWindow.PickDisplayEntry(keys, 5, null, false), "no main screen known: the first");
            Assert.Equal(0, MainWindow.PickDisplayEntry(new List<string>(), 0, null, false), "no screens");
            Assert.Equal(1, MainWindow.PickDisplayEntry(new List<string> { "A" }, 0, "a", false), "keys ignore case");
        }

        [Test]
        public static void UninstallOnlyRemovesItsOwnStartupEntry()
        {
            const string exe = @"C:\Users\x\AppData\Local\Programs\Displicare\Displicare.exe";
            Assert.True(ConfigManager.RunEntryStarts("\"" + exe + "\" --minimized", exe), "its own entry");
            Assert.True(ConfigManager.RunEntryStarts("\"" + exe.ToUpperInvariant() + "\"", exe), "paths ignore case");
            Assert.True(!ConfigManager.RunEntryStarts(@"""D:\Portable\Displicare.exe"" --portable", exe), "another copy's entry stays");
            Assert.True(!ConfigManager.RunEntryStarts(null, exe), "no entry");
        }

        [Test]
        public static void NewScreensStartAtFullBrightness()
        {
            // HDR on an OLED is normally run at 100%; a screen without DDC/CI would otherwise start dimmed in software
            Assert.Equal(100, new DisplayInfo().UserBrightness, "new display");
            Assert.Equal(100, new AppSettings().Brightness, "new settings");
            Assert.Equal(100, new AppSettings().SolarDayBrightness, "Solar Sync by day");
        }

        [Test]
        public static void MissingFieldsGetDefaultsNotZero()
        {
            // Regression: deserialization skipped constructors, so missing gains became 0 → clamped to 0.1
            var s = ConfigManager.Parse("{}");
            Assert.Equal(100, s.Brightness, "brightness");
            Assert.Equal(6500, s.ColorTemperature, "color temperature");
            Assert.Near(1.0, s.RedGain, 1e-9, "red gain");
            Assert.Near(1.0, s.GreenGain, 1e-9, "green gain");
            Assert.Near(1.0, s.BlueGain, 1e-9, "blue gain");
            Assert.Near(1.0, s.Contrast, 1e-9, "contrast");
            Assert.Near(1.0, s.Gamma, 1e-9, "gamma");
            Assert.Equal(100, s.SolarDayBrightness, "solar day brightness");
            Assert.Equal(35, s.SolarNightBrightness, "solar night brightness");
            Assert.True(s.StartMinimized, "start minimized defaults on");
            Assert.True(s.SyncAllMonitors, "sync all defaults on");
            Assert.True(s.DisplayProfiles != null && s.CustomPresets != null, "lists are never null");
        }

        [Test]
        public static void MissingProfileFieldsKeepHardwareMode()
        {
            // Regression: a profile without UseHardwareBrightness silently switched the monitor to software mode
            var s = ConfigManager.Parse("{\"DisplayProfiles\":[{\"DeviceName\":\"\\\\\\\\.\\\\DISPLAY1\"}]}");
            var p = s.DisplayProfiles[0];
            Assert.Equal(@"\\.\DISPLAY1", p.DeviceName, "device name");
            Assert.True(p.UseHardwareBrightness, "hardware mode defaults on");
            Assert.Near(1.0, p.RedGain, 1e-9, "profile red gain");
            Assert.Equal(100, p.Brightness, "profile brightness");
        }

        [Test]
        public static void OutOfRangeValuesAreRepaired()
        {
            var s = ConfigManager.Parse("{\"Brightness\":500,\"ColorTemperature\":99999,\"RedGain\":5,\"BlueGain\":-3," +
                                        "\"Contrast\":9,\"Gamma\":0.01,\"SolarNightBrightness\":95,\"UserLatitude\":200}");
            Assert.Equal(100, s.Brightness, "brightness falls back to default");
            Assert.Equal(6500, s.ColorTemperature, "color temperature falls back to default");
            Assert.Near(2.0, s.RedGain, 1e-9, "red gain clamps to max");
            Assert.Near(0.1, s.BlueGain, 1e-9, "blue gain clamps to min");
            Assert.Near(1.8, s.Contrast, 1e-9, "contrast clamps to max");
            Assert.Near(0.5, s.Gamma, 1e-9, "gamma clamps to min");
            Assert.Equal(35, s.SolarNightBrightness, "night brightness above slider max falls back");
            Assert.Near(90.0, s.UserLatitude, 1e-9, "latitude clamps");
        }

        [Test]
        public static void UnknownFieldsFromOlderVersionsAreIgnored()
        {
            var s = ConfigManager.Parse("{\"Brightness\":40,\"DisplayProfiles\":[{\"HdrSdrBaselineNits\":240,\"Brightness\":15}],\"SomethingNew\":true}");
            Assert.Equal(40, s.Brightness, "known field still read");
            Assert.Equal(15, s.DisplayProfiles[0].Brightness, "profile still read");
        }

        [Test]
        public static void PresetFloodIsTrimmedAndStillSanitized()
        {
            // Regression: when more than 100 presets were trimmed, the kept ones skipped sanitizing
            var json = new StringBuilder("{\"CustomPresets\":[");
            string longName = new string('x', 500);
            for (int i = 0; i < 150; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"Name\":\"").Append(longName).Append("\",\"Brightness\":999,\"RedGain\":50}");
            }
            json.Append("]}");

            var s = ConfigManager.Parse(json.ToString());
            Assert.Equal(ConfigManager.MaxCustomPresets, s.CustomPresets.Count, "preset count");
            foreach (var p in s.CustomPresets)
            {
                Assert.True(p.Name.Length <= ConfigManager.MaxPresetNameLength, "name trimmed");
                Assert.Equal(100, p.Brightness, "preset brightness repaired");
                Assert.Near(2.0, p.RedGain, 1e-9, "preset gain clamped");
                Assert.True(!string.IsNullOrEmpty(p.Id), "preset has an id");
            }
        }

        [Test]
        public static void NullEntriesAreDropped()
        {
            var s = ConfigManager.Parse("{\"CustomPresets\":[null,{\"Name\":\"Night\"}],\"DisplayProfiles\":[null]}");
            Assert.Equal(1, s.CustomPresets.Count, "presets");
            Assert.Equal("Night", s.CustomPresets[0].Name, "preset name");
            Assert.Equal(0, s.DisplayProfiles.Count, "profiles");
        }
    }
}
