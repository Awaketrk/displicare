using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Displicare.Rooms
{
    /// <summary>Message-only window that owns the global hotkeys. Nothing polls: Windows posts WM_HOTKEY.</summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        const int WM_HOTKEY = 0x0312;
        const uint MOD_NOREPEAT = 0x4000;
        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        readonly Action<int> pressed;
        readonly List<int> registered = new List<int>();

        public HotkeyWindow(Action<int> pressed)
        {
            this.pressed = pressed;
            var cp = new CreateParams();
            cp.Parent = HWND_MESSAGE;
            CreateHandle(cp);
        }

        /// <summary>False when the combination is invalid or another app already owns it.</summary>
        public bool Register(int id, string hotkey)
        {
            uint mods, key;
            if (!Hotkey.TryParse(hotkey, out mods, out key)) return false;
            if (!RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, key)) return false;
            registered.Add(id);
            return true;
        }

        public void UnregisterAll()
        {
            foreach (int id in registered) UnregisterHotKey(Handle, id);
            registered.Clear();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterAll();
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
