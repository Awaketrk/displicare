using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Displicare.Rooms
{
    /// <summary>
    /// "Keep this layout?" with a countdown, centred on the new primary display. No answer counts as no, so a switch
    /// to a TV that is off (or showing nothing) comes back on its own. Enter or a controller's A keeps, Esc or B goes back.
    /// </summary>
    /// <remarks>
    /// Laid out by hand in physical pixels for the display it opens on. WinForms on .NET 4.8 sizes point fonts and
    /// auto-layout with the DPI the process started with (the desk monitor's), which cut the buttons off on the TVs.
    /// Sizing also follows the screen height, so it stays readable from the couch on a 4K TV at 100% scaling.
    /// </remarks>
    internal sealed class ConfirmForm : Form
    {
        readonly Timer timer = new Timer();
        readonly Label countdown = new Label();
        readonly string profileName;
        int remaining;
        static volatile ConfirmForm open;

        /// <param name="controller">A controller is connected: the buttons say A and B, which answer through <see cref="Answer"/>.</param>
        public static bool Ask(string profileName, int seconds, bool controller)
        {
            using (var form = new ConfirmForm(profileName, seconds, Primary(), controller))
            {
                open = form;
                try { return form.ShowDialog() == DialogResult.Yes; }
                finally { open = null; }
            }
        }

        public static bool IsOpen { get { return open != null; } }

        /// <summary>Answers the open prompt from another thread (a controller's A or B). Does nothing when none is open.</summary>
        public static void Answer(bool keep)
        {
            var form = open;
            if (form == null) return;
            try { form.BeginInvoke((Action)delegate { form.DialogResult = keep ? DialogResult.Yes : DialogResult.No; }); }
            catch (InvalidOperationException) { } // closed in the meantime
        }

        static Tuple<float, Rectangle> Primary()
        {
            Rectangle work;
            float scale = PrimaryScale(out work);
            return Tuple.Create(scale, work);
        }

        /// <summary>Lays the prompt out for a given scale and work area; tests use this to check every scale.</summary>
        internal ConfirmForm(string profileName, int seconds, Tuple<float, Rectangle> screen, bool controller = false)
        {
            this.profileName = profileName;
            remaining = seconds;

            Rectangle work = screen.Item2;
            // Never more than 60% of the screen, whatever the scaling says.
            float scale = Math.Min(screen.Item1, Math.Min(work.Width * 0.6f / 640f, work.Height * 0.6f / 260f));
            if (scale <= 0) scale = 1f;
            Func<float, int> px = delegate (float v) { return (int)Math.Round(v * scale); };

            Text = "Displicare";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(30, 21, 32); // Displicare card colour
            ForeColor = Color.White;

            var titleFont = new Font("Segoe UI Semibold", px(30), GraphicsUnit.Pixel);
            var bodyFont = new Font("Segoe UI", px(20), GraphicsUnit.Pixel);
            var buttonFont = new Font("Segoe UI Semibold", px(20), GraphicsUnit.Pixel);

            int pad = px(32);
            int width = px(640);
            int inner = width - 2 * pad;

            var title = new Label();
            title.Font = titleFont;
            title.Text = "Keep this display layout?";
            title.AutoSize = false;
            title.SetBounds(pad, pad, inner, px(44));
            Controls.Add(title);

            countdown.Font = bodyFont;
            countdown.AutoSize = false;
            countdown.SetBounds(pad, title.Bottom + px(12), inner, px(64));
            Controls.Add(countdown);

            int buttonWidth = px(170), buttonHeight = px(52), gap = px(16);
            int buttonTop = countdown.Bottom + px(24);

            var back = MakeButton(controller ? "Go back · B" : "Go back", buttonFont, Color.FromArgb(46, 33, 48), DialogResult.No);
            back.SetBounds(width - pad - buttonWidth, buttonTop, buttonWidth, buttonHeight);
            var keep = MakeButton(controller ? "Keep · A" : "Keep", buttonFont, Color.FromArgb(232, 86, 74), DialogResult.Yes);
            keep.SetBounds(back.Left - gap - buttonWidth, buttonTop, buttonWidth, buttonHeight);
            Controls.Add(keep);
            Controls.Add(back);
            AcceptButton = keep;
            CancelButton = back;

            var size = new Size(width, keep.Bottom + pad);
            LayoutBounds = new Rectangle(new Point(work.Left + (work.Width - size.Width) / 2, work.Top + (work.Height - size.Height) / 2), size);
            Bounds = LayoutBounds;
            UpdateText();

            timer.Interval = 1000;
            timer.Tick += delegate
            {
                remaining--;
                if (remaining <= 0)
                {
                    timer.Stop();
                    DialogResult = DialogResult.No;
                    return;
                }
                UpdateText();
            };
        }

        static Button MakeButton(string text, Font font, Color color, DialogResult result)
        {
            var b = new Button();
            b.Text = text;
            b.Font = font;
            b.DialogResult = result;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = color;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Color.FromArgb(67, 49, 69);
            b.UseVisualStyleBackColor = false;
            return b;
        }

        void UpdateText()
        {
            countdown.Text = string.Format("\"{0}\" is on. Going back to the previous layout in {1} s.", profileName, remaining);
        }

        /// <summary>Where the prompt goes, in physical pixels (borderless, so window and client are the same).</summary>
        internal Rectangle LayoutBounds { get; private set; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Form.Bounds clips the size to SystemInformation.MaxWindowTrackSize, which can still describe the old screen
            // right after a switch; that clipping is what hid the buttons. Place the window directly instead.
            var b = LayoutBounds;
            SetWindowPos(Handle, HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_GETMINMAXINFO)
            {
                // Same reason as above: don't let a stale maximum track size shrink the prompt.
                var info = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                info.ptMaxTrackSize.x = Math.Max(info.ptMaxTrackSize.x, LayoutBounds.Width);
                info.ptMaxTrackSize.y = Math.Max(info.ptMaxTrackSize.y, LayoutBounds.Height);
                Marshal.StructureToPtr(info, m.LParam, false);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
            Activate();
            SetForegroundWindow(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(74, 54, 80), 2))
                e.Graphics.DrawRectangle(pen, 1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
        }

        /// <summary>
        /// Scale for the primary display (the one at 0,0, asked fresh because the layout just changed): its Windows
        /// scaling, or its height relative to 1080 when that is larger, so a 4K TV at 100% still gets a big prompt.
        /// </summary>
        internal static float PrimaryScale(out Rectangle work)
        {
            work = new Rectangle(0, 0, 1920, 1080);
            IntPtr monitor = MonitorFromPoint(new POINT(), 1 /* MONITOR_DEFAULTTOPRIMARY */);
            var info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(monitor, ref info))
                work = Rectangle.FromLTRB(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
            return LayoutScale(MonitorDpi(monitor), info.rcMonitor.bottom - info.rcMonitor.top);
        }

        internal static float LayoutScale(uint dpi, int monitorHeight)
        {
            float byDpi = dpi > 0 ? dpi / 96f : 1f;
            float byHeight = monitorHeight > 0 ? monitorHeight / 1080f : 1f;
            return Math.Max(1f, Math.Max(byDpi, byHeight));
        }

        static uint MonitorDpi(IntPtr monitor)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out x, out y) == 0) return x;
            }
            catch (Exception) { }
            return 96;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        const int WM_GETMINMAXINFO = 0x0024;
        const uint SWP_NOACTIVATE = 0x0010;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
