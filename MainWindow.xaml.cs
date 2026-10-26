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
        private System.Windows.Threading.DispatcherTimer _foregroundReassertTimer;
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

            // Restore saved per-display calibrations if present
            if (_settings.DisplayProfiles != null && _settings.DisplayProfiles.Count > 0)
            {
                foreach (var d in _engine.Displays)
                {
                    var prof = _settings.DisplayProfiles.Find(p =>
                        string.Equals(p.DeviceName, d.DeviceName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.FriendlyName, d.FriendlyName, StringComparison.OrdinalIgnoreCase));
                    if (prof != null)
                    {
                        d.UserBrightness = prof.Brightness;
                        d.ColorTemperature = prof.ColorTemperature;
                        d.RedGain = prof.RedGain;
                        d.GreenGain = prof.GreenGain;
                        d.BlueGain = prof.BlueGain;
                        d.Contrast = prof.Contrast;
                        d.Gamma = prof.Gamma;
                        d.UseHardwareBrightness = prof.UseHardwareBrightness;
                    }
                }
            }

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

            // Debounced foreground reassert timer (50ms) to counteract Windows DWM Alt-Tab gamma reset
            _foregroundReassertTimer = new System.Windows.Threading.DispatcherTimer();
            _foregroundReassertTimer.Interval = TimeSpan.FromMilliseconds(50);
            _foregroundReassertTimer.Tick += (s, ev) =>
            {
                _foregroundReassertTimer.Stop();
                ReassertDisplayState();
            };

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

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_wakeupMsg != 0 && (uint)msg == _wakeupMsg)
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (_tray != null)
                    {
                        _tray.ShowFlyout();
                    }
                }));
                handled = true;
            }
            else if (_shellHookMsg != 0 && (uint)msg == _shellHookMsg)
            {
                // Any shell event (HSHELL_TASKMAN, HSHELL_WINDOWACTIVATED, HSHELL_REDRAW, etc.)
                TriggerFastReassert();
            }
            else if (msg == WM_QUERYENDSESSION)
            {
                // Logoff/shutdown/restart: restore persistent state (SDR brightness, gamma) while we still can.
                // CleanExit is idempotent; returning non-zero lets Windows continue ending the session.
                // CleanExit removes this hook, so WM_ENDSESSION never reaches us; shut down right after instead
                // (otherwise a logoff cancelled by another app would leave a half-torn-down process behind).
                CleanExit();
                Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                handled = true;
                return new IntPtr(1);
            }
            else if (msg == WM_DISPLAYCHANGE)
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (_displayChangeDebounceTimer != null)
                    {
                        _displayChangeDebounceTimer.Stop();
                        _displayChangeDebounceTimer.Start();
                    }
                }));
                handled = true;
            }
            return IntPtr.Zero;
        }

        private bool IsStartMenuWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return false;
                string name = ProcessNameOf(pid);
                return string.Equals(name, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "SearchHost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder name, ref uint size);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>
        /// Executable name without extension, or null. Runs on every foreground change: Process.GetProcessById reads
        /// the whole process list each time (~0.7 ms and a pile of garbage), this asks for one process (~0.03 ms).
        /// </summary>
        internal static string ProcessNameOf(uint pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var path = new System.Text.StringBuilder(1024);
                uint size = (uint)path.Capacity;
                return QueryFullProcessImageName(h, 0, path, ref size) ? Path.GetFileNameWithoutExtension(path.ToString()) : null;
            }
            finally
            {
                CloseHandle(h);
            }
        }

        public void TriggerFastReassert()
        {
            try
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    _reassertBurstCount = 0;
                    if (_fastReassertBurstTimer == null)
                    {
                        _fastReassertBurstTimer = new System.Windows.Threading.DispatcherTimer();
                        _fastReassertBurstTimer.Interval = TimeSpan.FromMilliseconds(35);
                        _fastReassertBurstTimer.Tick += (s, ev) =>
                        {
                            _reassertBurstCount++;
                            ReassertDisplayState();
                            if (_reassertBurstCount >= 5) // Ticks at 35ms, 70ms, 105ms, 140ms, 175ms
                            {
                                _fastReassertBurstTimer.Stop();
                            }
                        };
                    }
                    _fastReassertBurstTimer.Stop();
                    _fastReassertBurstTimer.Start();
                    ReassertDisplayState();
                }));
            }
            catch { }
        }

        private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                bool isStart = IsStartMenuWindow(hwnd);
                if (isStart)
                {
                    _wasStartMenuForeground = true;
                    if (_startMenuDismissTimer == null)
                    {
                        _startMenuDismissTimer = new System.Windows.Threading.DispatcherTimer();
                        _startMenuDismissTimer.Interval = TimeSpan.FromMilliseconds(120);
                        _startMenuDismissTimer.Tick += (s, ev) =>
                        {
                            IntPtr curFg = GetForegroundWindow();
                            if (!IsStartMenuWindow(curFg))
                            {
                                _startMenuDismissTimer.Stop();
                                _wasStartMenuForeground = false;
                                ReassertDisplayState();
                                TriggerFastReassert();
                            }
                        };
                    }
                    _startMenuDismissTimer.Stop();
                    _startMenuDismissTimer.Start();
                }
                else if (_wasStartMenuForeground)
                {
                    _wasStartMenuForeground = false;
                    if (_startMenuDismissTimer != null) _startMenuDismissTimer.Stop();
                    ReassertDisplayState();
                    TriggerFastReassert();
                }
                else
                {
                    TriggerFastReassert();
                }
            }
            catch (Exception ex)
            {
                App.Log("OnForegroundChanged error: " + ex);
            }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (_displayChangeDebounceTimer != null)
                    {
                        _displayChangeDebounceTimer.Stop();
                        _displayChangeDebounceTimer.Start();
                    }
                }));
            }
            catch { }
        }

        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
            {
                try
                {
                    Dispatcher.BeginInvoke((Action)(() =>
                    {
                        HandleDisplayTopologyChange();
                    }));
                }
                catch { }
            }
        }

        private void HandleDisplayTopologyChange()
        {
            if (_hardwareTestRunning)
            {
                // Refreshing would swap out the display objects under the test; stop it and refresh afterwards
                _topologyChangedDuringTest = true;
                if (_hardwareTest != null) _hardwareTest.NoteInterruption("the display configuration changed");
                if (_hardwareTestCts != null) _hardwareTestCts.Cancel();
                return;
            }
            try
            {
                _engine.RefreshDisplays();
                PopulateDisplays();
                RoomsDisplaysChanged();

                foreach (var d in _engine.Displays)
                {
                    if (!d.SupportsDdcCi || !d.UseHardwareBrightness)
                    {
                        _engine.ApplyColorAndGamma(d);
                    }
                }

                ReassertDisplayState();
            }
            catch { }
        }

        private void ReassertDisplayState()
        {
            if (_hardwareTestRunning) return;
            try
            {
                if (_engine != null)
                {
                    _engine.ReapplyGammaRamps();
                }
            }
            catch (Exception ex)
            {
                App.Log("ReassertDisplayState error: " + ex);
            }
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

        private void PopulateDisplays()
        {
            int savedIdx = (CmbDisplaySelect != null) ? CmbDisplaySelect.SelectedIndex : 0;
            string savedDeviceName = null;
            if (savedIdx > 0 && savedIdx - 1 < _engine.Displays.Count)
            {
                savedDeviceName = _engine.Displays[savedIdx - 1].DeviceName;
            }

            bool wasUpdating = _isUpdatingUi;
            _isUpdatingUi = true;
            try
            {
                CmbDisplaySelect.Items.Clear();
                CmbDisplaySelect.Items.Add("✦ All Displays (Synchronized)");

                int targetIndex = 0;
                for (int i = 0; i < _engine.Displays.Count; i++)
                {
                    var d = _engine.Displays[i];
                    string primaryTag = d.IsPrimary ? " [Primary]" : "";
                    string capTag = d.SupportsDdcCi ? " • DDC/CI" : "";
                    string itemText = string.Format("{0}: {1}{2} ({3}x{4}){5}",
                        i + 1, d.FriendlyName, primaryTag, d.ResolutionWidth, d.ResolutionHeight, capTag);
                    CmbDisplaySelect.Items.Add(itemText);

                    if (savedDeviceName != null && string.Equals(d.DeviceName, savedDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetIndex = i + 1;
                    }
                }

                if (targetIndex > 0)
                {
                    CmbDisplaySelect.SelectedIndex = targetIndex;
                }
                else if (_settings != null && !_settings.SyncAllMonitors && _engine.Displays.Count > 0 && savedIdx > 0)
                {
                    CmbDisplaySelect.SelectedIndex = Math.Min(savedIdx, _engine.Displays.Count);
                }
                else
                {
                    CmbDisplaySelect.SelectedIndex = 0;
                }
            }
            finally
            {
                _isUpdatingUi = wasUpdating;
            }

            BuildMonitorRows();
            BuildDisplayHardwareCards();

            // Update header badge to reflect actual capabilities
            bool anyDdc = false;
            foreach (var d in _engine.Displays)
            {
                if (d.SupportsDdcCi) { anyDdc = true; break; }
            }
            int displayCount = _engine.Displays.Count;
            TxtHardwareBadge.Text = (anyDdc ? "DDC/CI" : "GAMMA") + " • " + displayCount + " DISPLAY" + (displayCount != 1 ? "S" : "");
        }

        private void BuildMonitorRows()
        {
            // Fast in-place badge update when monitor topology is unchanged (eliminates GC churn during slider drags)
            // Note: _engine.Displays returns a fresh copy per access, so snapshot it once
            var displays = _engine.Displays;
            if (_monitorBadgeTexts.Count == displays.Count && StackMonitorRows.Children.Count == displays.Count)
            {
                for (int i = 0; i < displays.Count; i++)
                {
                    _monitorBadgeTexts[i].Text = displays[i].UserBrightness + "%";
                }
                return;
            }

            StackMonitorRows.Children.Clear();
            _monitorBadgeTexts.Clear();

            for (int i = 0; i < _engine.Displays.Count; i++)
            {
                var d = _engine.Displays[i];

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(36, 26, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(58, 42, 59)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 0, 0, 7),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                int capturedIndex = i + 1;
                border.MouseLeftButtonUp += (s, ev) =>
                {
                    if (CmbDisplaySelect.Items.Count > capturedIndex)
                    {
                        CmbDisplaySelect.SelectedIndex = capturedIndex;
                    }
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftStack = new StackPanel();
                var nameText = new TextBlock
                {
                    Text = (i + 1) + ". " + d.FriendlyName + (d.IsPrimary ? " ★" : ""),
                    FontSize = 12.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255))
                };
                string softwareLabel = d.IsHdr
                    ? "🌗 HDR Color Filter Dimmer (8-100% light)"
                    : "🎨 Software GPU-LUT Dimmer (0-100%)";
                var modeText = new TextBlock
                {
                    Text = d.SupportsDdcCi && d.UseHardwareBrightness ? "⚡ Hardware DDC/CI (0-100%)" : softwareLabel,
                    FontSize = 10.5,
                    Foreground = d.SupportsDdcCi && d.UseHardwareBrightness ? new SolidColorBrush(Color.FromRgb(255, 138, 107)) : new SolidColorBrush(Color.FromRgb(232, 121, 166)),
                    Margin = new Thickness(0, 2, 0, 0)
                };
                leftStack.Children.Add(nameText);
                leftStack.Children.Add(modeText);

                var badgeBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(46, 33, 48)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(74, 54, 80)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(9, 3, 9, 3),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var badgeText = new TextBlock
                {
                    Text = d.UserBrightness + "%",
                    FontSize = 12.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36))
                };
                _monitorBadgeTexts.Add(badgeText);
                badgeBorder.Child = badgeText;

                Grid.SetColumn(leftStack, 0);
                Grid.SetColumn(badgeBorder, 1);
                grid.Children.Add(leftStack);
                grid.Children.Add(badgeBorder);

                border.Child = grid;
                StackMonitorRows.Children.Add(border);
            }
        }

        private void BuildDisplayHardwareCards()
        {
            StackDisplayCards.Children.Clear();
            for (int i = 0; i < _engine.Displays.Count; i++)
            {
                var d = _engine.Displays[i];

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(30, 21, 32)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(58, 42, 59)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var sp = new StackPanel();

                var titleGrid = new Grid();
                var titleText = new TextBlock
                {
                    Text = (i + 1) + ": " + d.FriendlyName,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(251, 246, 247))
                };
                titleGrid.Children.Add(titleText);

                if (d.IsPrimary)
                {
                    var primaryPill = new Border
                    {
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Background = new SolidColorBrush(Color.FromRgb(232, 86, 74)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 1, 6, 1)
                    };
                    primaryPill.Child = new TextBlock { Text = "PRIMARY", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
                    titleGrid.Children.Add(primaryPill);
                }
                sp.Children.Add(titleGrid);

                var infoText = new TextBlock
                {
                    Text = string.Format("Resolution: {0} x {1} • GPU: {2}\nDevice: {3}",
                        d.ResolutionWidth, d.ResolutionHeight, d.DeviceString, d.DeviceName),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(179, 163, 174)),
                    Margin = new Thickness(0, 6, 0, 8),
                    LineHeight = 16
                };
                sp.Children.Add(infoText);

                if (d.SupportsDdcCi)
                {
                    var hwChk = new CheckBox
                    {
                        Content = "Use Hardware DDC/CI for this display",
                        IsChecked = d.UseHardwareBrightness,
                        Style = (Style)FindResource("ModernCheckBox")
                    };
                    // forceColor: switching modes must rewrite the ramp, otherwise the software-dimmed ramp
                    // stays on screen after switching to hardware (and stacks on top of the backlight)
                    hwChk.Checked += (s, ev) => { d.UseHardwareBrightness = true; ApplyCurrentSettings(true); BuildMonitorRows(); BuildDisplayHardwareCards(); };
                    hwChk.Unchecked += (s, ev) => { d.UseHardwareBrightness = false; _engine.ResetBacklightForSoftwareMode(d); ApplyCurrentSettings(true); BuildMonitorRows(); BuildDisplayHardwareCards(); };
                    sp.Children.Add(hwChk);

                    if (!d.UseHardwareBrightness && !d.IsHdr)
                    {
                        var softNote = new TextBlock
                        {
                            Text = "🎨 Software dimming active via the GPU gamma ramp (dims down to ~30% light).",
                            FontSize = 10,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 4, 0, 0),
                            Foreground = new SolidColorBrush(Color.FromRgb(255, 138, 107))
                        };
                        sp.Children.Add(softNote);
                    }
                }
                else
                {
                    var note = new TextBlock
                    {
                        Text = "🎨 Using Software GPU-LUT Brightness & Gamma Calibration (DDC/CI not responding).",
                        FontSize = 10.5,
                        Foreground = new SolidColorBrush(Color.FromRgb(179, 163, 174))
                    };
                    sp.Children.Add(note);

                    var tip = new TextBlock
                    {
                        Text = "💡 Tip: Enable DDC/CI in your monitor's physical OSD buttons (or connect directly via DisplayPort/HDMI) for 100% hardware backlight control with seamless shell & taskbar preview dimming.",
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(133, 117, 128))
                    };
                    sp.Children.Add(tip);
                }

                if (d.IsHdr)
                {
                    sp.Children.Add(CreateHdrNote());
                }

                card.Child = sp;
                StackDisplayCards.Children.Add(card);
            }
        }

        private void BuildCustomPresetsList()
        {
            if (StackCustomPresets == null) return;
            StackCustomPresets.Children.Clear();

            if (_settings == null || _settings.CustomPresets == null || _settings.CustomPresets.Count == 0)
            {
                var emptyBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(36, 26, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(58, 42, 59)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                var emptyText = new TextBlock
                {
                    Text = "No custom presets saved yet.\nAdjust your Brightness, Warmth, Contrast or Gamma, enter a preset name above, and click 'Save Current'.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(179, 163, 174)),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 16,
                    TextAlignment = TextAlignment.Center
                };
                emptyBorder.Child = emptyText;
                StackCustomPresets.Children.Add(emptyBorder);
                return;
            }

            foreach (var preset in _settings.CustomPresets)
            {
                var p = preset;

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(36, 26, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(58, 42, 59)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var mainStack = new StackPanel();

                // Top row: Preset Name & Action Buttons
                var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameText = new TextBlock
                {
                    Text = "⭐ " + p.Name,
                    FontSize = 12.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(251, 246, 247)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(nameText, 0);
                topGrid.Children.Add(nameText);

                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var btnApply = new Button
                {
                    Content = "Apply",
                    Style = (Style)FindResource("PillButton"),
                    Background = new SolidColorBrush(Color.FromRgb(90, 36, 51)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(232, 86, 74)),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 138, 107)),
                    Padding = new Thickness(10, 3, 10, 3),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                btnApply.Click += (s, e) => { ApplyCustomPreset(p); };
                btnPanel.Children.Add(btnApply);

                var btnDelete = new Button
                {
                    Content = "✕",
                    Style = (Style)FindResource("PillButton"),
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Foreground = new SolidColorBrush(Color.FromRgb(179, 163, 174)),
                    Padding = new Thickness(6, 3, 6, 3),
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    ToolTip = "Delete this custom preset"
                };
                btnDelete.Click += (s, e) => { DeleteCustomPreset(p.Id); };
                btnPanel.Children.Add(btnDelete);

                Grid.SetColumn(btnPanel, 1);
                topGrid.Children.Add(btnPanel);
                mainStack.Children.Add(topGrid);

                // Bottom row: Value Badges
                var specsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                specsPanel.Children.Add(CreateSpecBadge("☀️ " + p.Brightness + "%", Color.FromRgb(251, 191, 36)));
                specsPanel.Children.Add(CreateSpecBadge("🌡️ " + p.ColorTemperature + "K", Color.FromRgb(255, 138, 107)));
                specsPanel.Children.Add(CreateSpecBadge("⚡ C:" + p.Contrast.ToString("0.00"), Color.FromRgb(232, 121, 166)));
                specsPanel.Children.Add(CreateSpecBadge("Γ:" + p.Gamma.ToString("0.00"), Color.FromRgb(52, 211, 153)));

                if (Math.Abs(p.RedGain - 1.0) > 0.01 || Math.Abs(p.GreenGain - 1.0) > 0.01 || Math.Abs(p.BlueGain - 1.0) > 0.01)
                {
                    string rgbText = string.Format("RGB {0}/{1}/{2}%", (int)(p.RedGain * 100), (int)(p.GreenGain * 100), (int)(p.BlueGain * 100));
                    specsPanel.Children.Add(CreateSpecBadge(rgbText, Color.FromRgb(244, 114, 182)));
                }

                mainStack.Children.Add(specsPanel);
                card.Child = mainStack;

                // Clicking anywhere on card applies the preset
                card.MouseLeftButtonUp += (s, e) =>
                {
                    DependencyObject dep = e.OriginalSource as DependencyObject;
                    if (dep != null && !IsChildOf(dep, btnDelete))
                    {
                        ApplyCustomPreset(p);
                    }
                };

                StackCustomPresets.Children.Add(card);
            }
        }

        private static Border CreateHdrNote()
        {
            var accent = Color.FromRgb(255, 195, 122);
            var note = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(28, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = "🌗 HDR is on",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accent)
            });
            sp.Children.Add(new TextBlock
            {
                Text = "Color temperature, RGB and software brightness (down to 8% light) go through a Windows color filter, " +
                       "which also applies to HDR games and videos. It covers all screens and can't run together with Windows' own " +
                       "color filters. Contrast and gamma may look different in HDR. Hardware DDC/CI brightness works normally.",
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 15,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(221, 208, 214))
            });
            note.Child = sp;
            return note;
        }

        private static Border CreateSpecBadge(string text, Color accentColor)
        {
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(35, accentColor.R, accentColor.G, accentColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 4)
            };
            b.Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accentColor)
            };
            return b;
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

        private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi) return;
            int val = (int)SliderBrightness.Value;
            TxtBrightnessValue.Text = val + "%";

            // Update icon
            if (val <= 30) TxtBrightnessIcon.Text = "🌙";
            else if (val <= 70) TxtBrightnessIcon.Text = "🌅";
            else TxtBrightnessIcon.Text = "☀️";

            // Mark manual override if user moved slider while solar sync was running
            if (_settings != null && _settings.SolarSyncEnabled)
            {
                _manualOverrideActive = true;
                if (BadgeSolarActive != null) BadgeSolarActive.Visibility = Visibility.Collapsed;
                if (BtnResumeSolar != null) BtnResumeSolar.Visibility = Visibility.Visible;
            }

            ApplyCurrentSettings();
            BuildMonitorRows();
            UpdateStatus();
        }

        private void SliderKelvin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi) return;
            int k = (int)SliderKelvin.Value;
            TxtKelvinValue.Text = k + " K";

            if (_settings != null && _settings.SolarSyncEnabled)
            {
                _manualOverrideActive = true;
                if (BadgeSolarActive != null) BadgeSolarActive.Visibility = Visibility.Collapsed;
                if (BtnResumeSolar != null) BtnResumeSolar.Visibility = Visibility.Visible;
            }

            // Compute RGB for Swatch preview
            double r, g, b;
            MonitorEngine.KelvinToRgb(k, out r, out g, out b);
            var swatchBrush = new SolidColorBrush(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
            swatchBrush.Freeze();
            BorderKelvinSwatch.Background = swatchBrush;

            if (k <= 3000) TxtKelvinDesc.Text = "Candlelight Warmth (Deep sleep & relaxation)";
            else if (k <= 4000) TxtKelvinDesc.Text = "Warm Amber (Nighttime blue light filter)";
            else if (k <= 5500) TxtKelvinDesc.Text = "Soft Warm White (Gentle reading & browsing)";
            else if (k <= 7000) TxtKelvinDesc.Text = "Natural Daylight (Standard sRGB reference)";
            else if (k <= 8500) TxtKelvinDesc.Text = "Crisp Daylight (High energy focus)";
            else TxtKelvinDesc.Text = "Ice Blue (Ultra-cool crisp whites)";

            ApplyCurrentSettings();
        }

        private void SliderRgb_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi) return;
            TxtRedVal.Text = (int)(SliderRed.Value * 100) + "%";
            TxtGreenVal.Text = (int)(SliderGreen.Value * 100) + "%";
            TxtBlueVal.Text = (int)(SliderBlue.Value * 100) + "%";
            ApplyCurrentSettings();
        }

        private void SliderCurves_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi) return;
            TxtContrastVal.Text = SliderContrast.Value.ToString("F2");
            TxtGammaVal.Text = SliderGamma.Value.ToString("F2");
            ApplyCurrentSettings();
        }

        private void ApplyCurrentSettings(bool forceColor = false)
        {
            if (_hardwareTestRunning) return;
            int brightness = (int)SliderBrightness.Value;
            int kelvin = (int)SliderKelvin.Value;
            double rGain = SliderRed.Value;
            double gGain = SliderGreen.Value;
            double bGain = SliderBlue.Value;
            double contrast = SliderContrast.Value;
            double gamma = SliderGamma.Value;

            bool syncAll = (ChkSyncDisplays.IsChecked == true) || (CmbDisplaySelect.SelectedIndex == 0);
            var targets = new List<DisplayInfo>();

            if (syncAll)
            {
                targets.AddRange(_engine.Displays);
            }
            else
            {
                int idx = CmbDisplaySelect.SelectedIndex - 1;
                if (idx >= 0 && idx < _engine.Displays.Count)
                {
                    targets.Add(_engine.Displays[idx]);
                }
            }

            foreach (var d in targets)
            {
                bool colorChanged = forceColor ||
                                    d.ColorTemperature != kelvin ||
                                    Math.Abs(d.RedGain - rGain) > 0.001 ||
                                    Math.Abs(d.GreenGain - gGain) > 0.001 ||
                                    Math.Abs(d.BlueGain - bGain) > 0.001 ||
                                    Math.Abs(d.Contrast - contrast) > 0.001 ||
                                    Math.Abs(d.Gamma - gamma) > 0.001;

                d.UserBrightness = brightness;
                d.ColorTemperature = kelvin;
                d.RedGain = rGain;
                d.GreenGain = gGain;
                d.BlueGain = bGain;
                d.Contrast = contrast;
                d.Gamma = gamma;

                if (d.SupportsDdcCi && d.UseHardwareBrightness)
                {
                    _engine.QueueHardwareBrightness(d, brightness);

                    if (colorChanged)
                    {
                        if (kelvin != 6500 || rGain != 1.0 || gGain != 1.0 || bGain != 1.0
                            || contrast != 1.0 || gamma != 1.0)
                        {
                            _engine.ApplyColorAndGamma(d);
                        }
                        else
                        {
                            _engine.ResetGammaRamp(d);
                        }
                    }
                }
                else
                {
                    // Software brightness via GPU gamma ramp (dims cursor, taskbar & all shell elements uniformly)
                    _engine.ApplyColorAndGamma(d);
                }
            }

            // Persist into memory settings
            _settings.Brightness = brightness;
            _settings.ColorTemperature = kelvin;
            _settings.RedGain = rGain;
            _settings.GreenGain = gGain;
            _settings.BlueGain = bGain;
            _settings.Contrast = contrast;
            _settings.Gamma = gamma;
            _settings.SyncAllMonitors = ChkSyncDisplays.IsChecked == true;

            SyncDisplayProfilesToSettings();

            // Debounced save: only writes to disk and registry after user stops adjusting
            if (_saveDebounceTimer != null)
            {
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        private void SyncDisplayProfilesToSettings()
        {
            if (_settings == null || _engine == null) return;
            if (_settings.DisplayProfiles == null) _settings.DisplayProfiles = new List<DisplayProfile>();

            foreach (var d in _engine.Displays)
            {
                var prof = _settings.DisplayProfiles.Find(p =>
                    string.Equals(p.DeviceName, d.DeviceName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.FriendlyName, d.FriendlyName, StringComparison.OrdinalIgnoreCase));
                if (prof == null)
                {
                    prof = new DisplayProfile { DeviceName = d.DeviceName, FriendlyName = d.FriendlyName };
                    _settings.DisplayProfiles.Add(prof);
                }

                prof.Brightness = d.UserBrightness;
                prof.ColorTemperature = d.ColorTemperature;
                prof.RedGain = d.RedGain;
                prof.GreenGain = d.GreenGain;
                prof.BlueGain = d.BlueGain;
                prof.Contrast = d.Contrast;
                prof.Gamma = d.Gamma;
                prof.UseHardwareBrightness = d.UseHardwareBrightness;
            }
        }

        private void ApplySolarCircadianTargets(int targetBrightness, int targetKelvin, bool applyColor, bool forceApply)
        {
            foreach (var d in _engine.Displays)
            {
                bool kelvinChanged = forceApply || (d.ColorTemperature != targetKelvin);
                bool brightnessChanged = forceApply || (d.UserBrightness != targetBrightness);
                d.UserBrightness = targetBrightness;
                if (applyColor)
                {
                    d.ColorTemperature = targetKelvin;
                }

                if (d.SupportsDdcCi && d.UseHardwareBrightness)
                {
                    // Only talk to the monitor when the target actually moved; DDC/CI writes are slow
                    // and some monitors flash their OSD on every write
                    if (brightnessChanged)
                    {
                        _engine.QueueHardwareBrightness(d, targetBrightness);
                    }

                    if (applyColor && kelvinChanged)
                    {
                        _engine.ApplyColorAndGamma(d);
                    }
                    else if (!applyColor && forceApply)
                    {
                        if (_engine.HasCustomGammaOrColor(d))
                            _engine.ApplyColorAndGamma(d);
                        else
                            _engine.ResetGammaRamp(d);
                    }
                }
                else if (brightnessChanged || (applyColor && kelvinChanged))
                {
                    _engine.ApplyColorAndGamma(d);
                }
            }

            SyncDisplayProfilesToSettings();
        }

        private void ApplyPresetByName(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName)) return;

            if (_settings != null && _settings.CustomPresets != null)
            {
                var cp = _settings.CustomPresets.Find(p =>
                    string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Id, presetName, StringComparison.OrdinalIgnoreCase));
                if (cp != null)
                {
                    ApplyCustomPreset(cp);
                    return;
                }
            }

            var presets = MonitorEngine.GetPresets();
            foreach (var p in presets)
            {
                if (p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase))
                {
                    if (_settings != null && _settings.SolarSyncEnabled)
                    {
                        _manualOverrideActive = true;
                        if (BadgeSolarActive != null) BadgeSolarActive.Visibility = Visibility.Collapsed;
                        if (BtnResumeSolar != null) BtnResumeSolar.Visibility = Visibility.Visible;
                    }

                    _isUpdatingUi = true;
                    SliderKelvin.Value = p.ColorTemp;
                    SliderRed.Value = p.RedGain;
                    SliderGreen.Value = p.GreenGain;
                    SliderBlue.Value = p.BlueGain;
                    SliderContrast.Value = p.Contrast;
                    SliderGamma.Value = p.Gamma;

                    TxtKelvinValue.Text = p.ColorTemp + " K";
                    TxtRedVal.Text = (int)(p.RedGain * 100) + "%";
                    TxtGreenVal.Text = (int)(p.GreenGain * 100) + "%";
                    TxtBlueVal.Text = (int)(p.BlueGain * 100) + "%";
                    TxtContrastVal.Text = p.Contrast.ToString("F2");
                    TxtGammaVal.Text = p.Gamma.ToString("F2");

                    double r, g, b;
                    MonitorEngine.KelvinToRgb(p.ColorTemp, out r, out g, out b);
                    BorderKelvinSwatch.Background = new SolidColorBrush(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));

                    _settings.ActivePreset = p.Name;
                    _isUpdatingUi = false;

                    ApplyCurrentSettings(true);
                    TxtStatus.Text = "Preset applied: " + p.Icon + " " + p.Name;
                    break;
                }
            }
        }

        private void ApplyCustomPreset(CustomPreset p)
        {
            if (p == null) return;

            if (_settings != null && _settings.SolarSyncEnabled)
            {
                _manualOverrideActive = true;
                if (BadgeSolarActive != null) BadgeSolarActive.Visibility = Visibility.Collapsed;
                if (BtnResumeSolar != null) BtnResumeSolar.Visibility = Visibility.Visible;
            }

            _isUpdatingUi = true;
            SliderBrightness.Value = p.Brightness;
            SliderKelvin.Value = p.ColorTemperature;
            SliderRed.Value = p.RedGain;
            SliderGreen.Value = p.GreenGain;
            SliderBlue.Value = p.BlueGain;
            SliderContrast.Value = p.Contrast;
            SliderGamma.Value = p.Gamma;

            TxtBrightnessValue.Text = p.Brightness + "%";
            TxtKelvinValue.Text = p.ColorTemperature + " K";
            TxtRedVal.Text = (int)(p.RedGain * 100) + "%";
            TxtGreenVal.Text = (int)(p.GreenGain * 100) + "%";
            TxtBlueVal.Text = (int)(p.BlueGain * 100) + "%";
            TxtContrastVal.Text = p.Contrast.ToString("F2");
            TxtGammaVal.Text = p.Gamma.ToString("F2");

            double r, g, b;
            MonitorEngine.KelvinToRgb(p.ColorTemperature, out r, out g, out b);
            var swatchBrush = new SolidColorBrush(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
            swatchBrush.Freeze();
            BorderKelvinSwatch.Background = swatchBrush;

            _settings.ActivePreset = p.Name;
            _isUpdatingUi = false;

            ApplyCurrentSettings(true);
            BuildMonitorRows();
            UpdateStatus();
            TxtStatus.Text = string.Format("Preset applied: ⭐ {0} ({1}%, {2}K, {3:0.00}γ)", p.Name, p.Brightness, p.ColorTemperature, p.Gamma);
        }

        private void DeleteCustomPreset(string id)
        {
            if (_settings == null || _settings.CustomPresets == null) return;
            int removed = _settings.CustomPresets.RemoveAll(p => p.Id == id);
            if (removed > 0)
            {
                ConfigManager.Save(_settings);
                BuildCustomPresetsList();
                if (_tray != null)
                {
                    _tray.UpdateCustomPresetsMenu(_settings.CustomPresets);
                }
                TxtStatus.Text = "Custom preset removed.";
            }
        }

        private void BtnSaveCustomPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;
            if (_settings.CustomPresets == null) _settings.CustomPresets = new List<CustomPreset>();

            string name = null;
            if (TxtCustomPresetName != null && !string.IsNullOrWhiteSpace(TxtCustomPresetName.Text))
            {
                name = TxtCustomPresetName.Text.Trim();
            }

            if (string.IsNullOrWhiteSpace(name) || name == "My Preset")
            {
                name = "Preset " + (_settings.CustomPresets.Count + 1);
            }
            if (name.Length > MaxPresetNameLength)
            {
                name = name.Substring(0, MaxPresetNameLength);
            }

            var preset = new CustomPreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Brightness = (int)SliderBrightness.Value,
                ColorTemperature = (int)SliderKelvin.Value,
                RedGain = SliderRed.Value,
                GreenGain = SliderGreen.Value,
                BlueGain = SliderBlue.Value,
                Contrast = SliderContrast.Value,
                Gamma = SliderGamma.Value
            };

            _settings.CustomPresets.Add(preset);
            ConfigManager.Save(_settings);

            BuildCustomPresetsList();
            if (_tray != null)
            {
                _tray.UpdateCustomPresetsMenu(_settings.CustomPresets);
            }

            if (TxtCustomPresetName != null)
            {
                TxtCustomPresetName.Text = "My Preset";
            }

            TxtStatus.Text = string.Format("Preset '{0}' saved! ({1}%, {2}K, {3:0.00}γ)", preset.Name, preset.Brightness, preset.ColorTemperature, preset.Gamma);
        }

        private void TxtCustomPresetName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnSaveCustomPreset_Click(sender, e);
                e.Handled = true;
            }
        }

        private void BtnQuickBrightness_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            int val;
            if (btn != null && btn.Tag != null && int.TryParse(btn.Tag.ToString(), out val))
            {
                SliderBrightness.Value = val;
            }
        }

        private void BtnQuickKelvin_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            int val;
            if (btn != null && btn.Tag != null && int.TryParse(btn.Tag.ToString(), out val))
            {
                SliderKelvin.Value = val;
            }
        }

        private void BtnPreset_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag != null)
            {
                ApplyPresetByName(btn.Tag.ToString());
            }
        }

        private void BtnResetRgb_Click(object sender, RoutedEventArgs e)
        {
            _isUpdatingUi = true;
            SliderRed.Value = 1.0;
            SliderGreen.Value = 1.0;
            SliderBlue.Value = 1.0;
            TxtRedVal.Text = "100%";
            TxtGreenVal.Text = "100%";
            TxtBlueVal.Text = "100%";
            _isUpdatingUi = false;
            ApplyCurrentSettings();
        }

        private void CmbDisplaySelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || CmbDisplaySelect.SelectedIndex < 0) return;

            int idx = CmbDisplaySelect.SelectedIndex;
            if (idx == 0) // All displays
            {
                if (ChkSyncDisplays != null && ChkSyncDisplays.IsChecked != true)
                {
                    _settings.SyncAllMonitors = true;
                    ChkSyncDisplays.IsChecked = true;
                }
                bool anyDdc = false;
                foreach (var d in _engine.Displays) { if (d.SupportsDdcCi) { anyDdc = true; break; } }
                TxtHardwareBadge.Text = (anyDdc ? "DDC/CI" : "GAMMA") + " • " + _engine.Displays.Count + " DISPLAYS";
            }
            else
            {
                if (ChkSyncDisplays != null && ChkSyncDisplays.IsChecked == true)
                {
                    _settings.SyncAllMonitors = false;
                    ChkSyncDisplays.IsChecked = false;
                }
                int monIdx = idx - 1;
                if (monIdx >= 0 && monIdx < _engine.Displays.Count)
                {
                    var d = _engine.Displays[monIdx];
                    string modeLabel = d.SupportsDdcCi ? "DDC/CI" : "GAMMA";
                    string resLabel = d.ResolutionWidth + "×" + d.ResolutionHeight;
                    TxtHardwareBadge.Text = modeLabel + " • " + resLabel;
                    _isUpdatingUi = true;
                    // Show actual hardware-read brightness for DDC/CI monitors
                    SliderBrightness.Value = d.SupportsDdcCi && d.UseHardwareBrightness
                        ? (int)d.CurrentBrightness
                        : d.UserBrightness;
                    TxtBrightnessValue.Text = (int)SliderBrightness.Value + "%";
                    SliderKelvin.Value = d.ColorTemperature;
                    SliderRed.Value = d.RedGain;
                    SliderGreen.Value = d.GreenGain;
                    SliderBlue.Value = d.BlueGain;
                    SliderContrast.Value = d.Contrast;
                    SliderGamma.Value = d.Gamma;

                    // Keep value labels and swatch in sync with the newly selected display
                    TxtKelvinValue.Text = d.ColorTemperature + " K";
                    TxtRedVal.Text = (int)(d.RedGain * 100) + "%";
                    TxtGreenVal.Text = (int)(d.GreenGain * 100) + "%";
                    TxtBlueVal.Text = (int)(d.BlueGain * 100) + "%";
                    TxtContrastVal.Text = d.Contrast.ToString("F2");
                    TxtGammaVal.Text = d.Gamma.ToString("F2");
                    double r, g, b;
                    MonitorEngine.KelvinToRgb(d.ColorTemperature, out r, out g, out b);
                    var swatchBrush = new SolidColorBrush(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
                    swatchBrush.Freeze();
                    BorderKelvinSwatch.Background = swatchBrush;
                    _isUpdatingUi = false;
                }
            }
        }

        private void ChkSyncDisplays_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkSyncDisplays == null || CmbDisplaySelect == null) return;
            _settings.SyncAllMonitors = ChkSyncDisplays.IsChecked == true;
            if (_settings.SyncAllMonitors)
            {
                CmbDisplaySelect.SelectedIndex = 0;
            }
            else
            {
                if (CmbDisplaySelect.SelectedIndex == 0 && _engine.Displays.Count > 0)
                {
                    CmbDisplaySelect.SelectedIndex = 1;
                }
            }
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

        #region Solar Circadian Methods

        private void UpdateSolarCircadian(bool forceApply)
        {
            if (_isUpdatingUi || _settings == null || TxtSolarPhase == null || _hardwareTestRunning) return;

            double lat = _settings.UserLatitude;
            double lon = _settings.UserLongitude;
            if (_settings.AutoLocation)
            {
                SolarEngine.GetDefaultCoordinates(out lat, out lon);
            }

            var status = SolarEngine.ComputeStatus(lat, lon, DateTime.Now,
                _settings.SolarDayBrightness, _settings.SolarNightBrightness,
                _settings.SolarDayKelvin, _settings.SolarNightKelvin);
            _lastSolarStatus = status;

            // Update UI elements
            TxtSolarPhase.Text = string.Format("{0} • {1:0.0}°", status.PhaseName, status.ElevationDeg);
            TxtSolarTimes.Text = status.SunTimesText;
            BarSunJourney.Value = status.DayProgressPercent;
            TxtSolarTargetBrightness.Text = "Target: " + status.TargetBrightness + "%";
            TxtSolarTargetKelvin.Text = "Warmth: " + status.TargetKelvin + " K";

            bool active = _settings.SolarSyncEnabled;
            bool colorSync = _settings.SolarColorSyncEnabled;

            if (BadgeSolarActive != null)
                BadgeSolarActive.Visibility = (active && !_manualOverrideActive) ? Visibility.Visible : Visibility.Collapsed;
            if (BtnResumeSolar != null)
                BtnResumeSolar.Visibility = (active && _manualOverrideActive) ? Visibility.Visible : Visibility.Collapsed;

            if (TxtSolarTargetKelvin != null)
            {
                if (colorSync)
                {
                    TxtSolarTargetKelvin.Text = "Warmth: " + status.TargetKelvin + " K";
                    TxtSolarTargetKelvin.Foreground = new SolidColorBrush(Color.FromRgb(255, 138, 107));
                }
                else
                {
                    TxtSolarTargetKelvin.Text = "Warmth: Off";
                    TxtSolarTargetKelvin.Foreground = new SolidColorBrush(Color.FromRgb(133, 117, 128));
                }
            }

            if (_tray != null)
            {
                string statusDesc = colorSync
                    ? (status.TargetBrightness + "% • " + status.TargetKelvin + "K")
                    : (status.TargetBrightness + "% Brightness Only");
                _tray.UpdateSolarMenuState(active, statusDesc);
            }

            if (active && (!_manualOverrideActive || forceApply))
            {
                _isUpdatingUi = true;
                SliderBrightness.Value = status.TargetBrightness;
                TxtBrightnessValue.Text = status.TargetBrightness + "%";

                if (colorSync)
                {
                    SliderKelvin.Value = status.TargetKelvin;
                    TxtKelvinValue.Text = status.TargetKelvin + " K";

                    double r, g, b;
                    MonitorEngine.KelvinToRgb(status.TargetKelvin, out r, out g, out b);
                    var swatchBrush = new SolidColorBrush(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
                    swatchBrush.Freeze();
                    BorderKelvinSwatch.Background = swatchBrush;
                }

                _isUpdatingUi = false;
                ApplySolarCircadianTargets(status.TargetBrightness, status.TargetKelvin, colorSync, forceApply);
                BuildMonitorRows();
                UpdateStatus();
            }
        }

        private void ChkSolarSync_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkSolarSync == null) return;
            bool isEnabled = ChkSolarSync.IsChecked == true;
            _settings.SolarSyncEnabled = isEnabled;
            _manualOverrideActive = false;

            if (!isEnabled)
            {
                // Return to the user's last manual brightness & warmth instead of wiping everything to defaults
                RestoreManualSettings();
                TxtStatus.Text = "Solar Sync turned off. Restored your manual settings.";
            }
            else
            {
                UpdateSolarCircadian(true);
                TxtStatus.Text = "Solar Sync enabled.";
            }

            if (_saveDebounceTimer != null)
            {
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        private void ChkSolarColorSync_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkSolarColorSync == null) return;
            bool enableColor = ChkSolarColorSync.IsChecked == true;
            _settings.SolarColorSyncEnabled = enableColor;
            UpdateSolarLimitsLabels();

            if (_settings.SolarSyncEnabled)
            {
                _manualOverrideActive = false;
                if (!enableColor)
                {
                    // Reset Kelvin to 6500K default across all displays while preserving individual calibration offsets
                    _isUpdatingUi = true;
                    SliderKelvin.Value = 6500;
                    TxtKelvinValue.Text = "6500 K";
                    BorderKelvinSwatch.Background = Brushes.White;
                    _isUpdatingUi = false;

                    foreach (var d in _engine.Displays)
                    {
                        d.ColorTemperature = 6500;
                        if (_engine.HasCustomGammaOrColor(d))
                            _engine.ApplyColorAndGamma(d);
                        else
                            _engine.ResetGammaRamp(d);
                    }
                    SyncDisplayProfilesToSettings();
                }
                UpdateSolarCircadian(true);
            }

            if (_saveDebounceTimer != null)
            {
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        private void RestoreManualSettings()
        {
            int brightness = _settings.Brightness;
            int kelvin = _settings.ColorTemperature;

            if (BadgeSolarActive != null) BadgeSolarActive.Visibility = Visibility.Collapsed;
            if (BtnResumeSolar != null) BtnResumeSolar.Visibility = Visibility.Collapsed;

            _isUpdatingUi = true;
            SliderBrightness.Value = brightness;
            SliderKelvin.Value = kelvin;
            TxtBrightnessValue.Text = brightness + "%";
            TxtKelvinValue.Text = kelvin + " K";
            double r, g, b;
            MonitorEngine.KelvinToRgb(kelvin, out r, out g, out b);
            var swatchBrush = new SolidColorBrush(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
            swatchBrush.Freeze();
            BorderKelvinSwatch.Background = swatchBrush;
            _isUpdatingUi = false;

            // Solar Sync drove every display, so restore every display
            foreach (var d in _engine.Displays)
            {
                d.UserBrightness = brightness;
                d.ColorTemperature = kelvin;
                if (d.SupportsDdcCi && d.UseHardwareBrightness)
                {
                    _engine.QueueHardwareBrightness(d, brightness);
                    if (_engine.HasCustomGammaOrColor(d))
                        _engine.ApplyColorAndGamma(d);
                    else
                        _engine.ResetGammaRamp(d);
                }
                else
                {
                    _engine.ApplyColorAndGamma(d);
                }
            }

            SyncDisplayProfilesToSettings();
            BuildMonitorRows();
            UpdateStatus();
        }

        private void BtnResumeSolar_Click(object sender, RoutedEventArgs e)
        {
            _manualOverrideActive = false;
            UpdateSolarCircadian(true);
        }

        private void SliderSolarLimits_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi || _settings == null || SliderSolarDayB == null) return;
            _settings.SolarDayBrightness = (int)SliderSolarDayB.Value;
            _settings.SolarNightBrightness = (int)SliderSolarNightB.Value;
            _settings.SolarDayKelvin = (int)SliderSolarDayK.Value;
            _settings.SolarNightKelvin = (int)SliderSolarNightK.Value;
            UpdateSolarLimitsLabels();
            UpdateSolarCircadian(false);
            if (_saveDebounceTimer != null)
            {
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        private void UpdateSolarLimitsLabels()
        {
            if (TxtSolarDayBVal != null && SliderSolarDayB != null)
                TxtSolarDayBVal.Text = (int)SliderSolarDayB.Value + "% Max Day";
            if (TxtSolarNightBVal != null && SliderSolarNightB != null)
                TxtSolarNightBVal.Text = (int)SliderSolarNightB.Value + "% Min Night";
            if (TxtSolarDayKVal != null && SliderSolarDayK != null)
                TxtSolarDayKVal.Text = (int)SliderSolarDayK.Value + " K";
            if (TxtSolarNightKVal != null && SliderSolarNightK != null)
                TxtSolarNightKVal.Text = (int)SliderSolarNightK.Value + " K";

            bool colorSync = _settings != null && _settings.SolarColorSyncEnabled;
            if (PanelSolarWarmthSettings != null)
            {
                PanelSolarWarmthSettings.Opacity = colorSync ? 1.0 : 0.45;
                PanelSolarWarmthSettings.IsEnabled = colorSync;
            }
            if (TxtSolarSubtitle != null)
            {
                TxtSolarSubtitle.Text = colorSync 
                    ? "Auto-adapts brightness & warmth with sun position" 
                    : "Auto-dims brightness based on sun position";
            }
        }

        #endregion

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
                    _engine.ApplyHardwareBrightness(d, 80);
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

            if (_foregroundReassertTimer != null)
            {
                _foregroundReassertTimer.Stop();
                _foregroundReassertTimer = null;
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

        private void BtnOpenColorCpl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(Environment.SystemDirectory, "colorcpl.exe");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnOpenWindowsDisplay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
            }
            catch { }
        }

        private string GetVersionInfo()
        {
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string built = File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd HH:mm");
            string mode = ConfigManager.IsPortableMode() ? "Portable" : "Installed";
            return string.Format("Displicare v{0}\nBuilt {1} • {2} • {3}\n{4}",
                App.Version, built, mode, Environment.Is64BitProcess ? "x64" : "x86",
                Environment.OSVersion.VersionString);
        }

        private void PopulateAbout()
        {
            TxtHeaderSubtitle.Text = "Display & Color Studio • v" + App.Version;
            TxtAboutVersion.Text = "v" + App.Version;

            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            TxtAboutDetails.Text = string.Format("Built {0} • {1} mode\nDisplay & color calibration with solar sync",
                File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd HH:mm"),
                ConfigManager.IsPortableMode() ? "Portable" : "Installed");
        }

        private void BtnCopyVersion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(GetVersionInfo());
                TxtStatus.Text = "Version info copied to clipboard.";
            }
            catch
            {
                // Clipboard can be briefly locked by another app
                TxtStatus.Text = "Couldn't access the clipboard, try again.";
            }
        }

        private void BtnOpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(ConfigManager.GetSettingsFolder()) { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnHardwareTest_Click(object sender, RoutedEventArgs e)
        {
            OpenHardwareTest(false);
        }

        internal void OpenHardwareTest(bool startNow)
        {
            if (_hardwareTestWindow == null)
            {
                _hardwareTestWindow = new HardwareTestWindow(this);
                _hardwareTestWindow.Closed += (s, ev) => { _hardwareTestWindow = null; };
                _hardwareTestWindow.Show();
            }
            _hardwareTestWindow.Activate();
            if (startNow) _hardwareTestWindow.StartTest();
        }

        /// <summary>
        /// Runs the hardware test with everything else that writes display state paused, then picks up again
        /// as if nothing happened (Solar Sync target, deferred display refresh).
        /// </summary>
        internal async System.Threading.Tasks.Task RunHardwareTestAsync(HardwareTest test, System.Threading.CancellationTokenSource cts)
        {
            if (_hardwareTestRunning) return;
            _hardwareTestRunning = true;
            _hardwareTest = test;
            _hardwareTestCts = cts;
            MainContentRoot.IsEnabled = false;
            try
            {
                await test.RunAsync(cts.Token);
            }
            finally
            {
                _hardwareTestRunning = false;
                _hardwareTest = null;
                _hardwareTestCts = null;
                MainContentRoot.IsEnabled = true;

                if (_topologyChangedDuringTest)
                {
                    _topologyChangedDuringTest = false;
                    HandleDisplayTopologyChange();
                }
                if (_settings != null && _settings.SolarSyncEnabled)
                {
                    UpdateSolarCircadian(true);
                }
                ReassertDisplayState();
            }
        }

        private void ChkRunAtStartup_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkRunAtStartup == null) return;
            bool enable = ChkRunAtStartup.IsChecked == true;
            ConfigManager.SetRunOnStartup(enable, _settings.StartMinimized);
            _settings.StartWithWindows = enable;
            if (_tray != null) _tray.UpdateStartupMenuState(enable);
            ScheduleSave();
        }

        private void ChkStartMinimized_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkStartMinimized == null) return;
            _settings.StartMinimized = ChkStartMinimized.IsChecked == true;
            // The startup entry carries --minimized, so rewrite it to match
            if (_settings.StartWithWindows)
            {
                ConfigManager.SetRunOnStartup(true, _settings.StartMinimized);
            }
            ScheduleSave();
        }

        private void ChkRestoreOnExit_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkRestoreOnExit == null) return;
            _settings.RestoreOnExit = ChkRestoreOnExit.IsChecked == true;
            ScheduleSave();
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
