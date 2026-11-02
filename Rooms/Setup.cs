using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Displicare
{
    /// <summary>
    /// A monitor Displicare has seen, keyed by model (GSM82CD) so it stays the same on any port or graphics card. Holds
    /// what belongs to the screen itself; which screens turn on together is a <see cref="Setup"/>.
    /// </summary>
    [DataContract]
    public class KnownScreen
    {
        public KnownScreen()
        {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Hdr = Room.HdrAuto;
            KnownModes = new List<string>();
        }

        [DataMember] public string Key { get; set; }
        /// <summary>The screen's own name from its EDID, e.g. "LG TV SSCR2".</summary>
        [DataMember] public string DisplayName { get; set; }
        [DataMember] public string DevicePath { get; set; }
        [DataMember] public string AdapterPath { get; set; }
        [DataMember] public int EdidManufacturer { get; set; }
        [DataMember] public int EdidProduct { get; set; }
        [DataMember] public int ConnectorInstance { get; set; }
        /// <summary>0 = native.</summary>
        [DataMember] public int Width { get; set; }
        [DataMember] public int Height { get; set; }
        /// <summary>0 = the highest at that resolution.</summary>
        [DataMember] public int RefreshHz { get; set; }
        [DataMember] public string Hdr { get; set; }
        /// <summary>Modes Windows offered while the screen was on ("3840x2160@144"), for the drop-downs.</summary>
        [DataMember] public List<string> KnownModes { get; set; }
    }

    /// <summary>
    /// One screen in a setup: where it goes and how it runs there. The same screen can run differently in another setup.
    /// </summary>
    [DataContract]
    public class SetupScreen
    {
        public SetupScreen()
        {
            Hdr = Room.HdrAuto;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Hdr = Room.HdrAuto;
        }

        [DataMember] public string Key { get; set; }
        /// <summary>0 = native.</summary>
        [DataMember] public int Width { get; set; }
        [DataMember] public int Height { get; set; }
        /// <summary>0 = the highest at that resolution.</summary>
        [DataMember] public int RefreshHz { get; set; }
        [DataMember] public string Hdr { get; set; }
        /// <summary>Top left corner on the desktop, in pixels; the main screen is at 0,0.</summary>
        [DataMember] public int X { get; set; }
        [DataMember] public int Y { get; set; }
        /// <summary>The size the screen had when it was put there; 0 = not placed yet (it goes to the right).</summary>
        [DataMember] public int PlacedWidth { get; set; }
        [DataMember] public int PlacedHeight { get; set; }

        public bool Placed { get { return PlacedWidth > 0 && PlacedHeight > 0; } }

        public SetupScreen Clone()
        {
            return (SetupScreen)MemberwiseClone();
        }
    }

    /// <summary>
    /// A profile you switch to: which screens turn on (the first is the main one), how they're arranged, and what
    /// goes with it (sound, hotkey, controller direction, launcher). Replaces rooms, which were always one screen.
    /// </summary>
    [DataContract]
    public class Setup
    {
        public Setup()
        {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Screens = new List<string>();
            Placement = new List<SetupScreen>();
            Audio = Room.AudioAuto;
            Hotkey = "";
            Launcher = Room.LaunchNone;
        }

        /// <summary>Stays the same when the setup is renamed; "room:GSM82CD" for setups made from a room.</summary>
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        /// <summary>Screen keys; the first is the main screen.</summary>
        [DataMember] public List<string> Screens { get; set; }
        /// <summary>Where each screen goes and its resolution, refresh rate and HDR: one per screen, in the order of <see cref="Screens"/>.</summary>
        [DataMember] public List<SetupScreen> Placement { get; set; }
        /// <summary>The exact arrangement earlier 2.5.0 test builds saved; turned into <see cref="Placement"/> on load, then dropped.</summary>
        [DataMember] internal global::Displicare.Rooms.Profile Layout { get; set; }
        [DataMember] public string Audio { get; set; }
        [DataMember] public string AudioId { get; set; }
        [DataMember] public string AudioName { get; set; }
        [DataMember] public string Hotkey { get; set; }
        /// <summary>"up", "right", "down", "left", "" for none, null until Displicare has given it one.</summary>
        [DataMember] public string PadDirection { get; set; }
        [DataMember] public string Launcher { get; set; }
        [DataMember] public bool Hidden { get; set; }
        /// <summary>A switch here was kept at least once with these screens and modes, so it no longer asks.</summary>
        [DataMember] public bool Confirmed { get; set; }

        public string MainScreen { get { return Screens != null && Screens.Count > 0 ? Screens[0] : null; } }

        /// <summary>The placement of one of the setup's screens, or null.</summary>
        public SetupScreen Entry(string key)
        {
            if (Placement == null) return null;
            foreach (var e in Placement) if (e != null && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }
    }
}

namespace Displicare.Rooms
{
    /// <summary>
    /// Rooms to setups and back. On upgrade every room becomes a known screen plus a one-screen setup with the same
    /// settings (a room with "Also turn on" screens keeps them); the old room list keeps being written from them, so
    /// going back to 2.4.x still works.
    /// </summary>
    internal static class SetupMigration
    {
        public const string RoomIdPrefix = "room:";
        public const int MaxSetups = 64;

        /// <summary>The setup made from a room has a fixed id, so converting again finds the same one.</summary>
        public static string IdForRoom(string roomKey)
        {
            return RoomIdPrefix + (roomKey ?? "").ToUpperInvariant();
        }

        public static string NewId()
        {
            return "setup:" + Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Screens and setups from (sanitized) rooms. The home room's setup is the safe one, or the first room's when no
        /// home is set.
        /// </summary>
        public static void FromRooms(IList<Room> rooms, string homeKey, out List<KnownScreen> screens, out List<Setup> setups, out string safeId)
        {
            screens = new List<KnownScreen>();
            setups = new List<Setup>();
            safeId = null;
            if (rooms == null) return;
            foreach (var r in rooms)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.Key)) continue;
                screens.Add(ScreenOf(r));

                var s = new Setup();
                s.Id = IdForRoom(r.Key);
                s.Name = r.Name;
                s.Screens.Add(r.Key);
                if (r.AlsoOn != null)
                    foreach (var k in r.AlsoOn)
                        if (!string.IsNullOrWhiteSpace(k) && !s.Screens.Exists(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase)))
                            s.Screens.Add(k);
                if (s.Screens.Count > 1) s.Layout = r.Layout;
                s.Audio = r.Audio;
                s.AudioId = r.AudioId;
                s.AudioName = r.AudioName;
                s.Hotkey = r.Hotkey ?? "";
                s.PadDirection = r.PadDirection;
                s.Launcher = string.IsNullOrEmpty(r.Launcher) ? (r.OpenBigPicture ? Room.LaunchBigPicture : Room.LaunchNone) : r.Launcher;
                s.Hidden = r.Hidden;
                s.Confirmed = r.Confirmed;
                setups.Add(s);

                if (safeId == null && string.Equals(r.Key, homeKey, StringComparison.OrdinalIgnoreCase)) safeId = s.Id;
            }
            if (safeId == null && setups.Count > 0) safeId = setups[0].Id;
        }

        static KnownScreen ScreenOf(Room r)
        {
            var screen = new KnownScreen();
            screen.Key = r.Key;
            screen.DisplayName = r.DisplayName;
            screen.DevicePath = r.DevicePath;
            screen.AdapterPath = r.AdapterPath;
            screen.EdidManufacturer = r.EdidManufacturer;
            screen.EdidProduct = r.EdidProduct;
            screen.ConnectorInstance = r.ConnectorInstance;
            screen.Width = r.Width;
            screen.Height = r.Height;
            screen.RefreshHz = r.RefreshHz;
            screen.Hdr = r.Hdr;
            screen.KnownModes = r.KnownModes != null ? new List<string>(r.KnownModes) : new List<string>();
            return screen;
        }

        /// <summary>
        /// The room list 2.4.x reads: one room per screen, with the settings of the setup that is that screen alone (or,
        /// failing that, the first setup with it as the main screen). <paramref name="homeKey"/> is the safe setup's
        /// main screen.
        /// </summary>
        public static List<Room> ToRooms(IList<KnownScreen> screens, IList<Setup> setups, string safeId, out string homeKey)
        {
            var rooms = new List<Room>();
            homeKey = "";
            if (screens == null) return rooms;
            foreach (var screen in screens)
            {
                if (screen == null || string.IsNullOrWhiteSpace(screen.Key)) continue;
                var r = new Room();
                r.Key = screen.Key;
                r.DisplayName = screen.DisplayName;
                r.DevicePath = screen.DevicePath;
                r.AdapterPath = screen.AdapterPath;
                r.EdidManufacturer = screen.EdidManufacturer;
                r.EdidProduct = screen.EdidProduct;
                r.ConnectorInstance = screen.ConnectorInstance;
                r.Width = screen.Width;
                r.Height = screen.Height;
                r.RefreshHz = screen.RefreshHz;
                r.Hdr = screen.Hdr;
                r.KnownModes = screen.KnownModes != null ? new List<string>(screen.KnownModes) : new List<string>();

                var s = SetupFor(setups, screen.Key);
                var entry = s != null ? s.Entry(screen.Key) : null;
                if (entry != null)
                {
                    r.Width = entry.Width;
                    r.Height = entry.Height;
                    r.RefreshHz = entry.RefreshHz;
                    r.Hdr = entry.Hdr;
                }
                if (s != null)
                {
                    r.Name = s.Name;
                    r.Audio = s.Audio;
                    r.AudioId = s.AudioId;
                    r.AudioName = s.AudioName;
                    r.Hotkey = s.Hotkey ?? "";
                    r.PadDirection = s.PadDirection;
                    r.Launcher = s.Launcher;
                    r.OpenBigPicture = s.Launcher == Room.LaunchBigPicture;
                    r.Hidden = s.Hidden;
                    r.Confirmed = s.Confirmed;
                }
                else
                {
                    r.Name = string.IsNullOrWhiteSpace(screen.DisplayName) ? screen.Key : screen.DisplayName;
                    r.Launcher = Room.LaunchNone;
                }
                rooms.Add(r);
            }
            if (setups != null)
                foreach (var s in setups)
                    if (s != null && s.Id == safeId && s.MainScreen != null) homeKey = s.MainScreen;
            return rooms;
        }

        /// <summary>The setup that stands for one screen: that screen alone, else the first with it as the main one.</summary>
        public static Setup SetupFor(IList<Setup> setups, string screenKey)
        {
            if (setups == null) return null;
            Setup main = null;
            foreach (var s in setups)
            {
                if (s == null || !string.Equals(s.MainScreen, screenKey, StringComparison.OrdinalIgnoreCase)) continue;
                if (s.Screens.Count == 1) return s;
                if (main == null) main = s;
            }
            return main;
        }

        /// <summary>A new placement for a screen, running as it last did on its own; not placed yet.</summary>
        public static SetupScreen EntryFor(KnownScreen screen)
        {
            var e = new SetupScreen();
            if (screen == null) return e;
            e.Key = screen.Key;
            e.Width = screen.Width;
            e.Height = screen.Height;
            e.RefreshHz = screen.RefreshHz;
            e.Hdr = screen.Hdr;
            return e;
        }

        /// <summary>
        /// One valid placement per screen, in the order of <see cref="Setup.Screens"/>. A missing one runs as the screen
        /// last did on its own; an arrangement saved by an earlier test build (<see cref="Setup.Layout"/>) gives its place.
        /// </summary>
        static void SanitizePlacement(Setup s, List<KnownScreen> screens)
        {
            var old = s.Placement ?? new List<SetupScreen>();
            s.Placement = new List<SetupScreen>();
            foreach (var key in s.Screens)
            {
                string k = key;
                var e = old.Find(x => x != null && string.Equals(x.Key, k, StringComparison.OrdinalIgnoreCase));
                if (e == null)
                {
                    e = EntryFor(screens.Find(sc => string.Equals(sc.Key, k, StringComparison.OrdinalIgnoreCase)));
                    FromLayout(s.Layout, k, e);
                }
                e.Key = key;
                if (e.Width < 0 || e.Width > 16384 || e.Height < 0 || e.Height > 16384 || (e.Width == 0) != (e.Height == 0))
                {
                    e.Width = 0;
                    e.Height = 0;
                }
                if (e.RefreshHz < 0 || e.RefreshHz > 1000) e.RefreshHz = 0;
                if (e.Hdr != Room.HdrAuto && e.Hdr != Room.HdrOn && e.Hdr != Room.HdrOff) e.Hdr = Room.HdrAuto;
                if (e.X < -65536 || e.X > 65536 || e.Y < -65536 || e.Y > 65536 || e.PlacedWidth <= 0 || e.PlacedWidth > 16384
                    || e.PlacedHeight <= 0 || e.PlacedHeight > 16384)
                {
                    e.X = 0;
                    e.Y = 0;
                    e.PlacedWidth = 0;
                    e.PlacedHeight = 0;
                }
                s.Placement.Add(e);
            }
            s.Layout = null;
        }

        /// <summary>Where a screen was in a saved arrangement, and HDR off if it was off there.</summary>
        static void FromLayout(Profile layout, string key, SetupScreen e)
        {
            if (layout == null || layout.Displays == null) return;
            foreach (var d in layout.Displays)
            {
                if (d == null || d.Identity == null || !string.Equals(RoomManager.ModelKey(d.Identity.DevicePath), key, StringComparison.OrdinalIgnoreCase)) continue;
                if (d.Width <= 0 || d.Height <= 0) return;
                e.X = d.X;
                e.Y = d.Y;
                e.PlacedWidth = d.Width;
                e.PlacedHeight = d.Height;
                if (d.Hdr == 0) e.Hdr = Room.HdrOff;
                return;
            }
        }

        /// <summary>
        /// Makes screens and setups read from settings.json safe to use: unique keys and ids, screens a setup names that
        /// Displicare knows, valid choices, sizes in range. A setup left without screens is dropped; every screen of a
        /// setup gets a valid placement. Returns the safe
        /// setup's id: the one given if it still exists, else the first setup's (null without setups).
        /// </summary>
        public static string Sanitize(List<KnownScreen> screens, List<Setup> setups, string safeId)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            screens.RemoveAll(sc => sc == null || string.IsNullOrWhiteSpace(sc.Key) || !keys.Add(sc.Key));
            if (screens.Count > ConfigManager.MaxRooms) screens.RemoveRange(ConfigManager.MaxRooms, screens.Count - ConfigManager.MaxRooms);
            keys.Clear();
            foreach (var sc in screens)
            {
                keys.Add(sc.Key);
                if (sc.Width < 0 || sc.Width > 16384 || sc.Height < 0 || sc.Height > 16384 || (sc.Width == 0) != (sc.Height == 0))
                {
                    sc.Width = 0;
                    sc.Height = 0;
                }
                if (sc.RefreshHz < 0 || sc.RefreshHz > 1000) sc.RefreshHz = 0;
                if (sc.Hdr != Room.HdrAuto && sc.Hdr != Room.HdrOn && sc.Hdr != Room.HdrOff) sc.Hdr = Room.HdrAuto;
                if (sc.KnownModes == null) sc.KnownModes = new List<string>();
                sc.KnownModes.RemoveAll(m => m == null);
                if (sc.KnownModes.Count > 64) sc.KnownModes.RemoveRange(64, sc.KnownModes.Count - 64);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var padTaken = new HashSet<string>();
            setups.RemoveAll(s => s == null);
            foreach (var s in setups)
            {
                if (string.IsNullOrWhiteSpace(s.Id) || !ids.Add(s.Id))
                {
                    s.Id = NewId();
                    ids.Add(s.Id);
                }
                // Screens Displicare knows, each once; the main one stays first.
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (s.Screens == null) s.Screens = new List<string>();
                s.Screens.RemoveAll(k => string.IsNullOrWhiteSpace(k) || !keys.Contains(k) || !seen.Add(k));
                if (s.Screens.Count > ConfigManager.MaxAlsoOn + 1) s.Screens.RemoveRange(ConfigManager.MaxAlsoOn + 1, s.Screens.Count - ConfigManager.MaxAlsoOn - 1);
                SanitizePlacement(s, screens);

                if (string.IsNullOrWhiteSpace(s.Name))
                {
                    var main = screens.Find(sc => string.Equals(sc.Key, s.MainScreen, StringComparison.OrdinalIgnoreCase));
                    s.Name = main != null && !string.IsNullOrWhiteSpace(main.DisplayName) ? main.DisplayName : (s.MainScreen ?? "Setup");
                }
                s.Name = s.Name.Trim();
                if (s.Name.Length > ConfigManager.MaxRoomNameLength) s.Name = s.Name.Substring(0, ConfigManager.MaxRoomNameLength);

                if (s.Audio != Room.AudioAuto && s.Audio != Room.AudioKeep && s.Audio != Room.AudioDevice) s.Audio = Room.AudioAuto;
                if (s.Audio == Room.AudioDevice && string.IsNullOrEmpty(s.AudioId) && string.IsNullOrEmpty(s.AudioName)) s.Audio = Room.AudioAuto;
                s.Hotkey = string.IsNullOrEmpty(s.Hotkey) ? "" : (Hotkey.Normalize(s.Hotkey) ?? "");
                // Unknown directions are given a new one; a direction two setups share stays with the first.
                if (s.PadDirection != null && s.PadDirection != "" && Array.IndexOf(Room.PadDirections, s.PadDirection) < 0) s.PadDirection = null;
                if (!string.IsNullOrEmpty(s.PadDirection) && !padTaken.Add(s.PadDirection)) s.PadDirection = "";
                if (s.Launcher != Room.LaunchNone && s.Launcher != Room.LaunchBigPicture && s.Launcher != Room.LaunchXbox) s.Launcher = Room.LaunchNone;
            }
            setups.RemoveAll(s => s.Screens.Count == 0);
            if (setups.Count > MaxSetups) setups.RemoveRange(MaxSetups, setups.Count - MaxSetups);

            foreach (var s in setups) if (s.Id == safeId) return safeId;
            return setups.Count > 0 ? setups[0].Id : null;
        }
    }
}
