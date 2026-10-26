using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace Displicare.Rooms
{
    /// <summary>A resolution and whole refresh rate, the way Windows' display settings list them.</summary>
    internal struct DisplayMode
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int Hz;

        public DisplayMode(int width, int height, int hz)
        {
            Width = width;
            Height = height;
            Hz = hz;
        }

        public override string ToString()
        {
            return Width + "x" + Height + " @ " + Hz + " Hz";
        }
    }

    /// <summary>
    /// Reads the modes a display advertises in its EDID, so a room can list them before it has ever been switched to.
    /// Covers the base block's detailed timings, CTA-861 (detailed timings and the common VIC codes) and DisplayID
    /// type I/VII timings. Windows can offer more once the display is on (an LG TV's 144 Hz, for example); rooms
    /// remember those after the first switch.
    /// </summary>
    internal static class Edid
    {
        /// <summary>The EDID Windows stored for a monitor, from its device path (\\?\DISPLAY#GSM82CD#5&amp;...#{guid}).</summary>
        public static byte[] Read(string monitorDevicePath)
        {
            if (string.IsNullOrEmpty(monitorDevicePath)) return null;
            string[] parts = monitorDevicePath.Split('#');
            if (parts.Length < 3) return null;
            try
            {
                string key = @"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + @"\" + parts[2] + @"\Device Parameters";
                using (var k = Registry.LocalMachine.OpenSubKey(key, false))
                    return k != null ? k.GetValue("EDID") as byte[] : null;
            }
            catch (Exception) { return null; }
        }

        public static bool IsValid(byte[] edid)
        {
            if (edid == null || edid.Length < 128) return false;
            if (edid[0] != 0 || edid[7] != 0) return false;
            for (int i = 1; i < 7; i++) if (edid[i] != 0xFF) return false;
            return true;
        }

        /// <summary>Every progressive mode found, best first (most pixels, then highest refresh rate).</summary>
        public static List<DisplayMode> Modes(byte[] edid)
        {
            var modes = new List<DisplayMode>();
            if (!IsValid(edid)) return modes;

            for (int offset = 54; offset <= 108; offset += 18) AddDetailedTiming(edid, offset, modes);

            for (int block = 128; block + 128 <= edid.Length; block += 128)
            {
                if (edid[block] == 0x02) ReadCta(edid, block, modes);
                else if (edid[block] == 0x70) ReadDisplayId(edid, block, modes);
            }

            var unique = new List<DisplayMode>();
            var seen = new HashSet<string>();
            foreach (var m in modes) if (m.Width > 0 && m.Hz > 0 && seen.Add(m.ToString())) unique.Add(m);
            unique.Sort(delegate (DisplayMode a, DisplayMode b)
            {
                long pa = (long)a.Width * a.Height, pb = (long)b.Width * b.Height;
                if (pa != pb) return pb.CompareTo(pa);
                return b.Hz.CompareTo(a.Hz);
            });
            return unique;
        }

        /// <summary>
        /// The display's native resolution: the first detailed timing of the base block, which EDID reserves for the
        /// preferred mode. TVs also advertise 4096x2160 (cinema 4K), which has more pixels but isn't their panel.
        /// </summary>
        public static DisplayMode? Preferred(byte[] edid)
        {
            if (!IsValid(edid)) return null;
            var modes = new List<DisplayMode>();
            AddDetailedTiming(edid, 54, modes);
            if (modes.Count == 0) return null;
            return modes[0];
        }

        /// <summary>True when a CTA-861 HDR static metadata block lists the PQ (SMPTE ST 2084) curve, i.e. HDR10.</summary>
        public static bool SupportsHdr(byte[] edid)
        {
            if (!IsValid(edid)) return false;
            for (int block = 128; block + 128 <= edid.Length; block += 128)
            {
                if (edid[block] != 0x02) continue;
                int end = Math.Min((int)edid[block + 2], 127);
                for (int i = block + 4; i < block + end; )
                {
                    int tag = edid[i] >> 5, len = edid[i] & 0x1F;
                    if (tag == 7 && len >= 2 && edid[i + 1] == 6 && (edid[i + 2] & 0x04) != 0) return true;
                    i += len + 1;
                }
            }
            return false;
        }

        static void AddDetailedTiming(byte[] e, int o, List<DisplayMode> modes)
        {
            if (o + 18 > e.Length) return;
            int clock = e[o] | (e[o + 1] << 8); // 10 kHz units; 0 marks a text/range descriptor
            if (clock == 0) return;
            if ((e[o + 17] & 0x80) != 0) return; // interlaced
            int hActive = e[o + 2] | ((e[o + 4] & 0xF0) << 4);
            int hBlank = e[o + 3] | ((e[o + 4] & 0x0F) << 8);
            int vActive = e[o + 5] | ((e[o + 7] & 0xF0) << 4);
            int vBlank = e[o + 6] | ((e[o + 7] & 0x0F) << 8);
            Add(modes, hActive, vActive, clock * 10000.0, hActive + hBlank, vActive + vBlank);
        }

        static void ReadCta(byte[] e, int block, List<DisplayMode> modes)
        {
            int dtdStart = e[block + 2];
            if (dtdStart < 4 || dtdStart > 127) dtdStart = 127;
            for (int i = block + 4; i < block + dtdStart; )
            {
                int tag = e[i] >> 5, len = e[i] & 0x1F;
                if (i + len >= block + 128) break;
                if (tag == 2) for (int j = 1; j <= len; j++) AddVic(Svd(e[i + j]), modes);
                // YCbCr 4:2:0 video data block: modes only offered in 4:2:0, still selectable in Windows
                if (tag == 7 && len >= 1 && e[i + 1] == 14) for (int j = 2; j <= len; j++) AddVic(Svd(e[i + j]), modes);
                i += len + 1;
            }
            if (dtdStart < 127)
                for (int o = block + dtdStart; o + 18 <= block + 127; o += 18) AddDetailedTiming(e, o, modes);
        }

        /// <summary>A short video descriptor: codes 129-192 are VIC 1-64 with the "native" bit set.</summary>
        static int Svd(byte b)
        {
            return b >= 129 && b <= 192 ? b & 0x7F : b;
        }

        static void ReadDisplayId(byte[] e, int block, List<DisplayMode> modes)
        {
            int sectionLength = e[block + 2];
            int end = Math.Min(block + 5 + sectionLength, block + 127);
            for (int i = block + 5; i + 3 <= end; )
            {
                int tag = e[i], len = e[i + 2];
                if (tag == 0 && len == 0) break; // padding
                int payload = i + 3;
                // 0x03: type I timings (10 kHz clock units); 0x22: DisplayID 2.0 type VII (1 kHz units)
                if (tag == 0x03 || tag == 0x22)
                {
                    double unit = tag == 0x03 ? 10000.0 : 1000.0;
                    for (int t = payload; t + 20 <= payload + len && t + 20 <= e.Length; t += 20)
                    {
                        if ((e[t + 3] & 0x10) != 0) continue; // interlaced
                        double clock = ((e[t] | (e[t + 1] << 8) | (e[t + 2] << 16)) + 1) * unit;
                        int hActive = (e[t + 4] | (e[t + 5] << 8)) + 1;
                        int hBlank = (e[t + 6] | (e[t + 7] << 8)) + 1;
                        int vActive = (e[t + 12] | (e[t + 13] << 8)) + 1;
                        int vBlank = (e[t + 14] | (e[t + 15] << 8)) + 1;
                        Add(modes, hActive, vActive, clock, hActive + hBlank, vActive + vBlank);
                    }
                }
                i = payload + len;
            }
        }

        static void Add(List<DisplayMode> modes, int width, int height, double pixelClock, int hTotal, int vTotal)
        {
            if (width <= 0 || height <= 0 || hTotal <= 0 || vTotal <= 0) return;
            int hz = (int)Math.Round(pixelClock / ((double)hTotal * vTotal));
            if (hz > 0) modes.Add(new DisplayMode(width, height, hz));
        }

        /// <summary>The progressive CTA-861 video codes PC displays and TVs actually advertise.</summary>
        static readonly Dictionary<int, int[]> Vics = new Dictionary<int, int[]>
        {
            { 1, new[] { 640, 480, 60 } }, { 2, new[] { 720, 480, 60 } }, { 3, new[] { 720, 480, 60 } },
            { 4, new[] { 1280, 720, 60 } }, { 16, new[] { 1920, 1080, 60 } }, { 17, new[] { 720, 576, 50 } },
            { 18, new[] { 720, 576, 50 } }, { 19, new[] { 1280, 720, 50 } }, { 31, new[] { 1920, 1080, 50 } },
            { 32, new[] { 1920, 1080, 24 } }, { 33, new[] { 1920, 1080, 25 } }, { 34, new[] { 1920, 1080, 30 } },
            { 47, new[] { 1280, 720, 120 } }, { 63, new[] { 1920, 1080, 120 } }, { 64, new[] { 1920, 1080, 100 } },
            { 93, new[] { 3840, 2160, 24 } }, { 94, new[] { 3840, 2160, 25 } }, { 95, new[] { 3840, 2160, 30 } },
            { 96, new[] { 3840, 2160, 50 } }, { 97, new[] { 3840, 2160, 60 } },
            { 98, new[] { 4096, 2160, 24 } }, { 99, new[] { 4096, 2160, 25 } }, { 100, new[] { 4096, 2160, 30 } },
            { 101, new[] { 4096, 2160, 50 } }, { 102, new[] { 4096, 2160, 60 } },
            { 103, new[] { 3840, 2160, 24 } }, { 104, new[] { 3840, 2160, 25 } }, { 105, new[] { 3840, 2160, 30 } },
            { 106, new[] { 3840, 2160, 50 } }, { 107, new[] { 3840, 2160, 60 } },
            { 114, new[] { 3840, 2160, 48 } }, { 115, new[] { 4096, 2160, 48 } }, { 116, new[] { 3840, 2160, 48 } },
            { 117, new[] { 3840, 2160, 100 } }, { 118, new[] { 3840, 2160, 120 } },
            { 119, new[] { 3840, 2160, 100 } }, { 120, new[] { 3840, 2160, 120 } },
            { 218, new[] { 4096, 2160, 100 } }, { 219, new[] { 4096, 2160, 120 } },
        };

        static void AddVic(int vic, List<DisplayMode> modes)
        {
            int[] m;
            if (Vics.TryGetValue(vic, out m)) modes.Add(new DisplayMode(m[0], m[1], m[2]));
        }
    }
}
