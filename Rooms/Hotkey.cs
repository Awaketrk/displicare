using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Displicare.Rooms
{
    /// <summary>Global hotkey text such as "Ctrl+Alt+1", parsed into RegisterHotKey modifiers and a virtual-key code.</summary>
    internal static class Hotkey
    {
        public const uint MOD_ALT = 0x1;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_SHIFT = 0x4;
        public const uint MOD_WIN = 0x8;

        public static bool TryParse(string text, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                string lower = part.ToLowerInvariant();
                bool last = i == parts.Length - 1;
                if (!last)
                {
                    if (lower == "ctrl" || lower == "control") modifiers |= MOD_CONTROL;
                    else if (lower == "alt") modifiers |= MOD_ALT;
                    else if (lower == "shift") modifiers |= MOD_SHIFT;
                    else if (lower == "win") modifiers |= MOD_WIN;
                    else return false;
                    continue;
                }
                key = ParseKey(part);
            }
            // A bare key would steal normal typing; insist on Ctrl, Alt or Win.
            return key != 0 && (modifiers & (MOD_CONTROL | MOD_ALT | MOD_WIN)) != 0;
        }

        static uint ParseKey(string part)
        {
            if (part.Length == 1)
            {
                char c = char.ToUpperInvariant(part[0]);
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z')) return c;
                return 0;
            }
            Keys parsed;
            if (Enum.TryParse(part, true, out parsed) && (parsed & Keys.Modifiers) == 0 && parsed != Keys.None)
                return (uint)parsed;
            return 0;
        }

        public static string Format(uint modifiers, uint key)
        {
            var parts = new List<string>();
            if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((modifiers & MOD_WIN) != 0) parts.Add("Win");
            if ((key >= '0' && key <= '9') || (key >= 'A' && key <= 'Z')) parts.Add(((char)key).ToString());
            else parts.Add(((Keys)key).ToString());
            return string.Join("+", parts.ToArray());
        }

        /// <summary>Same combination regardless of spelling ("ctrl+alt+1" vs "Ctrl + Alt + D1").</summary>
        public static string Normalize(string text)
        {
            uint mods, key;
            return TryParse(text, out mods, out key) ? Format(mods, key) : null;
        }

        /// <summary>First free Ctrl+Alt+1..9 for a new profile, or null when all are taken.</summary>
        public static string NextFree(IEnumerable<string> taken)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in taken)
            {
                string n = Normalize(t);
                if (n != null) used.Add(n);
            }
            for (int i = 1; i <= 9; i++)
            {
                string candidate = "Ctrl+Alt+" + i;
                if (!used.Contains(candidate)) return candidate;
            }
            return null;
        }
    }
}
