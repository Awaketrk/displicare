using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Displicare.Rooms
{
    /// <summary>One line of the room picker.</summary>
    internal sealed class PickerItem
    {
        public string Name;
        /// <summary>"On now", "Not connected" or empty.</summary>
        public string Note;
        public object Tag;
    }

    /// <summary>
    /// What Displicare shows on the screen itself: a short note after a switch ("Living room · 3840 × 2160 · 120 Hz ·
    /// HDR · LG TV") and the controller's room picker. It never takes focus and clicks go through it, so a game keeps
    /// running underneath; it is an ordinary window of Displicare's own, not drawn into the game. Sized like
    /// <see cref="ConfirmForm"/>, from the primary screen's scaling and height, so it is readable from the couch.
    /// </summary>
    internal sealed class RoomOverlay : Form
    {
        static RoomOverlay current;

        readonly float scale;
        string title;
        string detail;
        readonly List<PickerItem> items;
        /// <summary>0-100 for a level note (brightness), -1 otherwise.</summary>
        int level = -1;
        readonly Timer timer = new Timer();
        int selected;
        int remainingMs;
        Rectangle bounds;

        static readonly Color Back = Color.FromArgb(30, 21, 32);
        static readonly Color Edge = Color.FromArgb(74, 54, 80);
        static readonly Color Accent = Color.FromArgb(232, 86, 74);
        static readonly Color Row = Color.FromArgb(46, 33, 48);
        static readonly Color Muted = Color.FromArgb(190, 172, 192);

        /// <summary>A note at the top of the primary screen for a few seconds.</summary>
        public static void ShowNote(string title, string detail, int milliseconds)
        {
            Replace(new RoomOverlay(title, detail, null, 0, milliseconds));
        }

        /// <summary>
        /// A small level bar ("Brightness 60%", detail e.g. "All displays") above the taskbar on the primary screen, like
        /// Windows' own volume and brightness notes. While one is showing, it's updated in place, so turning the wheel
        /// doesn't flicker; it closes <paramref name="milliseconds"/> after the last change.
        /// </summary>
        public static void ShowLevel(string title, string detail, int percent, int milliseconds)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            if (current != null && current.level >= 0)
            {
                current.title = title;
                current.detail = detail;
                current.level = percent;
                current.remainingMs = milliseconds;
                current.Invalidate();
                return;
            }
            var note = new RoomOverlay(title, detail, null, 0, milliseconds, percent);
            Replace(note);
        }

        /// <summary>The room picker in the middle of the primary screen; closes itself after <paramref name="milliseconds"/>.</summary>
        public static void ShowPicker(List<PickerItem> items, int selected, int milliseconds)
        {
            Replace(new RoomOverlay("Switch room", null, items, selected, milliseconds));
        }

        public static bool PickerOpen { get { return current != null && current.items != null; } }

        /// <summary>Moves the picker's highlight by <paramref name="step"/> and keeps it open a while longer.</summary>
        public static void MovePicker(int step, int milliseconds)
        {
            if (!PickerOpen || current.items.Count == 0) return;
            var p = current;
            p.selected = (p.selected + step + p.items.Count) % p.items.Count;
            p.remainingMs = milliseconds;
            p.Invalidate();
        }

        /// <summary>The highlighted item's tag, or null when the picker isn't open.</summary>
        public static object PickerSelection
        {
            get { return PickerOpen && current.items.Count > 0 ? current.items[current.selected].Tag : null; }
        }

        public static void CloseAll()
        {
            if (current == null) return;
            var c = current;
            current = null;
            c.Close();
            c.Dispose();
        }

        static void Replace(RoomOverlay next)
        {
            CloseAll();
            current = next;
            next.Show();
        }

        RoomOverlay(string title, string detail, List<PickerItem> items, int selected, int milliseconds, int level = -1)
        {
            this.title = title;
            this.detail = detail;
            this.items = items;
            this.level = level;
            this.selected = items != null && items.Count > 0 ? Math.Max(0, Math.Min(selected, items.Count - 1)) : 0;
            remainingMs = milliseconds;

            Rectangle work;
            float s = ConfirmForm.PrimaryScale(out work);
            scale = Math.Min(s, work.Height * 0.7f / (items != null ? 140f + 64f * items.Count : 140f));
            if (scale <= 0) scale = 1f;

            Text = "Displicare";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Back;
            DoubleBuffered = true;
            Opacity = 0.96; // also makes it a layered window, which clicks go through (see CreateParams)

            if (level >= 0) scale = s > 0 ? s : 1f; // desk-sized: it's seen up close, not from the couch
            var size = level >= 0 ? new Size(Px(320), Px(78))
                : items != null ? new Size(Px(520), Px(104 + 64 * items.Count)) : new Size(Px(600), Px(detail != null ? 112 : 76));
            var location = level >= 0
                ? new Point(work.Left + (work.Width - size.Width) / 2, work.Bottom - size.Height - Px(24))
                : items != null
                ? new Point(work.Left + (work.Width - size.Width) / 2, work.Top + (work.Height - size.Height) / 2)
                : new Point(work.Left + (work.Width - size.Width) / 2, work.Top + work.Height / 16);
            bounds = new Rectangle(location, size);
            Bounds = bounds;

            timer.Interval = 100;
            timer.Tick += delegate
            {
                remainingMs -= timer.Interval;
                if (remainingMs > 0) return;
                timer.Stop();
                if (current == this) CloseAll();
            };
        }

        int Px(float v) { return (int)Math.Round(v * scale); }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; // DWMWCP_ROUND: Windows 11 rounds the corners; older Windows ignores it
            try { DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); }
            catch (Exception) { }
            // Placed directly for the same reason as ConfirmForm: right after a switch, Form.Bounds can be clipped to the old screen.
            SetWindowPos(Handle, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var pen = new Pen(Edge, Math.Max(2, Px(2))))
                g.DrawRectangle(pen, 1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
            // The Displicare sunset line along the top
            using (var line = new SolidBrush(Accent)) g.FillRectangle(line, Px(24), 0, ClientSize.Width - Px(48), Math.Max(2, Px(3)));

            if (level >= 0)
            {
                PaintLevel(g);
                return;
            }

            int pad = Px(24);
            using (var titleFont = new Font("Segoe UI Semibold", Px(items != null ? 22 : 26), GraphicsUnit.Pixel))
            using (var bodyFont = new Font("Segoe UI", Px(17), GraphicsUnit.Pixel))
            using (var nameFont = new Font("Segoe UI Semibold", Px(20), GraphicsUnit.Pixel))
            {
                var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, title, titleFont, new Rectangle(pad, Px(18), ClientSize.Width - 2 * pad, Px(36)), Color.White, flags);
                if (items == null)
                {
                    if (detail != null)
                        TextRenderer.DrawText(g, detail, bodyFont, new Rectangle(pad, Px(62), ClientSize.Width - 2 * pad, Px(28)), Muted, flags);
                    return;
                }

                int top = Px(64);
                for (int i = 0; i < items.Count; i++)
                {
                    var row = new Rectangle(Px(16), top + Px(64) * i, ClientSize.Width - Px(32), Px(56));
                    if (i == selected)
                    {
                        using (var fill = new SolidBrush(Row)) g.FillRectangle(fill, row);
                        using (var pen = new Pen(Accent, Math.Max(2, Px(2)))) g.DrawRectangle(pen, row);
                    }
                    var text = new Rectangle(row.X + Px(14), row.Y, row.Width - Px(28), row.Height);
                    TextRenderer.DrawText(g, items[i].Name, nameFont, text, Color.White,
                        flags | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                    if (!string.IsNullOrEmpty(items[i].Note))
                        TextRenderer.DrawText(g, items[i].Note, bodyFont, text, Muted,
                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                }
                TextRenderer.DrawText(g, "A  switch      B  close      ↑ ↓  choose", bodyFont,
                    new Rectangle(pad, ClientSize.Height - Px(36), ClientSize.Width - 2 * pad, Px(26)), Muted,
                    flags | TextFormatFlags.HorizontalCenter);
            }
        }

        /// <summary>"☀ Brightness" and the percentage on one line, the detail under it, and a bar along the bottom.</summary>
        void PaintLevel(Graphics g)
        {
            int pad = Px(18);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            using (var titleFont = new Font("Segoe UI Semibold", Px(16), GraphicsUnit.Pixel))
            using (var bodyFont = new Font("Segoe UI", Px(12), GraphicsUnit.Pixel))
            {
                var line = new Rectangle(pad, Px(14), ClientSize.Width - 2 * pad, Px(22));
                TextRenderer.DrawText(g, title, titleFont, line, Color.White, flags | TextFormatFlags.Left);
                TextRenderer.DrawText(g, level + "%", titleFont, line, Color.White, flags | TextFormatFlags.Right);
                if (!string.IsNullOrEmpty(detail))
                    TextRenderer.DrawText(g, detail, bodyFont, new Rectangle(pad, Px(36), ClientSize.Width - 2 * pad, Px(16)), Muted, flags);
            }
            var track = new Rectangle(pad, ClientSize.Height - Px(18), ClientSize.Width - 2 * pad, Math.Max(4, Px(5)));
            using (var fill = new SolidBrush(Row)) g.FillRectangle(fill, track);
            using (var fill = new SolidBrush(Accent))
                g.FillRectangle(fill, track.X, track.Y, track.Width * level / 100, track.Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        const int WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
        const uint SWP_NOACTIVATE = 0x0010;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
