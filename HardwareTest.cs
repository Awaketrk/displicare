using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Displicare
{
    internal sealed class HardwareTestCase
    {
        public string Name;
        public bool Hardware;   // DDC/CI backlight instead of software brightness
        public int Brightness = 100;
        public int Kelvin = 6500;
        public double RedGain = 1.0, GreenGain = 1.0, BlueGain = 1.0, Contrast = 1.0, Gamma = 1.0;
    }

    /// <summary>
    /// In-app hardware test: applies a few settings to each display through the normal engine paths and reads back
    /// what Windows and the monitor actually hold (GPU gamma ramp, HDR color filter, DDC/CI backlight), then writes a
    /// report the user can send. The expected values come from the same math (BuildRamp, ComputeHdrFilter), so a
    /// failure means this GPU, driver or monitor didn't accept or keep what we wrote.
    /// Everything is put back afterwards, also when cancelled. If the app dies mid-test, the ramp has its own record
    /// (gamma-*.dat), Windows drops the filter, and the backlight is restored on the next start from a recovery file.
    /// Must run on the UI thread: the color filter is per-thread.
    /// </summary>
    internal sealed class HardwareTest
    {
        internal const int RampTolerance = 256;          // drivers with 10/12-bit LUTs quantize on read-back
        internal const float FilterTolerance = 0.001f;
        internal const int BacklightTolerance = 1;
        private const int SettleMs = 400;
        private const int BacklightTimeoutMs = 3000;
        // Not "hardware-test-*": that's the report pattern users are asked to send
        internal const string RecoveryFileName = "backlight-recovery.txt";
        // What 2.2.0 wrote; still read so an interrupted 2.2.0 test is recovered after an update
        internal const string LegacyRecoveryFileName = "hardware-test-backlight.txt";
        internal const string ReportFilePrefix = "hardware-test-";

        private readonly MonitorEngine _engine;
        private readonly StringBuilder _report = new StringBuilder();
        private int _passed, _failed;
        private string _interruption;

        /// <summary>Each report line as it's written.</summary>
        public event Action<string> LineWritten;
        /// <summary>Short description of the current step.</summary>
        public event Action<string> StepChanged;

        public int Passed { get { return _passed; } }
        public int Failed { get { return _failed; } }
        public bool Cancelled { get; private set; }
        public string ReportPath { get; private set; }
        public string ReportText { get { return _report.ToString(); } }

        public HardwareTest(MonitorEngine engine)
        {
            _engine = engine;
        }

        private sealed class Snapshot
        {
            public DisplayInfo Display;
            public int Brightness, Kelvin;
            public double RedGain, GreenGain, BlueGain, Contrast, Gamma;
            public bool UseHardware;
            public bool HasBacklight;
            public uint Backlight;
        }

        /// <summary>
        /// Cases for one display. Hardware cases come first so the switch to software mode starts from a lowered
        /// backlight, which lets the test check that it's raised to 100 (no stacked dimming).
        /// </summary>
        internal static List<HardwareTestCase> PlanFor(bool supportsDdc)
        {
            var plan = new List<HardwareTestCase>();
            if (supportsDdc)
            {
                plan.Add(new HardwareTestCase { Name = "Hardware 30%, 6500K", Hardware = true, Brightness = 30 });
                plan.Add(new HardwareTestCase { Name = "Hardware 70%, 3400K", Hardware = true, Brightness = 70, Kelvin = 3400 });
            }
            plan.Add(new HardwareTestCase { Name = "Software 50%, 6500K", Brightness = 50 });
            plan.Add(new HardwareTestCase { Name = "Software 100%, 3400K", Kelvin = 3400 });
            plan.Add(new HardwareTestCase
            {
                Name = "Software 80%, RGB 1/0.9/0.8, contrast 1.2, gamma 1.3",
                Brightness = 80, GreenGain = 0.9, BlueGain = 0.8, Contrast = 1.2, Gamma = 1.3
            });
            return plan;
        }

        /// <summary>Largest per-entry difference, or -1 if either ramp is missing.</summary>
        internal static int MaxRampDifference(ushort[] expected, ushort[] live)
        {
            if (expected == null || live == null || expected.Length != 768 || live.Length != 768) return -1;
            int worst = 0;
            for (int i = 0; i < 768; i++)
            {
                worst = Math.Max(worst, Math.Abs(expected[i] - live[i]));
            }
            return worst;
        }

        internal static bool RampPasses(int maxDifference)
        {
            return maxDifference >= 0 && maxDifference <= RampTolerance;
        }

        internal static string SummaryLine(int passed, int failed, bool cancelled)
        {
            string result = failed == 0 ? (cancelled ? "STOPPED" : "ALL PASSED") : "FAILURES";
            return string.Format("Result: {0} ({1} passed, {2} failed)", result, passed, failed);
        }

        /// <summary>Stops the run early with a reason recorded in the report (e.g. displays changed).</summary>
        public void NoteInterruption(string reason)
        {
            _interruption = reason;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var displays = _engine.Displays;
            WriteHeader(displays);

            var snapshots = new List<Snapshot>();
            foreach (var d in displays)
            {
                snapshots.Add(await TakeSnapshot(d));
            }
            WriteRecoveryFile(snapshots);

            try
            {
                foreach (var d in displays)
                {
                    Line("");
                    Line(d.DeviceName + " (" + d.FriendlyName + ")");
                    bool wasHardware = d.SupportsDdcCi && d.UseHardwareBrightness;
                    foreach (var c in PlanFor(d.SupportsDdcCi))
                    {
                        ct.ThrowIfCancellationRequested();
                        bool switchedToSoftware = d.SupportsDdcCi && wasHardware && !c.Hardware;
                        await RunCase(d, displays, c, switchedToSoftware, ct);
                        wasHardware = c.Hardware;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                Line("");
                Line("Stopped: " + (_interruption ?? "cancelled by the user"));
            }
            catch (Exception ex)
            {
                _failed++;
                Line("");
                Line("  [FAIL] Test error: " + ex.Message);
                App.Log("Hardware test error: " + ex);
            }

            // Not a finally block: C# 5 can't await there. Every path above ends here.
            try
            {
                await Restore(snapshots);
                DeleteRecoveryFile();
            }
            catch (Exception ex)
            {
                // Recovery file stays, so the next start puts the backlight back
                _failed++;
                Line("  [FAIL] Restore error: " + ex.Message);
                App.Log("Hardware test restore error: " + ex);
            }
            Line("");
            Line(SummaryLine(_passed, _failed, Cancelled));
            AppendRecentLog();
            SaveReport();
        }

        private async Task RunCase(DisplayInfo d, List<DisplayInfo> displays, HardwareTestCase c, bool switchedToSoftware, CancellationToken ct)
        {
            Step(d.DeviceName + ": " + c.Name);
            d.UseHardwareBrightness = c.Hardware;
            d.UserBrightness = c.Brightness;
            d.ColorTemperature = c.Kelvin;
            d.RedGain = c.RedGain;
            d.GreenGain = c.GreenGain;
            d.BlueGain = c.BlueGain;
            d.Contrast = c.Contrast;
            d.Gamma = c.Gamma;

            // Same paths as MainWindow.ApplyCurrentSettings
            if (c.Hardware)
            {
                int brightness = c.Brightness;
                await Task.Run(() => _engine.ApplyHardwareBrightness(d, brightness));
                if (_engine.HasCustomGammaOrColor(d)) _engine.ApplyColorAndGamma(d);
                else _engine.ResetGammaRamp(d);
            }
            else
            {
                if (switchedToSoftware) _engine.ResetBacklightForSoftwareMode(d);
                _engine.ApplyColorAndGamma(d);
            }

            await Task.Delay(SettleMs, ct);
            CheckRamp(d, c.Name);
            CheckFilter(c.Name);
            if (c.Hardware)
            {
                await CheckBacklight(d, (uint)c.Brightness, c.Name);
            }
            else if (switchedToSoftware)
            {
                await CheckBacklight(d, 100, c.Name + " (switch to software raises backlight)");
            }
        }

        private void CheckRamp(DisplayInfo d, string name)
        {
            var expected = new ushort[768];
            MonitorEngine.BuildRamp(d, d.OriginalRamp, expected);
            var live = _engine.ReadGammaRamp(d);
            int diff = MaxRampDifference(expected, live);
            if (diff < 0)
            {
                Record(false, name + ": gamma ramp could not be read back");
                return;
            }
            Record(RampPasses(diff), string.Format("{0}: ramp max difference {1} (white R/G/B {2}/{3}/{4}, expected {5}/{6}/{7})",
                name, diff, live[255], live[511], live[767], expected[255], expected[511], expected[767]));
        }

        private void CheckFilter(string name)
        {
            float er, eg, eb, lr, lg, lb;
            _engine.ExpectedColorFilter(out er, out eg, out eb);
            if (!_engine.ReadColorFilter(out lr, out lg, out lb))
            {
                Record(false, name + ": color filter could not be read back");
                return;
            }
            bool pass = Math.Abs(er - lr) <= FilterTolerance && Math.Abs(eg - lg) <= FilterTolerance && Math.Abs(eb - lb) <= FilterTolerance;
            Record(pass, string.Format("{0}: filter {1:F3}/{2:F3}/{3:F3} (expected {4:F3}/{5:F3}/{6:F3})",
                name, lr, lg, lb, er, eg, eb));
        }

        /// <summary>Monitors take a moment to apply a DDC/CI write, so read back until it matches or times out.</summary>
        private async Task CheckBacklight(DisplayInfo d, uint expected, string name)
        {
            var started = DateTime.UtcNow;
            uint value = 0;
            bool read = false;
            while (true)
            {
                uint v = 0;
                read = await Task.Run(() => _engine.ReadBacklight(d, out v));
                value = v;
                if (read && Math.Abs((int)value - (int)expected) <= BacklightTolerance) break;
                if ((DateTime.UtcNow - started).TotalMilliseconds > BacklightTimeoutMs) break;
                await Task.Delay(250);
            }

            double seconds = (DateTime.UtcNow - started).TotalSeconds;
            if (!read)
            {
                Record(false, name + ": backlight could not be read over DDC/CI");
                return;
            }
            Record(Math.Abs((int)value - (int)expected) <= BacklightTolerance,
                string.Format("{0}: backlight {1} (expected {2}, read after {3:F1} s)", name, value, expected, seconds));
        }

        private async Task<Snapshot> TakeSnapshot(DisplayInfo d)
        {
            var s = new Snapshot
            {
                Display = d,
                Brightness = d.UserBrightness,
                Kelvin = d.ColorTemperature,
                RedGain = d.RedGain,
                GreenGain = d.GreenGain,
                BlueGain = d.BlueGain,
                Contrast = d.Contrast,
                Gamma = d.Gamma,
                UseHardware = d.UseHardwareBrightness
            };
            if (d.SupportsDdcCi)
            {
                uint v = 0;
                s.HasBacklight = await Task.Run(() => _engine.ReadBacklight(d, out v));
                s.Backlight = v;
            }
            return s;
        }

        private async Task Restore(List<Snapshot> snapshots)
        {
            Step("Restoring your settings...");
            foreach (var s in snapshots)
            {
                var d = s.Display;
                d.UserBrightness = s.Brightness;
                d.ColorTemperature = s.Kelvin;
                d.RedGain = s.RedGain;
                d.GreenGain = s.GreenGain;
                d.BlueGain = s.BlueGain;
                d.Contrast = s.Contrast;
                d.Gamma = s.Gamma;
                d.UseHardwareBrightness = s.UseHardware;

                if (s.HasBacklight)
                {
                    int value = (int)s.Backlight;
                    await Task.Run(() => _engine.SetBacklight(d, value));
                }

                if (d.UsesSoftwareBrightness || _engine.HasCustomGammaOrColor(d)) _engine.ApplyColorAndGamma(d);
                else _engine.ResetGammaRamp(d);
            }

            await Task.Delay(SettleMs);
            Line("");
            Line("Restore");
            foreach (var s in snapshots)
            {
                CheckRamp(s.Display, s.Display.DeviceName + " settings");
                if (s.HasBacklight)
                {
                    await CheckBacklight(s.Display, s.Backlight, s.Display.DeviceName + " original");
                }
            }
            CheckFilter("Color filter");
        }

        private void WriteHeader(List<DisplayInfo> displays)
        {
            Line("Displicare hardware test");
            Line(string.Format("App: v{0} ({1}, {2})", App.Version,
                ConfigManager.IsPortableMode() ? "portable" : "installed", Environment.Is64BitProcess ? "x64" : "x86"));
            Line("Windows: " + WindowsVersion());
            Line("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line("Displays: " + displays.Count);
            foreach (var d in displays)
            {
                string ddc;
                if (d.SupportsDdcCi)
                {
                    ddc = string.Format("DDC/CI backlight {0} (range {1}-{2})", d.CurrentBrightness, d.MinBrightness, d.MaxBrightness);
                }
                else
                {
                    ddc = "no DDC/CI";
                }
                Line(string.Format("  {0}: {1}, {2}x{3}{4}, GPU {5}, HDR {6}, {7}, {8} brightness",
                    d.DeviceName, d.FriendlyName, d.ResolutionWidth, d.ResolutionHeight, d.IsPrimary ? ", primary" : "",
                    d.DeviceString, d.IsHdr ? "on" : "off", ddc, d.UsesSoftwareBrightness ? "software" : "hardware"));
            }
        }

        internal static string WindowsVersion()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        string product = Convert.ToString(key.GetValue("ProductName"));
                        string display = Convert.ToString(key.GetValue("DisplayVersion"));
                        string build = Convert.ToString(key.GetValue("CurrentBuild"));
                        string ubr = Convert.ToString(key.GetValue("UBR"));
                        int buildNumber;
                        // Windows 11 still reports "Windows 10" as its product name
                        if (int.TryParse(build, out buildNumber) && buildNumber >= 22000 && product.StartsWith("Windows 10"))
                        {
                            product = "Windows 11" + product.Substring("Windows 10".Length);
                        }
                        return string.Format("{0} {1} (build {2}.{3})", product, display, build, ubr).Replace("  ", " ");
                    }
                }
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        private void AppendRecentLog()
        {
            try
            {
                string path = Path.Combine(ConfigManager.GetSettingsFolder(), "app.log");
                if (!File.Exists(path)) return;
                var lines = File.ReadAllLines(path);
                int start = Math.Max(0, lines.Length - 40);
                Line("");
                Line("Recent app.log");
                for (int i = start; i < lines.Length; i++) Line("  " + lines[i]);
            }
            catch { }
        }

        private void SaveReport()
        {
            try
            {
                string path = Path.Combine(ConfigManager.GetSettingsFolder(),
                    ReportFilePrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(path, _report.ToString(), Encoding.UTF8);
                ReportPath = path;
            }
            catch (Exception ex)
            {
                App.Log("Hardware test report could not be saved: " + ex.Message);
            }
            App.Log("Hardware test finished: " + SummaryLine(_passed, _failed, Cancelled));
        }

        private void Record(bool pass, string text)
        {
            if (pass) _passed++; else _failed++;
            Line("  [" + (pass ? "PASS" : "FAIL") + "] " + text);
        }

        private void Line(string text)
        {
            _report.AppendLine(text);
            var handler = LineWritten;
            if (handler != null) handler(text);
        }

        private void Step(string text)
        {
            var handler = StepChanged;
            if (handler != null) handler(text);
        }

        // ---- Backlight recovery for a run that never got to restore (app killed, logoff mid-test) ----

        private static string RecoveryPath()
        {
            return Path.Combine(ConfigManager.GetSettingsFolder(), RecoveryFileName);
        }

        private static string LegacyRecoveryPath()
        {
            return Path.Combine(ConfigManager.GetSettingsFolder(), LegacyRecoveryFileName);
        }

        private static void WriteRecoveryFile(List<Snapshot> snapshots)
        {
            var sb = new StringBuilder();
            foreach (var s in snapshots)
            {
                if (s.HasBacklight) sb.AppendLine(s.Display.DeviceName + "\t" + s.Backlight);
            }
            try
            {
                if (sb.Length > 0) File.WriteAllText(RecoveryPath(), sb.ToString());
            }
            catch { }
        }

        private static void DeleteRecoveryFile()
        {
            try
            {
                if (File.Exists(RecoveryPath())) File.Delete(RecoveryPath());
            }
            catch { }
        }

        /// <summary>Parses recovery file lines ("\\.\DISPLAY1&lt;tab&gt;55"); bad lines are skipped.</summary>
        internal static Dictionary<string, int> ParseRecovery(string[] lines)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (lines == null) return result;
            foreach (var line in lines)
            {
                var parts = (line ?? "").Split('\t');
                int value;
                if (parts.Length == 2 && parts[0].Length > 0 && int.TryParse(parts[1], out value) && value >= 0 && value <= 100)
                {
                    result[parts[0]] = value;
                }
            }
            return result;
        }

        /// <summary>
        /// Combines recovery files (contents oldest first, null for a missing file): a later file's value for a
        /// display replaces an earlier one's, so the current name wins over the 2.2.0 one.
        /// </summary>
        internal static Dictionary<string, int> MergeRecovery(params string[][] filesOldestFirst)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var lines in filesOldestFirst)
            {
                foreach (var kv in ParseRecovery(lines))
                {
                    result[kv.Key] = kv.Value;
                }
            }
            return result;
        }

        /// <summary>
        /// Called at startup: puts back the backlight values an interrupted test recorded. Returns how many were restored.
        /// </summary>
        public static int RecoverInterruptedRun(MonitorEngine engine)
        {
            int restored = 0;
            try
            {
                string path = RecoveryPath();
                string legacyPath = LegacyRecoveryPath();
                bool hasCurrent = File.Exists(path);
                bool hasLegacy = File.Exists(legacyPath);
                if (!hasCurrent && !hasLegacy) return 0;

                var saved = MergeRecovery(
                    hasLegacy ? File.ReadAllLines(legacyPath) : null,
                    hasCurrent ? File.ReadAllLines(path) : null);
                foreach (var d in engine.Displays)
                {
                    int value;
                    if (d.SupportsDdcCi && saved.TryGetValue(d.DeviceName, out value) && engine.SetBacklight(d, value))
                    {
                        restored++;
                    }
                }
                if (hasCurrent) File.Delete(path);
                if (hasLegacy) File.Delete(legacyPath);
                App.Log("Restored the backlight on " + restored + " display(s) after an interrupted hardware test");
            }
            catch { }
            return restored;
        }
    }
}
