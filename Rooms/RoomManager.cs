using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace Displicare.Rooms
{
    /// <summary>What the Rooms tab shows for one room.</summary>
    internal sealed class RoomStatus
    {
        public Room Room;
        public bool Connected;
        public bool Active;
        /// <summary>e.g. "HDMI · NVIDIA".</summary>
        public string Connection;
        /// <summary>Modes for the drop-downs: the ones Windows offered while the room was active, plus the EDID's.</summary>
        public List<DisplayMode> Modes;
        public bool HdrCapable;
    }

    /// <summary>
    /// Room switching: finds a room for every connected display, switches to one (only that display on, as the main
    /// screen, with its mode, HDR and sound), asks to keep new layouts, and learns the settings used in each room.
    /// Lives on the UI thread; switches run on their own STA thread so the prompt and the 10 s wait for a waking TV
    /// never block Displicare. Nothing runs while idle: it reacts to display changes, hotkeys and pipe commands.
    /// </summary>
    internal sealed class RoomManager : IDisposable
    {
        const int HomeHotkeyId = 1;
        const int FirstRoomHotkeyId = 100;
        const int ConfirmSeconds = 15;
        /// <summary>Right after a switch the TV's sound output and HDR are still settling; don't learn from that.</summary>
        static readonly TimeSpan LearnQuietTime = TimeSpan.FromSeconds(20);

        readonly AppSettings settings;
        readonly Action save;
        readonly Dispatcher dispatcher;
        readonly List<string> hotkeyRooms = new List<string>();
        readonly HashSet<string> reportedHotkeys = new HashSet<string>();
        readonly Dictionary<string, byte[]> edidCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        HotkeyWindow hotkeys;
        CommandPipe pipe;
        List<TargetInfo> lastTargets = new List<TargetInfo>();
        string activeKey;
        bool busy;
        DateTime lastSwitchUtc = DateTime.MinValue;

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

        public void Start()
        {
            if (hotkeys != null) return;
            hotkeys = new HotkeyWindow(OnHotkey);
            pipe = new CommandPipe(OnCommand);
            RoomLog.Write("Room switching on.");
            Refresh();
        }

        public void Stop()
        {
            if (hotkeys == null) return;
            hotkeys.Dispose();
            hotkeys = null;
            pipe.Dispose();
            pipe = null;
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
                if (DateTime.UtcNow - lastSwitchUtc > LearnQuietTime) Learn();
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
                s.Modes = MergeModes(room.KnownModes, Edid.Modes(edid));
                s.HdrCapable = Edid.SupportsHdr(edid);
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
            all.Sort(delegate (DisplayMode a, DisplayMode b)
            {
                long pa = (long)a.Width * a.Height, pb = (long)b.Width * b.Height;
                return pa != pb ? pb.CompareTo(pa) : b.Hz.CompareTo(a.Hz);
            });
            return all;
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
        /// Remembers what is in use in the room on screen (mode, HDR, sound) and which modes it offers. Runs after display
        /// changes and right before switching away, so a setting changed in Windows is kept for next time.
        /// </summary>
        void Learn()
        {
            var room = FindByKey(activeKey);
            if (room == null) return;
            ActiveDisplay display = null;
            foreach (var d in DisplayTopology.GetActiveDisplays()) if (!d.IsVirtual) display = d;
            if (display == null) return;

            bool changed = false;
            int hz = (int)Math.Round(display.RefreshHz);
            if (room.Width != display.Width || room.Height != display.Height || room.RefreshHz != hz)
            {
                room.Width = display.Width;
                room.Height = display.Height;
                room.RefreshHz = hz;
                changed = true;
            }
            if (display.Hdr >= 0)
            {
                // HDR on keeps "auto"; turning it off in Windows is remembered as off.
                string hdr = display.Hdr == 1 ? (room.Hdr == Room.HdrOff ? Room.HdrOn : room.Hdr) : Room.HdrOff;
                if (room.Hdr != hdr) { room.Hdr = hdr; changed = true; }
            }
            changed |= LearnAudio(room);

            if (display.GdiName != null)
            {
                var known = new List<string>();
                foreach (var m in LayoutEngine.ListModes(display.GdiName)) if (known.Count < 64) known.Add(FormatMode(m));
                if (known.Count > 0 && string.Join(",", known.ToArray()) != string.Join(",", room.KnownModes.ToArray()))
                {
                    room.KnownModes = known;
                    changed = true;
                }
            }
            if (changed)
            {
                RoomLog.Write("Learned \"" + room.Name + "\": " + room.Width + "x" + room.Height + " @ " + room.RefreshHz
                    + " Hz, HDR " + room.Hdr + ", sound " + (room.Audio == Room.AudioDevice ? room.AudioName : room.Audio));
                save();
            }
        }

        bool LearnAudio(Room room)
        {
            if (room.Audio == Room.AudioKeep) return false;
            List<AudioOutput> outputs;
            try { outputs = AudioOutputs.List(); }
            catch (Exception) { return false; }
            AudioOutput current = null;
            foreach (var o in outputs) if (o.IsDefault) current = o;
            if (current == null) return false;

            int own = AudioOutputs.ForDisplay(room.DisplayName, outputs);
            if (own >= 0 && outputs[own].IsDefault)
            {
                if (room.Audio == Room.AudioAuto) return false;
                room.Audio = Room.AudioAuto;
                return true;
            }
            if (room.Audio == Room.AudioDevice && room.AudioId == current.Id) return false;
            room.Audio = Room.AudioDevice;
            room.AudioId = current.Id;
            room.AudioName = current.Name;
            return true;
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
            var profile = BuildProfile(room);
            bool ask = !home && (settings.RoomAskToKeep == AppSettings.RoomAskAlways
                || (settings.RoomAskToKeep == AppSettings.RoomAskNew && !room.Confirmed));
            busy = true;
            RaiseChanged();

            var thread = new Thread(delegate ()
            {
                ApplyResult result;
                bool asked = false;
                try
                {
                    result = LayoutEngine.Apply(profile, 10);
                    if (result.Success && ask)
                    {
                        asked = true;
                        if (!ConfirmForm.Ask(room.Name, ConfirmSeconds))
                        {
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
                dispatcher.BeginInvoke((Action)delegate { Finish(room, leaving, result, asked); });
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "Displicare room switch";
            thread.Start();
        }

        void Finish(Room room, Room leaving, ApplyResult result, bool asked)
        {
            busy = false;
            lastSwitchUtc = DateTime.UtcNow;
            if (result.Success && !result.Reverted)
            {
                if (asked && !room.Confirmed)
                {
                    room.Confirmed = true;
                    save();
                }
                if (room.OpenBigPicture) OpenUrl("steam://open/bigpicture");
                else if (leaving != null && leaving != room && leaving.OpenBigPicture) OpenUrl("steam://close/bigpicture");
                if (result.Warnings.Count > 0) RaiseNotify("Switched to " + room.Name, string.Join("\n", result.Warnings.ToArray()), true);
            }
            else if (result.Reverted) RaiseNotify(room.Name + " wasn't kept", result.Message, false);
            else RaiseNotify("Couldn't switch to " + room.Name, result.Message, true);

            try
            {
                lastTargets = DisplayTopology.DescribeTargets(DisplayTopology.Query(DisplayConfigApi.QDC_ALL_PATHS));
                activeKey = ActiveKey(lastTargets);
            }
            catch (Exception) { }
            RaiseChanged();
        }

        internal static Profile BuildProfile(Room room)
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

            var profile = new Profile();
            profile.Name = room.Name;
            profile.Displays.Add(display);
            if (room.Audio == Room.AudioAuto) profile.AudioFollowsDisplay = true;
            else if (room.Audio == Room.AudioDevice)
            {
                profile.AudioId = room.AudioId;
                profile.AudioName = room.AudioName;
            }
            return profile;
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

        /// <summary>Settings changed in the tab: the next switch asks again, and hotkeys follow.</summary>
        public void RoomEdited(Room room)
        {
            if (room != null) room.Confirmed = false;
            save();
            RegisterHotkeys();
            RaiseChanged();
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
