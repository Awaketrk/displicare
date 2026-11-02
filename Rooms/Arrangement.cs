using System;
using System.Collections.Generic;

namespace Displicare.Rooms
{
    /// <summary>A screen's rectangle on the desktop, in pixels.</summary>
    internal struct Box
    {
        public string Key;
        public int X, Y, W, H;

        public Box(string key, int x, int y, int w, int h)
        {
            Key = key;
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public int Right { get { return X + W; } }
        public int Bottom { get { return Y + H; } }

        public bool Overlaps(Box o)
        {
            return X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
        }

        /// <summary>Shares a piece of an edge with <paramref name="o"/> (a corner alone doesn't count).</summary>
        public bool Touches(Box o)
        {
            bool sideBySide = (X == o.Right || Right == o.X) && Math.Min(Bottom, o.Bottom) - Math.Max(Y, o.Y) > 0;
            bool stacked = (Y == o.Bottom || Bottom == o.Y) && Math.Min(Right, o.Right) - Math.Max(X, o.X) > 0;
            return sideBySide || stacked;
        }

        public Box At(int x, int y)
        {
            return new Box(Key, x, y, W, H);
        }

        public override string ToString()
        {
            return Key + "@" + X + "," + Y + ":" + W + "x" + H;
        }
    }

    /// <summary>
    /// How screens sit next to each other, as in Windows Settings > Display: every screen touches another, none overlap,
    /// and the main one is at 0,0. Used by the setup editor (drag and snap) and when switching, where a screen that runs
    /// at another size or is missing would otherwise leave a gap or an overlap.
    /// </summary>
    internal static class Arrangement
    {
        /// <summary>
        /// The spot nearest to (<paramref name="wantX"/>, <paramref name="wantY"/>) where <paramref name="moving"/> touches
        /// one of <paramref name="placed"/> and overlaps none of them. Edges close to lining up are lined up.
        /// </summary>
        public static Box Snap(IList<Box> placed, Box moving, int wantX, int wantY)
        {
            if (placed.Count == 0) return moving.At(0, 0);
            Box best = moving;
            long bestDistance = long.MaxValue;
            foreach (var o in placed)
            {
                int alignY = Math.Max(1, Math.Min(moving.H, o.H) / 10);
                int alignX = Math.Max(1, Math.Min(moving.W, o.W) / 10);
                int y = Align(Clamp(wantY, o.Y - moving.H + 1, o.Bottom - 1), moving.H, o.Y, o.Bottom, alignY);
                int x = Align(Clamp(wantX, o.X - moving.W + 1, o.Right - 1), moving.W, o.X, o.Right, alignX);
                Consider(placed, moving.At(o.Right, y), wantX, wantY, ref best, ref bestDistance);
                Consider(placed, moving.At(o.X - moving.W, y), wantX, wantY, ref best, ref bestDistance);
                Consider(placed, moving.At(x, o.Bottom), wantX, wantY, ref best, ref bestDistance);
                Consider(placed, moving.At(x, o.Y - moving.H), wantX, wantY, ref best, ref bestDistance);
            }
            if (bestDistance == long.MaxValue)
            {
                // Boxed in on every side (can't happen with real screens): go to the right of everything.
                int right = int.MinValue;
                foreach (var o in placed) right = Math.Max(right, o.Right);
                best = moving.At(right, 0);
            }
            return best;
        }

        static void Consider(IList<Box> placed, Box candidate, int wantX, int wantY, ref Box best, ref long bestDistance)
        {
            foreach (var o in placed) if (candidate.Overlaps(o)) return;
            long dx = candidate.X - wantX, dy = candidate.Y - wantY;
            long distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        static int Clamp(int v, int min, int max)
        {
            return v < min ? min : v > max ? max : v;
        }

        /// <summary>Lines the start or end of a span up with the other screen's, when it's within <paramref name="near"/>.</summary>
        static int Align(int start, int size, int otherStart, int otherEnd, int near)
        {
            if (Math.Abs(start - otherStart) <= near) return otherStart;
            if (Math.Abs(start + size - otherEnd) <= near) return otherEnd - size;
            return start;
        }

        /// <summary>
        /// The same screens with the first (main) one at 0,0 and every other touching the ones before it and overlapping
        /// none: a screen that already does stays exactly where it is, the others move to the nearest spot that works.
        /// Closest to the main screen goes first, so a chain (A | B | C) stays a chain.
        /// </summary>
        public static List<Box> Tidy(IList<Box> boxes)
        {
            var result = new List<Box>();
            if (boxes.Count == 0) return result;
            var main = boxes[0];
            result.Add(main.At(0, 0));

            var rest = new List<Box>();
            for (int i = 1; i < boxes.Count; i++) rest.Add(boxes[i].At(boxes[i].X - main.X, boxes[i].Y - main.Y));
            rest.Sort((a, b) => Distance(a, result[0]).CompareTo(Distance(b, result[0])));

            // Keep picking the screen nearest to what's placed, so each one is placed next to its neighbour.
            while (rest.Count > 0)
            {
                int pick = 0;
                long pickDistance = long.MaxValue;
                for (int i = 0; i < rest.Count; i++)
                {
                    long d = long.MaxValue;
                    foreach (var p in result) d = Math.Min(d, Gap(rest[i], p));
                    if (d < pickDistance) { pick = i; pickDistance = d; }
                }
                var b = rest[pick];
                rest.RemoveAt(pick);
                result.Add(Fits(result, b) ? b : Snap(result, b, b.X, b.Y));
            }

            // Back in the order given.
            var ordered = new List<Box>();
            foreach (var b in boxes) ordered.Add(result.Find(r => r.Key == b.Key));
            return ordered;
        }

        /// <summary>Touches one of <paramref name="placed"/> and overlaps none.</summary>
        static bool Fits(IList<Box> placed, Box b)
        {
            bool touches = false;
            foreach (var p in placed)
            {
                if (b.Overlaps(p)) return false;
                if (b.Touches(p)) touches = true;
            }
            return touches;
        }

        static long Distance(Box a, Box b)
        {
            long dx = (a.X + a.W / 2) - (b.X + b.W / 2), dy = (a.Y + a.H / 2) - (b.Y + b.H / 2);
            return dx * dx + dy * dy;
        }

        /// <summary>How far apart two rectangles are (0 when they touch or overlap).</summary>
        static long Gap(Box a, Box b)
        {
            long dx = Math.Max(0, Math.Max(a.X - b.Right, b.X - a.Right));
            long dy = Math.Max(0, Math.Max(a.Y - b.Bottom, b.Y - a.Bottom));
            return dx * dx + dy * dy;
        }

        /// <summary>Puts a screen that has no place yet to the right of the others, top-aligned with the main one.</summary>
        public static Box Append(IList<Box> placed, Box b)
        {
            int right = 0;
            foreach (var p in placed) right = Math.Max(right, p.Right);
            return b.At(right, placed.Count > 0 ? placed[0].Y : 0);
        }
    }
}
