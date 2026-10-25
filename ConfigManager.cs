using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32;

namespace Displicare
{
    [DataContract]
    public class CustomPreset
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public int Brightness { get; set; }

        [DataMember]
        public int ColorTemperature { get; set; }

        [DataMember]
        public double RedGain { get; set; }

        [DataMember]
        public double GreenGain { get; set; }

        [DataMember]
        public double BlueGain { get; set; }

        [DataMember]
        public double Contrast { get; set; }

        [DataMember]
        public double Gamma { get; set; }

        public CustomPreset()
        {
            SetDefaults();
        }

        // DataContract deserialization skips constructors, so fields missing from the JSON (older settings
        // files, hand edits) would otherwise be 0/false: RGB gains of 0.1 after clamping, hardware mode off.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "My Preset";
            Brightness = 80;
            ColorTemperature = 6500;
            RedGain = 1.0;
            GreenGain = 1.0;
            BlueGain = 1.0;
            Contrast = 1.0;
            Gamma = 1.0;
        }
    }

    [DataContract]
    public class DisplayProfile
    {
        [DataMember]
        public string DeviceName { get; set; }

        [DataMember]
        public string FriendlyName { get; set; }

        [DataMember]
        public int Brightness { get; set; }

        [DataMember]
        public int ColorTemperature { get; set; }

        [DataMember]
        public double RedGain { get; set; }

        [DataMember]
        public double GreenGain { get; set; }

        [DataMember]
        public double BlueGain { get; set; }

        [DataMember]
        public double Contrast { get; set; }

        [DataMember]
        public double Gamma { get; set; }

        [DataMember]
        public bool UseHardwareBrightness { get; set; }

        public DisplayProfile()
        {
            SetDefaults();
        }

        // DataContract deserialization skips constructors, so fields missing from the JSON (older settings
        // files, hand edits) would otherwise be 0/false: RGB gains of 0.1 after clamping, hardware mode off.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Brightness = 80;
            ColorTemperature = 6500;
            RedGain = 1.0;
            GreenGain = 1.0;
            BlueGain = 1.0;
            Contrast = 1.0;
            Gamma = 1.0;
            UseHardwareBrightness = true;
        }
    }

    [DataContract]
    public class AppSettings
    {
        public AppSettings()
        {
            SetDefaults();
        }

        // DataContract deserialization skips constructors, so fields missing from the JSON (older settings
        // files, hand edits) would otherwise be 0/false: RGB gains of 0.1 after clamping, hardware mode off.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Brightness = 80;
            ColorTemperature = 6500;
            SyncAllMonitors = true;
            ActivePreset = "Balanced";
            StartWithWindows = false;
            StartMinimized = true;
            PinWindow = false;
            RestoreOnExit = false;
            RedGain = 1.0;
            GreenGain = 1.0;
            BlueGain = 1.0;
            Contrast = 1.0;
            Gamma = 1.0;
            SolarSyncEnabled = false;
            SolarColorSyncEnabled = false; // Default: false (only dim brightness, color range is a choice)
            SolarDayBrightness = 85;
            SolarNightBrightness = 35;
            SolarDayKelvin = 6500;
            SolarNightKelvin = 3400;
            UserLatitude = 41.01;
            UserLongitude = 28.97;
            AutoLocation = true;
            DisplayProfiles = new List<DisplayProfile>();
            CustomPresets = new List<CustomPreset>();
        }

        [DataMember]
        public bool SolarSyncEnabled { get; set; }

        [DataMember]
        public bool SolarColorSyncEnabled { get; set; }

        [DataMember]
        public int SolarDayBrightness { get; set; }

        [DataMember]
        public int SolarNightBrightness { get; set; }

        [DataMember]
        public int SolarDayKelvin { get; set; }

        [DataMember]
        public int SolarNightKelvin { get; set; }

        [DataMember]
        public double UserLatitude { get; set; }

        [DataMember]
        public double UserLongitude { get; set; }

        [DataMember]
        public bool AutoLocation { get; set; }

        [DataMember]
        public int Brightness { get; set; }

        [DataMember]
        public int ColorTemperature { get; set; }

        [DataMember]
        public bool SyncAllMonitors { get; set; }

        [DataMember]
        public string ActivePreset { get; set; }

        [DataMember]
        public bool StartWithWindows { get; set; }

        [DataMember]
        public bool StartMinimized { get; set; }

        [DataMember]
        public bool PinWindow { get; set; }

        [DataMember]
        public bool RestoreOnExit { get; set; }

        [DataMember]
        public double RedGain { get; set; }

        [DataMember]
        public double GreenGain { get; set; }

        [DataMember]
        public double BlueGain { get; set; }

        [DataMember]
        public double Contrast { get; set; }

        [DataMember]
        public double Gamma { get; set; }

        [DataMember]
        public List<DisplayProfile> DisplayProfiles { get; set; }

        [DataMember]
        public List<CustomPreset> CustomPresets { get; set; }
    }

    public static class ConfigManager
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "Displicare";
        private const string LegacyAppRegistryName = "LuminaDisplay";

        internal const int MaxCustomPresets = 100;
        internal const int MaxPresetNameLength = 40;

        private static double Sanitize(double val, double min, double max, double def)
        {
            if (double.IsNaN(val) || double.IsInfinity(val)) return def;
            return Math.Max(min, Math.Min(max, val));
        }

        private static int SanitizeInt(int val, int min, int max, int def)
        {
            if (val < min || val > max) return def;
            return val;
        }

        public static bool IsPortableMode()
        {
            if (App.IsPortableMode) return true;
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                if (File.Exists(Path.Combine(exeDir, "portable.dat"))) return true;
                if (File.Exists(Path.Combine(exeDir, "settings.json"))) return true;
            }
            catch { }
            return false;
        }

        public static string GetSettingsFolder()
        {
            return Path.GetDirectoryName(GetConfigPath());
        }

        private static string GetConfigPath()
        {
            if (IsPortableMode())
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "Displicare");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string targetPath = Path.Combine(dir, "settings.json");

            // Migration from legacy LuminaDisplay configuration if present
            if (!File.Exists(targetPath))
            {
                try
                {
                    string legacyPath = Path.Combine(appData, "LuminaDisplay", "settings.json");
                    if (File.Exists(legacyPath))
                    {
                        File.Copy(legacyPath, targetPath, false);
                    }
                }
                catch { }
            }

            return targetPath;
        }

        /// <summary>
        /// Deserializes settings JSON and clamps every value into range. Returns null if the JSON is unusable.
        /// Kept free of file and registry access so it can be unit tested.
        /// </summary>
        internal static AppSettings Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            AppSettings settings;
            try
            {
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AppSettings));
                    settings = ser.ReadObject(ms) as AppSettings;
                }
            }
            catch
            {
                return null;
            }
            if (settings == null) return null;

            settings.Brightness = SanitizeInt(settings.Brightness, 0, 100, 80);
            settings.ColorTemperature = SanitizeInt(settings.ColorTemperature, 2000, 10000, 6500);
            settings.RedGain = Sanitize(settings.RedGain, 0.1, 2.0, 1.0);
            settings.GreenGain = Sanitize(settings.GreenGain, 0.1, 2.0, 1.0);
            settings.BlueGain = Sanitize(settings.BlueGain, 0.1, 2.0, 1.0);
            settings.Contrast = Sanitize(settings.Contrast, 0.5, 1.8, 1.0);
            settings.Gamma = Sanitize(settings.Gamma, 0.5, 2.5, 1.0);

            // Ranges match the Solar Sync sliders so a saved value is never silently clamped by the UI
            settings.SolarDayBrightness = SanitizeInt(settings.SolarDayBrightness, 50, 100, 85);
            settings.SolarNightBrightness = SanitizeInt(settings.SolarNightBrightness, 10, 80, 35);
            settings.SolarDayKelvin = SanitizeInt(settings.SolarDayKelvin, 5000, 7500, 6500);
            settings.SolarNightKelvin = SanitizeInt(settings.SolarNightKelvin, 2500, 4500, 3400);

            settings.UserLatitude = Sanitize(settings.UserLatitude, -90.0, 90.0, 41.01);
            settings.UserLongitude = Sanitize(settings.UserLongitude, -180.0, 180.0, 28.97);

            if (Math.Abs(settings.UserLatitude) < 0.001 && Math.Abs(settings.UserLongitude) < 0.001)
            {
                settings.AutoLocation = true;
                double lat, lon;
                SolarEngine.GetDefaultCoordinates(out lat, out lon);
                settings.UserLatitude = lat;
                settings.UserLongitude = lon;
            }

            if (settings.DisplayProfiles == null)
            {
                settings.DisplayProfiles = new List<DisplayProfile>();
            }
            settings.DisplayProfiles.RemoveAll(p => p == null);
            foreach (var p in settings.DisplayProfiles)
            {
                p.Brightness = SanitizeInt(p.Brightness, 0, 100, 80);
                p.ColorTemperature = SanitizeInt(p.ColorTemperature, 2000, 10000, 6500);
                p.RedGain = Sanitize(p.RedGain, 0.1, 2.0, 1.0);
                p.GreenGain = Sanitize(p.GreenGain, 0.1, 2.0, 1.0);
                p.BlueGain = Sanitize(p.BlueGain, 0.1, 2.0, 1.0);
                p.Contrast = Sanitize(p.Contrast, 0.5, 1.8, 1.0);
                p.Gamma = Sanitize(p.Gamma, 0.5, 2.5, 1.0);
            }

            if (settings.CustomPresets == null)
            {
                settings.CustomPresets = new List<CustomPreset>();
            }
            settings.CustomPresets.RemoveAll(cp => cp == null);
            if (settings.CustomPresets.Count > MaxCustomPresets)
            {
                // A tampered settings file shouldn't be able to flood the UI and tray menu
                settings.CustomPresets.RemoveRange(MaxCustomPresets, settings.CustomPresets.Count - MaxCustomPresets);
            }
            // Sanitize whatever is kept (previously skipped when the list had to be trimmed)
            foreach (var cp in settings.CustomPresets)
            {
                if (string.IsNullOrWhiteSpace(cp.Id)) cp.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(cp.Name)) cp.Name = "Custom Preset";
                if (cp.Name.Length > MaxPresetNameLength) cp.Name = cp.Name.Substring(0, MaxPresetNameLength);
                cp.Brightness = SanitizeInt(cp.Brightness, 0, 100, 80);
                cp.ColorTemperature = SanitizeInt(cp.ColorTemperature, 2000, 10000, 6500);
                cp.RedGain = Sanitize(cp.RedGain, 0.1, 2.0, 1.0);
                cp.GreenGain = Sanitize(cp.GreenGain, 0.1, 2.0, 1.0);
                cp.BlueGain = Sanitize(cp.BlueGain, 0.1, 2.0, 1.0);
                cp.Contrast = Sanitize(cp.Contrast, 0.5, 1.8, 1.0);
                cp.Gamma = Sanitize(cp.Gamma, 0.5, 2.5, 1.0);
            }

            return settings;
        }

        public static AppSettings Load()
        {
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    AppSettings settings = Parse(File.ReadAllText(path, Encoding.UTF8));
                    if (settings != null)
                    {
                        settings.StartWithWindows = IsRunOnStartupEnabled();
                        return settings;
                    }
                }
            }
            catch { }

            AppSettings def = new AppSettings();
            def.StartWithWindows = IsRunOnStartupEnabled();
            return def;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                string path = GetConfigPath();
                string tmpPath = path + ".tmp";

                using (MemoryStream ms = new MemoryStream())
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AppSettings));
                    ser.WriteObject(ms, settings);
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    File.WriteAllText(tmpPath, json, Encoding.UTF8);
                }

                // Atomic file replace/move to guard against power-off or crash corruption
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmpPath, path, null);
                    }
                    catch
                    {
                        File.Copy(tmpPath, path, true);
                        File.Delete(tmpPath);
                    }
                }
                else
                {
                    File.Move(tmpPath, path);
                }

                bool currentRun = IsRunOnStartupEnabled();
                if (currentRun != settings.StartWithWindows)
                {
                    SetRunOnStartup(settings.StartWithWindows, settings.StartMinimized);
                }
            }
            catch { }
        }

        public static bool IsRunOnStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(AppRegistryName);
                        if (val != null) return true;
                        // Check legacy name
                        object legacyVal = key.GetValue(LegacyAppRegistryName);
                        return legacyVal != null;
                    }
                }
            }
            catch { }
            return false;
        }

        public static void SetRunOnStartup(bool enable, bool startMinimized)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        // Clean up legacy key regardless
                        try { key.DeleteValue(LegacyAppRegistryName, false); } catch { }

                        if (enable)
                        {
                            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                            string extraArg = IsPortableMode() ? " --portable" : "";
                            string minArg = startMinimized ? " --minimized" : "";
                            key.SetValue(AppRegistryName, "\"" + exePath + "\"" + minArg + extraArg);
                        }
                        else
                        {
                            key.DeleteValue(AppRegistryName, false);
                        }
                    }
                }
            }
            catch { }
        }
    }
}
