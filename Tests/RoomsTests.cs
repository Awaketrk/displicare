using System;
using System.Collections.Generic;
using System.IO;
using Displicare.Rooms;

namespace Displicare.Tests
{
    public static class RoomsTests
    {
        // ---------------------------------------------------------------- fixtures

        const string NvidiaPath = @"\\?\PCI#VEN_10DE&DEV_2C02#4&1&0&0008#{5b45201d-f2f2-4f3b-85bb-30ff1f953599}";
        const string AmdPath = @"\\?\PCI#VEN_1002&DEV_13C0#4&2&0&0041#{5b45201d-f2f2-4f3b-85bb-30ff1f953599}";

        /// <summary>Real EDIDs from the maintainer's PC (Tests\Fixtures\damepc-edids.txt), by "model instance".</summary>
        static Dictionary<string, byte[]> Edids()
        {
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Fixtures\damepc-edids.txt");
            var map = new Dictionary<string, byte[]>();
            foreach (string line in File.ReadAllLines(file))
            {
                if (line.StartsWith("#") || line.Trim().Length == 0) continue;
                string[] parts = line.Split(' ');
                string hex = parts[2];
                var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                map[parts[0] + " " + parts[1]] = bytes;
            }
            return map;
        }

        static bool Has(List<DisplayMode> modes, int w, int h, int hz)
        {
            foreach (var m in modes) if (m.Width == w && m.Height == h && m.Hz == hz) return true;
            return false;
        }

        static DisplayIdentity Lg(string uid, string adapter)
        {
            var id = new DisplayIdentity();
            id.DevicePath = @"\\?\DISPLAY#GSM82CD#5&3a6f26f9&3&" + uid + "#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
            id.FriendlyName = "LG TV SSCR2";
            id.EdidManufacturer = 0x6D1E; // "GSM", byte-swapped as DisplayConfig reports it
            id.EdidProduct = 0x82CD;
            id.ConnectorInstance = 1;
            id.AdapterPath = adapter;
            return id;
        }

        static TargetInfo Target(long luid, uint id, DisplayIdentity identity)
        {
            var t = new TargetInfo();
            t.AdapterLuid = luid;
            t.TargetId = id;
            t.Available = true;
            t.Identity = identity;
            return t;
        }

        /// <summary>A one-display LG layout saved on adapter LUID 0x1111 with target id 4353.</summary>
        static Profile LgLayout()
        {
            var p = new Profile();
            p.Name = "Living room";
            var d = new ProfileDisplay();
            d.Identity = Lg("UID4353", NvidiaPath);
            d.Primary = true;
            d.Width = 3840; d.Height = 2160; d.RefreshHz = 144; d.Hdr = 1;
            p.Displays.Add(d);

            var a = new SavedAdapter();
            a.Luid = 0x1111;
            a.DevicePath = NvidiaPath;
            p.Adapters.Add(a);

            var sp = new SavedPath();
            sp.Display = 0;
            sp.SourceAdapter = 0x1111; sp.SourceId = 0; sp.SourceModeIdx = 1;
            sp.TargetAdapter = 0x1111; sp.TargetId = 4353; sp.TargetModeIdx = 0;
            p.Paths.Add(sp);

            var target = new DISPLAYCONFIG_MODE_INFO();
            target.infoType = DisplayConfigApi.MODE_INFO_TYPE_TARGET;
            target.id = 4353;
            target.adapterId = LUID.FromValue(0x1111);
            target.targetMode.vSyncFreq.Numerator = 144000;
            target.targetMode.vSyncFreq.Denominator = 1000;
            p.Modes.Add(LayoutEngine.ToSaved(target));

            var source = new DISPLAYCONFIG_MODE_INFO();
            source.infoType = DisplayConfigApi.MODE_INFO_TYPE_SOURCE;
            source.adapterId = LUID.FromValue(0x1111);
            source.sourceMode.width = 3840;
            source.sourceMode.height = 2160;
            p.Modes.Add(LayoutEngine.ToSaved(source));
            return p;
        }

        // ---------------------------------------------------------------- EDID

        [Test]
        public static void EdidMonitorReachesItsDisplayIdRefreshRate()
        {
            var modes = Edid.Modes(Edids()["GBT3214 5&342cb30d&0&UID4353"]);
            Assert.True(Has(modes, 3840, 2160, 240), "FO32U2 4K 240 Hz from DisplayID");
            Assert.True(Has(modes, 2560, 1440, 240), "FO32U2 1440p 240 Hz");
            Assert.Equal(3840, modes[0].Width, "best first");
            Assert.Equal(240, modes[0].Hz, "best refresh first");
        }

        [Test]
        public static void EdidLgTvListsItsVicModes()
        {
            var modes = Edid.Modes(Edids()["GSM82CD 5&342cb30d&0&UID4352"]);
            Assert.True(Has(modes, 3840, 2160, 60), "4K 60 from the detailed timing");
            Assert.True(Has(modes, 3840, 2160, 120), "4K 120 from VIC 118");
            Assert.True(Has(modes, 1920, 1080, 120), "1080p 120 from VIC 63");
            Assert.True(Edid.SupportsHdr(Edids()["GSM82CD 5&342cb30d&0&UID4352"]), "LG HDR10");
        }

        [Test]
        public static void EdidBeyondTvParses()
        {
            // The relayed dump has a stray extra byte at the end; the four 128-byte blocks are what matter.
            byte[] tcl = Edids()["TCL9653 5&342cb30d&0&UID4352"];
            Assert.True(tcl.Length >= 512, "four blocks");
            Array.Resize(ref tcl, 512);
            var modes = Edid.Modes(tcl);
            Assert.True(Has(modes, 3840, 2160, 60), "Beyond TV 4K 60");
            Assert.True(Edid.SupportsHdr(tcl), "Beyond TV HDR10");
        }

        [Test]
        public static void EdidRejectsGarbage()
        {
            Assert.Equal(0, Edid.Modes(null).Count, "null");
            Assert.Equal(0, Edid.Modes(new byte[128]).Count, "no header");
            Assert.True(!Edid.SupportsHdr(new byte[256]), "no HDR in zeros");
        }

        // ---------------------------------------------------------------- engine

        [Test]
        public static void ModeStructsHaveWindowsSizes()
        {
            Assert.Equal(64, System.Runtime.InteropServices.Marshal.SizeOf(typeof(DISPLAYCONFIG_MODE_INFO)), "mode size");
            Assert.Equal(72, System.Runtime.InteropServices.Marshal.SizeOf(typeof(DISPLAYCONFIG_PATH_INFO)), "path size");
        }

        [Test]
        public static void MatchFollowsMonitorToAnotherPortAndGpu()
        {
            var targets = new List<TargetInfo>();
            targets.Add(Target(1, 1036, Lg("UID1036", AmdPath)));
            Assert.Equal(0, LayoutEngine.MatchTarget(Lg("UID4353", NvidiaPath), targets), "same EDID, other port");

            targets[0].Available = false;
            Assert.Equal(-1, LayoutEngine.MatchTarget(Lg("UID4353", NvidiaPath), targets), "unavailable");
        }

        [Test]
        public static void ExactLayoutRemapsNewBootLuidsAndTargetIds()
        {
            var luids = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            luids[NvidiaPath] = 0x9999;
            var targets = new List<TargetInfo>();
            targets.Add(Target(0x9999, 4354, Lg("UID4354", NvidiaPath)));

            string error;
            var config = LayoutEngine.BuildExactConfig(LgLayout(), luids, targets, out error);
            Assert.True(config != null, "built: " + error);
            Assert.Equal(0x9999L, config.Paths[0].sourceInfo.adapterId.Value, "source adapter");
            Assert.Equal(4354u, config.Paths[0].targetInfo.id, "target id");
            Assert.Equal(4354u, config.Modes[0].id, "target mode id");
            Assert.Equal(0x9999L, config.Modes[1].adapterId.Value, "source mode adapter");
        }

        [Test]
        public static void TopologyPicksFreeSourceForTarget()
        {
            var targets = new List<TargetInfo>();
            targets.Add(Target(0x2222, 1036, Lg("UID1036", AmdPath)));
            var all = new DisplayConfig();
            all.Modes = new DISPLAYCONFIG_MODE_INFO[0];
            all.Paths = new DISPLAYCONFIG_PATH_INFO[3];
            for (int i = 0; i < 3; i++)
            {
                all.Paths[i].sourceInfo.adapterId = LUID.FromValue(0x2222);
                all.Paths[i].sourceInfo.id = (uint)i;
                all.Paths[i].targetInfo.adapterId = LUID.FromValue(0x2222);
                all.Paths[i].targetInfo.id = i == 2 ? 1036u : 7u;
            }
            string error;
            var paths = LayoutEngine.BuildTopology(LgLayout(), all, targets, out error);
            Assert.True(paths != null, "built: " + error);
            Assert.Equal(1036u, paths[0].targetInfo.id, "target");
            Assert.Equal(2u, paths[0].sourceInfo.id, "source");
            Assert.Equal(DisplayConfigApi.DISPLAYCONFIG_PATH_ACTIVE, paths[0].flags, "active");
        }

        [Test]
        public static void PickModePrefersWantedThenBest()
        {
            var modes = new List<DisplayMode>();
            modes.Add(new DisplayMode(1920, 1080, 120));
            modes.Add(new DisplayMode(3840, 2160, 60));
            modes.Add(new DisplayMode(3840, 2160, 144));
            modes.Add(new DisplayMode(3840, 2160, 120));

            Assert.Equal("3840x2160 @ 144 Hz", LayoutEngine.PickMode(modes, 0, 0, 0).Value.ToString(), "best");
            Assert.Equal("3840x2160 @ 120 Hz", LayoutEngine.PickMode(modes, 3840, 2160, 119.88).Value.ToString(), "closest rate");
            Assert.Equal("3840x2160 @ 144 Hz", LayoutEngine.PickMode(modes, 3840, 2160, 0).Value.ToString(), "highest at size");
            Assert.Equal("3840x2160 @ 144 Hz", LayoutEngine.PickMode(modes, 2560, 1440, 165).Value.ToString(), "size gone: best");
            Assert.True(LayoutEngine.PickMode(new List<DisplayMode>(), 0, 0, 0) == null, "no modes");
        }

        // ---------------------------------------------------------------- rooms

        [Test]
        public static void RoomKeyIsTheMonitorModel()
        {
            Assert.Equal("GSM82CD", RoomManager.ModelKey(@"\\?\DISPLAY#GSM82CD#5&3a6f26f9&3&UID1036#{e6f07b5f}"), "lg");
            Assert.True(RoomManager.ModelKey("garbage") == null, "no model");
        }

        [Test]
        public static void ActiveRoomNeedsExactlyOnePhysicalDisplay()
        {
            var lg = Target(1, 1, Lg("UID1", NvidiaPath));
            lg.Active = true;
            var virt = Target(2, 2, new DisplayIdentity());
            virt.Active = true;
            virt.OutputTechnology = DisplayConfigApi.OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL;
            var targets = new List<TargetInfo> { lg, virt };
            Assert.Equal("GSM82CD", RoomManager.ActiveKey(targets), "virtual display ignored");

            var second = Target(3, 3, Lg("UID2", AmdPath));
            second.Active = true;
            targets.Add(second);
            Assert.True(RoomManager.ActiveKey(targets) == null, "two screens is no single room");
        }

        [Test]
        public static void ModesMergeKnownAndEdid()
        {
            var known = new List<string> { "3840x2160@144", "bad", "1920x1080@60" };
            var edid = new List<DisplayMode> { new DisplayMode(3840, 2160, 120), new DisplayMode(3840, 2160, 144) };
            var all = RoomManager.MergeModes(known, edid);
            Assert.Equal(3, all.Count, "4K 144 only once, bad entry skipped");
            Assert.Equal("3840x2160 @ 144 Hz", all[0].ToString(), "best first");
            Assert.Equal("3840x2160 @ 120 Hz", all[1].ToString(), "then lower rate");
            Assert.Equal("1920x1080 @ 60 Hz", all[2].ToString(), "smallest last");
        }

        [Test]
        public static void RoomProfileFollowsRoomSettings()
        {
            var room = new Room();
            room.Key = "GSM82CD";
            room.Name = "Living room";
            room.DevicePath = Lg("UID1", NvidiaPath).DevicePath;
            room.DisplayName = "LG TV SSCR2";
            room.EdidManufacturer = 0x6D1E;
            room.EdidProduct = 0x82CD;

            var p = RoomManager.BuildProfile(room, new DisplayMode(3840, 2160, 60));
            Assert.Equal(2, p.Displays[0].Hdr, "HDR auto");
            Assert.Equal(0, p.Displays[0].Width, "best mode");
            Assert.Equal(3840, p.Displays[0].NativeWidth, "native size passed on");
            Assert.True(!p.AudioFollowsDisplay && p.AudioId == null, "sound is set by the room manager, not the layout");
            Assert.True(p.Displays[0].Primary, "main screen");

            room.Hdr = Room.HdrOff;
            room.Audio = Room.AudioDevice;
            room.AudioId = "{x}";
            room.AudioName = "Headphones";
            room.Width = 3840; room.Height = 2160; room.RefreshHz = 120;
            p = RoomManager.BuildProfile(room, null);
            Assert.Equal(0, p.Displays[0].Hdr, "HDR off");
            Assert.Equal(3840, p.Displays[0].Width, "chosen size");
            Assert.Equal(120.0, p.Displays[0].RefreshHz, "refresh");
        }

        [Test]
        public static void AudioOutputBelongsToItsDisplay()
        {
            var outputs = new List<AudioOutput>();
            outputs.Add(new AudioOutput { Id = "a", Name = "Headphones (ROG CETRA OPEN WIRELESS)" });
            outputs.Add(new AudioOutput { Id = "b", Name = "LG TV SSCR2 (NVIDIA High Definition Audio)" });
            Assert.Equal(1, AudioOutputs.ForDisplay("LG TV SSCR2", outputs), "TV's HDMI sound");
            Assert.Equal(-1, AudioOutputs.ForDisplay("AORUS FO32U2", outputs), "monitor without speakers");
            Assert.Equal(1, AudioOutputs.Match("old-id", "LG TV SSCR2 (AMD High Definition Audio Device)", outputs), "moved GPU");
        }

        [Test]
        public static void HotkeysParseAndPickFreeOnes()
        {
            Assert.Equal("Ctrl+Alt+1", Hotkey.Normalize("ctrl+alt+1"), "normalize");
            Assert.True(Hotkey.Normalize("Shift+A") == null, "shift only is not a hotkey");
            Assert.Equal("Ctrl+Alt+2", Hotkey.NextFree(new[] { "Ctrl+Alt+0", "Ctrl+Alt+1", null }), "next free");
        }

        // ---------------------------------------------------------------- fixes from the first DAMEPC test

        [Test]
        public static void TvsGetTheirNativeResolutionNotCinema4K()
        {
            var modes = new List<DisplayMode>
            {
                new DisplayMode(4096, 2160, 60), new DisplayMode(4096, 2160, 120),
                new DisplayMode(3840, 2160, 60), new DisplayMode(3840, 2160, 120), new DisplayMode(1920, 1080, 120)
            };
            Assert.Equal("3840x2160 @ 120 Hz", LayoutEngine.PickMode(modes, 0, 0, 0, 3840, 2160).Value.ToString(), "native from EDID");
            Assert.Equal("3840x2160 @ 120 Hz", LayoutEngine.PickMode(modes, 0, 0, 0).Value.ToString(), "no EDID: 4096 wide skipped");
            Assert.Equal("4096x2160 @ 60 Hz", LayoutEngine.PickMode(modes, 4096, 2160, 60).Value.ToString(), "still selectable on purpose");
        }

        [Test]
        public static void CommandPipeTakesTheFirstLine()
        {
            Func<string, bool, string> line = (text, ended) =>
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(text);
                return CommandPipe.LineFrom(data, data.Length, ended);
            };
            Assert.Equal("room\tLG TV", line("room\tLG TV\r\nignored", false), "first line, CR dropped");
            Assert.True(line("room\tLG", false) == null, "no newline yet: keep reading");
            Assert.Equal("room\tLG", line("room\tLG", true), "client closed: what it sent is the line");
            Assert.Equal("room\tSalon büyük", line("room\tSalon büyük\n", false), "UTF-8 names");
            Assert.Equal("", line("\n", false), "empty line");
        }

        static string Learned(int width, int height, int hz)
        {
            var modes = new List<DisplayMode>
            {
                new DisplayMode(4096, 2160, 120), new DisplayMode(3840, 2160, 60), new DisplayMode(3840, 2160, 119),
                new DisplayMode(3840, 2160, 120), new DisplayMode(2560, 1440, 120), new DisplayMode(2560, 1440, 60)
            };
            RoomManager.AsDefaults(modes, new DisplayMode(3840, 2160, 60), ref width, ref height, ref hz);
            return width + "x" + height + "@" + hz;
        }

        [Test]
        public static void LearnedBestModeIsStoredAsNativeAndHighest()
        {
            Assert.Equal("0x0@0", Learned(3840, 2160, 120), "native at highest refresh");
            Assert.Equal("0x0@60", Learned(3840, 2160, 60), "native at a lower refresh keeps the refresh");
            Assert.Equal("2560x1440@0", Learned(2560, 1440, 120), "other size at its highest refresh");
            Assert.Equal("2560x1440@60", Learned(2560, 1440, 60), "other size and refresh both kept");
            Assert.Equal("4096x2160@0", Learned(4096, 2160, 120), "cinema 4K is never the native size");
        }

        [Test]
        public static void EdidPreferredModeIsTheNativeOne()
        {
            var lg = Edid.Preferred(Edids()["GSM82CD 5&342cb30d&0&UID4352"]);
            Assert.True(lg != null, "LG has a preferred timing");
            Assert.Equal(3840, lg.Value.Width, "LG native width");
            Assert.Equal(2160, lg.Value.Height, "LG native height");
            var aorus = Edid.Preferred(Edids()["GBT3214 5&342cb30d&0&UID4353"]);
            Assert.Equal(3840, aorus.Value.Width, "monitor native width");
        }

        [Test]
        public static void TvSoundFoundDespiteWindowsNumbering()
        {
            var outputs = new List<AudioOutput>();
            outputs.Add(new AudioOutput { Id = "a", Name = "Headphones (ROG CETRA OPEN WIRELESS)" });
            outputs.Add(new AudioOutput { Id = "b", Name = "1 - LG TV SSCR2 (AMD High Definition Audio Device)" });
            Assert.Equal(1, AudioOutputs.ForDisplay("LG TV SSCR2", outputs), "\"1 - \" prefix");
            Assert.Equal("LG TV SSCR2", AudioOutputs.DevicePart("12 - LG TV SSCR2 (NVIDIA High Definition Audio)"), "two digits");
            Assert.Equal("Speakers - Desk", AudioOutputs.DevicePart("Speakers - Desk (Realtek USB Audio)"), "a dash in a real name stays");
        }

        [Test]
        public static void RememberedModesKeepTheBestOnes()
        {
            var offered = new List<DisplayMode>();
            for (int i = 0; i < 80; i++) offered.Add(new DisplayMode(640 + i, 480, 60)); // lots of small modes first, as Windows lists them
            offered.Add(new DisplayMode(3840, 2160, 120));
            offered.Add(new DisplayMode(3840, 2160, 60));
            offered.Add(new DisplayMode(1920, 1080, 120));
            var known = RoomManager.ModesToRemember(offered);
            Assert.Equal("3840x2160@120", known[0], "best first");
            Assert.Equal("3840x2160@60", known[1], "then lower rate");
            Assert.Equal(3, known.Count, "small modes dropped when big ones exist");
        }

        [Test]
        public static void RoomArgumentReadsRoomName()
        {
            Assert.Equal("Living room", App.RoomArgument(new[] { "--room", "Living", "room", "--minimized" }), "unquoted words");
            Assert.Equal("Bedroom", App.RoomArgument(new[] { "-m", "--ROOM", "Bedroom" }), "any case");
            Assert.True(App.RoomArgument(new[] { "--room" }) == null, "missing name");
            Assert.True(App.RoomArgument(new[] { "--minimized" }) == null, "no room");
        }

        [Test]
        public static void RoomSettingsAreSanitized()
        {
            var s = ConfigManager.Parse("{\"RoomsEnabled\":true,\"RoomAskToKeep\":\"maybe\",\"Rooms\":[null," +
                "{\"Key\":\"GSM82CD\",\"Name\":\"\",\"DisplayName\":\"LG TV SSCR2\",\"Width\":3840,\"Height\":0," +
                "\"RefreshHz\":5000,\"Hdr\":\"sometimes\",\"Audio\":\"device\",\"Hotkey\":\"shift+q\"},{\"Name\":\"no key\"}]}");
            Assert.True(s.RoomsEnabled, "enabled kept");
            Assert.Equal(AppSettings.RoomAskNew, s.RoomAskToKeep, "ask reset");
            Assert.Equal(1, s.Rooms.Count, "null and keyless rooms dropped");
            var r = s.Rooms[0];
            Assert.Equal("LG TV SSCR2", r.Name, "name from display");
            Assert.Equal(0, r.Width, "half a size reset");
            Assert.Equal(0, r.RefreshHz, "refresh reset");
            Assert.Equal(Room.HdrAuto, r.Hdr, "hdr reset");
            Assert.Equal(Room.AudioAuto, r.Audio, "device without id reset");
            Assert.Equal("", r.Hotkey, "bad hotkey dropped");
        }

        [Test]
        public static void OldSettingsHaveRoomsOff()
        {
            var s = ConfigManager.Parse("{\"Brightness\":70}");
            Assert.True(!s.RoomsEnabled, "off by default");
            Assert.Equal(0, s.Rooms.Count, "no rooms");
            Assert.Equal("Ctrl+Alt+0", s.RoomHomeHotkey, "home hotkey default");
            Assert.True(s.RoomController && s.RoomStreamScreensOff && s.RoomShowOverlay, "new options on for older settings");
        }

        [Test]
        public static void ControllerComboNeedsAHold()
        {
            var t = new PadTracker();
            ushort both = PadTracker.View | PadTracker.Menu;
            Assert.True(t.Update(both, 0, 0) == null, "not on the first press");
            Assert.True(t.Update(both, 0, PadTracker.HoldMs - 1) == null, "not before the hold time");
            Assert.True(t.Update(both, 0, PadTracker.HoldMs) == PadButton.Combo, "after the hold time");
            Assert.True(t.Update(both, 0, PadTracker.HoldMs * 3) == null, "once per hold");
            Assert.True(t.Update(0, 0, PadTracker.HoldMs * 3 + 50) == null, "release");
            Assert.True(t.Update(PadTracker.View, 0, 0) == null, "View alone");
            Assert.True(t.Update(both, 0, 100) == null, "a new hold starts over");
            Assert.True(t.Update(both, 0, 100 + PadTracker.HoldMs) == PadButton.Combo, "second hold");
        }

        [Test]
        public static void ControllerMovesOncePerPress()
        {
            var t = new PadTracker();
            Assert.True(t.Update(PadTracker.DpadDown, 0, 0) == PadButton.Down, "d-pad down");
            Assert.True(t.Update(PadTracker.DpadDown, 0, 50) == null, "held d-pad doesn't repeat");
            Assert.True(t.Update(0, 30000, 100) == PadButton.Up, "stick up");
            Assert.True(t.Update(0, 30000, 150) == null, "held stick doesn't repeat");
            Assert.True(t.Update(0, 2000, 200) == null, "stick back to the middle");
            Assert.True(t.Update(0, -30000, 250) == PadButton.Down, "stick down");
            Assert.True(t.Update(PadTracker.ButtonA, 0, 300) == PadButton.A, "A");
            Assert.True(t.Update(PadTracker.ButtonA | PadTracker.ButtonB, 0, 350) == PadButton.B, "B while A is held");
        }

        [Test]
        public static void ViewAndDpadPickARoomDirectly()
        {
            var t = new PadTracker();
            Assert.True(t.Update(PadTracker.DpadRight, 0, 0) == null, "D-pad alone is left to the game");
            Assert.True(t.Update(PadTracker.View, 0, 50) == null, "View alone");
            Assert.True(t.Update(PadTracker.View | PadTracker.DpadRight, 0, 100) == PadButton.RoomRight, "View + right");
            Assert.True(t.Update(PadTracker.View | PadTracker.DpadRight, 0, 150) == null, "once per press");
            Assert.True(t.Update(PadTracker.View | PadTracker.DpadLeft, 0, 200) == PadButton.RoomLeft, "View + left");
            Assert.True(t.Update(PadTracker.View | PadTracker.DpadUp, 0, 250) == PadButton.RoomUp, "View + up, not a picker move");
            Assert.True(t.Update(PadTracker.View | PadTracker.DpadDown, 0, 300) == PadButton.RoomDown, "View + down");
        }

        [Test]
        public static void RoomsGetControllerDirections()
        {
            Func<string, Room> make = key => { var r = new Room(); r.Key = key; r.Name = key; return r; };
            var desk = make("GBT3214");
            var lg = make("GSM82CD");
            var tcl = make("TCL9653");
            var rooms = new List<Room> { lg, desk, tcl };
            Assert.True(RoomManager.AssignPadDirections(rooms, "GBT3214"), "assigned");
            Assert.Equal(Room.PadUp, desk.PadDirection, "home is up");
            Assert.Equal(Room.PadRight, lg.PadDirection, "first other room is right");
            Assert.Equal(Room.PadLeft, tcl.PadDirection, "next is left");
            Assert.True(!RoomManager.AssignPadDirections(rooms, "GBT3214"), "nothing more to do");

            var phone = make("SAM76C1");
            phone.Hidden = true;
            var portable = make("ARZ0001");
            lg.PadDirection = ""; // the user chose none
            rooms.Add(phone);
            rooms.Add(portable);
            RoomManager.AssignPadDirections(rooms, "GBT3214");
            Assert.Equal("", lg.PadDirection, "none stays none");
            Assert.True(phone.PadDirection == null, "hidden rooms get none");
            Assert.Equal(Room.PadRight, portable.PadDirection, "a new room takes the first free direction");
        }

        [Test]
        public static void BigPictureSettingBecomesTheLauncher()
        {
            var s = ConfigManager.Parse("{\"Rooms\":[{\"Key\":\"A\",\"OpenBigPicture\":true},{\"Key\":\"B\"},{\"Key\":\"C\",\"Launcher\":\"xbox\"},{\"Key\":\"D\",\"Launcher\":\"psx\"}]}");
            Assert.Equal(Room.LaunchBigPicture, s.Rooms[0].Launcher, "old Big Picture tick carried over");
            Assert.Equal(Room.LaunchNone, s.Rooms[1].Launcher, "nothing by default");
            Assert.Equal(Room.LaunchXbox, s.Rooms[2].Launcher, "Xbox mode kept");
            Assert.Equal(Room.LaunchNone, s.Rooms[3].Launcher, "unknown becomes nothing");
            Assert.True(s.Rooms[0].OpenBigPicture && !s.Rooms[2].OpenBigPicture, "old field kept in step for downgrades");
        }

        [Test]
        public static void SharedControllerDirectionsStayWithTheFirstRoom()
        {
            var s = ConfigManager.Parse("{\"Rooms\":[{\"Key\":\"A\",\"PadDirection\":\"right\"},{\"Key\":\"B\",\"PadDirection\":\"right\"},{\"Key\":\"C\",\"PadDirection\":\"sideways\"}]}");
            Assert.Equal("right", s.Rooms[0].PadDirection, "first keeps it");
            Assert.Equal("", s.Rooms[1].PadDirection, "second gets none");
            Assert.True(s.Rooms[2].PadDirection == null, "unknown is given a new one later");
        }

        [Test]
        public static void SoundNamesAreShortened()
        {
            Assert.Equal("LG TV SSCR2", RoomManager.ShortSoundName("LG TV SSCR2 (NVIDIA High Definition Audio)"), "adapter dropped");
            Assert.Equal("Beyond TV", RoomManager.ShortSoundName("1 - Beyond TV (AMD High Definition Audio Device)"), "number dropped");
            Assert.Equal("Headphones", RoomManager.ShortSoundName("Headphones"), "plain name kept");
            Assert.Equal("AB - CD", RoomManager.ShortSoundName("AB - CD"), "only a leading number is dropped");
        }

        [Test]
        public static void StreamsAreIndirectDisplaysButNotMiracast()
        {
            var t = new TargetInfo();
            t.Active = true;
            t.OutputTechnology = DisplayConfigApi.OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL;
            Assert.True(RoomManager.IsStreamTarget(t), "virtual display");
            t.OutputTechnology = DisplayConfigApi.OUTPUT_TECHNOLOGY_MIRACAST;
            Assert.True(!RoomManager.IsStreamTarget(t), "Miracast");
            t.OutputTechnology = 5; // HDMI
            Assert.True(!RoomManager.IsStreamTarget(t), "physical");
            // SudoVDA (Steam Link) says HDMI, but its adapter is a software device, not a graphics card.
            t.Identity = new DisplayIdentity();
            t.Identity.AdapterPath = @"\\?\ROOT#DISPLAY#0000#{5b45201d-f2f2-4f3b-85bb-30ff1f953599}";
            Assert.True(RoomManager.IsStreamTarget(t), "SudoVDA reporting HDMI");
            t.Identity.AdapterPath = @"\\?\PCI#VEN_10DE&DEV_2C02&SUBSYS_176219DA&REV_A1#8B1494F7412DB04800#{5b45201d-f2f2-4f3b-85bb-30ff1f953599}";
            Assert.True(!RoomManager.IsStreamTarget(t), "HDMI on a graphics card");
            t.OutputTechnology = DisplayConfigApi.OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL;
            t.Active = false;
            Assert.True(!RoomManager.IsStreamTarget(t), "inactive");
        }

        [Test]
        public static void VirtualDisplaysGetNoDimmingOrColor()
        {
            Assert.True(MonitorEngine.IsVirtualAdapter(@"root\sudomaker\sudovda"), "SudoVDA");
            Assert.True(!MonitorEngine.IsVirtualAdapter(@"PCI\VEN_10DE&DEV_2C02&SUBSYS_176219DA&REV_A1"), "NVIDIA");
            Assert.True(!MonitorEngine.IsVirtualAdapter(null), "unknown");
        }

        [Test]
        public static void StreamEndChecksWhichScreensAreBack()
        {
            var layout = new Profile();
            var d = new ProfileDisplay();
            d.Identity = new DisplayIdentity();
            d.Identity.DevicePath = @"\\?\DISPLAY#GBT3214#5&1#{e6f07b5f}";
            layout.Displays.Add(d);
            Assert.True(RoomManager.SameScreens(layout, new[] { "GBT3214" }), "same screen");
            Assert.True(!RoomManager.SameScreens(layout, new string[0]), "nothing on");
            Assert.True(!RoomManager.SameScreens(layout, new[] { "GBT3214", "GSM82CD" }), "an extra screen");
        }

        [Test]
        public static void KeepPromptFitsAtEveryScale()
        {
            foreach (float scale in new[] { 1f, 1.5f, 2f, 3f })
            foreach (bool controller in new[] { false, true })
            {
                var work = new System.Drawing.Rectangle(0, 0, (int)(1920 * scale), (int)(1040 * scale));
                using (var form = new ConfirmForm("Living room", 15, Tuple.Create(scale, work), controller))
                {
                    var client = new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.LayoutBounds.Size);
                    foreach (System.Windows.Forms.Control c in form.Controls)
                        Assert.True(client.Contains(c.Bounds), c.Text + " outside the prompt at scale " + scale);
                    Assert.True(work.Contains(form.LayoutBounds), "prompt off screen at scale " + scale);
                }
            }
        }
    }
}
