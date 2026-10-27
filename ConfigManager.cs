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
            RoomsEnabled = false;
            Rooms = new List<Room>();
            RoomHomeKey = "";
            RoomHomeHotkey = "Ctrl+Alt+0";
            RoomAskToKeep = RoomAskNew;
            RoomController = true;
            RoomStreamScreensOff = true;
            RoomShowOverlay = true;
        }

        public const string RoomAskNew = "new", RoomAskAlways = "always", RoomAskNever = "never";

        /// <summary>
        /// Set once displays switched to software brightness by the 2.3.0-and-earlier DDC/CI startup bug were put back
        /// to hardware brightness (MainWindow.RestoreHardwarePreference).
        /// </summary>
        [DataMember]
        public bool HardwareBrightnessRestored { get; set; }

        /// <summary>Room switching (one display at a time for PCs wired to TVs in other rooms). Off by default.</summary>
        [DataMember]
        public bool RoomsEnabled { get; set; }

        [DataMember]
        public List<Room> Rooms { get; set; }

        /// <summary>Room the home hotkey switches to without asking; empty = the first room.</summary>
        [DataMember]
        public string RoomHomeKey { get; set; }

        [DataMember]
        public string RoomHomeHotkey { get; set; }

        /// <summary>When to ask "Keep this display layout?": new (until a room's switch was kept once), always or never.</summary>
        [DataMember]
        public string RoomAskToKeep { get; set; }

        /// <summary>Holding View + Menu on an Xbox-style controller opens the room picker.</summary>
        [DataMember]
        public bool RoomController { get; set; }

        /// <summary>While a stream adds a virtual display (Steam Link, Apollo), the physical screens are switched off.</summary>
        [DataMember]
        public bool RoomStreamScreensOff { get; set; }

        /// <summary>A short note on the new screen after each switch: room, mode, HDR and sound.</summary>
        [DataMember]
        public bool RoomShowOverlay { get; set; }

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

            SanitizeRooms(settings);
            return settings;
        }

        internal const int MaxRooms = 32;
        internal const int MaxRoomNameLength = 40;

        private static void SanitizeRooms(AppSettings settings)
        {
            if (settings.RoomAskToKeep != AppSettings.RoomAskNew && settings.RoomAskToKeep != AppSettings.RoomAskAlways
                && settings.RoomAskToKeep != AppSettings.RoomAskNever)
                settings.RoomAskToKeep = AppSettings.RoomAskNew;
            if (settings.RoomHomeKey == null) settings.RoomHomeKey = "";
            if (settings.RoomHomeHotkey == null) settings.RoomHomeHotkey = "Ctrl+Alt+0";
            else if (settings.RoomHomeHotkey.Length > 0) settings.RoomHomeHotkey = Rooms.Hotkey.Normalize(settings.RoomHomeHotkey) ?? "";

            if (settings.Rooms == null) settings.Rooms = new List<Room>();
            settings.Rooms.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.Key));
            if (settings.Rooms.Count > MaxRooms) settings.Rooms.RemoveRange(MaxRooms, settings.Rooms.Count - MaxRooms);
            var padTaken = new HashSet<string>();
            foreach (var r in settings.Rooms)
            {
                // Unknown directions are given a new one; a direction two rooms share stays with the first.
                if (r.PadDirection != null && r.PadDirection != "" && Array.IndexOf(Room.PadDirections, r.PadDirection) < 0) r.PadDirection = null;
                if (!string.IsNullOrEmpty(r.PadDirection) && !padTaken.Add(r.PadDirection)) r.PadDirection = "";
                if (string.IsNullOrWhiteSpace(r.Name)) r.Name = string.IsNullOrWhiteSpace(r.DisplayName) ? r.Key : r.DisplayName;
                if (r.Name.Length > MaxRoomNameLength) r.Name = r.Name.Substring(0, MaxRoomNameLength);
                if (r.Width < 0 || r.Width > 16384 || r.Height < 0 || r.Height > 16384 || (r.Width == 0) != (r.Height == 0))
                {
                    r.Width = 0;
                    r.Height = 0;
                }
                r.RefreshHz = SanitizeInt(r.RefreshHz, 0, 1000, 0);
                if (r.Hdr != Room.HdrAuto && r.Hdr != Room.HdrOn && r.Hdr != Room.HdrOff) r.Hdr = Room.HdrAuto;
                if (r.Audio != Room.AudioAuto && r.Audio != Room.AudioKeep && r.Audio != Room.AudioDevice) r.Audio = Room.AudioAuto;
                if (r.Audio == Room.AudioDevice && string.IsNullOrEmpty(r.AudioId) && string.IsNullOrEmpty(r.AudioName)) r.Audio = Room.AudioAuto;
                r.Hotkey = string.IsNullOrEmpty(r.Hotkey) ? "" : (Rooms.Hotkey.Normalize(r.Hotkey) ?? "");
                if (r.KnownModes == null) r.KnownModes = new List<string>();
                r.KnownModes.RemoveAll(m => m == null);
                if (r.KnownModes.Count > 64) r.KnownModes.RemoveRange(64, r.KnownModes.Count - 64);
            }
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
