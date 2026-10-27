using System;
using System.Runtime.InteropServices;

namespace Displicare.Rooms
{
    /// <summary>
    /// The taskbar's auto-hide setting. When screens drop away at once, the taskbars of several monitors briefly share an
    /// edge of the one left; with auto-hide on, Explorer then says "Another toolbar is already hidden on this side of your
    /// screen" and turns auto-hide off. Turning it off around such a change, and back on after, avoids that.
    /// </summary>
    internal static class Taskbar
    {
        const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
        const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

        public static bool AutoHide
        {
            get
            {
                try
                {
                    var data = Data();
                    return ((int)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
                }
                catch (Exception) { return false; }
            }
            set
            {
                try
                {
                    var data = Data();
                    data.lParam = new IntPtr(value ? ABS_AUTOHIDE : ABS_ALWAYSONTOP);
                    SHAppBarMessage(ABM_SETSTATE, ref data);
                }
                catch (Exception ex) { RoomLog.Write("Couldn't change taskbar auto-hide: " + ex.Message); }
            }
        }

        static APPBARDATA Data()
        {
            var data = new APPBARDATA();
            data.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            data.hWnd = FindWindow("Shell_TrayWnd", null);
            return data;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll")]
        static extern IntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string className, string windowName);
    }
}
