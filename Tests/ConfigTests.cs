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
        public static void MissingFieldsGetDefaultsNotZero()
        {
            // Regression: deserialization skipped constructors, so missing gains became 0 → clamped to 0.1
            var s = ConfigManager.Parse("{}");
            Assert.Equal(80, s.Brightness, "brightness");
            Assert.Equal(6500, s.ColorTemperature, "color temperature");
            Assert.Near(1.0, s.RedGain, 1e-9, "red gain");
            Assert.Near(1.0, s.GreenGain, 1e-9, "green gain");
            Assert.Near(1.0, s.BlueGain, 1e-9, "blue gain");
            Assert.Near(1.0, s.Contrast, 1e-9, "contrast");
            Assert.Near(1.0, s.Gamma, 1e-9, "gamma");
            Assert.Equal(85, s.SolarDayBrightness, "solar day brightness");
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
            Assert.Equal(80, p.Brightness, "profile brightness");
        }

        [Test]
        public static void OutOfRangeValuesAreRepaired()
        {
            var s = ConfigManager.Parse("{\"Brightness\":500,\"ColorTemperature\":99999,\"RedGain\":5,\"BlueGain\":-3," +
                                        "\"Contrast\":9,\"Gamma\":0.01,\"SolarNightBrightness\":95,\"UserLatitude\":200}");
            Assert.Equal(80, s.Brightness, "brightness falls back to default");
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
                Assert.Equal(80, p.Brightness, "preset brightness repaired");
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
