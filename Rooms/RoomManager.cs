using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace Displicare.Rooms
{
    /// <summary>What the Rooms tab and window show for one setup (shown as a room until the UI is renamed).</summary>
    internal sealed class RoomStatus
    {
        public Setup Setup;
        /// <summary>The setup's main screen; its mode, HDR and EDID are what the settings show.</summary>
        public KnownScreen Screen;
        /// <summary>The main screen is connected (the others are left out if they aren't).</summary>
        public bool Connected;
        public bool Active;
        /// <summary>e.g. "HDMI · NVIDIA".</summary>
        public string Connection;
        /// <summary>Modes for the drop-downs, best first.</summary>
        public List<DisplayMode> Modes;
        /// <summary>True until the room has been on screen: Modes then come from the EDID and may not all work.</summary>
        public bool ModesFromEdid;
        /// <summary>The display's native resolution from its EDID, if known.</summary>
        public DisplayMode? Native;
        public bool HdrCapable;
        /// <summary>"Dell U2723QE, LG TV SSCR2": the other screens this setup turns on, or null.</summary>
        public string AlsoOnNames;
        /// <summary>Those of them that aren't connected now, or null.</summary>
        public string MissingNames;
    }

    /// <summary>
    /// Room switching, on setups: knows every screen it has seen (a new one also gets a setup of its own, once), switches
    /// to a setup (its screens on, the first as the main one, in its saved arrangement or side by side, with its sound),
    /// asks to keep new layouts, and learns each screen's mode and HDR while it is on by itself. A setup only changes when
    /// the user changes it.
    /// Lives on the UI thread; switches run on their own STA thread so the prompt and the 10 s wait for a waking TV
    /// never block Displicare. It reacts to display changes, sound notifications, hotkeys and pipe commands; the only
    /// thing that runs while idle is the controller check (<see cref="Gamepad"/>), and only while that option is on.
    /// While a stream adds a virtual display, the physical screens are turned off until it ends.
    /// </summary>
    internal sealed class RoomManager : IDisposable
    {
        const int HomeHotkeyId = 1;
        const int FirstRoomHotkeyId = 100;
        const int ConfirmSeconds = 15;
        const int MaxKnownModes = 64;
        /// <summary>Right after a switch the display is still settling; don't learn its mode and HDR from that.</summary>
        static readonly TimeSpan LearnQuietTime = TimeSpan.FromSeconds(20);
        /// <summary>A TV's HDMI sound output can show up well after the picture (seen: more than 10 s on an AMD port).</summary>
        static readonly TimeSpan AudioWaitTime = TimeSpan.FromSeconds(60);
        /// <summary>A new stream sets its resolution first; turning the other screens off waits for that.</summary>
        static readonly TimeSpan StreamSettleTime = TimeSpan.FromSeconds(3);
        static readonly TimeSpan MinimizeDelay = TimeSpan.FromSeconds(3);
        const int PickerMs = 10000;
        const int NoteMs = 4000;

        readonly AppSettings settings;
        readonly Action save;
        readonly Dispatcher dispatcher;
        /// <summary>Setup ids, by hotkey id - <see cref="FirstRoomHotkeyId"/>.</summary>
        readonly List<string> hotkeySetups = new List<string>();
        readonly HashSet<string> reportedHotkeys = new HashSet<string>();
        readonly Dictionary<string, byte[]> edidCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        HotkeyWindow hotkeys;
        CommandPipe pipe;
        AudioWatcher audioWatcher;
        Gamepad gamepad;
        DispatcherTimer streamTimer;
        /// <summary>The physical screens from before a stream turned them off; put back when the stream ends.</summary>
        Profile beforeStream;
        /// <summary>The layout at the last display change without a stream: what to put back after one.</summary>
        Profile lastLayout;
        bool wasStreaming;
        Dictionary<string, string> seenScreens;
        /// <summary>Auto-hide was on when a stream began and Displicare turned it off; it goes back on after the stream.</summary>
        bool autoHideAfterStream;
        bool afterStreamPending;
        List<TargetInfo> lastTargets = new List<TargetInfo>();
        /// <summary>The setup whose screens are exactly the ones on, or null.</summary>
        string activeId;
        bool busy;
        DateTime lastSwitchUtc = DateTime.MinValue;
        Setup pendingAudioSetup;
        DateTime pendingAudioUntil;
        /// <summary>Our own default-output changes notify us too; those aren't the user's choice to learn.</summary>
        DateTime ignoreAudioChangesUntil;

        /// <summary>Rooms, connection or active room changed: rebuild the tab and tray menu.</summary>
        public event Action Changed;
        /// <summary>Title, text, warning: shown as a tray balloon.</summary>
        public event Action<string, string, bool> Notify;
        /// <summary>Asked before every switch; false while something else owns the displays (the hardware test).</summary>
        public Func<bool> CanSwitch;

        public RoomManager(AppSettings settings, Action save, Dispatcher dispatcher)
        {
            this.settings = settings;
            this.save = save;
            this.dispatcher = dispatcher;
            ConfigManager.EnsureSetups(settings);
        }

        public bool IsRunning { get { return hotkeys != null; } }
        public bool Busy { get { return busy; } }
        public AppSettings Settings { get { return settings; } }

        public void Start()
        {
            if (hotkeys != null) return;
            hotkeys = new HotkeyWindow(OnHotkey);
            pipe = new CommandPipe(OnCommand);
            try { audioWatcher = new AudioWatcher(OnDefaultAudioChanged, OnAudioOutputsChanged, dispatcher); }
            catch (Exception ex) { RoomLog.Write("Sound notifications unavailable: " + ex.Message); }
            RoomLog.Write("Room switching on.");
            ApplyController();
            TidyLearnedModes();
            Refresh();
        }

        /// <summary>Starts or stops watching controllers to match the setting.</summary>
        void ApplyController()
        {
            bool want = hotkeys != null && settings.RoomController;
            if (want && gamepad == null) gamepad = new Gamepad(OnPad, dispatcher);
            else if (!want && gamepad != null)
            {
                gamepad.Dispose();
                gamepad = null;
                RoomOverlay.CloseAll();
            }
        }

        /// <summary>
        /// Screens learned before "Native" / "Highest" were kept as defaults (2.2.1 test builds) hold the exact mode; turn it
        /// back into the defaults from the modes the screen offered. Same mode, so it doesn't ask to keep again.
        /// </summary>
        void TidyLearnedModes()
        {
            bool changed = false;
            foreach (var room in settings.KnownScreens)
            {
                if (room.Width <= 0 && room.RefreshHz <= 0) continue;
                var offered = MergeModes(room.KnownModes, null);
                if (offered.Count == 0) continue;
                var native = Edid.Preferred(EdidFor(room));
                int width = room.Width, height = room.Height, hz = room.RefreshHz;
                if (width <= 0)
                {
                    // Already "Native": check the refresh rate against the native size.
                    var best = LayoutEngine.PickMode(offered, 0, 0, 0,
                        native != null ? native.Value.Width : 0, native != null ? native.Value.Height : 0);
                    if (best == null) continue;
                    width = best.Value.Width;
                    height = best.Value.Height;
                }
                AsDefaults(offered, native, ref width, ref height, ref hz);
                if (width == room.Width && height == room.Height && hz == room.RefreshHz) continue;
                room.Width = width;
                room.Height = height;
                room.RefreshHz = hz;
                RoomLog.Write(ScreenName(room) + " now uses " + RoomsView.Summary(room));
                changed = true;
            }
            if (changed) save();
        }

        /// <summary>"LG TV SSCR2", or the key for a screen without an EDID name.</summary>
        internal static string ScreenName(KnownScreen screen)
        {
            if (screen == null) return "?";
            return string.IsNullOrWhiteSpace(screen.DisplayName) ? screen.Key : screen.DisplayName;
        }

        public void Stop()
        {
            if (hotkeys == null) return;
            hotkeys.Dispose();
            hotkeys = null;
            pipe.Dispose();
            pipe = null;
            if (audioWatcher != null)
            {
                audioWatcher.Dispose();
                audioWatcher = null;
            }
            pendingAudioSetup = null;
            ApplyController();
            if (streamTimer != null) streamTimer.Stop();
            if (autoHideAfterStream)
            {
                // Closing during a stream: don't leave the user without the auto-hide Displicare took away.
                autoHideAfterStream = false;
                Taskbar.AutoHide = true;
            }
            RoomOverlay.CloseAll();
            RoomLog.Write("Room switching off.");
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>Finds new displays and learns from the active room. Called on start and after display changes.</summary>
        public void Refresh()
        {
            if (busy || hotkeys == null) return;
            try
            {
                Discover();
                // A stream can change the desk screen's resolution to the client's; that isn't the room's setting.
                bool streaming = StreamOn();
                if (!streaming && DateTime.UtcNow - lastSwitchUtc > LearnQuietTime) Learn();
                if (!streaming) RememberLayout();
                CheckStream();
            }
            catch (Exception ex) { RoomLog.Write("Refresh failed: " + ex.Message); }
            RegisterHotkeys();
            RaiseChanged();
        }

        // ---------------------------------------------------------------- discovery

        void Discover()
        {
            lastTargets = DisplayTopology.DescribeTargets(DisplayTopology.Query(DisplayConfigApi.QDC_ALL_PATHS));
            // 2.3.2 test builds took a streaming display (SudoVDA reports a connector like HDMI) for a screen of its own.
            int stray = settings.KnownScreens.RemoveAll(sc => DisplayConfigApi.IsVirtualAdapter(sc.AdapterPath));
            if (stray > 0)
            {
                settings.SafeSetupId = SetupMigration.Sanitize(settings.KnownScreens, settings.Setups, settings.SafeSetupId);
                RoomLog.Write("Removed " + stray + " screen(s) for virtual displays.");
                save();
            }
            bool added = false;
            foreach (var t in lastTargets)
            {
                if (!t.Available || t.IsVirtual || t.Identity == null) continue;
                string key = ModelKey(t.Identity.DevicePath);
                if (key == null) continue;
                var screen = FindScreen(key);
                if (screen == null)
                {
                    // A screen seen for the first time also gets a setup of its own. Only now: a setup deleted later
                    // stays deleted, because the screen is still known.
                    screen = new KnownScreen();
                    screen.Key = key;
                    settings.KnownScreens.Add(screen);
                    var setup = new Setup();
                    setup.Id = SetupMigration.IdForRoom(key);
                    if (FindSetupById(setup.Id) != null) setup.Id = SetupMigration.NewId();
                    setup.Name = t.Identity.DisplayName;
                    setup.Screens.Add(key);
                    setup.Hotkey = NextFreeHotkey();
                    settings.Setups.Add(setup);
                    added = true;
                    RoomLog.Write("New screen " + t.Identity.DisplayName + " (" + key + "), room \"" + setup.Name + "\"");
                }
                screen.DisplayName = t.Identity.FriendlyName;
                screen.DevicePath = t.Identity.DevicePath;
                screen.AdapterPath = t.Identity.AdapterPath;
                screen.EdidManufacturer = t.Identity.EdidManufacturer;
                screen.EdidProduct = t.Identity.EdidProduct;
                screen.ConnectorInstance = (int)t.Identity.ConnectorInstance;
            }
            activeId = FindActiveSetup();
            if (added)
            {
                // The screen in use when rooms were first set up is the natural way home.
                if (FindSetupById(settings.SafeSetupId) == null)
                    settings.SafeSetupId = activeId ?? (settings.Setups.Count > 0 ? settings.Setups[0].Id : null);
                save();
            }
            if (AssignPadDirections(settings.Setups, settings.SafeSetupId))
            {
                foreach (var s in settings.Setups)
                    if (!string.IsNullOrEmpty(s.PadDirection)) RoomLog.Write("\"" + s.Name + "\" is on " + PadLabel(s.PadDirection) + ".");
                save();
            }
            LogScreensComingAndGoing();
        }

        /// <summary>The first Ctrl+Alt+1..9 no setup and no home hotkey uses, or "".</summary>
        string NextFreeHotkey()
        {
            var taken = new List<string>();
            taken.Add(settings.RoomHomeHotkey);
            foreach (var s in settings.Setups) taken.Add(s.Hotkey);
            return Hotkey.NextFree(taken) ?? "";
        }

        /// <summary>
        /// Notes when a screen appears or disappears for Windows. Some TVs vanish when they are off or on another input;
        /// for those, turning the TV on could later switch to it on its own.
        /// </summary>
        void LogScreensComingAndGoing()
        {
            var now = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in lastTargets)
            {
                if (!t.Available || t.IsVirtual || t.Identity == null) continue;
                string key = ModelKey(t.Identity.DevicePath);
                if (key != null) now[key] = t.Identity.DisplayName;
            }
            if (seenScreens != null)
            {
                foreach (var pair in now) if (!seenScreens.ContainsKey(pair.Key)) RoomLog.Write("Screen appeared: " + pair.Value + " (" + pair.Key + ")");
                foreach (var pair in seenScreens) if (!now.ContainsKey(pair.Key)) RoomLog.Write("Screen went away: " + pair.Value + " (" + pair.Key + ")");
            }
            seenScreens = now;
        }

        /// <summary>The monitor model from a device path: \\?\DISPLAY#GSM82CD#5&amp;...#{guid} gives GSM82CD.</summary>
        internal static string ModelKey(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return null;
            string[] parts = devicePath.Split('#');
            return parts.Length >= 2 && parts[1].Length > 0 ? parts[1].ToUpperInvariant() : null;
        }

        /// <summary>Key of the one physical display that is on (virtual ones don't count), or null.</summary>
        internal static string ActiveKey(IList<TargetInfo> targets)
        {
            string key = null;
            int physical = 0;
            foreach (var t in targets)
            {
                if (!t.Active || t.IsVirtual) continue;
                physical++;
                key = t.Identity != null ? ModelKey(t.Identity.DevicePath) : null;
            }
            return physical == 1 ? key : null;
        }

        /// <summary>The setup on screen now, from <see cref="lastTargets"/> and which screen is the main one.</summary>
        string FindActiveSetup()
        {
            string primary = null;
            try
            {
                foreach (var d in DisplayTopology.GetActiveDisplays())
                    if (d.Primary && !d.IsVirtual && d.Identity != null) primary = ModelKey(d.Identity.DevicePath);
            }
            catch (Exception) { }
            return ActiveSetupId(lastTargets, settings.Setups, primary);
        }

        /// <summary>
        /// Id of the setup on screen: one whose screens are exactly the physical ones on. Of several, the one with the main
        /// screen as its own main one, then one that isn't hidden, then the first.
        /// </summary>
        internal static string ActiveSetupId(IList<TargetInfo> targets, IList<Setup> setups, string primaryKey)
        {
            var on = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in targets)
            {
                if (!t.Active || t.IsVirtual) continue;
                string key = t.Identity != null ? ModelKey(t.Identity.DevicePath) : null;
                if (key == null) return null;
                on.Add(key);
            }
            if (on.Count == 0) return null;
            Setup best = null;
            int bestScore = -1;
            foreach (var s in setups)
            {
                if (s == null || s.Screens.Count != on.Count || !new HashSet<string>(s.Screens, StringComparer.OrdinalIgnoreCase).SetEquals(on)) continue;
                int score = (string.Equals(s.MainScreen, primaryKey, StringComparison.OrdinalIgnoreCase) ? 2 : 0) + (s.Hidden ? 0 : 1);
                if (score > bestScore) { best = s; bestScore = score; }
            }
            return best != null ? best.Id : null;
        }

        public KnownScreen FindScreen(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var s in settings.KnownScreens) if (string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        public Setup FindSetupById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in settings.Setups) if (s.Id == id) return s;
            return null;
        }

        /// <summary>
        /// A setup by name ("Living room"), its screen's model key, or a unique part of its name or its main screen's
        /// ("living", "lg"): for --room and the pipe.
        /// </summary>
        public Setup FindSetup(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            foreach (var s in settings.Setups) if (string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase)) return s;
            var byId = FindSetupById(text);
            if (byId != null) return byId;
            var byKey = SetupMigration.SetupFor(settings.Setups, text);
            if (byKey != null) return byKey;
            Setup hit = null;
            foreach (var s in settings.Setups)
            {
                var main = FindScreen(s.MainScreen);
                bool match = (s.Name ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                    || (main != null && (main.DisplayName ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) continue;
                if (hit != null) return null;
                hit = s;
            }
            return hit;
        }

        /// <summary>Where the home hotkey goes: the safe setup, else the first one that isn't hidden.</summary>
        public Setup SafeSetup
        {
            get
            {
                var safe = FindSetupById(settings.SafeSetupId);
                if (safe != null) return safe;
                foreach (var s in settings.Setups) if (!s.Hidden) return s;
                return null;
            }
        }

        public List<RoomStatus> Statuses()
        {
            var list = new List<RoomStatus>();
            foreach (var setup in settings.Setups)
            {
                var s = new RoomStatus();
                s.Setup = setup;
                s.Screen = FindScreen(setup.MainScreen);
                s.Active = setup.Id == activeId;
                foreach (var t in lastTargets)
                {
                    if (!t.Available || t.Identity == null || !string.Equals(ModelKey(t.Identity.DevicePath), setup.MainScreen, StringComparison.OrdinalIgnoreCase)) continue;
                    s.Connected = true;
                    s.Connection = DisplayConfigApi.OutputName(t.OutputTechnology) + " · " + GpuName(t.Identity.AdapterPath);
                }
                byte[] edid = EdidFor(s.Screen);
                s.Native = Edid.Preferred(edid);
                s.HdrCapable = Edid.SupportsHdr(edid);
                // Once the screen has been on, only offer what Windows actually allowed on this connection.
                s.ModesFromEdid = s.Screen == null || s.Screen.KnownModes.Count == 0;
                s.Modes = MergeModes(s.Screen != null ? s.Screen.KnownModes : null, s.ModesFromEdid ? Edid.Modes(edid) : null);
                if (setup.Screens.Count > 1)
                {
                    var names = new List<string>();
                    var missing = new List<string>();
                    for (int i = 1; i < setup.Screens.Count; i++)
                    {
                        string name = ScreenName(FindScreen(setup.Screens[i]));
                        names.Add(name);
                        if (!IsConnected(setup.Screens[i])) missing.Add(name);
                    }
                    s.AlsoOnNames = string.Join(", ", names.ToArray());
                    if (missing.Count > 0) s.MissingNames = string.Join(", ", missing.ToArray());
                }
                list.Add(s);
            }
            return list;
        }

        byte[] EdidFor(KnownScreen screen)
        {
            if (screen == null || string.IsNullOrEmpty(screen.DevicePath)) return null;
            byte[] edid;
            if (!edidCache.TryGetValue(screen.DevicePath, out edid))
            {
                edid = Edid.Read(screen.DevicePath);
                edidCache[screen.DevicePath] = edid;
            }
            return edid;
        }

        /// <summary>Known modes ("3840x2160@144") and EDID modes together, best first, no duplicates.</summary>
        internal static List<DisplayMode> MergeModes(IList<string> known, IList<DisplayMode> fromEdid)
        {
            var all = new List<DisplayMode>();
            var seen = new HashSet<string>();
            if (known != null)
                foreach (var k in known)
                {
                    DisplayMode m;
                    if (TryParseMode(k, out m) && seen.Add(m.ToString())) all.Add(m);
                }
            if (fromEdid != null) foreach (var m in fromEdid) if (seen.Add(m.ToString())) all.Add(m);
            SortBestFirst(all);
            return all;
        }

        internal static void SortBestFirst(List<DisplayMode> modes)
        {
            modes.Sort(delegate (DisplayMode a, DisplayMode b)
            {
                long pa = (long)a.Width * a.Height, pb = (long)b.Width * b.Height;
                return pa != pb ? pb.CompareTo(pa) : b.Hz.CompareTo(a.Hz);
            });
        }

        /// <summary>
        /// The modes worth remembering: best first, nothing below 1280 wide (unless that's all there is), capped so the
        /// settings file stays small. Windows lists modes smallest first, so taking the first ones would keep only junk.
        /// </summary>
        internal static List<string> ModesToRemember(IList<DisplayMode> offered)
        {
            var modes = new List<DisplayMode>(offered);
            SortBestFirst(modes);
            var list = new List<string>();
            bool large = modes.Count > 0 && modes[0].Width >= 1280;
            foreach (var m in modes)
            {
                if (list.Count >= MaxKnownModes) break;
                if (large && m.Width < 1280) continue;
                list.Add(FormatMode(m));
            }
            return list;
        }

        internal static string FormatMode(DisplayMode m)
        {
            return m.Width + "x" + m.Height + "@" + m.Hz;
        }

        internal static bool TryParseMode(string text, out DisplayMode mode)
        {
            mode = new DisplayMode();
            if (string.IsNullOrEmpty(text)) return false;
            string[] at = text.Split('@');
            if (at.Length != 2) return false;
            string[] wh = at[0].Split('x');
            int w, h, hz;
            if (wh.Length != 2 || !int.TryParse(wh[0], out w) || !int.TryParse(wh[1], out h) || !int.TryParse(at[1], out hz)) return false;
            if (w <= 0 || h <= 0 || hz <= 0) return false;
            mode = new DisplayMode(w, h, hz);
            return true;
        }

        internal static string GpuName(string adapterPath)
        {
            string p = (adapterPath ?? "").ToUpperInvariant();
            if (p.Contains("VEN_10DE")) return "NVIDIA";
            if (p.Contains("VEN_1002")) return "AMD";
            if (p.Contains("VEN_8086")) return "Intel";
            return p.Length == 0 ? "?" : "virtual";
        }

        // ---------------------------------------------------------------- learning

        /// <summary>
        /// Remembers which modes each screen on offers, and the mode and HDR of a screen on by itself in its own setup. Runs
        /// after display changes and right before switching away, so a setting changed in Windows is kept for next time.
        /// A setup's screens and arrangement never change on their own (<see cref="UseCurrentArrangement"/>). Sound is
        /// learned from the user's own default-output changes instead (<see cref="OnDefaultAudioChanged"/>).
        /// </summary>
        void Learn()
        {
            var active = FindSetupById(activeId);
            bool alone = active != null && active.Screens.Count == 1;
            bool changed = false;
            foreach (var display in DisplayTopology.GetActiveDisplays())
            {
                if (display.IsVirtual || display.Identity == null) continue;
                var screen = FindScreen(ModelKey(display.Identity.DevicePath));
                if (screen == null) continue;
                var offered = display.GdiName != null ? LayoutEngine.ListModes(display.GdiName) : null;
                if (offered != null)
                {
                    var known = ModesToRemember(offered);
                    if (known.Count > 0 && string.Join(",", known.ToArray()) != string.Join(",", screen.KnownModes.ToArray()))
                    {
                        screen.KnownModes = known;
                        changed = true;
                    }
                }
                // Mode and HDR only from a screen on by itself: next to others it may run as the setup arranged it.
                if (!alone || !string.Equals(screen.Key, active.MainScreen, StringComparison.OrdinalIgnoreCase)) continue;

                bool modeChanged = false;
                int width = display.Width, height = display.Height, hz = (int)Math.Round(display.RefreshHz);
                if (offered != null) AsDefaults(offered, Edid.Preferred(EdidFor(screen)), ref width, ref height, ref hz);
                if (screen.Width != width || screen.Height != height || screen.RefreshHz != hz)
                {
                    screen.Width = width;
                    screen.Height = height;
                    screen.RefreshHz = hz;
                    modeChanged = true;
                }
                if (display.Hdr >= 0)
                {
                    // HDR on keeps "auto"; turning it off in Windows is remembered as off.
                    string hdr = display.Hdr == 1 ? (screen.Hdr == Room.HdrOff ? Room.HdrOn : screen.Hdr) : Room.HdrOff;
                    if (screen.Hdr != hdr) { screen.Hdr = hdr; modeChanged = true; }
                }
                if (modeChanged)
                {
                    changed = true;
                    RoomLog.Write("Learned " + ScreenName(screen) + ": " + RoomsView.Summary(screen));
                }
            }
            if (changed) save();
        }

        /// <summary>Which screens, where, in which mode: two layouts with the same signature look the same.</summary>
        internal static string LayoutSignature(Profile layout)
        {
            var parts = new List<string>();
            foreach (var d in layout.Displays)
                parts.Add(string.Format("{0}@{1},{2}:{3}x{4}@{5:0.##}/{6}{7}", d.Identity != null ? ModelKey(d.Identity.DevicePath) : "?",
                    d.X, d.Y, d.Width, d.Height, d.RefreshHz, d.Hdr, d.Primary ? "*" : ""));
            parts.Sort(StringComparer.Ordinal);
            return string.Join(" ", parts.ToArray());
        }

        /// <summary>The screens in a layout, by key.</summary>
        internal static HashSet<string> ScreenKeys(Profile layout)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in layout.Displays)
            {
                string key = d != null && d.Identity != null ? ModelKey(d.Identity.DevicePath) : null;
                keys.Add(key ?? "");
            }
            return keys;
        }

        /// <summary>
        /// Whether a saved layout (from settings.json) can be used: two or more screens that each say which monitor they
        /// are. Lists missing from the file are filled in empty.
        /// </summary>
        internal static bool UsableLayout(Profile layout)
        {
            if (layout == null || layout.Displays == null || layout.Displays.Count < 2 || layout.Displays.Count > 16) return false;
            foreach (var d in layout.Displays) if (d == null || d.Identity == null || ModelKey(d.Identity.DevicePath) == null) return false;
            if (layout.Paths == null) layout.Paths = new List<SavedPath>();
            if (layout.Modes == null) layout.Modes = new List<SavedMode>();
            if (layout.Adapters == null) layout.Adapters = new List<SavedAdapter>();
            return true;
        }

        /// <summary>
        /// Turns a learned mode back into "Native" / "Highest" (0) when it is what those would pick anyway, so the
        /// settings show the defaults and only a mode the user chose is pinned.
        /// </summary>
        internal static void AsDefaults(IList<DisplayMode> offered, DisplayMode? native, ref int width, ref int height, ref int hz)
        {
            int highest = 0;
            foreach (var m in offered) if (m.Width == width && m.Height == height && m.Hz > highest) highest = m.Hz;
            if (highest > 0 && hz >= highest) hz = 0;

            var best = LayoutEngine.PickMode(offered, 0, 0, 0,
                native != null ? native.Value.Width : 0, native != null ? native.Value.Height : 0);
            if (best != null && best.Value.Width == width && best.Value.Height == height) { width = 0; height = 0; }
        }

        // ---------------------------------------------------------------- sound

        /// <summary>Sets the setup's sound now if its output is there, else as soon as it appears (up to a minute).</summary>
        void BeginSetupAudio(Setup setup)
        {
            pendingAudioSetup = null;
            if (setup.Audio == Room.AudioKeep) return;
            if (TrySetSetupAudio(setup)) return;
            pendingAudioSetup = setup;
            pendingAudioUntil = DateTime.UtcNow + AudioWaitTime;
            RoomLog.Write("Waiting for the sound output of \"" + setup.Name + "\"");
        }

        /// <summary>"Auto" is the main screen's own sound output (a TV's HDMI audio).</summary>
        bool TrySetSetupAudio(Setup setup)
        {
            List<AudioOutput> outputs;
            try { outputs = AudioOutputs.List(); }
            catch (Exception ex) { RoomLog.Write("Couldn't list sound outputs: " + ex.Message); return false; }
            var main = FindScreen(setup.MainScreen);
            int index = setup.Audio == Room.AudioAuto
                ? AudioOutputs.ForDisplay(main != null ? main.DisplayName : null, outputs)
                : AudioOutputs.Match(setup.AudioId, setup.AudioName, outputs);
            if (index < 0) return false;
            if (outputs[index].IsDefault) return true;
            ignoreAudioChangesUntil = DateTime.UtcNow.AddSeconds(5);
            if (AudioOutputs.SetDefault(outputs[index].Id)) RoomLog.Write("Sound output: " + outputs[index].Name);
            else RoomLog.Write("Couldn't switch sound to " + outputs[index].Name);
            return true;
        }

        void OnAudioOutputsChanged()
        {
            if (pendingAudioSetup == null) return;
            if (DateTime.UtcNow > pendingAudioUntil)
            {
                RoomLog.Write("The sound output of \"" + pendingAudioSetup.Name + "\" never appeared; sound stays where it was.");
                pendingAudioSetup = null;
                return;
            }
            if (TrySetSetupAudio(pendingAudioSetup)) pendingAudioSetup = null;
        }

        /// <summary>The user picked another output while a setup is on screen: that setup uses it from now on.</summary>
        void OnDefaultAudioChanged(string id)
        {
            if (busy || DateTime.UtcNow < ignoreAudioChangesUntil || hotkeys == null) return;
            var setup = FindSetupById(activeId);
            if (setup == null || setup.Audio == Room.AudioKeep) return;
            List<AudioOutput> outputs;
            try { outputs = AudioOutputs.List(); }
            catch (Exception) { return; }
            AudioOutput chosen = null;
            foreach (var o in outputs) if (o.Id == id) chosen = o;
            if (chosen == null) return;

            var main = FindScreen(setup.MainScreen);
            int own = AudioOutputs.ForDisplay(main != null ? main.DisplayName : null, outputs);
            bool changed;
            if (own >= 0 && outputs[own].Id == id)
            {
                changed = setup.Audio != Room.AudioAuto;
                setup.Audio = Room.AudioAuto;
            }
            else
            {
                changed = setup.Audio != Room.AudioDevice || setup.AudioId != id;
                setup.Audio = Room.AudioDevice;
                setup.AudioId = id;
                setup.AudioName = chosen.Name;
            }
            if (pendingAudioSetup == setup) pendingAudioSetup = null; // the user chose; stop waiting
            if (!changed) return;
            RoomLog.Write("Learned \"" + setup.Name + "\" sound: " + (setup.Audio == Room.AudioAuto ? "its own (" + chosen.Name + ")" : chosen.Name));
            save();
            RaiseChanged();
        }

        // ---------------------------------------------------------------- switching

        /// <summary>
        /// Turns the setup's screens on (the first as the main one) and the others off. Asks to keep it unless it's the
        /// home hotkey (<paramref name="safe"/>) or the setup was kept before (per the "ask" setting), and switches back
        /// when nobody answers.
        /// </summary>
        public void SwitchTo(Setup setup, bool safe)
        {
            if (setup == null || busy) return;
            if (CanSwitch != null && !CanSwitch())
            {
                RaiseNotify("Can't switch rooms right now", "Finish the hardware test first.", true);
                return;
            }
            try
            {
                lastTargets = DisplayTopology.DescribeTargets(DisplayTopology.Query(DisplayConfigApi.QDC_ALL_PATHS));
                activeId = FindActiveSetup();
                Learn(); // keep what was used on the screens we're leaving
            }
            catch (Exception ex) { RoomLog.Write("Couldn't read the current room: " + ex.Message); }

            var leaving = FindSetupById(activeId);
            var profile = SetupProfile(setup);
            if (profile == null)
            {
                RaiseNotify("Couldn't switch to " + setup.Name, "Its main screen isn't known any more.", true);
                return;
            }
            bool ask = !safe && (settings.RoomAskToKeep == AppSettings.RoomAskAlways
                || (settings.RoomAskToKeep == AppSettings.RoomAskNew && !setup.Confirmed));
            bool pad = gamepad != null && gamepad.Connected;
            if (pad && ask) gamepad.Navigating = true; // A and B answer the prompt
            busy = true;
            pendingAudioSetup = null;
            RoomOverlay.CloseAll();
            RaiseChanged();

            var thread = new Thread(delegate ()
            {
                ApplyResult result;
                bool asked = false;
                // Going from several screens to one can trip Explorer's "only one hidden toolbar per side" (see Taskbar).
                bool autoHide = Taskbar.AutoHide;
                if (autoHide)
                {
                    Taskbar.AutoHide = false;
                    Thread.Sleep(300);
                }
                try
                {
                    result = LayoutEngine.Apply(profile, 10);
                    // Sound starts while the prompt is up, so you hear the TV before deciding.
                    if (result.Success) dispatcher.Invoke((Action)delegate { BeginSetupAudio(setup); });
                    if (result.Success && ask)
                    {
                        asked = true;
                        bool kept = ConfirmForm.Ask(setup.Name, ConfirmSeconds, pad);
                        RoomLog.Write(kept ? "Kept \"" + setup.Name + "\"." : "\"" + setup.Name + "\" not kept; switching back.");
                        if (!kept)
                        {
                            dispatcher.Invoke((Action)delegate { pendingAudioSetup = null; });
                            var back = result.Previous != null ? LayoutEngine.Apply(result.Previous, 0) : null;
                            result.Reverted = true;
                            result.Message = back != null && back.Success
                                ? "Went back to the previous layout."
                                : "Going back failed: " + (back != null ? back.Message : "the previous layout wasn't recorded.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    RoomLog.Write("Switch failed: " + ex);
                    result = new ApplyResult();
                    result.Message = ex.Message;
                }
                dispatcher.BeginInvoke((Action)delegate
                {
                    Finish(setup, leaving, result, asked);
                    if (autoHide) AutoHideLater();
                });
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "Displicare room switch";
            thread.Start();
        }

        void Finish(Setup setup, Setup leaving, ApplyResult result, bool asked)
        {
            busy = false;
            if (gamepad != null) gamepad.Navigating = false;
            lastSwitchUtc = DateTime.UtcNow;
            // Going back restores the old output too; that isn't the user picking one.
            ignoreAudioChangesUntil = DateTime.UtcNow.AddSeconds(5);
            if (result.Success && !result.Reverted)
            {
                if (asked && !setup.Confirmed)
                {
                    setup.Confirmed = true;
                    save();
                }
                StartLauncher(setup, leaving);
                if (result.Warnings.Count > 0) RaiseNotify("Switched to " + setup.Name, string.Join("\n", result.Warnings.ToArray()), true);
                if (settings.RoomShowOverlay) RoomOverlay.ShowNote(setup.Name, SwitchNote(setup), NoteMs);
            }
            else if (result.Reverted) RaiseNotify(setup.Name + " wasn't kept", result.Message, false);
            else RaiseNotify("Couldn't switch to " + setup.Name, result.Message, true);

            try
            {
                lastTargets = DisplayTopology.DescribeTargets(DisplayTopology.Query(DisplayConfigApi.QDC_ALL_PATHS));
                activeId = FindActiveSetup();
                CheckStream(); // a stream that started during the switch
                if (!StreamOn()) RememberLayout();
            }
            catch (Exception) { }
            RaiseChanged();
        }

        /// <summary>
        /// "3840 × 2160 · 120 Hz · HDR · LG TV SSCR2", or with several screens "AORUS FO32U2 + Dell · 2560 × 1440 · …":
        /// what the setup's main screen and sound are now, and any of its screens that weren't connected.
        /// </summary>
        string SwitchNote(Setup setup)
        {
            var parts = new List<string>();
            try
            {
                if (setup.Screens.Count > 1)
                {
                    var on = new List<string>();
                    var missing = new List<string>();
                    foreach (var key in setup.Screens)
                    {
                        if (IsActive(key)) on.Add(ScreenName(FindScreen(key)));
                        else missing.Add(ScreenName(FindScreen(key)));
                    }
                    parts.Add(string.Join(" + ", on.ToArray()));
                    foreach (var m in missing) parts.Add(m + " not connected");
                }
                foreach (var d in DisplayTopology.GetActiveDisplays())
                {
                    if (d.IsVirtual || !d.Primary) continue;
                    parts.Add(d.Width + " × " + d.Height);
                    parts.Add(Math.Round(d.RefreshHz) + " Hz");
                    if (d.Hdr == 1) parts.Add("HDR");
                    break;
                }
                if (pendingAudioSetup == setup) parts.Add("sound when the TV is ready");
                else
                {
                    var audio = AudioOutputs.Default();
                    if (audio != null) parts.Add(ShortSoundName(audio.Name));
                }
            }
            catch (Exception ex) { RoomLog.Write("Couldn't describe the switch: " + ex.Message); }
            return string.Join("  ·  ", parts.ToArray());
        }

        /// <summary>"LG TV SSCR2 (NVIDIA High Definition Audio)" → "LG TV SSCR2"; "1 - LG TV" → "LG TV".</summary>
        internal static string ShortSoundName(string name)
        {
            name = (name ?? "").Trim();
            int paren = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (paren > 0 && name.EndsWith(")")) name = name.Substring(0, paren);
            int dash = name.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && dash <= 3)
            {
                int n;
                if (int.TryParse(name.Substring(0, dash), out n)) name = name.Substring(dash + 3);
            }
            return name;
        }

        /// <summary>
        /// The layout for switching to a setup: its saved arrangement when every one of its screens is connected, else
        /// its main screen with the connected others side by side to its right, each in its own mode. Null when the main
        /// screen isn't known.
        /// </summary>
        Profile SetupProfile(Setup setup)
        {
            var main = FindScreen(setup.MainScreen);
            if (main == null) return null;
            var native = Edid.Preferred(EdidFor(main));
            var others = new List<KnownScreen>();
            var natives = new List<DisplayMode?>();
            for (int i = 1; i < setup.Screens.Count; i++)
            {
                var other = FindScreen(setup.Screens[i]);
                if (other == null) continue;
                if (!IsConnected(other.Key))
                {
                    RoomLog.Write(ScreenName(other) + " isn't connected, so \"" + setup.Name + "\" goes without it.");
                    continue;
                }
                others.Add(other);
                natives.Add(Edid.Preferred(EdidFor(other)));
            }
            if (others.Count == 0) return BuildProfile(main, native, setup.Name);

            if (others.Count == setup.Screens.Count - 1 && UsableLayout(setup.Layout)
                && ScreenKeys(setup.Layout).SetEquals(setup.Screens))
            {
                bool all = true;
                foreach (var d in setup.Layout.Displays) if (LayoutEngine.MatchTarget(d.Identity, lastTargets) < 0) all = false;
                if (all) return setup.Layout;
            }
            return BuildCombo(setup.Name, main, native, others, natives);
        }

        bool IsConnected(string key)
        {
            foreach (var t in lastTargets)
                if (t.Available && !t.IsVirtual && t.Identity != null && string.Equals(ModelKey(t.Identity.DevicePath), key, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        bool IsActive(string key)
        {
            foreach (var t in lastTargets)
                if (t.Active && !t.IsVirtual && t.Identity != null && string.Equals(ModelKey(t.Identity.DevicePath), key, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// The main screen (at 0,0) with other screens side by side to its right, each in its own mode and HDR. Used until
        /// the setup has a saved arrangement.
        /// </summary>
        internal static Profile BuildCombo(string name, KnownScreen main, DisplayMode? native, IList<KnownScreen> others, IList<DisplayMode?> otherNatives)
        {
            var profile = BuildProfile(main, native, name);
            int x = Wide(profile.Displays[0]);
            for (int i = 0; i < others.Count; i++)
            {
                var d = BuildProfile(others[i], i < otherNatives.Count ? otherNatives[i] : null, name).Displays[0];
                d.Primary = false;
                d.X = x;
                d.Y = 0;
                x += Wide(d);
                profile.Displays.Add(d);
            }
            return profile;
        }

        /// <summary>The width a display will have: its set width, else its native one, else a guess.</summary>
        static int Wide(ProfileDisplay d)
        {
            return d.Width > 0 ? d.Width : d.NativeWidth > 0 ? d.NativeWidth : 1920;
        }

        /// <summary>One screen on by itself, as the main one. Sound is handled separately (<see cref="BeginSetupAudio"/>).</summary>
        internal static Profile BuildProfile(KnownScreen screen, DisplayMode? native, string name)
        {
            var id = new DisplayIdentity();
            id.DevicePath = screen.DevicePath;
            id.FriendlyName = screen.DisplayName;
            id.EdidManufacturer = (ushort)screen.EdidManufacturer;
            id.EdidProduct = (ushort)screen.EdidProduct;
            id.ConnectorInstance = (uint)screen.ConnectorInstance;
            id.AdapterPath = screen.AdapterPath;

            var display = new ProfileDisplay();
            display.Identity = id;
            display.Primary = true;
            display.Width = screen.Width;
            display.Height = screen.Height;
            display.RefreshHz = screen.RefreshHz;
            display.Hdr = screen.Hdr == Room.HdrOn ? 1 : screen.Hdr == Room.HdrOff ? 0 : 2;
            if (native != null)
            {
                display.NativeWidth = native.Value.Width;
                display.NativeHeight = native.Value.Height;
            }

            var profile = new Profile();
            profile.Name = name;
            profile.Displays.Add(display);
            return profile;
        }

        /// <summary>
        /// Opens the setup's launcher (Steam Big Picture or Xbox mode) and closes the one the setup left behind used,
        /// unless both use the same. Xbox mode waits for Windows to confirm the change, so it runs off the UI thread.
        /// </summary>
        void StartLauncher(Setup setup, Setup leaving)
        {
            string open = setup.Launcher ?? Room.LaunchNone;
            string close = leaving != null && leaving != setup ? leaving.Launcher ?? Room.LaunchNone : Room.LaunchNone;
            if (close == open) close = Room.LaunchNone;

            if (close == Room.LaunchBigPicture) OpenUrl("steam://close/bigpicture");
            // Only when nothing opens here: a new Big Picture or Xbox mode is meant to be on screen.
            if (close != Room.LaunchNone && open == Room.LaunchNone) MinimizeAfterLauncher(close);
            if (open == Room.LaunchBigPicture) OpenUrl("steam://open/bigpicture");
            if (open == Room.LaunchXbox || close == Room.LaunchXbox)
            {
                bool on = open == Room.LaunchXbox;
                string name = setup.Name;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    if (XboxMode.Set(on) || !on) return;
                    dispatcher.BeginInvoke((Action)delegate
                    {
                        RaiseNotify("Xbox mode didn't open", "Switched to " + name + ", but Windows didn't start Xbox mode. It needs Windows 11 with Xbox mode (Settings > Gaming).", true);
                    });
                });
            }
        }

        static void OpenUrl(string url)
        {
            try
            {
                var info = new ProcessStartInfo(url);
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex) { RoomLog.Write("Couldn't open " + url + ": " + ex.Message); }
        }

        // ---------------------------------------------------------------- editing setups

        /// <summary>
        /// Settings changed in the tab or window. Only new screens or a new resolution or refresh rate can leave a screen
        /// black, so only that makes the next switch ask again; names, sound, HDR and hotkeys don't.
        /// </summary>
        public void SetupEdited(Setup setup, bool screensChanged)
        {
            if (setup != null && screensChanged)
            {
                setup.Confirmed = false;
                // Saved for other screens: no longer fits.
                if (setup.Layout != null && !ScreenKeys(setup.Layout).SetEquals(setup.Screens)) setup.Layout = null;
            }
            OptionsEdited();
        }

        /// <summary>A screen's own resolution or refresh rate changed: every setup with it asks again.</summary>
        public void ScreenEdited(KnownScreen screen, bool modeChanged)
        {
            if (screen != null && modeChanged)
                foreach (var s in settings.Setups)
                    if (s.Screens.Exists(k => string.Equals(k, screen.Key, StringComparison.OrdinalIgnoreCase))) s.Confirmed = false;
            OptionsEdited();
        }

        /// <summary>Options for all setups changed (or a setup did): save and apply.</summary>
        public void OptionsEdited()
        {
            save();
            RegisterHotkeys();
            ApplyController();
            if (!settings.RoomStreamScreensOff && streamTimer != null) streamTimer.Stop();
            RaiseChanged();
        }

        /// <summary>Whether exactly this setup's screens are on now, so its arrangement can be taken from Windows.</summary>
        public bool ScreensAreOn(Setup setup)
        {
            if (setup == null) return false;
            var on = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in lastTargets)
                if (t.Active && !t.IsVirtual && t.Identity != null) on.Add(ModelKey(t.Identity.DevicePath) ?? "");
            return on.Count > 0 && on.SetEquals(setup.Screens);
        }

        /// <summary>
        /// Takes the arrangement of the screens on now (positions, exact modes, HDR) for the setup, whose screens they
        /// must be. Keeping it is the user's choice, so it counts as kept and doesn't ask on the next switch.
        /// </summary>
        public bool UseCurrentArrangement(Setup setup)
        {
            if (busy || StreamOn() || !ScreensAreOn(setup) || setup.Screens.Count < 2) return false;
            Profile now;
            try { now = LayoutEngine.Capture(setup.Name, false); }
            catch (Exception ex)
            {
                RoomLog.Write("Couldn't record the layout of \"" + setup.Name + "\": " + ex.Message);
                return false;
            }
            if (!ScreenKeys(now).SetEquals(setup.Screens)) return false;
            now.AudioId = null;
            now.AudioName = null;
            setup.Layout = now;
            setup.Confirmed = true;
            RoomLog.Write("Saved the arrangement of \"" + setup.Name + "\": " + now.Summary);
            OptionsEdited();
            return true;
        }

        /// <summary>
        /// A new setup from the screens on now, the main one first, in their current arrangement: what you see is what
        /// you save. Null when nothing can be saved (no screen on, a stream, a switch running).
        /// </summary>
        public Setup SaveCurrentLayout()
        {
            if (busy || StreamOn()) return null;
            Profile now;
            try
            {
                lastTargets = DisplayTopology.DescribeTargets(DisplayTopology.Query(DisplayConfigApi.QDC_ALL_PATHS));
                Discover(); // every screen on is a known one
                now = LayoutEngine.Capture("New room", false);
            }
            catch (Exception ex)
            {
                RoomLog.Write("Couldn't record the layout: " + ex.Message);
                return null;
            }
            var keys = new List<string>();
            foreach (var d in now.Displays)
            {
                string key = d.Identity != null ? ModelKey(d.Identity.DevicePath) : null;
                if (key == null || FindScreen(key) == null) return null;
                if (d.Primary) keys.Insert(0, key);
                else keys.Add(key);
            }
            if (keys.Count == 0) return null;

            var setup = new Setup();
            setup.Id = SetupMigration.NewId();
            var names = new List<string>();
            foreach (var k in keys) names.Add(ScreenName(FindScreen(k)));
            setup.Name = UniqueName(string.Join(" + ", names.ToArray()));
            setup.Screens.AddRange(keys);
            if (keys.Count > 1)
            {
                now.Name = setup.Name;
                now.AudioId = null;
                now.AudioName = null;
                setup.Layout = now;
            }
            setup.Hotkey = NextFreeHotkey();
            setup.Confirmed = true; // it's on screen now
            settings.Setups.Add(setup);
            AssignPadDirections(settings.Setups, settings.SafeSetupId);
            activeId = FindActiveSetup();
            RoomLog.Write("New room \"" + setup.Name + "\" from the screens on now: " + now.Summary);
            OptionsEdited();
            return setup;
        }

        /// <summary>"Desk", or "Desk 2" when that is taken; cut to the longest name allowed.</summary>
        string UniqueName(string name)
        {
            if (name.Length > ConfigManager.MaxRoomNameLength) name = name.Substring(0, ConfigManager.MaxRoomNameLength);
            string candidate = name;
            for (int n = 2; settings.Setups.Exists(s => string.Equals(s.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            {
                string suffix = " " + n;
                candidate = (name.Length + suffix.Length > ConfigManager.MaxRoomNameLength
                    ? name.Substring(0, ConfigManager.MaxRoomNameLength - suffix.Length) : name) + suffix;
            }
            return candidate;
        }

        /// <summary>Deletes a setup for good: its screens stay known, so it doesn't come back by itself.</summary>
        public void DeleteSetup(Setup setup)
        {
            if (setup == null || busy || !settings.Setups.Remove(setup)) return;
            if (settings.SafeSetupId == setup.Id)
            {
                var next = SafeSetup;
                settings.SafeSetupId = next != null ? next.Id : null;
            }
            if (pendingAudioSetup == setup) pendingAudioSetup = null;
            activeId = FindActiveSetup();
            RoomLog.Write("Deleted room \"" + setup.Name + "\".");
            OptionsEdited();
        }

        // ---------------------------------------------------------------- controller

        void OnPad(PadButton button)
        {
            if (gamepad == null) return;
            if (ConfirmForm.IsOpen)
            {
                if (button == PadButton.A) ConfirmForm.Answer(true);
                else if (button == PadButton.B) ConfirmForm.Answer(false);
                return;
            }
            if (button >= PadButton.RoomUp && button <= PadButton.RoomLeft)
            {
                if (!busy) SwitchByPad(Room.PadDirections[button - PadButton.RoomUp]);
                return;
            }
            if (button == PadButton.Combo)
            {
                if (RoomOverlay.PickerOpen) ClosePicker();
                else if (!busy) OpenPicker();
                return;
            }
            if (!RoomOverlay.PickerOpen)
            {
                gamepad.Navigating = busy; // the picker closed on its own
                return;
            }
            switch (button)
            {
                case PadButton.Up: RoomOverlay.MovePicker(-1, PickerMs); break;
                case PadButton.Down: RoomOverlay.MovePicker(1, PickerMs); break;
                case PadButton.B: ClosePicker(); break;
                case PadButton.A:
                    var setup = FindSetupById(RoomOverlay.PickerSelection as string);
                    ClosePicker();
                    if (setup == null) break;
                    if (setup.Id == activeId)
                    {
                        if (settings.RoomShowOverlay) RoomOverlay.ShowNote(setup.Name, SwitchNote(setup), NoteMs);
                        break;
                    }
                    SwitchTo(setup, setup == SafeSetup); // going home never asks, like the home hotkey
                    break;
            }
        }

        /// <summary>
        /// View + a D-pad direction: straight to the room on that direction, wherever you are. Made for the couch, where
        /// the TV is still blank and the picker would open on a screen you can't see.
        /// </summary>
        void SwitchByPad(string direction)
        {
            Setup setup = null;
            foreach (var s in settings.Setups) if (!s.Hidden && s.PadDirection == direction) setup = s;
            if (setup == null)
            {
                RoomLog.Write("Controller: no room on View + " + direction + ".");
                return;
            }
            if (setup.Id == activeId)
            {
                if (settings.RoomShowOverlay) RoomOverlay.ShowNote(setup.Name, SwitchNote(setup), NoteMs);
                return;
            }
            RoomLog.Write("Controller: View + " + direction + ", \"" + setup.Name + "\".");
            SwitchTo(setup, setup == SafeSetup); // going home never asks, like the home hotkey
        }

        /// <summary>
        /// Gives setups that never had one a controller direction: the safe setup Up (the way back to the desk), the
        /// others Right, Left, Down in the order of the Rooms tab. Directions already taken, or set to none, are left alone.
        /// </summary>
        internal static bool AssignPadDirections(IList<Setup> setups, string safeId)
        {
            var taken = new HashSet<string>();
            foreach (var r in setups) if (!string.IsNullOrEmpty(r.PadDirection)) taken.Add(r.PadDirection);
            var order = new List<Setup>();
            foreach (var r in setups) if (r.Id == safeId) order.Add(r);
            foreach (var r in setups) if (!order.Contains(r)) order.Add(r);

            bool changed = false;
            foreach (var r in order)
            {
                if (r.PadDirection != null || r.Hidden) continue;
                bool home = r.Id == safeId;
                var preference = home
                    ? new[] { Room.PadUp, Room.PadRight, Room.PadLeft, Room.PadDown }
                    : new[] { Room.PadRight, Room.PadLeft, Room.PadDown, Room.PadUp };
                r.PadDirection = "";
                foreach (var d in preference) if (taken.Add(d)) { r.PadDirection = d; break; }
                changed = true;
            }
            return changed;
        }

        /// <summary>"View + →" for a room's direction, or null.</summary>
        internal static string PadLabel(string direction)
        {
            switch (direction)
            {
                case Room.PadUp: return "View + ↑";
                case Room.PadRight: return "View + →";
                case Room.PadDown: return "View + ↓";
                case Room.PadLeft: return "View + ←";
                default: return null;
            }
        }

        void OpenPicker()
        {
            var items = new List<PickerItem>();
            int selected = -1;
            foreach (var s in Statuses())
            {
                if (s.Setup.Hidden) continue;
                var item = new PickerItem();
                item.Name = s.Setup.Name;
                item.Tag = s.Setup.Id;
                item.Note = s.Active ? "On now" : s.Connected ? "" : "Not connected";
                // Start on the first other screen that is there: the likely reason for opening the picker.
                if (selected < 0 && !s.Active && s.Connected) selected = items.Count;
                items.Add(item);
            }
            if (items.Count == 0) return;
            RoomLog.Write("Controller: room picker opened.");
            RoomOverlay.ShowPicker(items, Math.Max(0, selected), PickerMs);
            gamepad.Navigating = true;
        }

        void ClosePicker()
        {
            RoomOverlay.CloseAll();
            if (gamepad != null) gamepad.Navigating = false;
        }

        // ---------------------------------------------------------------- streaming

        /// <summary>
        /// A stream (Steam Link, Apollo, Sunshine) shows up as a new indirect display next to the physical ones, which
        /// otherwise sit there lit while nobody looks at them. Miracast mirrors a screen, so it doesn't count.
        /// </summary>
        internal static bool IsStreamTarget(TargetInfo t)
        {
            return t.Active && t.IsVirtual && t.OutputTechnology != DisplayConfigApi.OUTPUT_TECHNOLOGY_MIRACAST;
        }

        /// <summary>Called after every display change: turns the physical screens off when a stream starts, and back on after.</summary>
        void CheckStream()
        {
            bool streaming = false;
            var physical = new List<string>();
            foreach (var t in lastTargets)
            {
                if (IsStreamTarget(t)) streaming = true;
                else if (t.Active && !t.IsVirtual) physical.Add(t.Identity != null ? ModelKey(t.Identity.DevicePath) : null);
            }
            // Windows often does the switch itself (it remembers "only the stream's display" for this set of screens), so
            // Displicare can't wrap that in its own auto-hide care: it acts on the stream starting and ending instead.
            if (!wasStreaming && streaming && settings.RoomStreamScreensOff) StreamBegan();
            if (wasStreaming && !streaming && settings.RoomStreamScreensOff) AfterStream();
            wasStreaming = streaming;

            if (beforeStream != null)
            {
                if (streaming || busy) return; // still streaming; a screen turned back on by hand stays on
                var restore = beforeStream;
                beforeStream = null;
                if (SameScreens(restore, physical))
                {
                    RoomLog.Write("Stream ended; Windows already turned the screens back on.");
                    return;
                }
                RoomLog.Write("Stream ended; turning the screens back on.");
                RunLayout(restore, 10, delegate (ApplyResult r)
                {
                    if (!r.Success) RaiseNotify("Couldn't turn the screens back on", r.Message, true);
                    else AfterStream(); // count from when the screens are really back
                });
                return;
            }

            if (!streaming || physical.Count == 0 || !settings.RoomStreamScreensOff || busy)
            {
                if (streamTimer != null) streamTimer.Stop();
                return;
            }
            if (streamTimer == null)
            {
                streamTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
                streamTimer.Interval = StreamSettleTime;
                streamTimer.Tick += delegate
                {
                    streamTimer.Stop();
                    ScreensOffForStream();
                };
            }
            if (!streamTimer.IsEnabled) streamTimer.Start();
        }

        /// <summary>Turns taskbar auto-hide back on once Explorer has settled after a switch.</summary>
        void AutoHideLater()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
            timer.Interval = TimeSpan.FromSeconds(2.5);
            timer.Tick += delegate
            {
                timer.Stop();
                Taskbar.AutoHide = true;
                RoomLog.Write("Taskbar auto-hide back on.");
            };
            timer.Start();
        }

        /// <summary>
        /// A stream started. When it ends, Windows brings several screens back at once, and with auto-hide on Explorer then
        /// says "A toolbar is already hidden on this side of your screen" and turns auto-hide off. That return can't be
        /// wrapped (Windows may do it), so auto-hide goes off now, while the stream's display is the only screen, and comes
        /// back in <see cref="AfterStream"/>.
        /// </summary>
        void StreamBegan()
        {
            if (autoHideAfterStream || !Taskbar.AutoHide) return;
            Taskbar.AutoHide = false;
            autoHideAfterStream = true;
            RoomLog.Write("Stream started; taskbar auto-hide off until it ends.");
        }

        /// <summary>
        /// The stream ended: once the screens are back and Steam has brought its window up, auto-hide goes back on and every
        /// window is minimized, so nothing sits still on an OLED. Called again while waiting, it doesn't start a second wait.
        /// </summary>
        void AfterStream()
        {
            if (afterStreamPending) return;
            afterStreamPending = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(MinimizeDelay);
                for (int i = 0; i < 16 && !SteamWindowShown(); i++) Thread.Sleep(500); // Steam's window, if it comes
                Thread.Sleep(500);
                dispatcher.BeginInvoke((Action)delegate
                {
                    afterStreamPending = false;
                    if (hotkeys == null || StreamOn()) return; // switched off, or a new stream began
                    if (busy)
                    {
                        AfterStream(); // the screens are still being put back; wait for that
                        return;
                    }
                    if (autoHideAfterStream)
                    {
                        autoHideAfterStream = false;
                        Taskbar.AutoHide = true;
                        RoomLog.Write("Stream ended; taskbar auto-hide back on.");
                    }
                    MinimizeAll("Stream ended");
                });
            });
        }

        static void MinimizeAll(string why)
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(type);
                try { type.InvokeMember("MinimizeAll", System.Reflection.BindingFlags.InvokeMethod, null, shell, null); }
                finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
                RoomLog.Write(why + "; minimized all windows.");
            }
            catch (Exception ex) { RoomLog.Write("Couldn't minimize the windows: " + ex.Message); }
        }

        /// <summary>
        /// Closing Big Picture (or Xbox mode) brings the launcher's own desktop window back up, on the screen you just
        /// switched to: for an OLED, a still window that stays there. Wait for Steam's window to show (Big Picture takes a
        /// few seconds to close), then minimize everything, as after a stream.
        /// </summary>
        void MinimizeAfterLauncher(string closed)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(2000);
                if (closed == Room.LaunchBigPicture)
                {
                    for (int i = 0; i < 24 && !SteamWindowShown(); i++) Thread.Sleep(500);
                    Thread.Sleep(500); // let it finish drawing, or it can come back over the desktop
                }
                else Thread.Sleep(1500);
                dispatcher.BeginInvoke((Action)delegate
                {
                    if (hotkeys == null || busy) return; // switched off, or switching again
                    MinimizeAll(closed == Room.LaunchBigPicture ? "Big Picture closed" : "Xbox mode closed");
                });
            });
        }

        /// <summary>Steam's desktop window ("Steam") is up and Big Picture's is gone.</summary>
        static bool SteamWindowShown()
        {
            IntPtr big = FindWindow(null, "Steam Big Picture Mode");
            if (big != IntPtr.Zero && IsWindowVisible(big)) return false;
            IntPtr main = FindWindow(null, "Steam");
            return main != IntPtr.Zero && IsWindowVisible(main);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr FindWindow(string className, string windowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        bool StreamOn()
        {
            foreach (var t in lastTargets) if (IsStreamTarget(t)) return true;
            return false;
        }

        void RememberLayout()
        {
            bool physical = false;
            foreach (var t in lastTargets) if (t.Active && !t.IsVirtual) physical = true;
            if (!physical) return;
            try { lastLayout = LayoutEngine.Capture("Before streaming", false); }
            catch (Exception ex) { RoomLog.Write("Couldn't record the layout: " + ex.Message); }
        }

        /// <summary>Whether the screens on now are the ones in <paramref name="layout"/> (by model).</summary>
        internal static bool SameScreens(Profile layout, IList<string> activeKeys)
        {
            var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in layout.Displays) want.Add(d.Identity != null ? ModelKey(d.Identity.DevicePath) ?? "" : "");
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in activeKeys) have.Add(k ?? "");
            return have.SetEquals(want);
        }

        void ScreensOffForStream()
        {
            if (hotkeys == null || busy || beforeStream != null || !settings.RoomStreamScreensOff) return;
            ActiveDisplay stream = null;
            bool physical = false;
            try
            {
                lastTargets = DisplayTopology.DescribeTargets(DisplayTopology.Query(DisplayConfigApi.QDC_ALL_PATHS));
                foreach (var t in lastTargets) if (t.Active && !t.IsVirtual) physical = true;
                foreach (var d in DisplayTopology.GetActiveDisplays())
                    if (d.IsVirtual && d.OutputTechnology != DisplayConfigApi.OUTPUT_TECHNOLOGY_MIRACAST) stream = d;
                if (stream == null || !physical) return; // the stream ended, or the screens are off already
                // Not the layout now: when the stream's display arrives, Windows or Steam may already have turned a screen off.
                beforeStream = lastLayout ?? LayoutEngine.Capture("Before streaming", false);
            }
            catch (Exception ex)
            {
                RoomLog.Write("Couldn't prepare for the stream: " + ex.Message);
                return;
            }

            // The stream's own display, as it is now (the client already chose its resolution), alone and primary.
            var display = new ProfileDisplay();
            display.Identity = stream.Identity;
            display.Primary = true;
            display.Width = stream.Width;
            display.Height = stream.Height;
            display.RefreshHz = stream.RefreshHz;
            display.Hdr = -1; // leave HDR to the stream
            var profile = new Profile();
            profile.Name = "Streaming";
            profile.Displays.Add(display);

            string name = stream.Identity != null ? stream.Identity.DisplayName : "the stream";
            RoomLog.Write("Stream to " + name + " started; turning the other screens off.");
            RunLayout(profile, 0, delegate (ApplyResult r)
            {
                if (!r.Success)
                {
                    beforeStream = null;
                    RaiseNotify("Couldn't turn the screens off for the stream", r.Message, true);
                }
                else if (settings.RoomShowOverlay)
                    RoomOverlay.ShowNote("Streaming to " + name, "Your screens are off and come back when the stream ends", NoteMs);
            });
        }

        /// <summary>Applies a layout on its own thread (it can wait for screens), then reports on the UI thread.</summary>
        void RunLayout(Profile profile, int waitSeconds, Action<ApplyResult> done)
        {
            busy = true;
            RaiseChanged();
            var thread = new Thread(delegate ()
            {
                ApplyResult result;
                // Several screens appearing or going at once trip Explorer's "only one hidden toolbar per side" with
                // auto-hide on (see Taskbar); keep it off for the change and give Explorer a moment before turning it back on.
                bool autoHide = Taskbar.AutoHide;
                if (autoHide)
                {
                    Taskbar.AutoHide = false;
                    Thread.Sleep(300);
                }
                try { result = LayoutEngine.Apply(profile, waitSeconds); }
                catch (Exception ex)
                {
                    RoomLog.Write("Layout failed: " + ex);
                    result = new ApplyResult();
                    result.Message = ex.Message;
                }
                if (autoHide)
                {
                    Thread.Sleep(2500);
                    Taskbar.AutoHide = true;
                    RoomLog.Write("Taskbar auto-hide back on.");
                }
                dispatcher.BeginInvoke((Action)delegate
                {
                    busy = false;
                    lastSwitchUtc = DateTime.UtcNow;
                    done(result);
                    try
                    {
                        lastTargets = DisplayTopology.DescribeTargets(DisplayTopology.Query(DisplayConfigApi.QDC_ALL_PATHS));
                        activeId = FindActiveSetup();
                        CheckStream(); // the stream may have ended meanwhile
                        if (!StreamOn() && !busy) RememberLayout();
                    }
                    catch (Exception) { }
                    RaiseChanged();
                });
            });
            thread.IsBackground = true;
            thread.Name = "Displicare layout";
            thread.Start();
        }

        // ---------------------------------------------------------------- hotkeys and commands

        public void RegisterHotkeys()
        {
            if (hotkeys == null) return;
            hotkeys.UnregisterAll();
            hotkeySetups.Clear();
            var failed = new List<string>();
            if (!string.IsNullOrEmpty(settings.RoomHomeHotkey) && !hotkeys.Register(HomeHotkeyId, settings.RoomHomeHotkey))
                failed.Add(settings.RoomHomeHotkey + " (home)");
            foreach (var s in settings.Setups)
            {
                if (s.Hidden || string.IsNullOrEmpty(s.Hotkey)) continue;
                int id = FirstRoomHotkeyId + hotkeySetups.Count;
                hotkeySetups.Add(s.Id);
                if (!hotkeys.Register(id, s.Hotkey)) failed.Add(s.Hotkey + " (" + s.Name + ")");
            }
            var fresh = new List<string>();
            foreach (var f in failed) if (reportedHotkeys.Add(f)) fresh.Add(f);
            if (fresh.Count > 0)
            {
                RoomLog.Write("Hotkeys taken by another app: " + string.Join(", ", fresh.ToArray()));
                RaiseNotify("Some room hotkeys are taken", string.Join(", ", fresh.ToArray()), true);
            }
        }

        void OnHotkey(int id)
        {
            if (id == HomeHotkeyId)
            {
                SwitchTo(SafeSetup, true); // the way back to a known screen: never asks
                return;
            }
            int index = id - FirstRoomHotkeyId;
            if (index >= 0 && index < hotkeySetups.Count) SwitchTo(FindSetupById(hotkeySetups[index]), false);
        }

        /// <summary>"Displicare.exe --room Living room" from a second instance (Steam shortcut, Stream Deck).</summary>
        void OnCommand(string[] args)
        {
            if (args.Length < 2 || args[0] != "room") return;
            var setup = FindSetup(string.Join(" ", args, 1, args.Length - 1));
            if (setup == null) RaiseNotify("No room called \"" + string.Join(" ", args, 1, args.Length - 1) + "\"", "", true);
            else SwitchTo(setup, false);
        }

        /// <summary>Hands "--room" to the running Displicare. False when it isn't running or has rooms off.</summary>
        public static bool SendRoomCommand(string room)
        {
            return CommandPipe.Send(new[] { "room", room });
        }

        void RaiseChanged()
        {
            if (Changed != null) Changed();
        }

        void RaiseNotify(string title, string text, bool warning)
        {
            if (Notify != null) Notify(title, text, warning);
        }
    }
}
