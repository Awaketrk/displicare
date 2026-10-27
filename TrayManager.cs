using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace Displicare
{
    public class TrayManager : IDisposable
    {
        private NotifyIcon _notifyIcon;
        private MainWindow _window;
        private MonitorEngine _engine;
        private Icon _customIcon;
        private ToolStripMenuItem _presetMenu;
        private ToolStripMenuItem _startupItem;
        private Font _headerFont;
        private Font _boldFont;
        private Font _subHeaderFont;

        public event Action<int> OnQuickBrightnessSelected;
        public event Action<bool> OnStartupToggled;
        public event Action<string> OnQuickPresetSelected;
        public event Action<bool> OnSolarSyncToggled;
        public event Action OnResetRequested;
        public event Action OnExitRequested;
        /// <summary>A room picked from the tray's Rooms submenu (its key).</summary>
        public event Action<string> OnRoomSelected;
        public event Action OnRoomsWindowRequested;
        /// <summary>A wheel notch over the tray icon: +1 up, -1 down.</summary>
        public event Action<int> OnBrightnessWheel;

        private TrayWheel _wheel;

        private ToolStripMenuItem _roomsMenu;
        private ToolStripSeparator _roomsSeparator;

        public TrayManager(MainWindow window, MonitorEngine engine)
        {
            _window = window;
            _engine = engine;

            InitializeTray();
        }

        private void InitializeTray()
        {
            _notifyIcon = new NotifyIcon();

            // Load Icon
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
                if (File.Exists(iconPath))
                {
                    _customIcon = new Icon(iconPath);
                    _notifyIcon.Icon = _customIcon;
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.Text = "Displicare: Display & Color Studio";
            _notifyIcon.Visible = true;

            // Click handling - single left-click toggles flyout smoothly
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ToggleFlyout();
                }
            };

            // Mouse wheel over the icon changes brightness
            try
            {
                _wheel = new TrayWheel(_notifyIcon);
                _wheel.Notch += step => { if (OnBrightnessWheel != null) OnBrightnessWheel(step); };
            }
            catch (Exception ex) { App.Log("Tray wheel unavailable: " + ex.Message); }

            // Build Context Menu
            BuildContextMenu();
        }

        public void UpdateTooltip(string text)
        {
            if (_notifyIcon != null)
            {
                if (text.Length > 63) text = text.Substring(0, 60) + "...";
                _notifyIcon.Text = text;
            }
        }

        public void UpdateSolarMenuState(bool enabled, string statusText)
        {
            try
            {
                if (_notifyIcon != null && _notifyIcon.ContextMenuStrip != null)
                {
                    var item = _notifyIcon.ContextMenuStrip.Items["menuItemSolar"] as ToolStripMenuItem;
                    if (item != null)
                    {
                        item.Checked = enabled;
                        if (!string.IsNullOrEmpty(statusText))
                        {
                            item.Text = "🌅 Solar Sync: " + (enabled ? "ON (" + statusText + ")" : "OFF");
                        }
                        else
                        {
                            item.Text = "🌅 Solar Circadian Sync: " + (enabled ? "ON" : "OFF");
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Fills the Rooms submenu: (name, key, active, connected) per room. Null or empty hides it (room switching off).
        /// </summary>
        public void UpdateRoomsMenu(System.Collections.Generic.IList<Tuple<string, string, bool, bool>> rooms, bool busy)
        {
            try
            {
                if (_roomsMenu == null) return;
                var oldItems = new ToolStripItem[_roomsMenu.DropDownItems.Count];
                _roomsMenu.DropDownItems.CopyTo(oldItems, 0);
                _roomsMenu.DropDownItems.Clear();
                foreach (var old in oldItems) old.Dispose();

                bool show = rooms != null && rooms.Count > 0;
                _roomsMenu.Visible = show;
                _roomsSeparator.Visible = show;
                if (!show) return;

                foreach (var r in rooms)
                {
                    string key = r.Item2;
                    var item = new ToolStripMenuItem(r.Item1 + (r.Item4 ? "" : "  (not connected)"), null, (s, e) =>
                    {
                        if (OnRoomSelected != null) OnRoomSelected(key);
                    });
                    item.Checked = r.Item3;
                    item.Enabled = r.Item4 && !r.Item3 && !busy;
                    _roomsMenu.DropDownItems.Add(item);
                }
                _roomsMenu.DropDownItems.Add(new ToolStripSeparator());
                _roomsMenu.DropDownItems.Add(new ToolStripMenuItem("⚙️ Room settings…", null, (s, e) =>
                {
                    if (OnRoomsWindowRequested != null) OnRoomsWindowRequested();
                }));
            }
            catch { }
        }

        public void ShowBalloon(string title, string text, bool warning)
        {
            if (_notifyIcon == null) return;
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = string.IsNullOrEmpty(text) ? " " : text;
            _notifyIcon.BalloonTipIcon = warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(5000);
        }

        public void UpdateStartupMenuState(bool enabled)
        {
            if (_startupItem != null) _startupItem.Checked = enabled;
        }

        public void UpdateCustomPresetsMenu(System.Collections.Generic.List<CustomPreset> customPresets)
        {
            try
            {
                if (_presetMenu == null) return;

                // Clear() alone leaks the old items' native handles; dispose them first
                var oldItems = new ToolStripItem[_presetMenu.DropDownItems.Count];
                _presetMenu.DropDownItems.CopyTo(oldItems, 0);
                _presetMenu.DropDownItems.Clear();
                foreach (var old in oldItems) old.Dispose();

                // Built-in presets
                var presets = MonitorEngine.GetPresets();
                foreach (var p in presets)
                {
                    string pName = p.Name;
                    ToolStripMenuItem item = new ToolStripMenuItem(p.Icon + " " + pName, null, (s, e) =>
                    {
                        if (OnQuickPresetSelected != null)
                            OnQuickPresetSelected(pName);
                    });
                    _presetMenu.DropDownItems.Add(item);
                }

                // Custom user presets
                if (customPresets != null && customPresets.Count > 0)
                {
                    _presetMenu.DropDownItems.Add(new ToolStripSeparator());
                    ToolStripMenuItem customHeader = new ToolStripMenuItem("⭐ My Custom Presets");
                    customHeader.Enabled = false;
                    customHeader.Font = _subHeaderFont;
                    _presetMenu.DropDownItems.Add(customHeader);

                    foreach (var cp in customPresets)
                    {
                        string cpId = cp.Id;
                        string cpName = cp.Name;
                        ToolStripMenuItem item = new ToolStripMenuItem("⭐ " + cpName, null, (s, e) =>
                        {
                            if (OnQuickPresetSelected != null)
                                OnQuickPresetSelected("custom:" + cpId);
                        });
                        _presetMenu.DropDownItems.Add(item);
                    }
                }
            }
            catch { }
        }

        private void BuildContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new ModernMenuRenderer();

            _headerFont = new Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold);
            _boldFont = new Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold);
            _subHeaderFont = new Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Bold);

            // Header item
            ToolStripMenuItem header = new ToolStripMenuItem("Displicare  v" + App.Version);
            header.Font = _headerFont;
            header.Enabled = false;
            menu.Items.Add(header);

            // Rooms submenu: only shown while room switching is on (UpdateRoomsMenu)
            _roomsMenu = new ToolStripMenuItem("🛋️ Rooms");
            _roomsMenu.Visible = false;
            menu.Items.Add(_roomsMenu);
            _roomsSeparator = new ToolStripSeparator();
            _roomsSeparator.Visible = false;
            menu.Items.Add(_roomsSeparator);

            // Solar Circadian Sync Quick Toggle
            ToolStripMenuItem solarItem = new ToolStripMenuItem("🌅 Solar Circadian Sync", null, (s, e) =>
            {
                var item = s as ToolStripMenuItem;
                if (item != null)
                {
                    bool newState = !item.Checked;
                    item.Checked = newState;
                    if (OnSolarSyncToggled != null)
                        OnSolarSyncToggled(newState);
                }
            });
            solarItem.Name = "menuItemSolar";
            menu.Items.Add(solarItem);

            menu.Items.Add(new ToolStripSeparator());

            // Quick Brightness submenu
            ToolStripMenuItem brightMenu = new ToolStripMenuItem("🔆 Quick Brightness");
            int[] levels = new int[] { 25, 50, 75, 100 };
            foreach (var lvl in levels)
            {
                int val = lvl;
                ToolStripMenuItem item = new ToolStripMenuItem(val + "%", null, (s, e) =>
                {
                    if (OnQuickBrightnessSelected != null)
                        OnQuickBrightnessSelected(val);
                });
                brightMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(brightMenu);

            // Quick Presets submenu
            ToolStripMenuItem presetMenu = new ToolStripMenuItem("🎨 Color Presets");
            _presetMenu = presetMenu;
            var presets = MonitorEngine.GetPresets();
            foreach (var p in presets)
            {
                string pName = p.Name;
                ToolStripMenuItem item = new ToolStripMenuItem(p.Icon + " " + pName, null, (s, e) =>
                {
                    if (OnQuickPresetSelected != null)
                        OnQuickPresetSelected(pName);
                });
                presetMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(presetMenu);

            menu.Items.Add(new ToolStripSeparator());

            // Open window
            ToolStripMenuItem openItem = new ToolStripMenuItem("🎛️ Open Controls", null, (s, e) =>
            {
                ShowFlyout();
            });
            openItem.Font = _boldFont;
            menu.Items.Add(openItem);

            // Reset
            ToolStripMenuItem resetItem = new ToolStripMenuItem("🔄 Reset to Default", null, (s, e) =>
            {
                if (OnResetRequested != null)
                    OnResetRequested();
            });
            menu.Items.Add(resetItem);

            // Run on Startup toggle
            ToolStripMenuItem startupItem = new ToolStripMenuItem("🚀 Start with Windows");
            startupItem.Checked = ConfigManager.IsRunOnStartupEnabled();
            startupItem.Click += (s, e) =>
            {
                bool newState = !startupItem.Checked;
                startupItem.Checked = newState;
                // Routed through MainWindow so the settings checkbox and saved settings stay in sync
                // (previously the registry change was undone on exit by the stale checkbox value)
                if (OnStartupToggled != null)
                    OnStartupToggled(newState);
            };
            _startupItem = startupItem;
            menu.Items.Add(startupItem);

            menu.Items.Add(new ToolStripSeparator());

            // Exit
            ToolStripMenuItem exitItem = new ToolStripMenuItem("✕ Exit", null, (s, e) =>
            {
                if (OnExitRequested != null)
                    OnExitRequested();
            });
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
        }

        private DateTime _lastHideTime = DateTime.MinValue;

        public void NotifyWindowDeactivated()
        {
            _lastHideTime = DateTime.UtcNow;
        }

        public void ToggleFlyout()
        {
            // If the window was hidden within the last 350ms (for example when clicking the tray icon
            // while the window was already open), ignore the click so it stays closed instead of immediately re-popping!
            if ((DateTime.UtcNow - _lastHideTime).TotalMilliseconds < 350)
            {
                return;
            }

            if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
            {
                _lastHideTime = DateTime.UtcNow;
                _window.Hide();
            }
            else
            {
                ShowFlyout();
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        public void ShowFlyout()
        {
            _window.WindowState = WindowState.Normal;
            PositionWindowNearTray();
            _window.Show();
            _window.Activate();
            _window.Focus();
            // Re-verify position after Show() in case WPF DPI scale adjusted rendered dimensions on show
            PositionWindowNearTray();
        }

        /// <summary>The window's size in MainWindow.xaml, in device-independent pixels.</summary>
        private const double DesignWidth = 440, DesignHeight = 640;

        public void PositionWindowNearTray()
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(_window);
                IntPtr hwnd = helper.EnsureHandle();

                // 1. Determine which screen the tray icon / cursor is currently located on
                POINT cursorPos;
                if (!GetCursorPos(out cursorPos))
                {
                    cursorPos = new POINT { X = 0, Y = 0 };
                }

                Screen targetScreen = Screen.FromPoint(new System.Drawing.Point(cursorPos.X, cursorPos.Y));
                if (targetScreen == null)
                {
                    targetScreen = Screen.PrimaryScreen;
                }
                if (targetScreen == null) return;

                var workArea = targetScreen.WorkingArea;
                var bounds = targetScreen.Bounds;

                // 2. Query target screen DPI scaling
                double scaleX = 1.0;
                double scaleY = 1.0;

                try
                {
                    IntPtr hMon = MonitorFromPoint(cursorPos, MONITOR_DEFAULTTONEAREST);
                    if (hMon != IntPtr.Zero)
                    {
                        uint dpiX, dpiY;
                        if (GetDpiForMonitor(hMon, 0, out dpiX, out dpiY) == 0 && dpiX > 0 && dpiY > 0)
                        {
                            scaleX = dpiX / 96.0;
                            scaleY = dpiY / 96.0;
                        }
                    }
                }
                catch { }

                if (scaleX <= 0.001 && hwnd != IntPtr.Zero)
                {
                    try
                    {
                        uint dpi = GetDpiForWindow(hwnd);
                        if (dpi > 0)
                        {
                            scaleX = dpi / 96.0;
                            scaleY = dpi / 96.0;
                        }
                    }
                    catch { }
                }

                if (scaleX <= 0.001) scaleX = 1.0;
                if (scaleY <= 0.001) scaleY = 1.0;

                // 3. Physical size from the design size and this screen's scaling. Never from the window's current
                //    pixels: those belong to the screen it was last on, and after a stream left only a 100% virtual
                //    display they made the window two thirds the size on the 150% monitor, every time after.
                _window.Width = DesignWidth;
                _window.Height = DesignHeight;
                int physicalWidth = (int)Math.Round(DesignWidth * scaleX);
                int physicalHeight = (int)Math.Round(DesignHeight * scaleY);

                // Margins from screen edges in physical pixels
                int marginX = (int)Math.Round(12 * scaleX);
                int marginY = (int)Math.Round(10 * scaleY);

                int targetX;
                int targetY;

                // 4. Taskbar orientation detection on the target screen
                if (workArea.Bottom < bounds.Bottom) // Taskbar is at Bottom (Default Windows layout)
                {
                    targetX = workArea.Right - physicalWidth - marginX;
                    targetY = workArea.Bottom - physicalHeight - marginY;
                }
                else if (workArea.Top > bounds.Top) // Taskbar is at Top
                {
                    targetX = workArea.Right - physicalWidth - marginX;
                    targetY = workArea.Top + marginY;
                }
                else if (workArea.Right < bounds.Right) // Taskbar is on Right
                {
                    targetX = workArea.Right - physicalWidth - marginX;
                    targetY = workArea.Bottom - physicalHeight - marginY;
                }
                else if (workArea.Left > bounds.Left) // Taskbar is on Left
                {
                    targetX = workArea.Left + marginX;
                    targetY = workArea.Bottom - physicalHeight - marginY;
                }
                else // Fallback bottom-right
                {
                    targetX = workArea.Right - physicalWidth - marginX;
                    targetY = workArea.Bottom - physicalHeight - marginY;
                }

                // 5. CRITICAL: Strict clamping to targetScreen.WorkingArea
                // Ensures the window NEVER spills into adjacent monitors or outside the screen bounds!
                if (targetX + physicalWidth > workArea.Right)
                {
                    targetX = workArea.Right - physicalWidth - marginX;
                }
                if (targetX < workArea.Left)
                {
                    targetX = workArea.Left + marginX;
                }
                if (targetY + physicalHeight > workArea.Bottom)
                {
                    targetY = workArea.Bottom - physicalHeight - marginY;
                }
                if (targetY < workArea.Top)
                {
                    targetY = workArea.Top + marginY;
                }

                // 6. Apply physical pixel coordinates directly to the Win32 HWND
                SetWindowPos(hwnd, HWND_TOPMOST, targetX, targetY, physicalWidth, physicalHeight, SWP_NOACTIVATE);
            }
            catch
            {
                _window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        public void Dispose()
        {
            if (_wheel != null)
            {
                _wheel.Dispose();
                _wheel = null;
            }

            if (_notifyIcon != null)
            {
                try
                {
                    if (_notifyIcon.ContextMenuStrip != null)
                    {
                        _notifyIcon.ContextMenuStrip.Dispose();
                    }
                }
                catch { }

                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            if (_customIcon != null)
            {
                try
                {
                    _customIcon.Dispose();
                }
                catch { }
                _customIcon = null;
            }

            if (_headerFont != null) { _headerFont.Dispose(); _headerFont = null; }
            if (_boldFont != null) { _boldFont.Dispose(); _boldFont = null; }
            if (_subHeaderFont != null) { _subHeaderFont.Dispose(); _subHeaderFont = null; }
        }
    }

    // Modern Dark ToolStrip Renderer for Windows 11 look
    internal class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        public ModernMenuRenderer() : base(new ModernColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                using (var brush = new SolidBrush(Color.FromArgb(90, 36, 51)))
                {
                    var rect = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
                    e.Graphics.FillRectangle(brush, rect);
                }
            }
            else
            {
                base.OnRenderMenuItemBackground(e);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Color.FromArgb(251, 246, 247) : Color.FromArgb(133, 117, 128);
            base.OnRenderItemText(e);
        }
    }

    internal class ModernColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(26, 17, 26); } }
        public override Color MenuBorder { get { return Color.FromArgb(67, 49, 69); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(90, 36, 51); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(74, 30, 44); } }
        public override Color MenuStripGradientBegin { get { return Color.FromArgb(26, 17, 26); } }
        public override Color MenuStripGradientEnd { get { return Color.FromArgb(26, 17, 26); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(30, 21, 32); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(30, 21, 32); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(30, 21, 32); } }
        public override Color SeparatorDark { get { return Color.FromArgb(58, 42, 59); } }
        public override Color SeparatorLight { get { return Color.Transparent; } }
    }
}
