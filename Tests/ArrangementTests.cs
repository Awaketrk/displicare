using System;
using System.Collections.Generic;
using Displicare;
using Displicare.Rooms;

namespace Displicare.Tests
{
    public static class ArrangementTests
    {
        const string Aorus = "GBT3214", Lg = "GSM82CD", Dell = "DELA1F2";

        static bool Fine(IList<Box> boxes)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                bool touches = boxes.Count == 1;
                for (int j = 0; j < boxes.Count; j++)
                {
                    if (i == j) continue;
                    if (boxes[i].Overlaps(boxes[j])) return false;
                    if (boxes[i].Touches(boxes[j])) touches = true;
                }
                if (!touches) return false;
            }
            return true;
        }

        [Test]
        public static void ADroppedScreenSnapsToTheNearestEdge()
        {
            var main = new Box(Aorus, 0, 0, 3840, 2160);
            var dell = new Box(Dell, 0, 0, 2560, 1440);
            var placed = new List<Box> { main };

            var right = Arrangement.Snap(placed, dell, 4000, 300);
            Assert.Equal(3840, right.X, "dropped a bit right of the AORUS: against its right edge");
            Assert.Equal(300, right.Y, "the height it was dropped at");

            var aligned = Arrangement.Snap(placed, dell, 3900, 60);
            Assert.Equal(0, aligned.Y, "close to the top edge: lined up with it");
            var bottom = Arrangement.Snap(placed, dell, 3900, 2160 - 1440 + 100);
            Assert.Equal(2160 - 1440, bottom.Y, "close to the bottom edge: lined up with that");

            var left = Arrangement.Snap(placed, dell, -3000, 500);
            Assert.Equal(-2560, left.X, "to the left");
            var below = Arrangement.Snap(placed, dell, 600, 2500);
            Assert.True(below.Y == 2160 && below.X == 600, "underneath");

            var inside = Arrangement.Snap(placed, dell, 1000, 400);
            Assert.True(!inside.Overlaps(main) && inside.Touches(main), "dropped on top of it: moved out to an edge");

            var far = Arrangement.Snap(placed, dell, 12000, -9000);
            Assert.True(far.Touches(main), "dropped far away: still next to it, no gaps");
        }

        [Test]
        public static void TidyClosesGapsAndOverlaps()
        {
            var ok = new List<Box> { new Box(Aorus, 0, 0, 3840, 2160), new Box(Dell, 3840, 300, 2560, 1440) };
            var tidy = Arrangement.Tidy(ok);
            Assert.Equal(3840, tidy[1].X, "a screen that fits stays exactly where it is");
            Assert.Equal(300, tidy[1].Y, "not even lined up");

            // The LG in the middle of a chain was left out: the Dell moves in next to the AORUS
            var gap = new List<Box> { new Box(Aorus, 0, 0, 3840, 2160), new Box(Dell, 7680, 0, 2560, 1440) };
            tidy = Arrangement.Tidy(gap);
            Assert.Equal(3840, tidy[1].X, "the gap closes");
            Assert.True(Fine(tidy), "every screen touches, none overlap");

            // The AORUS went from 2560 to 3840 wide: the Dell next to it would overlap
            var grown = new List<Box> { new Box(Aorus, 0, 0, 3840, 2160), new Box(Dell, 2560, 0, 2560, 1440) };
            tidy = Arrangement.Tidy(grown);
            Assert.Equal(3840, tidy[1].X, "pushed out to the new edge");

            // Main not at 0,0 (captured with a screen to its left): everything moves so it is
            var shifted = new List<Box> { new Box(Aorus, 2560, 0, 3840, 2160), new Box(Dell, 0, 0, 2560, 1440) };
            tidy = Arrangement.Tidy(shifted);
            Assert.True(tidy[0].X == 0 && tidy[0].Y == 0, "main at 0,0");
            Assert.Equal(-2560, tidy[1].X, "the Dell stays to its left");

            var three = new List<Box> { new Box(Aorus, 0, 0, 3840, 2160), new Box(Lg, 3840, 0, 3840, 2160), new Box(Dell, 7680, 0, 2560, 1440) };
            tidy = Arrangement.Tidy(three);
            Assert.Equal(7680, tidy[2].X, "a chain stays a chain");
            Assert.Equal(Dell, tidy[2].Key, "same order as given");
        }

        static KnownScreen ScreenFor(string key)
        {
            var s = new KnownScreen();
            s.Key = key;
            s.DisplayName = key;
            s.DevicePath = @"\\?\DISPLAY#" + key + "#5&1&0&UID1#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
            return s;
        }

        static Setup Desk(params string[] keys)
        {
            var setup = new Setup();
            setup.Id = "desk";
            setup.Name = "Desk";
            foreach (var k in keys)
            {
                setup.Screens.Add(k);
                setup.Placement.Add(SetupMigration.EntryFor(ScreenFor(k)));
            }
            return setup;
        }

        static DisplayMode? Natives(string key)
        {
            if (key == Dell) return new DisplayMode(2560, 1440, 60);
            return new DisplayMode(3840, 2160, 120);
        }

        static void Place(Setup setup, string key, int x, int y, int w, int h)
        {
            var e = setup.Entry(key);
            e.X = x;
            e.Y = y;
            e.PlacedWidth = w;
            e.PlacedHeight = h;
        }

        [Test]
        public static void SwitchingPutsScreensWhereTheSetupSays()
        {
            var setup = Desk(Aorus, Dell, Lg);
            var screens = new List<KnownScreen> { ScreenFor(Aorus), ScreenFor(Dell), ScreenFor(Lg) };

            // Not placed yet: side by side to the right, each at its own size
            var p = RoomManager.BuildSetupProfile(setup, screens, Natives);
            Assert.Equal(3, p.Displays.Count, "three screens");
            Assert.True(p.Displays[0].Primary && p.Displays[0].X == 0 && p.Displays[0].Y == 0, "main at 0,0 and primary");
            Assert.Equal(3840, p.Displays[1].X, "next to the AORUS's native width");
            Assert.Equal(3840 + 2560, p.Displays[2].X, "then the LG");
            Assert.Equal(0, p.Paths.Count, "rebuilt by Windows, modes and places set afterwards");

            // Placed: the Dell on the left, the LG on top
            Place(setup, Aorus, 0, 0, 3840, 2160);
            Place(setup, Dell, -2560, 720, 2560, 1440);
            Place(setup, Lg, 0, -2160, 3840, 2160);
            setup.Entry(Lg).Hdr = Room.HdrOff;
            setup.Entry(Dell).RefreshHz = 60;
            p = RoomManager.BuildSetupProfile(setup, screens, Natives);
            Assert.True(p.Displays[1].X == -2560 && p.Displays[1].Y == 720, "the Dell where it was put");
            Assert.True(p.Displays[2].X == 0 && p.Displays[2].Y == -2160, "the LG above");
            Assert.Equal(0, p.Displays[2].Hdr, "each screen's HDR in this setup");
            Assert.Equal(60.0, p.Displays[1].RefreshHz, "and refresh rate");

            // The Dell isn't connected: the LG keeps its place (it touches the AORUS anyway)
            p = RoomManager.BuildSetupProfile(setup, new List<KnownScreen> { ScreenFor(Aorus), ScreenFor(Lg) }, Natives);
            Assert.Equal(2, p.Displays.Count, "without the Dell");
            Assert.Equal(-2160, p.Displays[1].Y, "the LG still above");

            // The AORUS now runs at 2560 x 1440: the others stay touching it, no gaps or overlaps
            setup.Entry(Aorus).Width = 2560;
            setup.Entry(Aorus).Height = 1440;
            var boxes = RoomManager.Boxes(setup, setup.Screens, Natives);
            Assert.True(Fine(boxes), "smaller main screen: still a proper arrangement");
        }

        [Test]
        public static void TheMainScreenLeadsEvenWhenNotPlaced()
        {
            var setup = Desk(Aorus, Dell);
            Place(setup, Dell, 5000, 0, 2560, 1440); // placed by an old build, main never placed
            var boxes = RoomManager.Boxes(setup, setup.Screens, Natives);
            Assert.True(boxes[0].X == 0 && boxes[0].Y == 0, "main at 0,0");
            Assert.True(Fine(boxes), "the Dell snaps next to it");
        }

        [Test]
        public static void AnOldSavedArrangementBecomesPlaces()
        {
            var screens = new List<KnownScreen> { ScreenFor(Aorus), ScreenFor(Dell) };
            screens[1].RefreshHz = 60;
            var setup = new Setup { Id = "desk", Name = "Desk" };
            setup.Screens.AddRange(new[] { Aorus, Dell });
            setup.Layout = new Profile();
            foreach (var k in new[] { Aorus, Dell })
            {
                var d = new ProfileDisplay();
                d.Identity = new DisplayIdentity();
                d.Identity.DevicePath = screens[0].DevicePath.Replace(Aorus, k);
                d.X = k == Aorus ? 0 : -2560;
                d.Width = k == Aorus ? 3840 : 2560;
                d.Height = k == Aorus ? 2160 : 1440;
                d.Hdr = k == Dell ? 0 : 1;
                setup.Layout.Displays.Add(d);
            }
            SetupMigration.Sanitize(screens, new List<Setup> { setup }, "desk");
            Assert.True(setup.Layout == null, "the old layout goes");
            Assert.Equal(2, setup.Placement.Count, "a place per screen");
            var dell = setup.Entry(Dell);
            Assert.True(dell.Placed && dell.X == -2560, "where it was");
            Assert.Equal(Room.HdrOff, dell.Hdr, "HDR off stays off");
            Assert.Equal(60, dell.RefreshHz, "its mode as the screen last used it");
        }
    }
}
