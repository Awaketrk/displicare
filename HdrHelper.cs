using System;
using System.Runtime.InteropServices;

namespace Displicare
{
    /// <summary>
    /// Windows HDR ("advanced color") detection via the DisplayConfig API.
    /// In HDR mode the legacy gamma ramp has undefined behavior, so software brightness uses
    /// <see cref="ColorFilterDimmer"/> instead.
    /// </summary>
    public static class HdrHelper
    {
        private const uint QDC_ONLY_ACTIVE_PATHS = 2;
        private const uint TYPE_GET_SOURCE_NAME = 1;
        private const uint TYPE_GET_ADVANCED_COLOR_INFO = 9;

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

        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topologyId);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME info);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref ADVANCED_COLOR_INFO info);

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

                    var color = new ADVANCED_COLOR_INFO();
                    color.header.type = TYPE_GET_ADVANCED_COLOR_INFO;
                    color.header.size = (uint)Marshal.SizeOf(typeof(ADVANCED_COLOR_INFO));
                    color.header.adapterId = paths[i].targetInfo.adapterId;
                    color.header.id = paths[i].targetInfo.id;
                    return DisplayConfigGetDeviceInfo(ref color) == 0 && (color.value & 0x2) != 0; // advancedColorEnabled
                }
            }
            catch { }
            return false;
        }
    }
}
