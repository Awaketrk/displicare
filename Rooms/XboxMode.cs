using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Displicare.Rooms
{
    /// <summary>
    /// Windows 11's Xbox mode (the Xbox full screen experience). Windows offers no command or link for it, only the
    /// Win + F11 toggle, so Displicare presses that, once, when switching rooms, and only after checking whether Xbox mode
    /// is already on: while it is, Explorer shows a "GamingFallbackWindow" (checked on 26300 with Xbox app 2610). The key
    /// press goes to Windows' own shortcut handling at the moment of the switch, never into a game.
    /// </summary>
    internal static class XboxMode
    {
        public static bool IsOn
        {
            get
            {
                IntPtr h = FindWindow("GamingFallbackWindow", null);
                return h != IntPtr.Zero && IsWindowVisible(h);
            }
        }

        /// <summary>Turns Xbox mode on or off and waits up to 5 s for it. False when it didn't change (not available here).</summary>
        public static bool Set(bool on)
        {
            if (IsOn == on) return true;
            var keys = new[] { Key(VK_LWIN, false), Key(VK_F11, false), Key(VK_F11, true), Key(VK_LWIN, true) };
            if (SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(INPUT))) != keys.Length)
            {
                RoomLog.Write("Couldn't press Win + F11 for Xbox mode.");
                return false;
            }
            for (int i = 0; i < 50; i++)
            {
                if (IsOn == on)
                {
                    RoomLog.Write("Xbox mode " + (on ? "on." : "off."));
                    return true;
                }
                Thread.Sleep(100);
            }
            RoomLog.Write("Xbox mode didn't turn " + (on ? "on" : "off") + " after Win + F11.");
            return false;
        }

        static INPUT Key(ushort vk, bool up)
        {
            var input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.ki.wVk = vk;
            input.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
            return input;
        }

        const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2;
        const ushort VK_LWIN = 0x5B, VK_F11 = 0x7A;

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        /// <summary>INPUT is a union whose largest member (MOUSEINPUT) makes it 40 bytes on x64; SendInput rejects any other size.</summary>
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }

        [DllImport("user32.dll")]
        static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);
    }
}
