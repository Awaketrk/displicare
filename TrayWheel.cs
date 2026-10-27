using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Displicare
{
    /// <summary>
    /// The mouse wheel over the tray icon. Windows doesn't send tray icons wheel messages, and Displicare uses no mouse
    /// hooks, so it asks for raw mouse input (RIDEV_INPUTSINK) only while the pointer is over the icon: the icon's own
    /// mouse-move message arms it, and a 150 ms check disarms it once the pointer leaves the icon. The rest of the
    /// time nothing is registered and nothing runs.
    /// </summary>
    internal sealed class TrayWheel : NativeWindow, IDisposable
    {
        /// <summary>Raised once per wheel notch over the icon: +1 up, -1 down.</summary>
        public event Action<int> Notch;

        readonly NotifyIcon icon;
        readonly Timer leaveCheck = new Timer();
        bool armed;
        int accumulated;
        POINT hoverPoint;

        public TrayWheel(NotifyIcon icon)
        {
            this.icon = icon;
            var cp = new CreateParams();
            cp.Parent = HWND_MESSAGE;
            CreateHandle(cp);
            leaveCheck.Interval = 150;
            leaveCheck.Tick += delegate { if (!PointerOnIcon()) Disarm(); };
            icon.MouseMove += delegate { Arm(); };
        }

        void Arm()
        {
            GetCursorPos(out hoverPoint);
            if (armed) return;
            var device = new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = RIDEV_INPUTSINK, hwndTarget = Handle };
            if (!RegisterRawInputDevices(new[] { device }, 1, Marshal.SizeOf(typeof(RAWINPUTDEVICE)))) return;
            armed = true;
            accumulated = 0;
            leaveCheck.Start();
        }

        void Disarm()
        {
            leaveCheck.Stop();
            if (!armed) return;
            armed = false;
            var device = new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero };
            RegisterRawInputDevices(new[] { device }, 1, Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        }

        /// <summary>
        /// Whether the pointer is on the icon: its rectangle from the shell, or, if that can't be had, within a few
        /// pixels of where the icon last reported the pointer.
        /// </summary>
        bool PointerOnIcon()
        {
            POINT p;
            if (!GetCursorPos(out p)) return false;
            RECT r;
            if (TryGetIconRect(out r)) return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
            return Math.Abs(p.X - hoverPoint.X) <= 12 && Math.Abs(p.Y - hoverPoint.Y) <= 12;
        }

        /// <summary>
        /// NotifyIcon keeps its window and id private; the shell needs them to say where the icon is (also in the
        /// hidden-icons flyout). Read once by reflection; without them the hover point stands in.
        /// </summary>
        bool TryGetIconRect(out RECT rect)
        {
            rect = new RECT();
            if (iconHwnd == IntPtr.Zero && !lookedUp)
            {
                lookedUp = true;
                try
                {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var window = typeof(NotifyIcon).GetField("window", flags).GetValue(icon) as NativeWindow;
                    iconId = (int)typeof(NotifyIcon).GetField("id", flags).GetValue(icon);
                    if (window != null) iconHwnd = window.Handle;
                }
                catch (Exception ex) { App.Log("Tray wheel: icon position unavailable, using the hover point (" + ex.Message + ")"); }
            }
            if (iconHwnd == IntPtr.Zero) return false;
            var id = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)), hWnd = iconHwnd, uID = (uint)iconId };
            return Shell_NotifyIconGetRect(ref id, out rect) == 0;
        }

        IntPtr iconHwnd;
        int iconId;
        bool lookedUp;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT && armed)
            {
                int delta = WheelDelta(m.LParam);
                if (delta != 0 && PointerOnIcon()) Add(delta);
            }
            base.WndProc(ref m);
        }

        /// <summary>Precision touchpads send small steps: a notch is every 120 units, in either direction.</summary>
        void Add(int delta)
        {
            if (Math.Sign(delta) != Math.Sign(accumulated)) accumulated = 0;
            accumulated += delta;
            while (Math.Abs(accumulated) >= WHEEL_DELTA)
            {
                int step = Math.Sign(accumulated);
                accumulated -= step * WHEEL_DELTA;
                var handler = Notch;
                if (handler != null) handler(step);
            }
        }

        static int WheelDelta(IntPtr hRawInput)
        {
            int header = Marshal.SizeOf(typeof(RAWINPUTHEADER));
            int size = 0;
            GetRawInputData(hRawInput, RID_INPUT, null, ref size, header);
            if (size <= header + 8) return 0;
            var data = new byte[size];
            if (GetRawInputData(hRawInput, RID_INPUT, data, ref size, header) != size) return 0;
            if (BitConverter.ToUInt32(data, 0) != RIM_TYPEMOUSE) return 0;
            // RAWMOUSE: usFlags (2 bytes, padded to 4), then usButtonFlags and usButtonData
            ushort buttonFlags = BitConverter.ToUInt16(data, header + 4);
            return (buttonFlags & RI_MOUSE_WHEEL) != 0 ? BitConverter.ToInt16(data, header + 6) : 0;
        }

        public void Dispose()
        {
            Disarm();
            leaveCheck.Dispose();
            DestroyHandle();
        }

        const int WM_INPUT = 0x00FF, WHEEL_DELTA = 120;
        const uint RIDEV_REMOVE = 0x1, RIDEV_INPUTSINK = 0x100, RID_INPUT = 0x10000003, RIM_TYPEMOUSE = 0;
        const ushort RI_MOUSE_WHEEL = 0x0400;
        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }

        [StructLayout(LayoutKind.Sequential)]
        struct NOTIFYICONIDENTIFIER { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, int size);

        [DllImport("user32.dll")]
        static extern int GetRawInputData(IntPtr hRawInput, uint command, byte[] data, ref int size, int headerSize);

        [DllImport("shell32.dll")]
        static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT point);
    }
}
