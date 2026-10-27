using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Displicare
{
    public partial class MainWindow : Window
    {
        private static readonly uint CurrentPid = GetCurrentProcessId();

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        private const int MaxPresetNameLength = ConfigManager.MaxPresetNameLength;

        private MonitorEngine _engine;
        private TrayManager _tray;
        private AppSettings _settings;
        private bool _isUpdatingUi = false;
        private bool _isPinned = false;

        // While the hardware test runs it owns the displays: background reasserts, Solar Sync, tray actions
        // and display refreshes stand down until it has restored everything
        private bool _hardwareTestRunning = false;
        private bool _topologyChangedDuringTest = false;
        private HardwareTest _hardwareTest;
        private System.Threading.CancellationTokenSource _hardwareTestCts;
        private HardwareTestWindow _hardwareTestWindow;

        internal MonitorEngine Engine { get { return _engine; } }
        private System.Windows.Threading.DispatcherTimer _saveDebounceTimer;
        private System.Windows.Threading.DispatcherTimer _solarTimer;
        private bool _manualOverrideActive = false;
        private SolarStatus _lastSolarStatus = null;
        private List<TextBlock> _monitorBadgeTexts = new List<TextBlock>();
        private uint _wakeupMsg = 0;
        private System.Windows.Interop.HwndSource _hwndSource = null;

        private WinEventDelegate _foregroundWinEventDelegate;
        private IntPtr _hForegroundWinEventHook = IntPtr.Zero;
        private System.Windows.Threading.DispatcherTimer _fastReassertBurstTimer;
        private int _reassertBurstCount = 0;
        private System.Windows.Threading.DispatcherTimer _watchdogTimer;
        private System.Windows.Threading.DispatcherTimer _displayChangeDebounceTimer;
        private System.Windows.Threading.DispatcherTimer _startMenuDismissTimer;
        private bool _wasStartMenuForeground = false;

        private uint _shellHookMsg = 0;

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterShellHookWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DeregisterShellHookWindow(IntPtr hWnd);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_QUERYENDSESSION = 0x0011;

        public MainWindow()
        {
            _isUpdatingUi = true;
            _engine = new MonitorEngine();
            _engine.DdcRecovered += d => Dispatcher.BeginInvoke((Action)(() => OnDdcRecovered(d)));
            _settings = ConfigManager.Load();

            _saveDebounceTimer = new System.Windows.Threading.DispatcherTimer();
            _saveDebounceTimer.Interval = TimeSpan.FromMilliseconds(400);
            _saveDebounceTimer.Tick += (s, ev) =>
            {
                _saveDebounceTimer.Stop();
                ConfigManager.Save(_settings);
            };

            _solarTimer = new System.Windows.Threading.DispatcherTimer();
            _solarTimer.Interval = TimeSpan.FromSeconds(20);
            _solarTimer.Tick += (s, ev) =>
            {
                UpdateSolarCircadian(false);
            };

            InitializeComponent();

            _tray = new TrayManager(this, _engine);

            // Wire tray events
            _tray.OnSolarSyncToggled += (enabled) =>
            {
                if (_hardwareTestRunning) return;
                Dispatcher.Invoke((Action)(() =>
                {
                    if (ChkSolarSync != null)
                    {
                        ChkSolarSync.IsChecked = enabled;
                    }
                }));
            };
            _tray.OnQuickBrightnessSelected += (val) =>
            {
                if (_hardwareTestRunning) return;
                Dispatcher.Invoke(() =>
                {
                    SliderBrightness.Value = val;
                });
            };

            _tray.OnQuickPresetSelected += (presetName) =>
            {
                if (_hardwareTestRunning) return;
                Dispatcher.Invoke(() =>
                {
                    if (presetName != null && presetName.StartsWith("custom:"))
                    {
                        string id = presetName.Substring("custom:".Length);
                        var cp = _settings.CustomPresets != null ? _settings.CustomPresets.Find(p => p.Id == id) : null;
                        if (cp != null)
                        {
                            ApplyCustomPreset(cp);
                        }
                    }
                    else
                    {
                        ApplyPresetByName(presetName);
                    }
                });
            };

            _tray.OnStartupToggled += (enabled) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _settings.StartWithWindows = enabled;
                    _isUpdatingUi = true;
                    ChkRunAtStartup.IsChecked = enabled;
                    _isUpdatingUi = false;
                    ConfigManager.SetRunOnStartup(enabled, _settings.StartMinimized);
                });
            };

            _tray.OnResetRequested += () =>
            {
                if (_hardwareTestRunning) return;
                Dispatcher.Invoke(() =>
                {
                    ResetAll();
                });
            };

            _tray.OnExitRequested += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    ExitApp();
                });
            };

            Loaded += (s, e) => { InitializeApplicationState(); };
            Deactivated += MainWindow_Deactivated;

            // Immediately initialize state so displays, solar sync, and dimmer overlays
            // are fully active even when starting minimized in tray (e.g. on Windows startup)
            InitializeApplicationState();
        }

        private bool _isStateInitialized = false;

        public void InitializeApplicationState()
        {
            if (_isStateInitialized) return;
            _isStateInitialized = true;

            _isUpdatingUi = true;

            // Load saved settings
            ChkSyncDisplays.IsChecked = _settings.SyncAllMonitors;
            ChkRunAtStartup.IsChecked = _settings.StartWithWindows;
            ChkStartMinimized.IsChecked = _settings.StartMinimized;
            ChkRestoreOnExit.IsChecked = _settings.RestoreOnExit;
            _isPinned = _settings.PinWindow;
            UpdatePinVisual();

            // A hardware test that never got to restore (app killed, logoff mid-test) left its backlight values
            HardwareTest.RecoverInterruptedRun(_engine);

            RestoreDisplayProfiles();

            RestoreHardwarePreference(_engine.Displays);

            foreach (var d in _engine.Displays)
            {
                _engine.DiscardOwnLeftoverBaseline(d);
            }

            PopulateDisplays();
            BuildCustomPresetsList();
            if (_tray != null)
            {
                _tray.UpdateCustomPresetsMenu(_settings.CustomPresets);
            }

            // Determine initial brightness & Kelvin
            int initialBrightness = _settings.Brightness;
            int initialKelvin = _settings.ColorTemperature;

            // If Solar Circadian is enabled, compute target right now based on astronomical sun position!
            if (_settings.SolarSyncEnabled)
            {
                double lat = _settings.UserLatitude;
                double lon = _settings.UserLongitude;
                if (_settings.AutoLocation)
                {
                    SolarEngine.GetDefaultCoordinates(out lat, out lon);
                }
                var solarStatus = SolarEngine.ComputeStatus(lat, lon, DateTime.Now,
                    _settings.SolarDayBrightness, _settings.SolarNightBrightness,
                    _settings.SolarDayKelvin, _settings.SolarNightKelvin);

                initialBrightness = solarStatus.TargetBrightness;
                if (_settings.SolarColorSyncEnabled)
                {
                    initialKelvin = solarStatus.TargetKelvin;
                }
                else
                {
                    initialKelvin = 6500;
                }
            }
            else if (_engine.Displays.Count > 0)
            {
                // If primary DDC/CI monitor has a real reading, prefer it over last saved setting.
                // Only when it's actually in hardware mode: in software mode the backlight reading is unrelated.
                foreach (var d in _engine.Displays)
                {
                    if (d.IsPrimary && d.SupportsDdcCi && d.UseHardwareBrightness)
                    {
                        initialBrightness = (int)d.CurrentBrightness;
                        break;
                    }
                }
            }

            // Set initial slider values
            SliderBrightness.Value = initialBrightness;
            SliderKelvin.Value = initialKelvin;
            SliderRed.Value = _settings.RedGain;
            SliderGreen.Value = _settings.GreenGain;
            SliderBlue.Value = _settings.BlueGain;
            SliderContrast.Value = _settings.Contrast;
            SliderGamma.Value = _settings.Gamma;

            // Update labels to match
            TxtBrightnessValue.Text = initialBrightness + "%";
            TxtKelvinValue.Text = initialKelvin + " K";

            // Initialize software-controlled displays via GPU gamma ramp
            foreach (var d in _engine.Displays)
            {
                if (!d.SupportsDdcCi || !d.UseHardwareBrightness)
                {
                    // A DDC/CI display in software mode may still have a low backlight from hardware mode
                    // (or another tool); without this the software dimmer would stack on top of it
                    if (d.SupportsDdcCi && d.CurrentBrightness < 100)
                    {
                        _engine.ResetBacklightForSoftwareMode(d);
                    }
                    _engine.ApplyColorAndGamma(d);
                }
            }

            UpdateStatus();
            PopulateAbout();

            if (TxtCustomPresetName != null)
            {
                TxtCustomPresetName.GotFocus += (s, ev) =>
                {
                    if (TxtCustomPresetName.Text == "My Preset")
                    {
                        TxtCustomPresetName.SelectAll();
                    }
                };
            }

            // Ensure Win32 HWND handle is allocated so positioning, DPI, and hooks work even while hidden
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.EnsureHandle();

            _tray.PositionWindowNearTray();

            // Load Solar Circadian Settings
            ChkSolarSync.IsChecked = _settings.SolarSyncEnabled;
            ChkSolarColorSync.IsChecked = _settings.SolarColorSyncEnabled;
            SliderSolarDayB.Value = _settings.SolarDayBrightness;
            SliderSolarNightB.Value = _settings.SolarNightBrightness;
            SliderSolarDayK.Value = _settings.SolarDayKelvin;
            SliderSolarNightK.Value = _settings.SolarNightKelvin;
            UpdateSolarLimitsLabels();

            InitializeBrightnessKeys();

            _isUpdatingUi = false;

            // Start Solar Circadian Rhythm Timer
            _solarTimer.Start();

            // Immediately apply circadian sync on boot if enabled, or apply saved settings
            if (_settings.SolarSyncEnabled)
            {
                UpdateSolarCircadian(true);
            }
            else
            {
                ApplyCurrentSettings(true);
                // Fill the sun panel with real data now instead of showing XAML placeholder text until the first 20s tick
                UpdateSolarCircadian(false);
            }

            // Hook up single-instance wake-up message listener and Shell Hook listener
            try
            {
                _wakeupMsg = App.RegisterWindowMessage(App.WakeupMessageName);
                _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
                if (_hwndSource != null)
                {
                    _hwndSource.AddHook(WndProc);
                }

                _shellHookMsg = App.RegisterWindowMessage("SHELLHOOK");
                if (_shellHookMsg != 0 && helper.Handle != IntPtr.Zero)
                {
                    RegisterShellHookWindow(helper.Handle);
                }
            }
            catch (Exception ex)
            {
                App.Log("ShellHook registration error: " + ex);
            }

            // Note: global low-level mouse/keyboard hooks were removed. They ran on the UI thread, so any
            // slow DDC/CI call stalled mouse & keyboard input system-wide. Foreground + shell hooks cover the same cases.

            // Boot reassurance timer: In case external monitors were waking from DP/HDMI sleep
            // during logon, refresh handles and re-assert circadian targets 2.5s after boot.
            var bootSyncTimer = new System.Windows.Threading.DispatcherTimer();
            bootSyncTimer.Interval = TimeSpan.FromSeconds(2.5);
            bootSyncTimer.Tick += (s, ev) =>
            {
                bootSyncTimer.Stop();
                try
                {
                    // RunHardwareTestAsync applies the solar target itself when a test started at launch ends
                    if (_settings != null && _settings.SolarSyncEnabled && !_hardwareTestRunning)
                    {
                        _engine.RefreshDisplays();
                        PopulateDisplays();
                        UpdateSolarCircadian(true);
                    }
                }
                catch (Exception ex)
                {
                    App.Log("bootSyncTimer error: " + ex);
                }
            };
            bootSyncTimer.Start();

            // Install native WinEventHook for foreground window transitions (Alt-Tab, game focus, desktop switch)
            try
            {
                _foregroundWinEventDelegate = new WinEventDelegate(OnForegroundChanged);
                _hForegroundWinEventHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, _foregroundWinEventDelegate,
                    0, 0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            }
            catch (Exception ex)
            {
                App.Log("SetWinEventHook error: " + ex);
            }

            // Debounced display topology change timer (150ms) for resolution switches & monitor plug/unplug
            _displayChangeDebounceTimer = new System.Windows.Threading.DispatcherTimer();
            _displayChangeDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
            _displayChangeDebounceTimer.Tick += (s, ev) =>
            {
                _displayChangeDebounceTimer.Stop();
                HandleDisplayTopologyChange();
            };

            try
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            }
            catch { }

            // Background watchdog: reads back the GPU ramp and only rewrites it if a driver/game reset it
            _watchdogTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background);
            _watchdogTimer.Interval = TimeSpan.FromMilliseconds(1500);
            _watchdogTimer.Tick += (s, ev) =>
            {
                ReassertDisplayState();
            };
            _watchdogTimer.Start();

            InitializeRooms();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            if (_isPinned) return;

            // Check if focus is still within any window of our own process
            // (such as the dimmer overlay, combobox popup, or tooltips)
            try
            {
                IntPtr fgHwnd = GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    uint fgPid;
                    GetWindowThreadProcessId(fgHwnd, out fgPid);
                    if (fgPid == CurrentPid)
                    {
                        return; // Focus is still inside our application, do not hide!
                    }
                }
            }
            catch { }

            if (IsVisible)
            {
                if (_tray != null)
                {
                    _tray.NotifyWindowDeactivated();
                }
                Hide();
            }
        }

        private static bool IsChildOf(DependencyObject child, DependencyObject parent)
        {
            if (child == null || parent == null) return false;
            DependencyObject current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelBrightness == null || PanelColor == null || PanelPresets == null || PanelMonitors == null) return;

            PanelBrightness.Visibility = TabBrightness.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelColor.Visibility = TabColor.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelPresets.Visibility = TabPresets.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelMonitors.Visibility = TabMonitors.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (PanelRooms != null) PanelRooms.Visibility = TabRooms.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            _isPinned = !_isPinned;
            _settings.PinWindow = _isPinned;
            UpdatePinVisual();
        }

        private void UpdatePinVisual()
        {
            if (_isPinned)
            {
                BtnPin.Background = new SolidColorBrush(Color.FromRgb(232, 86, 74));
                TxtPinIcon.Foreground = Brushes.White;
            }
            else
            {
                BtnPin.Background = Brushes.Transparent;
                TxtPinIcon.Foreground = new SolidColorBrush(Color.FromRgb(179, 163, 174));
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _engine.RefreshDisplays();
            PopulateDisplays();
            ApplyCurrentSettings();
            TxtStatus.Text = "Refreshed " + _engine.Displays.Count + " connected displays.";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void BtnResetAll_Click(object sender, RoutedEventArgs e)
        {
            ResetAll();
        }

        private void ResetAll()
        {
            bool solarOn = _settings != null && _settings.SolarSyncEnabled;
            _manualOverrideActive = solarOn;
            if (_settings != null) _settings.ActivePreset = "Balanced";
            if (BadgeSolarActive != null) BadgeSolarActive.Visibility = Visibility.Collapsed;
            if (BtnResumeSolar != null) BtnResumeSolar.Visibility = solarOn ? Visibility.Visible : Visibility.Collapsed;

            _isUpdatingUi = true;
            SliderBrightness.Value = 80;
            SliderKelvin.Value = 6500;
            SliderRed.Value = 1.0;
            SliderGreen.Value = 1.0;
            SliderBlue.Value = 1.0;
            SliderContrast.Value = 1.0;
            SliderGamma.Value = 1.0;

            TxtBrightnessValue.Text = "80%";
            TxtKelvinValue.Text = "6500 K";
            TxtRedVal.Text = "100%";
            TxtGreenVal.Text = "100%";
            TxtBlueVal.Text = "100%";
            TxtContrastVal.Text = "1.00";
            TxtGammaVal.Text = "1.00";

            BorderKelvinSwatch.Background = Brushes.White;
            _isUpdatingUi = false;

            // Unconditionally restore standard linear sRGB reference on all displays
            foreach (var d in _engine.Displays)
            {
                d.ColorTemperature = 6500;
                d.RedGain = 1.0;
                d.GreenGain = 1.0;
                d.BlueGain = 1.0;
                d.Contrast = 1.0;
                d.Gamma = 1.0;
                d.UserBrightness = 80;

                _engine.ResetGammaRamp(d);
                if (d.SupportsDdcCi && d.UseHardwareBrightness)
                {
                    // Queued like slider moves: each DDC/CI write takes tens of ms, too long for the UI thread
                    _engine.QueueHardwareBrightness(d, 80);
                }
            }

            BuildMonitorRows();
            TxtStatus.Text = "All display profiles and brightness restored to default.";

            // Persist clean reset settings immediately
            if (_settings != null)
            {
                _settings.Brightness = 80;
                _settings.ColorTemperature = 6500;
                _settings.RedGain = 1.0;
                _settings.GreenGain = 1.0;
                _settings.BlueGain = 1.0;
                _settings.Contrast = 1.0;
                _settings.Gamma = 1.0;
                SyncDisplayProfilesToSettings();
                ConfigManager.Save(_settings);
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private bool _isExited = false;

        public void CleanExit()
        {
            if (_isExited) return;
            _isExited = true;

            ShutdownRooms();
            ShutdownBrightnessKeys();

            if (_solarTimer != null)
            {
                _solarTimer.Stop();
                _solarTimer = null;
            }

            if (_watchdogTimer != null)
            {
                _watchdogTimer.Stop();
                _watchdogTimer = null;
            }

            if (_fastReassertBurstTimer != null)
            {
                _fastReassertBurstTimer.Stop();
                _fastReassertBurstTimer = null;
            }

            if (_displayChangeDebounceTimer != null)
            {
                _displayChangeDebounceTimer.Stop();
                _displayChangeDebounceTimer = null;
            }

            if (_hForegroundWinEventHook != IntPtr.Zero)
            {
                try { UnhookWinEvent(_hForegroundWinEventHook); } catch { }
                _hForegroundWinEventHook = IntPtr.Zero;
            }

            if (_startMenuDismissTimer != null)
            {
                _startMenuDismissTimer.Stop();
                _startMenuDismissTimer = null;
            }

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                if (_shellHookMsg != 0 && helper.Handle != IntPtr.Zero)
                {
                    DeregisterShellHookWindow(helper.Handle);
                }
            }
            catch { }

            try
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            }
            catch { }

            if (_saveDebounceTimer != null)
            {
                _saveDebounceTimer.Stop();
                _saveDebounceTimer = null;
            }

            if (ChkRestoreOnExit != null && ChkRestoreOnExit.IsChecked == true)
            {
                // Always write the true baseline ramp; the old path re-applied software dimming
                // for non-DDC displays and left them dimmed after exit
                foreach (var d in _engine.Displays)
                {
                    _engine.RestoreOriginalGamma(d);
                }
            }

            if (_settings != null)
            {
                if (ChkRunAtStartup != null) _settings.StartWithWindows = ChkRunAtStartup.IsChecked == true;
                if (ChkStartMinimized != null) _settings.StartMinimized = ChkStartMinimized.IsChecked == true;
                if (ChkRestoreOnExit != null) _settings.RestoreOnExit = ChkRestoreOnExit.IsChecked == true;
                _settings.PinWindow = _isPinned;
                ConfigManager.Save(_settings);
            }

            if (_hwndSource != null)
            {
                try
                {
                    _hwndSource.RemoveHook(WndProc);
                    _hwndSource.Dispose();
                }
                catch { }
                _hwndSource = null;
            }

            if (_tray != null)
            {
                _tray.Dispose();
            }

            if (_engine != null)
            {
                _engine.Dispose();
            }
        }

        private void ExitApp()
        {
            CleanExit();
            Application.Current.Shutdown();
        }

        private void ScheduleSave()
        {
            if (_saveDebounceTimer != null)
            {
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        private void UpdateStatus()
        {
            string primaryName = "Primary";
            var displays = _engine.Displays;
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].IsPrimary)
                {
                    primaryName = displays[i].FriendlyName;
                    break;
                }
            }
            int b = (int)SliderBrightness.Value;
            string solarTag = (_settings != null && _settings.SolarSyncEnabled) ? " • 🌅 Solar Sync" : "";
            string txt = string.Format("Displicare: {0} ({1}%){2}", primaryName, b, solarTag);
            _tray.UpdateTooltip(txt);
            TxtStatus.Text = string.Format("Ready • {0} ({1}%){2} • {3} Display{4}", primaryName, b, solarTag, displays.Count, displays.Count == 1 ? "" : "s");
        }
    }
}
