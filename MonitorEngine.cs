using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Displicare
{
    public class DisplayInfo
    {
        public DisplayInfo()
        {
            UserBrightness = 80;
            ColorTemperature = 6500;
            RedGain = 1.0;
            GreenGain = 1.0;
            BlueGain = 1.0;
            Contrast = 1.0;
            Gamma = 1.0;
            UseHardwareBrightness = true;
        }

        public string DeviceName { get; set; }        // e.g. \\.\DISPLAY1
        public string FriendlyName { get; set; }      // e.g. AORUS FO32U2
        public string DeviceString { get; set; }      // e.g. NVIDIA GeForce RTX 5080
        public bool IsPrimary { get; set; }
        public int ResolutionWidth { get; set; }
        public int ResolutionHeight { get; set; }
        public int BoundsX { get; set; }
        public int BoundsY { get; set; }
        public int BoundsWidth { get; set; }
        public int BoundsHeight { get; set; }
        public IntPtr HMonitor { get; set; }
        public bool SupportsDdcCi { get; set; }
        public uint MinBrightness { get; set; }
        public uint MaxBrightness { get; set; }
        public uint CurrentBrightness { get; set; }

        public int UserBrightness { get; set; }
        public int ColorTemperature { get; set; } // Kelvin
        public double RedGain { get; set; }
        public double GreenGain { get; set; }
        public double BlueGain { get; set; }
        public double Contrast { get; set; }
        public double Gamma { get; set; }
        public bool UseHardwareBrightness { get; set; }

        public ushort[] OriginalRamp { get; set; }

        // Last ramp written by ApplyColorAndGamma; the watchdog compares against it instead of recomputing & rewriting
        public ushort[] LastAppliedRamp { get; set; }

        // Windows HDR state. In HDR, software brightness uses the color filter dimmer instead of the gamma ramp.
        public bool IsHdr { get; set; }

        public bool UsesSoftwareBrightness { get { return !SupportsDdcCi || !UseHardwareBrightness; } }
    }

    public class ColorPreset
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Description { get; set; }
        public int ColorTemp { get; set; }
        public double RedGain { get; set; }
        public double GreenGain { get; set; }
        public double BlueGain { get; set; }
        public double Contrast { get; set; }
        public double Gamma { get; set; }
    }

    public class MonitorEngine : IDisposable
    {
        #region Win32 API Definitions

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint pdwMinimumBrightness, out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool GetDeviceGammaRamp(IntPtr hdc, [Out] ushort[] lpRamp);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, [In] ushort[] lpRamp);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        #endregion

        private readonly List<DisplayInfo> _displays;
        private readonly object _displaysLock = new object();

        public List<DisplayInfo> Displays
        {
            get
            {
                lock (_displaysLock)
                {
                    return new List<DisplayInfo>(_displays);
                }
            }
        }

        // Reusable Gamma Ramp buffer to eliminate memory allocations and GC churn
        private readonly object _gammaRampLock = new object();
        private readonly ushort[] _gammaBuffer = new ushort[768];
        private readonly ushort[] _readBackBuffer = new ushort[768];

        // Drivers with 10/12-bit LUTs return slightly quantized values on read-back
        private const int RampCompareTolerance = 256;

        // Hardware I2C auxiliary bus synchronization lock (prevents bus collisions across threads)
        private static readonly object _i2cBusLock = new object();

        // DDC/CI I2C Throttle Queue
        private readonly object _ddcSyncLock = new object();
        private readonly Dictionary<string, int> _pendingBrightness = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool _ddcWorkerActive = false;
        private volatile bool _disposed = false;
        private volatile int _refreshGeneration;

        public MonitorEngine()
        {
            _displays = new List<DisplayInfo>();
            RefreshDisplays();
        }

        private static bool TryFindExistingByFriendly(Dictionary<string, DisplayInfo> map, string friendly, out DisplayInfo match)
        {
            match = null;
            if (string.IsNullOrEmpty(friendly) || map == null) return false;
            foreach (var kvp in map)
            {
                if (string.Equals(kvp.Value.FriendlyName, friendly, StringComparison.OrdinalIgnoreCase))
                {
                    match = kvp.Value;
                    return true;
                }
            }
            return false;
        }

        public void RefreshDisplays()
        {
            var newDisplays = new List<DisplayInfo>();

            // Snapshot existing displays to preserve user calibration & original ramps across re-enumerations
            var oldMap = new Dictionary<string, DisplayInfo>(StringComparer.OrdinalIgnoreCase);
            lock (_displaysLock)
            {
                foreach (var old in _displays)
                {
                    if (!string.IsNullOrEmpty(old.DeviceName))
                    {
                        oldMap[old.DeviceName] = old;
                    }
                }
            }

            // Step 1: Collect all active monitor handles and their GDI device names
            var hMonMap = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
            var hMonRects = new Dictionary<string, RECT>(StringComparer.OrdinalIgnoreCase);

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data) =>
            {
                MONITORINFOEX mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(mi);
                if (GetMonitorInfo(hMon, ref mi))
                {
                    string dev = mi.szDevice;
                    hMonMap[dev] = hMon;
                    hMonRects[dev] = mi.rcMonitor;
                }
                return true;
            }, IntPtr.Zero);

            // Step 2: Enumerate Display Devices to get friendly names
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);
            uint devNum = 0;

            while (EnumDisplayDevices(null, devNum, ref d, 0))
            {
                // Attached to the desktop, and not a virtual display (a stream's screen: the device shows it with its own brightness)
                if ((d.StateFlags & 1) != 0 && !IsVirtualAdapter(d.DeviceID))
                {
                    string devName = d.DeviceName;
                    string friendlyName = "Display " + (devNum + 1);

                    DISPLAY_DEVICE mon = new DISPLAY_DEVICE();
                    mon.cb = Marshal.SizeOf(mon);
                    if (EnumDisplayDevices(devName, 0, ref mon, 0))
                    {
                        if (!string.IsNullOrWhiteSpace(mon.DeviceString))
                        {
                            friendlyName = mon.DeviceString.Trim();
                        }
                    }

                    bool isPrimary = (d.StateFlags & 4) != 0;

                    DisplayInfo existing = null;
                    bool hasExisting = oldMap.TryGetValue(devName, out existing) ||
                                       (!string.IsNullOrEmpty(friendlyName) && TryFindExistingByFriendly(oldMap, friendlyName, out existing));

                    var disp = new DisplayInfo();
                    disp.DeviceName = devName;
                    disp.FriendlyName = friendlyName;
                    disp.DeviceString = d.DeviceString;
                    disp.IsPrimary = isPrimary;

                    if (hasExisting && existing != null)
                    {
                        disp.UserBrightness = existing.UserBrightness;
                        disp.ColorTemperature = existing.ColorTemperature;
                        disp.RedGain = existing.RedGain;
                        disp.GreenGain = existing.GreenGain;
                        disp.BlueGain = existing.BlueGain;
                        disp.Contrast = existing.Contrast;
                        disp.Gamma = existing.Gamma;
                        disp.UseHardwareBrightness = existing.UseHardwareBrightness;
                        disp.OriginalRamp = existing.OriginalRamp;
                    }
                    else
                    {
                        disp.UserBrightness = 80;
                        disp.ColorTemperature = 6500;
                        disp.RedGain = 1.0;
                        disp.GreenGain = 1.0;
                        disp.BlueGain = 1.0;
                        disp.Contrast = 1.0;
                        disp.Gamma = 1.0;
                        disp.UseHardwareBrightness = true;
                    }

                    RECT rect;
                    if (hMonRects.TryGetValue(devName, out rect))
                    {
                        disp.ResolutionWidth = Math.Abs(rect.right - rect.left);
                        disp.ResolutionHeight = Math.Abs(rect.bottom - rect.top);
                        disp.BoundsX = rect.left;
                        disp.BoundsY = rect.top;
                        disp.BoundsWidth = disp.ResolutionWidth;
                        disp.BoundsHeight = disp.ResolutionHeight;
                    }

                    IntPtr hMon;
                    if (hMonMap.TryGetValue(devName, out hMon))
                    {
                        disp.HMonitor = hMon;

                        // Query Physical Monitor capabilities on-demand, then destroy immediately
                        uint count = 0;
                        if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count) && count > 0)
                        {
                            var physArray = new PHYSICAL_MONITOR[count];
                            if (GetPhysicalMonitorsFromHMONITOR(hMon, count, physArray))
                            {
                                try
                                {
                                    bool foundBrightness = false;
                                    for (uint p = 0; p < count; p++)
                                    {
                                        if (!string.IsNullOrWhiteSpace(physArray[p].szPhysicalMonitorDescription) &&
                                            !physArray[p].szPhysicalMonitorDescription.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
                                        {
                                            disp.FriendlyName = physArray[p].szPhysicalMonitorDescription;
                                        }

                                        uint minB = 0, curB = 100, maxB = 100;
                                        bool read;
                                        lock (_i2cBusLock) read = GetMonitorBrightness(physArray[p].hPhysicalMonitor, out minB, out curB, out maxB);
                                        if (read)
                                        {
                                            SetDdcRange(disp, minB, curB, maxB);
                                            if (!hasExisting)
                                            {
                                                disp.UserBrightness = (int)disp.CurrentBrightness;
                                                disp.UseHardwareBrightness = true;
                                            }
                                            foundBrightness = true;
                                            break;
                                        }
                                    }

                                    // A miss is often temporary (a monitor waking up, or busy right after sign-in), so it
                                    // leaves the user's hardware/software choice alone; RetryDdc keeps asking.
                                    if (!foundBrightness) disp.SupportsDdcCi = false;
                                    App.Log("DDC/CI " + disp.FriendlyName + ": " + (foundBrightness
                                        ? "backlight " + disp.CurrentBrightness + "% (range " + disp.MinBrightness + "-" + disp.MaxBrightness + ")"
                                        : "no answer" + (disp.UseHardwareBrightness ? ", will retry" : "")));
                                }
                                finally
                                {
                                    DestroyPhysicalMonitors(count, physArray);
                                }
                            }
                        }
                    }

                    // Save snapshot of original baseline Gamma Ramp only if not already captured
                    if (disp.OriginalRamp == null)
                    {
                        CaptureOriginalGammaRamp(disp);
                    }

                    // HDR state can change at runtime (Win+Alt+B), which also triggers this refresh
                    disp.IsHdr = HdrHelper.IsHdrEnabled(devName);

                    newDisplays.Add(disp);
                }
                devNum++;
            }

            int generation;
            lock (_displaysLock)
            {
                _displays.Clear();
                _displays.AddRange(newDisplays);
                generation = ++_refreshGeneration;
            }

            // The filter covers every screen, a stream's too: with no HDR display left (say only a stream is on), clear it.
            if (FilterSource(newDisplays) == null) UpdateColorFilter();

            var waiting = newDisplays.FindAll(x => !x.SupportsDdcCi && x.UseHardwareBrightness && x.HMonitor != IntPtr.Zero);
            if (waiting.Count > 0) ThreadPool.QueueUserWorkItem(_ => RetryDdc(waiting, generation));
        }

        /// <summary>
        /// Virtual display drivers (SudoVDA for Steam Link and Apollo, Parsec, Virtual Display Driver) are software devices
        /// with a ROOT\ device ID, e.g. root\sudomaker\sudovda; graphics cards are PCI\VEN_...
        /// </summary>
        internal static bool IsVirtualAdapter(string deviceId)
        {
            return deviceId != null && deviceId.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Seconds after a refresh to ask again a display set to hardware brightness that didn't answer.</summary>
        private static readonly int[] DdcRetrySeconds = { 1, 3, 10, 30 };

        /// <summary>
        /// Raised (on a worker thread) when a display that didn't answer over DDC/CI does now; it can switch from the
        /// software dimmer back to its own backlight.
        /// </summary>
        public event Action<DisplayInfo> DdcRecovered;

        private void RetryDdc(List<DisplayInfo> waiting, int generation)
        {
            int elapsed = 0;
            foreach (int at in DdcRetrySeconds)
            {
                Thread.Sleep((at - elapsed) * 1000);
                elapsed = at;
                // A newer refresh replaced these display objects and does its own retrying
                if (_disposed || generation != _refreshGeneration) return;

                for (int i = waiting.Count - 1; i >= 0; i--)
                {
                    var disp = waiting[i];
                    uint min, cur, max;
                    if (!TryReadDdc(disp.HMonitor, out min, out cur, out max)) continue;
                    SetDdcRange(disp, min, cur, max);
                    waiting.RemoveAt(i);
                    App.Log("DDC/CI " + disp.FriendlyName + ": answered after " + at + " s, backlight " + disp.CurrentBrightness + "%");
                    var handler = DdcRecovered;
                    if (handler != null) handler(disp);
                }
                if (waiting.Count == 0) return;
            }
            foreach (var disp in waiting) App.Log("DDC/CI " + disp.FriendlyName + ": still no answer, using software brightness");
        }

        private bool TryReadDdc(IntPtr hMonitor, out uint min, out uint cur, out uint max)
        {
            min = 0; cur = 0; max = 100;
            lock (_i2cBusLock)
            {
                try
                {
                    uint count;
                    if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count) || count == 0) return false;
                    var phys = new PHYSICAL_MONITOR[count];
                    if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, phys)) return false;
                    try
                    {
                        for (uint p = 0; p < count; p++)
                            if (GetMonitorBrightness(phys[p].hPhysicalMonitor, out min, out cur, out max)) return true;
                    }
                    finally
                    {
                        DestroyPhysicalMonitors(count, phys);
                    }
                }
                catch { }
            }
            return false;
        }

        private static void SetDdcRange(DisplayInfo disp, uint min, uint cur, uint max)
        {
            disp.SupportsDdcCi = true;
            disp.MinBrightness = min;
            disp.MaxBrightness = max;
            disp.CurrentBrightness = (uint)RawToPercent(cur, min, max);
        }

        /// <summary>
        /// The monitor's own backlight value for a 0-100% setting. Most monitors report 0-100, but DDC/CI lets them
        /// use any range (0-50, 0-255, 10-100...); writing a percentage straight to those dims by the wrong amount.
        /// </summary>
        internal static uint PercentToRaw(int percent, uint min, uint max)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            if (max <= min) return (uint)percent;
            return min + (uint)Math.Round((max - min) * percent / 100.0);
        }

        /// <summary>The 0-100% setting for a backlight value the monitor reports in its own range.</summary>
        internal static int RawToPercent(uint raw, uint min, uint max)
        {
            if (max <= min) return (int)Math.Min(100u, raw);
            if (raw <= min) return 0;
            if (raw >= max) return 100;
            return (int)Math.Round((raw - min) * 100.0 / (max - min));
        }

        internal static bool IsNeutralBaseline(ushort[] ramp)
        {
            if (ramp == null || ramp.Length != 768) return false;
            if (ramp[0] > 2000 || ramp[256] > 2000 || ramp[512] > 2000) return false;
            if (ramp[255] < 50000 || ramp[256 + 255] < 45000 || ramp[512 + 255] < 45000) return false;
            // A real baseline (identity or an ICC calibration curve) keeps at least one channel at full white.
            // A ramp whose brightest channel tops out lower is a dimmed leftover (e.g. from our own software
            // brightness on a previous run); capturing it would cap 100% below real brightness and compound every dim.
            if (Math.Max(ramp[255], Math.Max(ramp[256 + 255], ramp[512 + 255])) < 64000) return false;
            // Ratio between Blue and Red peak must be at least 0.80 (neutral sRGB reference)
            if ((double)ramp[512 + 255] / ramp[255] < 0.80) return false;
            return true;
        }

        private void CaptureOriginalGammaRamp(DisplayInfo disp)
        {
            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = CreateDC(null, disp.DeviceName, null, IntPtr.Zero);
                if (hdc != IntPtr.Zero)
                {
                    ushort[] ramp = new ushort[768];
                    if (GetDeviceGammaRamp(hdc, ramp))
                    {
                        ushort[] savedBaseline, savedApplied;
                        if (LoadRampState(disp, out savedBaseline, out savedApplied))
                        {
                            ramp = ResolveCapturedBaseline(ramp, savedBaseline, savedApplied);
                        }

                        if (IsNeutralBaseline(ramp))
                        {
                            disp.OriginalRamp = (ushort[])ramp.Clone();
                            return;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    DeleteDC(hdc);
                }
            }

            // Fallback to pure clean linear reference ramp (prevents capturing pre-tinted red ramps)
            disp.OriginalRamp = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                ushort val = (ushort)((i * 65535) / 255);
                disp.OriginalRamp[i]       = val;
                disp.OriginalRamp[256 + i] = val;
                disp.OriginalRamp[512 + i] = val;
            }
        }

        public void ApplyBrightness(DisplayInfo disp, int brightness)
        {
            disp.UserBrightness = Math.Max(0, Math.Min(100, brightness));

            if (disp.SupportsDdcCi && disp.UseHardwareBrightness)
            {
                ApplyHardwareBrightness(disp, brightness);
            }
        }

        /// <summary>
        /// Asynchronously queues a hardware brightness command with 35ms bus pacing
        /// so rapid slider dragging never blocks the UI thread or freezes the monitor's I2C scaler.
        /// </summary>
        public void QueueHardwareBrightness(DisplayInfo disp, int brightness)
        {
            if (!disp.SupportsDdcCi || disp.HMonitor == IntPtr.Zero || _disposed) return;
            disp.UserBrightness = Math.Max(0, Math.Min(100, brightness));

            lock (_ddcSyncLock)
            {
                _pendingBrightness[disp.DeviceName] = disp.UserBrightness;
                if (!_ddcWorkerActive)
                {
                    _ddcWorkerActive = true;
                    ThreadPool.QueueUserWorkItem(ProcessDdcQueue);
                }
            }
        }

        private void ProcessDdcQueue(object state)
        {
            while (!_disposed)
            {
                KeyValuePair<string, int>[] work;
                lock (_ddcSyncLock)
                {
                    if (_pendingBrightness.Count == 0)
                    {
                        _ddcWorkerActive = false;
                        return;
                    }
                    work = new KeyValuePair<string, int>[_pendingBrightness.Count];
                    int idx = 0;
                    foreach (var kvp in _pendingBrightness)
                    {
                        work[idx++] = kvp;
                    }
                    _pendingBrightness.Clear();
                }

                foreach (var item in work)
                {
                    if (_disposed) return;
                    DisplayInfo targetDisp = null;
                    lock (_displaysLock)
                    {
                        foreach (var d in _displays)
                        {
                            if (d.DeviceName.Equals(item.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                targetDisp = d;
                                break;
                            }
                        }
                    }

                    if (targetDisp != null && targetDisp.SupportsDdcCi && targetDisp.HMonitor != IntPtr.Zero)
                    {
                        ApplyHardwareBrightness(targetDisp, item.Value);
                    }
                }

                // Respect I2C auxiliary bus clock and avoid monitor scaler overload
                Thread.Sleep(35);
            }
        }

        public bool ApplyHardwareBrightness(DisplayInfo disp, int brightness)
        {
            if (!disp.SupportsDdcCi || disp.HMonitor == IntPtr.Zero) return false;
            disp.UserBrightness = Math.Max(0, Math.Min(100, brightness));
            return SetBacklight(disp, disp.UserBrightness);
        }

        /// <summary>
        /// When a DDC/CI display is switched to software brightness, put its backlight at full so the software
        /// dimmer is the only thing dimming. Otherwise a low backlight left over from hardware mode stacks with
        /// software dimming (e.g. 15% backlight × 19% light). Runs off the UI thread; leaves UserBrightness alone.
        /// </summary>
        public void ResetBacklightForSoftwareMode(DisplayInfo disp)
        {
            if (!disp.SupportsDdcCi || disp.HMonitor == IntPtr.Zero || _disposed) return;
            ThreadPool.QueueUserWorkItem(_ => SetBacklight(disp, 100));
        }

        internal bool SetBacklight(DisplayInfo disp, int value)
        {
            lock (_i2cBusLock)
            {
                try
                {
                    uint count = 0;
                    if (GetNumberOfPhysicalMonitorsFromHMONITOR(disp.HMonitor, out count) && count > 0)
                    {
                        var phys = new PHYSICAL_MONITOR[count];
                        if (GetPhysicalMonitorsFromHMONITOR(disp.HMonitor, count, phys))
                        {
                            try
                            {
                                int percent = Math.Max(0, Math.Min(100, value));
                                uint raw = PercentToRaw(percent, disp.MinBrightness, disp.MaxBrightness);
                                for (uint p = 0; p < count; p++)
                                {
                                    if (SetMonitorBrightness(phys[p].hPhysicalMonitor, raw))
                                    {
                                        disp.CurrentBrightness = (uint)percent;
                                        return true;
                                    }
                                }
                            }
                            finally
                            {
                                DestroyPhysicalMonitors(count, phys);
                            }
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        public void ResetGammaRamp(DisplayInfo disp)
        {
            if (disp == null) return;
            UpdateColorFilter();

            // If software brightness is active and below 100%, reapply software brightness instead of blowing out to 100% white
            if (disp.UsesSoftwareBrightness && !disp.IsHdr && disp.UserBrightness != 100)
            {
                ApplyColorAndGamma(disp);
                return;
            }

            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = CreateDC(null, disp.DeviceName, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero) return;

                lock (_gammaRampLock)
                {
                    if (disp.OriginalRamp != null && IsNeutralBaseline(disp.OriginalRamp))
                    {
                        Array.Copy(disp.OriginalRamp, _gammaBuffer, 768);
                    }
                    else
                    {
                        for (int i = 0; i < 256; i++)
                        {
                            ushort val = (ushort)((i * 65535) / 255);
                            _gammaBuffer[i]       = val;
                            _gammaBuffer[256 + i] = val;
                            _gammaBuffer[512 + i] = val;
                        }

                        // Also re-initialize disp.OriginalRamp to clean linear so it cannot stay corrupted
                        disp.OriginalRamp = (ushort[])_gammaBuffer.Clone();
                    }

                    SetDeviceGammaRamp(hdc, _gammaBuffer);
                    disp.LastAppliedRamp = null;
                    ClearRampState(disp);
                }
            }
            catch { }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    DeleteDC(hdc);
                }
            }
        }

        public void ApplyGammaRamp(DisplayInfo disp)
        {
            ApplyColorAndGamma(disp);
        }

        // Software brightness at 0% still emits this fraction of full luminance, roughly where a monitor's
        // backlight bottoms out, so the software slider feels like the hardware one instead of going near-black
        private const double SoftwareMinLuminance = 0.30;
        private const double DisplayGamma = 2.2;

        /// <summary>
        /// Signal scale for software brightness. The slider is treated as a target luminance fraction (like a
        /// DDC/CI backlight, which is roughly linear in light output); because the panel's response is
        /// signal^2.2, the signal is scaled by L^(1/2.2). The previous linear signal scale plus an extra gamma
        /// darkening produced far less light than the same number on the hardware slider.
        /// </summary>
        internal static double SoftwareBrightnessScale(int brightness)
        {
            double bFrac = Math.Max(0, Math.Min(100, brightness)) / 100.0;
            double luminance = SoftwareMinLuminance + (1.0 - SoftwareMinLuminance) * bFrac;
            return Math.Pow(luminance, 1.0 / DisplayGamma);
        }

        /// <summary>
        /// Computes the ramp for a display's settings on top of the given baseline (null = linear identity).
        /// </summary>
        internal static void BuildRamp(DisplayInfo disp, ushort[] baseline, ushort[] output)
        {
            // In HDR, color temperature and RGB gains go through the color filter (ComputeHdrFilter) instead,
            // since the ramp's behavior is undefined there; only contrast and gamma stay on the ramp
            double finalRedGain = 1.0, finalGreenGain = 1.0, finalBlueGain = 1.0;
            if (!disp.IsHdr)
            {
                double tR = 1.0, tG = 1.0, tB = 1.0;
                if (disp.ColorTemperature != 6500)
                {
                    KelvinToRgb(disp.ColorTemperature, out tR, out tG, out tB);
                }

                finalRedGain   = Math.Max(0.0, Math.Min(1.0, tR * disp.RedGain));
                finalGreenGain = Math.Max(0.0, Math.Min(1.0, tG * disp.GreenGain));
                finalBlueGain  = Math.Max(0.0, Math.Min(1.0, tB * disp.BlueGain));
            }

            // Software brightness via the GPU lookup table when DDC/CI is unsupported or turned off for this display
            // (not in HDR: there the ramp's behavior is undefined, and UpdateColorFilter dims via the color filter)
            double softwareBrightnessScale = 1.0;
            if (disp.UsesSoftwareBrightness && !disp.IsHdr)
            {
                softwareBrightnessScale = SoftwareBrightnessScale(disp.UserBrightness);
            }

            double contrast = Math.Max(0.5, Math.Min(1.8, disp.Contrast));
            double gamma    = Math.Max(0.5, Math.Min(2.5, disp.Gamma));
            double invGamma = 1.0 / gamma;
            bool useBaseline = baseline != null && IsNeutralBaseline(baseline);

            ushort prevR = 0, prevG = 0, prevB = 0;

            for (int i = 0; i < 256; i++)
            {
                double r0, g0, b0;
                if (useBaseline)
                {
                    r0 = baseline[i] / 65535.0;
                    g0 = baseline[256 + i] / 65535.0;
                    b0 = baseline[512 + i] / 65535.0;
                }
                else
                {
                    double x = i / 255.0;
                    r0 = x;
                    g0 = x;
                    b0 = x;
                }

                // 1. Gamma transfer curve (preserves factory calibrated white point & relative balance)
                double rg = (gamma == 1.0) ? r0 : Math.Pow(r0, invGamma);
                double gg = (gamma == 1.0) ? g0 : Math.Pow(g0, invGamma);
                double bg = (gamma == 1.0) ? b0 : Math.Pow(b0, invGamma);

                // 2. Contrast adjustment centered on 0.5 midpoint
                double rc = (contrast == 1.0) ? rg : ((rg - 0.5) * contrast + 0.5);
                double gc = (contrast == 1.0) ? gg : ((gg - 0.5) * contrast + 0.5);
                double bc = (contrast == 1.0) ? bg : ((bg - 0.5) * contrast + 0.5);

                // 3. Planckian color temperature, RGB channel gains & Software Brightness Scale
                double rFinal = Math.Max(0.0, Math.Min(1.0, rc * finalRedGain * softwareBrightnessScale));
                double gFinal = Math.Max(0.0, Math.Min(1.0, gc * finalGreenGain * softwareBrightnessScale));
                double bFinal = Math.Max(0.0, Math.Min(1.0, bc * finalBlueGain * softwareBrightnessScale));

                // 4. Windows GDI driver deviation clamp (|val - identity| <= 32760); larger deviations are rejected
                int identity = (i * 65535) / 255;
                int minAllowed = Math.Max(0, identity - 32760);
                int maxAllowed = Math.Min(65535, identity + 32760);

                int rInt = Math.Max(minAllowed, Math.Min(maxAllowed, (int)Math.Round(rFinal * 65535.0)));
                int gInt = Math.Max(minAllowed, Math.Min(maxAllowed, (int)Math.Round(gFinal * 65535.0)));
                int bInt = Math.Max(minAllowed, Math.Min(maxAllowed, (int)Math.Round(bFinal * 65535.0)));

                ushort rVal = (ushort)rInt;
                ushort gVal = (ushort)gInt;
                ushort bVal = (ushort)bInt;

                // 5. Monotonic non-decreasing sequence required by hardware display drivers
                if (rVal < prevR) rVal = prevR;
                if (gVal < prevG) gVal = prevG;
                if (bVal < prevB) bVal = prevB;
                prevR = rVal;
                prevG = gVal;
                prevB = bVal;

                output[i]       = rVal;
                output[256 + i] = gVal;
                output[512 + i] = bVal;
            }
        }

        private static bool RampsMatch(ushort[] a, ushort[] b)
        {
            if (a == null || b == null || a.Length != 768 || b.Length != 768) return false;
            for (int i = 0; i < 768; i++)
            {
                if (Math.Abs(a[i] - b[i]) > RampCompareTolerance) return false;
            }
            return true;
        }

        private static ushort[] LinearRamp()
        {
            var linear = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                ushort val = (ushort)((i * 65535) / 255);
                linear[i] = val;
                linear[256 + i] = val;
                linear[512 + i] = val;
            }
            return linear;
        }

        /// <summary>
        /// If the ramp read at startup is the one we last wrote (the app quit without restoring it, e.g. a crash),
        /// returns the baseline we had under it (or linear if that's unusable); otherwise the captured ramp.
        /// Unlike <see cref="DiscardOwnLeftoverBaseline"/> this doesn't depend on the saved settings, which may
        /// have changed since the leftover was written (slider moved just before the crash, hand edits).
        /// </summary>
        internal static ushort[] ResolveCapturedBaseline(ushort[] captured, ushort[] savedBaseline, ushort[] savedApplied)
        {
            if (!RampsMatch(captured, savedApplied)) return captured;
            return IsNeutralBaseline(savedBaseline) ? (ushort[])savedBaseline.Clone() : LinearRamp();
        }

        /// <summary>Ramp state file contents: baseline then applied ramp, 768 little-endian ushorts each.</summary>
        internal static byte[] EncodeRampState(ushort[] baseline, ushort[] applied)
        {
            var data = new byte[768 * 2 * 2];
            Buffer.BlockCopy(baseline, 0, data, 0, 768 * 2);
            Buffer.BlockCopy(applied, 0, data, 768 * 2, 768 * 2);
            return data;
        }

        internal static bool TryDecodeRampState(byte[] data, out ushort[] baseline, out ushort[] applied)
        {
            baseline = null;
            applied = null;
            if (data == null || data.Length != 768 * 2 * 2) return false;
            baseline = new ushort[768];
            applied = new ushort[768];
            Buffer.BlockCopy(data, 0, baseline, 0, 768 * 2);
            Buffer.BlockCopy(data, 768 * 2, applied, 0, 768 * 2);
            return true;
        }

        // One file per display next to settings.json (so portable copies keep their own), e.g. gamma-DISPLAY1.dat
        private static string RampStatePath(DisplayInfo disp)
        {
            var name = new StringBuilder();
            foreach (char c in disp.DeviceName ?? "")
            {
                if (char.IsLetterOrDigit(c)) name.Append(c);
            }
            return Path.Combine(ConfigManager.GetSettingsFolder(), "gamma-" + name + ".dat");
        }

        private static bool LoadRampState(DisplayInfo disp, out ushort[] baseline, out ushort[] applied)
        {
            baseline = null;
            applied = null;
            try
            {
                string path = RampStatePath(disp);
                return File.Exists(path) && TryDecodeRampState(File.ReadAllBytes(path), out baseline, out applied);
            }
            catch { return false; }
        }

        /// <summary>
        /// Records the ramp we just wrote, synchronously so it's on disk before any crash that could leave it
        /// behind. A ramp equal to the baseline needs no record.
        /// </summary>
        private static void SaveRampState(DisplayInfo disp, ushort[] baseline, ushort[] applied)
        {
            if (baseline == null || RampsMatch(baseline, applied))
            {
                ClearRampState(disp);
                return;
            }
            try { File.WriteAllBytes(RampStatePath(disp), EncodeRampState(baseline, applied)); }
            catch { }
        }

        private static void ClearRampState(DisplayInfo disp)
        {
            try
            {
                string path = RampStatePath(disp);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        /// <summary>
        /// If the baseline captured at startup is exactly the ramp this app would produce for the display's
        /// saved settings, it's our own leftover from the last run (app quit without restoring), not the
        /// monitor's real baseline. Using it would stack every adjustment on top of the old one.
        /// </summary>
        public void DiscardOwnLeftoverBaseline(DisplayInfo disp)
        {
            if (disp == null || disp.OriginalRamp == null || !HasCustomGammaOrColor(disp)) return;

            lock (_gammaRampLock)
            {
                BuildRamp(disp, null, _readBackBuffer);
                for (int i = 0; i < 768; i++)
                {
                    if (Math.Abs(_readBackBuffer[i] - disp.OriginalRamp[i]) > RampCompareTolerance) return;
                }
            }
            // Linear identity, not null: a null baseline would be re-captured from the (custom) ramp on the next refresh
            var linear = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                ushort val = (ushort)((i * 65535) / 255);
                linear[i] = val;
                linear[256 + i] = val;
                linear[512 + i] = val;
            }
            disp.OriginalRamp = linear;
        }

        public void ApplyColorAndGamma(DisplayInfo disp)
        {
            if (disp == null) return;
            UpdateColorFilter();

            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = CreateDC(null, disp.DeviceName, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero) return;

                lock (_gammaRampLock)
                {
                    BuildRamp(disp, disp.OriginalRamp, _gammaBuffer);
                    SetDeviceGammaRamp(hdc, _gammaBuffer);

                    // Slider drags reapply the same ramp often; only touch the disk when it changed
                    if (!RampsMatch(_gammaBuffer, disp.LastAppliedRamp))
                    {
                        SaveRampState(disp, disp.OriginalRamp, _gammaBuffer);
                    }

                    if (disp.LastAppliedRamp == null) disp.LastAppliedRamp = new ushort[768];
                    Array.Copy(_gammaBuffer, disp.LastAppliedRamp, 768);
                }
            }
            catch { }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    DeleteDC(hdc);
                }
            }
        }

        /// <summary>
        /// Re-writes the last applied ramp only if the driver's current ramp no longer matches it.
        /// Reading the LUT is cheap; writing it can cause a visible frame hitch on some GPUs/VRR setups.
        /// </summary>
        private void ReassertIfReset(DisplayInfo disp)
        {
            if (disp.LastAppliedRamp == null)
            {
                ApplyColorAndGamma(disp);
                return;
            }

            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = CreateDC(null, disp.DeviceName, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero) return;

                lock (_gammaRampLock)
                {
                    bool matches = GetDeviceGammaRamp(hdc, _readBackBuffer);
                    if (matches)
                    {
                        ushort[] expected = disp.LastAppliedRamp;
                        for (int i = 0; i < 768; i++)
                        {
                            if (Math.Abs(_readBackBuffer[i] - expected[i]) > RampCompareTolerance)
                            {
                                matches = false;
                                break;
                            }
                        }
                    }

                    if (!matches)
                    {
                        SetDeviceGammaRamp(hdc, disp.LastAppliedRamp);
                    }
                }
            }
            catch { }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    DeleteDC(hdc);
                }
            }
        }

        /// <summary>
        /// Writes the captured baseline (or linear) ramp back regardless of brightness mode. Used on exit.
        /// </summary>
        public void RestoreOriginalGamma(DisplayInfo disp)
        {
            if (disp == null) return;

            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = CreateDC(null, disp.DeviceName, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero) return;

                lock (_gammaRampLock)
                {
                    if (disp.OriginalRamp != null && IsNeutralBaseline(disp.OriginalRamp))
                    {
                        Array.Copy(disp.OriginalRamp, _gammaBuffer, 768);
                    }
                    else
                    {
                        for (int i = 0; i < 256; i++)
                        {
                            ushort val = (ushort)((i * 65535) / 255);
                            _gammaBuffer[i]       = val;
                            _gammaBuffer[256 + i] = val;
                            _gammaBuffer[512 + i] = val;
                        }
                    }
                    SetDeviceGammaRamp(hdc, _gammaBuffer);
                    disp.LastAppliedRamp = null;
                    ClearRampState(disp);
                }
            }
            catch { }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    DeleteDC(hdc);
                }
            }
        }

        public bool HasCustomGammaOrColor(DisplayInfo disp)
        {
            if (disp == null) return false;
            bool curveChanged = Math.Abs(disp.Contrast - 1.0) > 0.001 || Math.Abs(disp.Gamma - 1.0) > 0.001;
            // In HDR, brightness, warmth and gains live in the color filter; only the curve is on the ramp
            if (disp.IsHdr) return curveChanged;

            bool hasSoftwareBrightness = disp.UsesSoftwareBrightness && disp.UserBrightness != 100;
            return hasSoftwareBrightness ||
                   disp.ColorTemperature != 6500 ||
                   Math.Abs(disp.RedGain - 1.0) > 0.001 ||
                   Math.Abs(disp.GreenGain - 1.0) > 0.001 ||
                   Math.Abs(disp.BlueGain - 1.0) > 0.001 ||
                   Math.Abs(disp.Contrast - 1.0) > 0.001 ||
                   Math.Abs(disp.Gamma - 1.0) > 0.001;
        }

        public void ReapplyGammaRamps()
        {
            foreach (var d in Displays)
            {
                if (HasCustomGammaOrColor(d))
                {
                    ReassertIfReset(d);
                }
            }
        }

        public static void KelvinToRgb(int kelvin, out double r, out double g, out double b)
        {
            // Tanner Helland Planckian approximation algorithm
            double temp = kelvin / 100.0;
            double red, green, blue;

            // Calculate Red
            if (temp <= 66)
            {
                red = 255;
            }
            else
            {
                red = temp - 60;
                red = 329.698727446 * Math.Pow(red, -0.1332047592);
                if (red < 0) red = 0;
                if (red > 255) red = 255;
            }

            // Calculate Green
            if (temp <= 66)
            {
                green = temp;
                green = 99.4708025861 * Math.Log(green) - 161.1195681661;
                if (green < 0) green = 0;
                if (green > 255) green = 255;
            }
            else
            {
                green = temp - 60;
                green = 288.1221695283 * Math.Pow(green, -0.0755148492);
                if (green < 0) green = 0;
                if (green > 255) green = 255;
            }

            // Calculate Blue
            if (temp >= 66)
            {
                blue = 255;
            }
            else if (temp <= 19)
            {
                blue = 0;
            }
            else
            {
                blue = temp - 10;
                blue = 138.5177312231 * Math.Log(blue) - 305.0447927307;
                if (blue < 0) blue = 0;
                if (blue > 255) blue = 255;
            }

            r = red / 255.0;
            g = green / 255.0;
            b = blue / 255.0;
        }

        public static List<ColorPreset> GetPresets()
        {
            var list = new List<ColorPreset>();

            var p1 = new ColorPreset();
            p1.Name = "Balanced";
            p1.Icon = "☀️";
            p1.Description = "Standard 6500K neutral reference curve with accurate color response";
            p1.ColorTemp = 6500;
            p1.RedGain = 1.0;
            p1.GreenGain = 1.0;
            p1.BlueGain = 1.0;
            p1.Contrast = 1.0;
            p1.Gamma = 1.0;
            list.Add(p1);

            var p2 = new ColorPreset();
            p2.Name = "Night Light";
            p2.Icon = "🌙";
            p2.Description = "Warm 3400K soothing amber glow with pure inky blacks & crisp uncompressed contrast";
            p2.ColorTemp = 3400;
            p2.RedGain = 1.0;
            p2.GreenGain = 1.0;
            p2.BlueGain = 1.0;
            p2.Contrast = 1.0;
            p2.Gamma = 1.0;
            list.Add(p2);

            var p3 = new ColorPreset();
            p3.Name = "Paper Read";
            p3.Icon = "📖";
            p3.Description = "Soft 4600K warm sepia with gentle contrast optimized for e-reading & documents";
            p3.ColorTemp = 4600;
            p3.RedGain = 1.0;
            p3.GreenGain = 0.94;
            p3.BlueGain = 0.82;
            p3.Contrast = 1.05;
            p3.Gamma = 1.0;
            list.Add(p3);

            var p4 = new ColorPreset();
            p4.Name = "OLED Vivid";
            p4.Icon = "✨";
            p4.Description = "Deep inky blacks & punchy vibrant highlights tailored for OLED panels";
            p4.ColorTemp = 6500;
            p4.RedGain = 1.02;
            p4.GreenGain = 0.98;
            p4.BlueGain = 1.02;
            p4.Contrast = 1.14;
            p4.Gamma = 1.16;
            list.Add(p4);

            var p5 = new ColorPreset();
            p5.Name = "Gaming FPS";
            p5.Icon = "🎯";
            p5.Description = "Shadow-boosted gamma (1.24) to illuminate enemies in dark scenes + crisp dynamic range";
            p5.ColorTemp = 6800;
            p5.RedGain = 0.98;
            p5.GreenGain = 1.0;
            p5.BlueGain = 1.02;
            p5.Contrast = 1.0;
            p5.Gamma = 1.24;
            list.Add(p5);

            var p6 = new ColorPreset();
            p6.Name = "Cool Daylight";
            p6.Icon = "❄️";
            p6.Description = "Crisp 8500K cool energizing daylight white balance for high focus";
            p6.ColorTemp = 8500;
            p6.RedGain = 0.92;
            p6.GreenGain = 0.96;
            p6.BlueGain = 1.0;
            p6.Contrast = 1.0;
            p6.Gamma = 1.0;
            list.Add(p6);

            return list;
        }

        public static List<string> GetInstalledColorProfiles()
        {
            var list = new List<string>();
            try
            {
                string colorDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "drivers", "color");
                if (Directory.Exists(colorDir))
                {
                    foreach (var file in Directory.GetFiles(colorDir, "*.ic*"))
                    {
                        list.Add(Path.GetFileName(file));
                    }
                }
            }
            catch { }
            return list;
        }

        #region HDR color filter dimming

        // Software brightness in HDR at 0% still lets this fraction of light through. Much deeper than the
        // 80-nit floor of Windows' SDR content brightness, without going fully black.
        // 0.08 tuned by eye on an AORUS FO32U2 so Solar Sync's night level matches its hardware DDC/CI level.
        private const double HdrMinLuminance = 0.08;

        private readonly ColorFilterDimmer _colorFilter = new ColorFilterDimmer();

        /// <summary>Software brightness → color filter scale for HDR: 0% = 8% light, 100% = unchanged.</summary>
        public static float HdrFilterScale(int brightness)
        {
            double bFrac = Math.Max(0, Math.Min(100, brightness)) / 100.0;
            return (float)(HdrMinLuminance + (1.0 - HdrMinLuminance) * bFrac);
        }

        /// <summary>
        /// Per-channel filter values for an HDR display: color temperature × RGB gains, plus software
        /// brightness when the display is in software mode. Warmth and gains are defined on the encoded
        /// signal (like the SDR gamma ramp), while the compositor applies the filter to linear light in HDR,
        /// so they're converted with the 2.2 display gamma to give the same white point as in SDR.
        /// </summary>
        internal static void ComputeHdrFilter(DisplayInfo d, out float r, out float g, out float b)
        {
            double tR = 1.0, tG = 1.0, tB = 1.0;
            if (d.ColorTemperature != 6500)
            {
                KelvinToRgb(d.ColorTemperature, out tR, out tG, out tB);
            }

            double encR = Math.Max(0.0, Math.Min(1.0, tR * d.RedGain));
            double encG = Math.Max(0.0, Math.Min(1.0, tG * d.GreenGain));
            double encB = Math.Max(0.0, Math.Min(1.0, tB * d.BlueGain));

            double light = d.UsesSoftwareBrightness ? HdrFilterScale(d.UserBrightness) : 1.0;

            r = (float)(Math.Pow(encR, DisplayGamma) * light);
            g = (float)(Math.Pow(encG, DisplayGamma) * light);
            b = (float)(Math.Pow(encB, DisplayGamma) * light);
        }

        /// <summary>
        /// The color filter covers every screen, so it follows one HDR display: the first in software mode
        /// (its brightness matters most), otherwise the first HDR display. Cleared when no display is in HDR.
        /// </summary>
        internal static DisplayInfo FilterSource(IList<DisplayInfo> displays)
        {
            DisplayInfo source = null;
            foreach (var d in displays)
            {
                if (!d.IsHdr) continue;
                if (d.UsesSoftwareBrightness) return d;
                if (source == null) source = d;
            }
            return source;
        }

        /// <summary>The filter values Windows should be applying right now: identity when no display is in HDR.</summary>
        internal void ExpectedColorFilter(out float r, out float g, out float b)
        {
            r = g = b = 1f;
            DisplayInfo source = FilterSource(Displays);
            if (source != null) ComputeHdrFilter(source, out r, out g, out b);
        }

        public bool ReadColorFilter(out float r, out float g, out float b)
        {
            return _colorFilter.TryRead(out r, out g, out b);
        }

        /// <summary>The ramp the driver currently holds for this display, or null if it can't be read.</summary>
        public ushort[] ReadGammaRamp(DisplayInfo disp)
        {
            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = CreateDC(null, disp.DeviceName, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero) return null;
                var ramp = new ushort[768];
                return GetDeviceGammaRamp(hdc, ramp) ? ramp : null;
            }
            catch { return null; }
            finally
            {
                if (hdc != IntPtr.Zero) DeleteDC(hdc);
            }
        }

        /// <summary>Reads the DDC/CI backlight as 0-100% straight from the monitor (blocking I2C call).</summary>
        public bool ReadBacklight(DisplayInfo disp, out uint value)
        {
            value = 0;
            if (!disp.SupportsDdcCi || disp.HMonitor == IntPtr.Zero) return false;
            uint min, cur, max;
            if (!TryReadDdc(disp.HMonitor, out min, out cur, out max)) return false;
            value = (uint)RawToPercent(cur, min, max);
            return true;
        }

        private void UpdateColorFilter()
        {
            DisplayInfo source = FilterSource(Displays);

            if (source == null)
            {
                _colorFilter.Clear();
                return;
            }

            float r, g, b;
            ComputeHdrFilter(source, out r, out g, out b);
            _colorFilter.Apply(r, g, b);
        }

        #endregion

        public void Dispose()
        {
            _disposed = true;
            _colorFilter.Dispose();
            lock (_ddcSyncLock)
            {
                _pendingBrightness.Clear();
            }
        }
    }
}
