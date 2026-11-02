using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Displicare;
using Displicare.Rooms;

namespace Displicare.Tests
{
    public static class SetupTests
    {
        const string Aorus = "GBT3214", Lg = "GSM82CD", Dell = "DELA1F2";

        static Room MakeRoom(string key, string name)
        {
            var r = new Room();
            r.Key = key;
            r.Name = name;
            r.DisplayName = name + " (EDID)";
            r.DevicePath = @"\\?\DISPLAY#" + key + @"#5&1#{e6f07b5f}";
            r.KnownModes.Add("3840x2160@144");
            return r;
        }

        static ProfileDisplay Shown(string key, int x)
        {
            var d = new ProfileDisplay();
            d.Identity = new DisplayIdentity();
            d.Identity.DevicePath = @"\\?\DISPLAY#" + key + @"#5&1#{e6f07b5f}";
            d.X = x;
            d.Width = 2560; d.Height = 1440; d.RefreshHz = 144;
            return d;
        }

        static Profile DeskLayout()
        {
            var p = new Profile();
            p.Name = "Desk";
            var main = Shown(Aorus, 0);
            main.Primary = true;
            p.Displays.Add(main);
            p.Displays.Add(Shown(Dell, 2560));
            return p;
        }

        /// <summary>The rooms of a 2.4.5 install with a PR #1 test build's desk: AORUS (home, two screens) and the LG TV.</summary>
        static List<Room> Rooms245()
        {
            var desk = MakeRoom(Aorus, "Desk");
            desk.Hotkey = "Ctrl+Alt+1";
            desk.PadDirection = Room.PadUp;
            desk.Launcher = Room.LaunchNone;
            desk.Confirmed = true;
            desk.AlsoOn = new List<string> { Dell };
            desk.Layout = DeskLayout();

            var tv = MakeRoom(Lg, "Living room");
            tv.Width = 3840; tv.Height = 2160; tv.RefreshHz = 120; tv.Hdr = Room.HdrOn;
            tv.Hotkey = "Ctrl+Alt+2";
            tv.PadDirection = Room.PadRight;
            tv.OpenBigPicture = true; // 2.4.0 settings: no Launcher yet
            tv.Launcher = null;
            tv.Audio = Room.AudioDevice;
            tv.AudioName = "LG TV SSCR2 (NVIDIA High Definition Audio)";

            var dell = MakeRoom(Dell, "Dell");
            dell.Hidden = true;
            return new List<Room> { desk, tv, dell };
        }

        [Test]
        public static void RoomsBecomeScreensAndOneScreenSetups()
        {
            List<KnownScreen> screens;
            List<Setup> setups;
            string safe;
            SetupMigration.FromRooms(Rooms245(), Aorus, out screens, out setups, out safe);

            Assert.Equal(3, screens.Count, "a screen per room");
            Assert.Equal(3, setups.Count, "a setup per room");
            Assert.Equal("room:GBT3214", safe, "the home room's setup is the safe one");

            var tvScreen = screens.Find(s => s.Key == Lg);
            Assert.Equal("3840x2160@120/on", tvScreen.Width + "x" + tvScreen.Height + "@" + tvScreen.RefreshHz + "/" + tvScreen.Hdr, "the TV keeps its mode and HDR");
            Assert.Equal(1, tvScreen.KnownModes.Count, "offered modes go with the screen");

            var tv = setups.Find(s => s.Id == "room:GSM82CD");
            Assert.Equal("Living room", tv.Name, "name");
            Assert.Equal(Lg, tv.MainScreen, "its own screen");
            Assert.Equal(1, tv.Screens.Count, "alone");
            Assert.Equal(Room.LaunchBigPicture, tv.Launcher, "2.4.0's Big Picture flag becomes the launcher");
            Assert.Equal(Room.AudioDevice, tv.Audio, "sound");
            Assert.Equal("Ctrl+Alt+2", tv.Hotkey, "hotkey");
            Assert.Equal(Room.PadRight, tv.PadDirection, "controller direction");
            Assert.True(setups.Find(s => s.Id == "room:DELA1F2").Hidden, "hidden stays hidden");
        }

        [Test]
        public static void AlsoOnScreensAndTheirLayoutCarryOver()
        {
            List<KnownScreen> screens;
            List<Setup> setups;
            string safe;
            SetupMigration.FromRooms(Rooms245(), Aorus, out screens, out setups, out safe);
            var desk = setups.Find(s => s.Id == "room:GBT3214");
            Assert.Equal("GBT3214,DELA1F2", string.Join(",", desk.Screens.ToArray()), "main screen first, then Also turn on");
            Assert.True(desk.Layout != null && desk.Layout.Displays.Count == 2, "the learned arrangement comes along");
            Assert.True(desk.Confirmed, "already kept once");

            SetupMigration.Sanitize(screens, setups, safe);
            Assert.True(desk.Layout == null, "and becomes places");
            Assert.True(desk.Entry(Dell).Placed && desk.Entry(Dell).X == 2560, "the Dell where it was");
            Assert.Equal(1, setups.Find(s => s.Id == "room:GSM82CD").Placement.Count, "a one-screen setup gets its place too");
        }

        [Test]
        public static void SetupsWriteBackRoomsForADowngrade()
        {
            List<KnownScreen> screens;
            List<Setup> setups;
            string safe;
            var before = Rooms245();
            SetupMigration.FromRooms(before, Aorus, out screens, out setups, out safe);
            string home;
            var rooms = SetupMigration.ToRooms(screens, setups, safe, out home);

            Assert.Equal(3, rooms.Count, "a room per screen");
            Assert.Equal(Aorus, home, "home is the safe setup's main screen");
            var tv = rooms.Find(r => r.Key == Lg);
            Assert.Equal("Living room", tv.Name, "name back");
            Assert.Equal(Room.LaunchBigPicture, tv.Launcher, "launcher back");
            Assert.True(tv.OpenBigPicture, "2.4.0's flag kept in step");
            Assert.Equal(120, tv.RefreshHz, "mode back");
            Assert.Equal("Ctrl+Alt+2", tv.Hotkey, "hotkey back");
            Assert.True(rooms.Find(r => r.Key == Dell).Hidden, "hidden back");

            // A two-screen setup next to the desk's own one-screen setup doesn't take over the room's settings
            var solo = new Setup();
            solo.Id = "setup:solo";
            solo.Name = "AORUS only";
            solo.Screens.Add(Aorus);
            solo.Hotkey = "Ctrl+Alt+5";
            setups.Insert(0, solo);
            rooms = SetupMigration.ToRooms(screens, setups, safe, out home);
            Assert.Equal("AORUS only", rooms.Find(r => r.Key == Aorus).Name, "the one-screen setup stands for the screen");
        }

        [Test]
        public static void SavedSetupsAreCleanedUp()
        {
            var screens = new List<KnownScreen>();
            foreach (var k in new[] { Aorus, Lg, Aorus.ToLowerInvariant(), "" })
            {
                var sc = new KnownScreen();
                sc.Key = k;
                screens.Add(sc);
            }
            screens[1].Width = 3840; // no height: not a mode
            screens[1].Hdr = "maybe";

            var a = new Setup { Id = "same", Name = "  Desk  " };
            a.Screens.AddRange(new[] { Aorus, "GONE123", Aorus, Lg });
            a.Layout = DeskLayout(); // AORUS + Dell, but this setup is AORUS + LG
            a.PadDirection = Room.PadUp;
            var b = new Setup { Id = "same", Name = "" };
            b.Screens.Add(Lg);
            b.PadDirection = Room.PadUp; // taken by the first
            b.Launcher = "steamdeck";
            b.Audio = Room.AudioDevice; // without a device
            var empty = new Setup { Id = "empty", Name = "Old TV" };
            empty.Screens.Add("GONE123");
            var setups = new List<Setup> { a, null, b, empty };

            string safe = SetupMigration.Sanitize(screens, setups, "empty");

            Assert.Equal(2, screens.Count, "blank and duplicate screens go");
            Assert.Equal("0x0/auto", screens[1].Width + "x" + screens[1].Height + "/" + screens[1].Hdr, "bad mode and HDR reset");
            Assert.Equal(2, setups.Count, "a setup left without known screens goes");
            Assert.Equal("GBT3214,GSM82CD", string.Join(",", a.Screens.ToArray()), "unknown and repeated screens go, order kept");
            Assert.True(a.Layout == null, "an old layout is dropped once read");
            Assert.Equal("GBT3214,GSM82CD", a.Placement[0].Key + "," + a.Placement[1].Key, "a place per screen, in order");
            Assert.True(a.Entry(Aorus).Placed && !a.Entry(Lg).Placed, "only the screens the old layout had are placed");
            Assert.Equal("Desk", a.Name, "name trimmed");
            Assert.True(b.Id != "same" && b.Id.StartsWith("setup:"), "a repeated id gets a new one");
            Assert.Equal("GSM82CD", b.Name, "an empty name falls back to the screen");
            Assert.Equal("", b.PadDirection, "a direction already taken is cleared");
            Assert.Equal(Room.LaunchNone, b.Launcher, "unknown launcher");
            Assert.Equal(Room.AudioAuto, b.Audio, "sound device without a device");
            Assert.Equal("same", safe, "the safe setup was dropped: the first one takes over");
            Assert.True(SetupMigration.Sanitize(new List<KnownScreen>(), new List<Setup>(), "x") == null, "no setups, no safe setup");
        }

        [Test]
        public static void OldSettingsTurnIntoSetupsOnLoad()
        {
            var s = ConfigManager.Parse("{\"RoomsEnabled\":true,\"RoomHomeKey\":\"GBT3214\",\"Rooms\":[" +
                "{\"Key\":\"GBT3214\",\"Name\":\"Desk\",\"Hotkey\":\"Ctrl+Alt+1\",\"PadDirection\":\"up\"}," +
                "{\"Key\":\"GSM82CD\",\"Name\":\"Living room\",\"Launcher\":\"bigpicture\",\"RefreshHz\":120}]}");
            Assert.Equal(2, s.Setups.Count, "a setup per room");
            Assert.Equal(2, s.KnownScreens.Count, "a screen per room");
            Assert.Equal("room:GBT3214", s.SafeSetupId, "home becomes the safe setup");

            ConfigManager.WriteRoomsForDowngrade(s);
            Assert.Equal("GBT3214", s.RoomHomeKey, "home written back");
            Assert.Equal(Room.LaunchBigPicture, s.Rooms.Find(r => r.Key == "GSM82CD").Launcher, "rooms written back");
            Assert.Equal(120, s.Rooms.Find(r => r.Key == "GSM82CD").RefreshHz, "with their screen's mode");
        }

        [Test]
        public static void ADeletedSetupStaysDeleted()
        {
            // The TV's own setup was deleted; its screen is still known, so loading doesn't bring the setup back
            // (and discovery only gives a screen its own setup the first time it sees the screen).
            var s = ConfigManager.Parse("{\"KnownScreens\":[{\"Key\":\"GBT3214\"},{\"Key\":\"GSM82CD\"}]," +
                "\"Setups\":[{\"Id\":\"room:GBT3214\",\"Name\":\"Desk\",\"Screens\":[\"GBT3214\"]}],\"SafeSetupId\":\"room:GBT3214\"}");
            Assert.Equal(1, s.Setups.Count, "still one setup");
            Assert.Equal(2, s.KnownScreens.Count, "both screens known");
            ConfigManager.WriteRoomsForDowngrade(s);
            Assert.Equal(2, s.Rooms.Count, "2.4.x still sees both screens as rooms");
        }

        [Test]
        public static void SetupsSurviveSettingsJson()
        {
            List<KnownScreen> screens;
            List<Setup> setups;
            string safe;
            SetupMigration.FromRooms(Rooms245(), Aorus, out screens, out setups, out safe);
            SetupMigration.Sanitize(screens, setups, safe);
            setups.Find(s => s.Id == "room:GSM82CD").Entry(Lg).Hdr = Room.HdrOff;

            var ser = new DataContractJsonSerializer(typeof(List<Setup>));
            string json;
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, setups);
                json = Encoding.UTF8.GetString(ms.ToArray());
            }
            List<Setup> back;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                back = (List<Setup>)ser.ReadObject(ms);

            var desk = back.Find(s => s.Id == "room:GBT3214");
            Assert.Equal(2, desk.Screens.Count, "screens read back");
            Assert.Equal(2, desk.Placement.Count, "places read back");
            Assert.Equal(2560, desk.Entry(Dell).X, "where the second screen goes");
            var tv = back.Find(s => s.Id == "room:GSM82CD");
            Assert.Equal(Room.LaunchBigPicture, tv.Launcher, "launcher read back");
            Assert.Equal(Room.HdrOff, tv.Entry(Lg).Hdr, "a screen's HDR in the setup read back");
            Assert.Equal(120, tv.Entry(Lg).RefreshHz, "and its refresh rate");

            // A setup written by hand with fields missing gets the defaults
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("[{\"Id\":\"x\",\"Screens\":[\"GBT3214\"]}]")))
                back = (List<Setup>)ser.ReadObject(ms);
            Assert.Equal(Room.AudioAuto, back[0].Audio, "default sound");
            Assert.Equal(Room.LaunchNone, back[0].Launcher, "default launcher");
            Assert.Equal("", back[0].Hotkey, "no hotkey");
            Assert.True(back[0].Placement != null && back[0].Placement.Count == 0, "no places yet (Sanitize adds them)");
        }
    }
}
