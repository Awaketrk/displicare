using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace Displicare.Rooms
{
    /// <summary>What the Rooms tab and window show for one room.</summary>
    internal sealed class RoomStatus
    {
        public Room Room;
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
    }

    /// <summary>
    /// Room switching: finds a room for every connected display, switches to one (only that display on, as the main
    /// screen, with its mode, HDR and sound), asks to keep new layouts, and learns the settings used in each room.
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
        readonly List<string> hotkeyRooms = new List<string>();
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
        DispatcherTimer minimizeTimer;
        List<TargetInfo> lastTargets = new List<TargetInfo>();
        string activeKey;
        bool busy;
        DateTime lastSwitchUtc = DateTime.MinValue;
        Room pendingAudioRoom;
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
        /// Rooms learned before "Native" / "Highest" were kept as defaults (2.2.1 test builds) hold the exact mode; turn it
        /// back into the defaults from the modes the room offered. Same mode, so it doesn't ask to keep again.
        /// </summary>
        void TidyLearnedModes()
        {
            bool changed = false;
            foreach (var room in settings.Rooms)
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
                RoomLog.Write("\"" + room.Name + "\" now uses " + RoomsView.Summary(room));
                changed = true;
            }
            if (changed) save();
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
            pendingAudioRoom = null;
            ApplyController();
            if (streamTimer != null) streamTimer.Stop();
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
            activeKey = ActiveKey(lastTargets);
            // 2.3.2 test builds took a streaming display (SudoVDA reports a connector like HDMI) for a screen of its own.
            int stray = settings.Rooms.RemoveAll(r => DisplayConfigApi.IsVirtualAdapter(r.AdapterPath));
            if (stray > 0)
            {
                RoomLog.Write("Removed " + stray + " room(s) for virtual displays.");
                save();
            }
            bool added = false;
            foreach (var t in lastTargets)
            {
                if (!t.Available || t.IsVirtual || t.Identity == null) continue;
                string key = ModelKey(t.Identity.DevicePath);
                if (key == null) continue;
                var room = FindByKey(key);
                if (room == null)
                {
                    room = new Room();
                    room.Key = key;
                    room.Name = t.Identity.DisplayName;
                    var taken = new List<string>();
                    taken.Add(settings.RoomHomeHotkey);
                    foreach (var r in settings.Rooms) taken.Add(r.Hotkey);
                    room.Hotkey = Hotkey.NextFree(taken) ?? "";
                    settings.Rooms.Add(room);
                    added = true;
                    RoomLog.Write("New room \"" + room.Name + "\" (" + key + ")");
                }
                room.DisplayName = t.Identity.FriendlyName;
                room.DevicePath = t.Identity.DevicePath;
                room.AdapterPath = t.Identity.AdapterPath;
                room.EdidManufacturer = t.Identity.EdidManufacturer;
                room.EdidProduct = t.Identity.EdidProduct;
                room.ConnectorInstance = (int)t.Identity.ConnectorInstance;
            }
            if (added)
            {
                // The screen in use when rooms were first set up is the natural way home.
                if (string.IsNullOrEmpty(settings.RoomHomeKey) || FindByKey(settings.RoomHomeKey) == null)
                    settings.RoomHomeKey = activeKey ?? settings.Rooms[0].Key;
                save();
            }
            if (AssignPadDirections(settings.Rooms, settings.RoomHomeKey))
            {
                foreach (var r in settings.Rooms)
                    if (!string.IsNullOrEmpty(r.PadDirection)) RoomLog.Write("\"" + r.Name + "\" is on " + PadLabel(r.PadDirection) + ".");
                save();
            }
            LogScreensComingAndGoing();
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

        /// <summary>Key of the room on screen: exactly one physical display active (virtual ones don't count).</summary>
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

        public Room FindByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var r in settings.Rooms) if (string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        /// <summary>A room by name ("Living room"), model key or a unique part of its name ("living", "lg").</summary>
        public Room FindRoom(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            foreach (var r in settings.Rooms) if (string.Equals(r.Name, text, StringComparison.OrdinalIgnoreCase)) return r;
            var byKey = FindByKey(text);
            if (byKey != null) return byKey;
            Room hit = null;
            foreach (var r in settings.Rooms)
            {
                bool match = (r.Name ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                    || (r.DisplayName ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match) continue;
                if (hit != null) return null;
                hit = r;
            }
            return hit;
        }

        public Room HomeRoom
        {
            get
            {
                var home = FindByKey(settings.RoomHomeKey);
                if (home != null) return home;
                foreach (var r in settings.Rooms) if (!r.Hidden) return r;
                return null;
            }
        }

        public List<RoomStatus> Statuses()
        {
            var list = new List<RoomStatus>();
            foreach (var room in settings.Rooms)
            {
                var s = new RoomStatus();
                s.Room = room;
                s.Active = string.Equals(room.Key, activeKey, StringComparison.OrdinalIgnoreCase);
                foreach (var t in lastTargets)
                {
                    if (!t.Available || t.Identity == null || !string.Equals(ModelKey(t.Identity.DevicePath), room.Key, StringComparison.OrdinalIgnoreCase)) continue;
                    s.Connected = true;
                    s.Connection = DisplayConfigApi.OutputName(t.OutputTechnology) + " · " + GpuName(t.Identity.AdapterPath);
                }
                byte[] edid = EdidFor(room);
                s.Native = Edid.Preferred(edid);
                s.HdrCapable = Edid.SupportsHdr(edid);
                // Once the room has been on screen, only offer what Windows actually allowed on this connection.
                s.ModesFromEdid = room.KnownModes.Count == 0;
                s.Modes = MergeModes(room.KnownModes, s.ModesFromEdid ? Edid.Modes(edid) : null);
                list.Add(s);
            }
            return list;
        }

        byte[] EdidFor(Room room)
        {
            if (string.IsNullOrEmpty(room.DevicePath)) return null;
            byte[] edid;
            if (!edidCache.TryGetValue(room.DevicePath, out edid))
            {
                edid = Edid.Read(room.DevicePath);
                edidCache[room.DevicePath] = edid;
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
        /// Remembers the mode and HDR in use in the room on screen, and which modes it offers. Runs after display changes
        /// and right before switching away, so a setting changed in Windows is kept for next time. Sound is learned from
        /// the user's own default-output changes instead (<see cref="OnDefaultAudioChanged"/>).
        /// </summary>
        void Learn()
        {
            var room = FindByKey(activeKey);
            if (room == null) return;
            ActiveDisplay display = null;
            foreach (var d in DisplayTopology.GetActiveDisplays()) if (!d.IsVirtual) display = d;
            if (display == null) return;

            bool changed = false;
            int width = display.Width, height = display.Height, hz = (int)Math.Round(display.RefreshHz);
            var offered = display.GdiName != null ? LayoutEngine.ListModes(display.GdiName) : null;
            if (offered != null) AsDefaults(offered, Edid.Preferred(EdidFor(room)), ref width, ref height, ref hz);
            if (room.Width != width || room.Height != height || room.RefreshHz != hz)
            {
                room.Width = width;
                room.Height = height;
                room.RefreshHz = hz;
                changed = true;
            }
            if (display.Hdr >= 0)
            {
                // HDR on keeps "auto"; turning it off in Windows is remembered as off.
                string hdr = display.Hdr == 1 ? (room.Hdr == Room.HdrOff ? Room.HdrOn : room.Hdr) : Room.HdrOff;
                if (room.Hdr != hdr) { room.Hdr = hdr; changed = true; }
            }
            if (offered != null)
            {
                var known = ModesToRemember(offered);
                if (known.Count > 0 && string.Join(",", known.ToArray()) != string.Join(",", room.KnownModes.ToArray()))
                {
                    room.KnownModes = known;
                    changed = true;
                }
            }
            if (changed)
            {
                RoomLog.Write("Learned \"" + room.Name + "\": " + RoomsView.Summary(room));
                save();
            }
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

        /// <summary>Sets the room's sound now if its output is there, else as soon as it appears (up to a minute).</summary>
        void BeginRoomAudio(Room room)
        {
            pendingAudioRoom = null;
            if (room.Audio == Room.AudioKeep) return;
            if (TrySetRoomAudio(room)) return;
            pendingAudioRoom = room;
            pendingAudioUntil = DateTime.UtcNow + AudioWaitTime;
            RoomLog.Write("Waiting for the sound output of \"" + room.Name + "\"");
        }

        bool TrySetRoomAudio(Room room)
        {
            List<AudioOutput> outputs;
            try { outputs = AudioOutputs.List(); }
            catch (Exception ex) { RoomLog.Write("Couldn't list sound outputs: " + ex.Message); return false; }
            int index = room.Audio == Room.AudioAuto
                ? AudioOutputs.ForDisplay(room.DisplayName, outputs)
                : AudioOutputs.Match(room.AudioId, room.AudioName, outputs);
            if (index < 0) return false;
            if (outputs[index].IsDefault) return true;
            ignoreAudioChangesUntil = DateTime.UtcNow.AddSeconds(5);
            if (AudioOutputs.SetDefault(outputs[index].Id)) RoomLog.Write("Sound output: " + outputs[index].Name);
            else RoomLog.Write("Couldn't switch sound to " + outputs[index].Name);
            return true;
        }

        void OnAudioOutputsChanged()
        {
            if (pendingAudioRoom == null) return;
            if (DateTime.UtcNow > pendingAudioUntil)
            {
                RoomLog.Write("The sound output of \"" + pendingAudioRoom.Name + "\" never appeared; sound stays where it was.");
                pendingAudioRoom = null;
                return;
            }
            if (TrySetRoomAudio(pendingAudioRoom)) pendingAudioRoom = null;
        }

        /// <summary>The user picked another output while a room is on screen: that room uses it from now on.</summary>
        void OnDefaultAudioChanged(string id)
        {
            if (busy || DateTime.UtcNow < ignoreAudioChangesUntil || hotkeys == null) return;
            var room = FindByKey(activeKey);
            if (room == null || room.Audio == Room.AudioKeep) return;
            List<AudioOutput> outputs;
            try { outputs = AudioOutputs.List(); }
            catch (Exception) { return; }
            AudioOutput chosen = null;
            foreach (var o in outputs) if (o.Id == id) chosen = o;
            if (chosen == null) return;

            int own = AudioOutputs.ForDisplay(room.DisplayName, outputs);
            bool changed;
            if (own >= 0 && outputs[own].Id == id)
            {
                changed = room.Audio != Room.AudioAuto;
                room.Audio = Room.AudioAuto;
            }
            else
            {
                changed = room.Audio != Room.AudioDevice || room.AudioId != id;
                room.Audio = Room.AudioDevice;
                room.AudioId = id;
                room.AudioName = chosen.Name;
            }
            if (pendingAudioRoom == room) pendingAudioRoom = null; // the user chose; stop waiting
            if (!changed) return;
            RoomLog.Write("Learned \"" + room.Name + "\" sound: " + (room.Audio == Room.AudioAuto ? "its own (" + chosen.Name + ")" : chosen.Name));
            save();
            RaiseChanged();
        }

        // ---------------------------------------------------------------- switching

        /// <summary>
        /// Makes the room's display the only one. Asks to keep it unless it's the home hotkey or this room was kept
        /// before (per the "ask" setting), and switches back when nobody answers.
        /// </summary>
        public void SwitchTo(Room room, bool home)
        {
            if (room == null || busy) return;
            if (CanSwitch != null && !CanSwitch())
            {
                RaiseNotify("Can't switch rooms right now", "Finish the hardware test first.", true);
                return;
            }
            try
            {
                lastTargets = DisplayTopology.DescribeTargets(DisplayTopology.Query(DisplayConfigApi.QDC_ALL_PATHS));
                activeKey = ActiveKey(lastTargets);
                Learn(); // keep what was used in the room we're leaving
            }
            catch (Exception ex) { RoomLog.Write("Couldn't read the current room: " + ex.Message); }

            var leaving = FindByKey(activeKey);
            var native = Edid.Preferred(EdidFor(room));
            var profile = BuildProfile(room, native);
            bool ask = !home && (settings.RoomAskToKeep == AppSettings.RoomAskAlways
                || (settings.RoomAskToKeep == AppSettings.RoomAskNew && !room.Confirmed));
            bool pad = gamepad != null && gamepad.Connected;
            if (pad && ask) gamepad.Navigating = true; // A and B answer the prompt
            busy = true;
            pendingAudioRoom = null;
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
                    if (result.Success) dispatcher.Invoke((Action)delegate { BeginRoomAudio(room); });
                    if (result.Success && ask)
                    {
                        asked = true;
                        bool kept = ConfirmForm.Ask(room.Name, ConfirmSeconds, pad);
                        RoomLog.Write(kept ? "Kept \"" + room.Name + "\"." : "\"" + room.Name + "\" not kept; switching back.");
                        if (!kept)
                        {
                            dispatcher.Invoke((Action)delegate { pendingAudioRoom = null; });
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
                    Finish(room, leaving, result, asked);
                    if (autoHide) AutoHideLater();
                });
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "Displicare room switch";
            thread.Start();
        }

        void Finish(Room room, Room leaving, ApplyResult result, bool asked)
        {
            busy = false;
            if (gamepad != null) gamepad.Navigating = false;
            lastSwitchUtc = DateTime.UtcNow;
            // Going back restores the old output too; that isn't the user picking one.
            ignoreAudioChangesUntil = DateTime.UtcNow.AddSeconds(5);
            if (result.Success && !result.Reverted)
            {
                if (asked && !room.Confirmed)
                {
                    room.Confirmed = true;
                    save();
                }
                StartLauncher(room, leaving);
                if (result.Warnings.Count > 0) RaiseNotify("Switched to " + room.Name, string.Join("\n", result.Warnings.ToArray()), true);
                if (settings.RoomShowOverlay) RoomOverlay.ShowNote(room.Name, SwitchNote(room), NoteMs);
            }
            else if (result.Reverted) RaiseNotify(room.Name + " wasn't kept", result.Message, false);
            else RaiseNotify("Couldn't switch to " + room.Name, result.Message, true);

            try
            {
                lastTargets = DisplayTopology.DescribeTargets(DisplayTopology.Query(DisplayConfigApi.QDC_ALL_PATHS));
                activeKey = ActiveKey(lastTargets);
                CheckStream(); // a stream that started during the switch
                if (!StreamOn()) RememberLayout();
            }
            catch (Exception) { }
            RaiseChanged();
        }

        /// <summary>"3840 × 2160 · 120 Hz · HDR · LG TV SSCR2": what the room's screen and sound are now.</summary>
        string SwitchNote(Room room)
        {
            var parts = new List<string>();
            try
            {
                foreach (var d in DisplayTopology.GetActiveDisplays())
                {
                    if (d.IsVirtual) continue;
                    parts.Add(d.Width + " × " + d.Height);
                    parts.Add(Math.Round(d.RefreshHz) + " Hz");
                    if (d.Hdr == 1) parts.Add("HDR");
                    break;
                }
                if (pendingAudioRoom == room) parts.Add("sound when the TV is ready");
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

        /// <summary>The one-display layout for a room. Sound is handled separately (<see cref="BeginRoomAudio"/>).</summary>
        internal static Profile BuildProfile(Room room, DisplayMode? native)
        {
            var id = new DisplayIdentity();
            id.DevicePath = room.DevicePath;
            id.FriendlyName = room.DisplayName;
            id.EdidManufacturer = (ushort)room.EdidManufacturer;
            id.EdidProduct = (ushort)room.EdidProduct;
            id.ConnectorInstance = (uint)room.ConnectorInstance;
            id.AdapterPath = room.AdapterPath;

            var display = new ProfileDisplay();
            display.Identity = id;
            display.Primary = true;
            display.Width = room.Width;
            display.Height = room.Height;
            display.RefreshHz = room.RefreshHz;
            display.Hdr = room.Hdr == Room.HdrOn ? 1 : room.Hdr == Room.HdrOff ? 0 : 2;
            if (native != null)
            {
                display.NativeWidth = native.Value.Width;
                display.NativeHeight = native.Value.Height;
            }

            var profile = new Profile();
            profile.Name = room.Name;
            profile.Displays.Add(display);
            return profile;
        }

        /// <summary>
        /// Opens the room's launcher (Steam Big Picture or Xbox mode) and closes the one the room left behind used, unless
        /// both rooms use the same. Xbox mode waits for Windows to confirm the change, so it runs off the UI thread.
        /// </summary>
        void StartLauncher(Room room, Room leaving)
        {
            string open = room.Launcher ?? Room.LaunchNone;
            string close = leaving != null && leaving != room ? leaving.Launcher ?? Room.LaunchNone : Room.LaunchNone;
            if (close == open) close = Room.LaunchNone;

            if (close == Room.LaunchBigPicture) OpenUrl("steam://close/bigpicture");
            if (open == Room.LaunchBigPicture) OpenUrl("steam://open/bigpicture");
            if (open == Room.LaunchXbox || close == Room.LaunchXbox)
            {
                bool on = open == Room.LaunchXbox;
                string name = room.Name;
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

        /// <summary>
        /// Settings changed in the tab or window. Only a new resolution or refresh rate can leave a screen black, so only
        /// that makes the next switch ask again; names, sound, HDR and hotkeys don't.
        /// </summary>
        public void RoomEdited(Room room, bool modeChanged)
        {
            if (room != null && modeChanged) room.Confirmed = false;
            save();
            RegisterHotkeys();
            ApplyController();
            if (!settings.RoomStreamScreensOff && streamTimer != null) streamTimer.Stop();
            RaiseChanged();
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
                    var room = FindByKey(RoomOverlay.PickerSelection as string);
                    ClosePicker();
                    if (room == null) break;
                    if (string.Equals(room.Key, activeKey, StringComparison.OrdinalIgnoreCase))
                    {
                        if (settings.RoomShowOverlay) RoomOverlay.ShowNote(room.Name, SwitchNote(room), NoteMs);
                        break;
                    }
                    SwitchTo(room, room == HomeRoom); // going home never asks, like the home hotkey
                    break;
            }
        }

        /// <summary>
        /// View + a D-pad direction: straight to the room on that direction, wherever you are. Made for the couch, where
        /// the TV is still blank and the picker would open on a screen you can't see.
        /// </summary>
        void SwitchByPad(string direction)
        {
            Room room = null;
            foreach (var r in settings.Rooms) if (!r.Hidden && r.PadDirection == direction) room = r;
            if (room == null)
            {
                RoomLog.Write("Controller: no room on View + " + direction + ".");
                return;
            }
            if (string.Equals(room.Key, activeKey, StringComparison.OrdinalIgnoreCase))
            {
                if (settings.RoomShowOverlay) RoomOverlay.ShowNote(room.Name, SwitchNote(room), NoteMs);
                return;
            }
            RoomLog.Write("Controller: View + " + direction + ", \"" + room.Name + "\".");
            SwitchTo(room, room == HomeRoom); // going home never asks, like the home hotkey
        }

        /// <summary>
        /// Gives rooms that never had one a controller direction: the home room Up (the way back to the desk), the others
        /// Right, Left, Down in the order of the Rooms tab. Directions already taken, or set to none, are left alone.
        /// </summary>
        internal static bool AssignPadDirections(IList<Room> rooms, string homeKey)
        {
            var taken = new HashSet<string>();
            foreach (var r in rooms) if (!string.IsNullOrEmpty(r.PadDirection)) taken.Add(r.PadDirection);
            var order = new List<Room>();
            foreach (var r in rooms) if (string.Equals(r.Key, homeKey, StringComparison.OrdinalIgnoreCase)) order.Add(r);
            foreach (var r in rooms) if (!order.Contains(r)) order.Add(r);

            bool changed = false;
            foreach (var r in order)
            {
                if (r.PadDirection != null || r.Hidden) continue;
                bool home = string.Equals(r.Key, homeKey, StringComparison.OrdinalIgnoreCase);
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
                if (s.Room.Hidden) continue;
                var item = new PickerItem();
                item.Name = s.Room.Name;
                item.Tag = s.Room.Key;
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
            // Whoever turned the screens off (Displicare, or Windows remembering the last stream), they come back with
            // whatever was open, Steam's window first; minimize it all so nothing sits still on an OLED.
            if (wasStreaming && !streaming && settings.RoomStreamScreensOff) ScheduleMinimize();
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
                    else ScheduleMinimize(); // count from when the screens are really back
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

        /// <summary>Minimizes every window a few seconds from now (Steam brings its window up as the stream closes).</summary>
        void ScheduleMinimize()
        {
            if (minimizeTimer == null)
            {
                minimizeTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
                minimizeTimer.Interval = MinimizeDelay;
                minimizeTimer.Tick += delegate
                {
                    minimizeTimer.Stop();
                    if (hotkeys == null || StreamOn()) return; // switched off, or a new stream began
                    try
                    {
                        var type = Type.GetTypeFromProgID("Shell.Application");
                        object shell = Activator.CreateInstance(type);
                        try { type.InvokeMember("MinimizeAll", System.Reflection.BindingFlags.InvokeMethod, null, shell, null); }
                        finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
                        RoomLog.Write("Stream ended; minimized all windows.");
                    }
                    catch (Exception ex) { RoomLog.Write("Couldn't minimize the windows: " + ex.Message); }
                };
            }
            minimizeTimer.Stop();
            minimizeTimer.Start();
        }

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
                        activeKey = ActiveKey(lastTargets);
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
            hotkeyRooms.Clear();
            var failed = new List<string>();
            if (!string.IsNullOrEmpty(settings.RoomHomeHotkey) && !hotkeys.Register(HomeHotkeyId, settings.RoomHomeHotkey))
                failed.Add(settings.RoomHomeHotkey + " (home)");
            foreach (var r in settings.Rooms)
            {
                if (r.Hidden || string.IsNullOrEmpty(r.Hotkey)) continue;
                int id = FirstRoomHotkeyId + hotkeyRooms.Count;
                hotkeyRooms.Add(r.Key);
                if (!hotkeys.Register(id, r.Hotkey)) failed.Add(r.Hotkey + " (" + r.Name + ")");
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
                SwitchTo(HomeRoom, true); // the way back to a known screen: never asks
                return;
            }
            int index = id - FirstRoomHotkeyId;
            if (index >= 0 && index < hotkeyRooms.Count) SwitchTo(FindByKey(hotkeyRooms[index]), false);
        }

        /// <summary>"Displicare.exe --room Living room" from a second instance (Steam shortcut, Stream Deck).</summary>
        void OnCommand(string[] args)
        {
            if (args.Length < 2 || args[0] != "room") return;
            var room = FindRoom(string.Join(" ", args, 1, args.Length - 1));
            if (room == null) RaiseNotify("No room called \"" + string.Join(" ", args, 1, args.Length - 1) + "\"", "", true);
            else SwitchTo(room, false);
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
