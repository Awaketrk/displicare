using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Displicare.Rooms
{
    /// <summary>A path/mode array pair as returned by QueryDisplayConfig.</summary>
    internal sealed class DisplayConfig
    {
        public DISPLAYCONFIG_PATH_INFO[] Paths;
        public DISPLAYCONFIG_MODE_INFO[] Modes;
    }

    /// <summary>One monitor connector (target) Windows knows about, active or not.</summary>
    internal sealed class TargetInfo
    {
        public long AdapterLuid;
        public uint TargetId;
        public bool Available;
        public bool Active;
        public uint OutputTechnology;
        public DisplayIdentity Identity;

        public bool IsVirtual { get { return DisplayConfigApi.IsVirtual(OutputTechnology, Identity); } }
    }

    /// <summary>An active display as the user sees it.</summary>
    internal sealed class ActiveDisplay
    {
        public DisplayIdentity Identity;
        public string GdiName;
        public bool Primary;
        public int X, Y, Width, Height;
        public double RefreshHz;
        public int Hdr;
        public uint OutputTechnology;
        public bool IsVirtual { get { return DisplayConfigApi.IsVirtual(OutputTechnology, Identity); } }
    }

    /// <summary>Reads the live display configuration through the CCD API.</summary>
    internal static class DisplayTopology
    {
        public static DisplayConfig Query(uint flags)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                uint numPaths, numModes;
                int rc = DisplayConfigApi.GetDisplayConfigBufferSizes(flags, out numPaths, out numModes);
                if (rc != DisplayConfigApi.ERROR_SUCCESS) throw new Win32Exception(rc, "GetDisplayConfigBufferSizes failed");

                var paths = new DISPLAYCONFIG_PATH_INFO[numPaths];
                var modes = new DISPLAYCONFIG_MODE_INFO[numModes];
                rc = DisplayConfigApi.QueryDisplayConfig(flags, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);
                if (rc == DisplayConfigApi.ERROR_INSUFFICIENT_BUFFER) continue; // topology changed between the two calls
                if (rc != DisplayConfigApi.ERROR_SUCCESS) throw new Win32Exception(rc, "QueryDisplayConfig failed");

                Array.Resize(ref paths, (int)numPaths);
                Array.Resize(ref modes, (int)numModes);
                var config = new DisplayConfig();
                config.Paths = paths;
                config.Modes = modes;
                return config;
            }
            throw new InvalidOperationException("The display configuration kept changing while it was being read.");
        }

        public static DisplayIdentity GetIdentity(LUID adapter, uint targetId, out uint outputTechnology)
        {
            var name = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
            name.header = DisplayConfigApi.Header(DisplayConfigApi.DEVICE_INFO_GET_TARGET_NAME,
                Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME)), adapter, targetId);
            outputTechnology = 0;
            var identity = new DisplayIdentity();
            identity.AdapterPath = GetAdapterPath(adapter);
            if (DisplayConfigApi.DisplayConfigGetDeviceInfo(ref name) != DisplayConfigApi.ERROR_SUCCESS) return identity;

            outputTechnology = name.outputTechnology;
            identity.DevicePath = name.monitorDevicePath;
            identity.FriendlyName = name.monitorFriendlyDeviceName;
            identity.EdidManufacturer = name.edidManufactureId;
            identity.EdidProduct = name.edidProductCodeId;
            identity.ConnectorInstance = name.connectorInstance;
            return identity;
        }

        static readonly Dictionary<long, string> AdapterPathCache = new Dictionary<long, string>();

        public static string GetAdapterPath(LUID adapter)
        {
            string cached;
            if (AdapterPathCache.TryGetValue(adapter.Value, out cached)) return cached;
            var name = new DISPLAYCONFIG_ADAPTER_NAME();
            name.header = DisplayConfigApi.Header(DisplayConfigApi.DEVICE_INFO_GET_ADAPTER_NAME,
                Marshal.SizeOf(typeof(DISPLAYCONFIG_ADAPTER_NAME)), adapter, 0);
            string path = DisplayConfigApi.DisplayConfigGetDeviceInfo(ref name) == DisplayConfigApi.ERROR_SUCCESS ? name.adapterDevicePath : "";
            AdapterPathCache[adapter.Value] = path;
            return path;
        }

        /// <summary>GDI name of a source, e.g. \\.\DISPLAY1 (used by ChangeDisplaySettingsEx).</summary>
        public static string GetSourceGdiName(LUID adapter, uint sourceId)
        {
            var name = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            name.header = DisplayConfigApi.Header(DisplayConfigApi.DEVICE_INFO_GET_SOURCE_NAME,
                Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME)), adapter, sourceId);
            return DisplayConfigApi.DisplayConfigGetDeviceInfo(ref name) == DisplayConfigApi.ERROR_SUCCESS ? name.viewGdiDeviceName : null;
        }

        /// <summary>-1 if HDR isn't supported (or unknown), 0 if off, 1 if on.</summary>
        public static int GetHdrState(LUID adapter, uint targetId)
        {
            var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
            info.header = DisplayConfigApi.Header(DisplayConfigApi.DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO)), adapter, targetId);
            if (DisplayConfigApi.DisplayConfigGetDeviceInfo(ref info) != DisplayConfigApi.ERROR_SUCCESS) return -1;
            if ((info.value & 0x1) == 0) return -1; // advancedColorSupported
            return (info.value & 0x2) != 0 ? 1 : 0;  // advancedColorEnabled
        }

        public static bool SetHdrState(LUID adapter, uint targetId, bool enabled)
        {
            var state = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
            state.header = DisplayConfigApi.Header(DisplayConfigApi.DEVICE_INFO_SET_ADVANCED_COLOR_STATE,
                Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE)), adapter, targetId);
            state.value = enabled ? 1u : 0u;
            return DisplayConfigApi.DisplayConfigSetDeviceInfo(ref state) == DisplayConfigApi.ERROR_SUCCESS;
        }

        /// <summary>Every distinct target in an all-paths query (one entry per monitor connector).</summary>
        public static List<TargetInfo> DescribeTargets(DisplayConfig all)
        {
            var list = new List<TargetInfo>();
            var byKey = new Dictionary<string, TargetInfo>();
            foreach (var p in all.Paths)
            {
                string key = p.targetInfo.adapterId.Value + ":" + p.targetInfo.id;
                TargetInfo t;
                if (!byKey.TryGetValue(key, out t))
                {
                    t = new TargetInfo();
                    t.AdapterLuid = p.targetInfo.adapterId.Value;
                    t.TargetId = p.targetInfo.id;
                    uint tech;
                    t.Identity = GetIdentity(p.targetInfo.adapterId, p.targetInfo.id, out tech);
                    t.OutputTechnology = tech != 0 ? tech : p.targetInfo.outputTechnology;
                    byKey[key] = t;
                    list.Add(t);
                }
                if (p.targetInfo.targetAvailable != 0) t.Available = true;
                if ((p.flags & DisplayConfigApi.DISPLAYCONFIG_PATH_ACTIVE) != 0) t.Active = true;
            }
            return list;
        }

        public static List<ActiveDisplay> GetActiveDisplays()
        {
            var config = Query(DisplayConfigApi.QDC_ONLY_ACTIVE_PATHS);
            var list = new List<ActiveDisplay>();
            foreach (var p in config.Paths)
            {
                var d = new ActiveDisplay();
                uint tech;
                d.Identity = GetIdentity(p.targetInfo.adapterId, p.targetInfo.id, out tech);
                d.OutputTechnology = tech != 0 ? tech : p.targetInfo.outputTechnology;
                d.GdiName = GetSourceGdiName(p.sourceInfo.adapterId, p.sourceInfo.id);
                d.Hdr = GetHdrState(p.targetInfo.adapterId, p.targetInfo.id);
                FillMode(config, p, d);
                list.Add(d);
            }
            return list;
        }

        static void FillMode(DisplayConfig config, DISPLAYCONFIG_PATH_INFO p, ActiveDisplay d)
        {
            if (p.sourceInfo.modeInfoIdx < config.Modes.Length)
            {
                var src = config.Modes[p.sourceInfo.modeInfoIdx].sourceMode;
                d.Width = (int)src.width;
                d.Height = (int)src.height;
                d.X = src.position.x;
                d.Y = src.position.y;
                d.Primary = d.X == 0 && d.Y == 0;
            }
            d.RefreshHz = Hz(p.targetInfo.refreshRate);
            if (p.targetInfo.modeInfoIdx < config.Modes.Length)
            {
                double hz = Hz(config.Modes[p.targetInfo.modeInfoIdx].targetMode.vSyncFreq);
                if (hz > 0) d.RefreshHz = hz;
            }
        }

        internal static double Hz(DISPLAYCONFIG_RATIONAL r)
        {
            return r.Denominator == 0 ? 0 : (double)r.Numerator / r.Denominator;
        }
    }
}
