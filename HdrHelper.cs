using System;
using System.Runtime.InteropServices;

namespace Displicare
{
    /// <summary>
    /// Windows HDR ("advanced color") detection via the DisplayConfig API.
    /// Microsoft documents the legacy gamma ramp as undefined in HDR; Displicare still uses it there (the Windows
    /// color filter, the alternative, turns off VRR in games), so this is only reported on the cards and in the hardware test.
    /// </summary>
    public static class HdrHelper
    {
        private const uint QDC_ONLY_ACTIVE_PATHS = 2;
        private const uint TYPE_GET_SOURCE_NAME = 1;
        private const uint TYPE_GET_ADVANCED_COLOR_INFO = 9;
        private const uint TYPE_GET_ADVANCED_COLOR_INFO_2 = 15; // Windows 11 24H2+

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RATIONAL { public uint Numerator; public uint Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology;
            public uint rotation; public uint scaling; public RATIONAL refreshRate; public uint scanLineOrdering;
            public int targetAvailable; public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MODE_INFO
        {
            public uint infoType; public uint id; public LUID adapterId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] modeData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SOURCE_DEVICE_NAME
        {
            public HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ADVANCED_COLOR_INFO { public HEADER header; public uint value; public uint colorEncoding; public uint bitsPerColorChannel; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ADVANCED_COLOR_INFO_2 { public HEADER header; public uint value; public uint colorEncoding; public uint bitsPerColorChannel; public uint activeColorMode; }

        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topologyId);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME info);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref ADVANCED_COLOR_INFO info);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref ADVANCED_COLOR_INFO_2 info);

        /// <summary>DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR in ADVANCED_COLOR_INFO_2 (0 = SDR, 1 = wide color SDR).</summary>
        internal const int ColorModeHdr = 2;

        /// <summary>
        /// Whether a display is in HDR. Windows also turns "advanced color" on in SDR when Auto Color Management (wide
        /// color) is on, so advancedColorEnabled (0x2) alone isn't HDR: up to 2.5.0 an SDR screen with ACM got the HDR
        /// curve, which barely dimmed white and crushed blacks. <paramref name="activeColorMode"/> is from
        /// ADVANCED_COLOR_INFO_2 (24H2+), -1 without it; then wideColorEnforced (0x4) tells ACM from HDR.
        /// </summary>
        internal static bool IsHdrMode(uint value, int activeColorMode)
        {
            if (activeColorMode >= 0) return activeColorMode == ColorModeHdr;
            return (value & 0x2) != 0 && (value & 0x4) == 0;
        }

        /// <summary>True if Windows HDR is currently on for the display with this GDI name (e.g. \\.\DISPLAY1).</summary>
        public static bool IsHdrEnabled(string gdiDeviceName)
        {
            try
            {
                uint numPaths, numModes;
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out numPaths, out numModes) != 0) return false;
                var paths = new PATH_INFO[numPaths];
                var modes = new MODE_INFO[numModes];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero) != 0) return false;

                for (int i = 0; i < numPaths; i++)
                {
                    var src = new SOURCE_DEVICE_NAME();
                    src.header.type = TYPE_GET_SOURCE_NAME;
                    src.header.size = (uint)Marshal.SizeOf(typeof(SOURCE_DEVICE_NAME));
                    src.header.adapterId = paths[i].sourceInfo.adapterId;
                    src.header.id = paths[i].sourceInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;
                    if (!string.Equals(src.viewGdiDeviceName, gdiDeviceName, StringComparison.OrdinalIgnoreCase)) continue;

                    var color2 = new ADVANCED_COLOR_INFO_2();
                    color2.header.type = TYPE_GET_ADVANCED_COLOR_INFO_2;
                    color2.header.size = (uint)Marshal.SizeOf(typeof(ADVANCED_COLOR_INFO_2));
                    color2.header.adapterId = paths[i].targetInfo.adapterId;
                    color2.header.id = paths[i].targetInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref color2) == 0) return IsHdrMode(color2.value, (int)color2.activeColorMode);

                    var color = new ADVANCED_COLOR_INFO();
                    color.header.type = TYPE_GET_ADVANCED_COLOR_INFO;
                    color.header.size = (uint)Marshal.SizeOf(typeof(ADVANCED_COLOR_INFO));
                    color.header.adapterId = paths[i].targetInfo.adapterId;
                    color.header.id = paths[i].targetInfo.id;
                    return DisplayConfigGetDeviceInfo(ref color) == 0 && IsHdrMode(color.value, -1);
                }
            }
            catch { }
            return false;
        }
    }
}
