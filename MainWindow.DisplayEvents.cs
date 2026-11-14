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
    /// <summary>Reacting to Windows: window messages, foreground and Start menu changes, display changes, sign-in/lock, late DDC/CI answers, and putting the gamma ramp back when something resets it.</summary>
    public partial class MainWindow
    {
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
            else if (_exitMsg != 0 && (uint)msg == _exitMsg)
            {
                // The uninstaller: exit cleanly so the files can be removed
                Dispatcher.BeginInvoke((Action)ExitApp);
                handled = true;
            }
            else if (_shellHookMsg != 0 && (uint)msg == _shellHookMsg)
            {
                // Shell events that can come with a gamma reset (activation, new windows, fullscreen, monitor moves).
                // Not title redraws, flashes, media keys or language switches: some apps redraw their title many times
                // a second, and each event costs a burst of six ramp read-backs. The watchdog still covers those.
                if (IsGammaRelevantShellEvent(wParam.ToInt64())) TriggerFastReassert();
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
            else if (msg == WM_POWERBROADCAST && wParam.ToInt64() == PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
            {
                OnPowerSetting(lParam);
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

        private const long HSHELL_REDRAW = 6, HSHELL_LANGUAGE = 8, HSHELL_APPCOMMAND = 12, HSHELL_HIGHBIT = 0x8000;

        internal static bool IsGammaRelevantShellEvent(long code)
        {
            code &= ~HSHELL_HIGHBIT; // HSHELL_FLASH = HSHELL_REDRAW | HSHELL_HIGHBIT, HSHELL_RUDEAPPACTIVATED likewise
            return code != HSHELL_REDRAW && code != HSHELL_LANGUAGE && code != HSHELL_APPCOMMAND;
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

        /// <summary>
        /// A display answered DDC/CI late (common right after sign-in or an HDR switch). Set to hardware brightness, it
        /// hands dimming from the software dimmer to its backlight; set to software, its card gets the DDC/CI option back.
        /// </summary>
        /// <summary>A monitor that used to answer DDC/CI missed every retry: software dimming takes over and its card says so.</summary>
        private void OnDdcLost(DisplayInfo d)
        {
            if (_hardwareTestRunning || !_engine.Displays.Contains(d)) return;
            _engine.ApplyColorAndGamma(d);
            PopulateDisplays();
            BuildMonitorRows();
            BuildDisplayHardwareCards();
        }

        private void OnDdcRecovered(DisplayInfo d)
        {
            if (_hardwareTestRunning || !_engine.Displays.Contains(d)) return;
            RestoreHardwarePreference(new[] { d });
            if (!d.UseHardwareBrightness)
            {
                BuildDisplayHardwareCards(); // the DDC/CI option comes back; dimming stays in software as chosen
                return;
            }
            try
            {
                _engine.QueueHardwareBrightness(d, d.UserBrightness);
                _engine.ApplyColorAndGamma(d); // rebuilt without software dimming
                PopulateDisplays();
                BuildMonitorRows();
                BuildDisplayHardwareCards();
            }
            catch (Exception ex)
            {
                App.Log("OnDdcRecovered error: " + ex);
            }
        }

        /// <summary>
        /// Up to 2.3.0 a single missed DDC/CI answer at startup switched a display to software brightness and saved
        /// that. Once, the first time a DDC/CI display answers, undo it; a later choice of software mode is kept.
        /// </summary>
        private void RestoreHardwarePreference(IEnumerable<DisplayInfo> displays)
        {
            if (_settings.HardwareBrightnessRestored) return;
            foreach (var d in displays)
            {
                if (!d.SupportsDdcCi) continue;
                _settings.HardwareBrightnessRestored = true;
                if (d.UseHardwareBrightness) continue;
                d.UseHardwareBrightness = true;
                App.Log("DDC/CI " + d.FriendlyName + ": back to hardware brightness (was switched off by a missed DDC/CI answer)");
            }
            if (_settings.HardwareBrightnessRestored) ScheduleSave();
        }

        private bool _displayRefreshRunning;
        private bool _displayRefreshAgain;
        private bool _forceSolarAfterRefresh;
        private bool _announceRefresh;

        /// <summary>
        /// Reads the screens again after a display change and puts everything back on them. The reading runs off the UI
        /// thread: each monitor is asked over DDC/CI, and one that doesn't answer (waking up, an HDMI handshake that
        /// resets every display) takes its whole timeout, several times in a row. Up to 2.5.3 the window, the tray menu
        /// and the hotkeys froze for that long.
        /// </summary>
        private void HandleDisplayTopologyChange()
        {
            if (_isExited) return;
            if (_hardwareTestRunning)
            {
                // Refreshing would swap out the display objects under the test; stop it and refresh afterwards
                StopHardwareTestForDisplayChange();
                return;
            }
            if (_displayRefreshRunning)
            {
                // Another change while the screens are being read: read them once more afterwards
                _displayRefreshAgain = true;
                return;
            }
            _displayRefreshRunning = true;
            var before = DisplayKeys(_engine.Displays);
            System.Threading.Tasks.Task.Run((Action)_engine.RefreshDisplays).ContinueWith(t =>
            {
                var error = t.Exception;
                Dispatcher.BeginInvoke((Action)(() => DisplaysRefreshed(before, error)));
            });
        }

        private void StopHardwareTestForDisplayChange()
        {
            _topologyChangedDuringTest = true;
            if (_hardwareTest != null) _hardwareTest.NoteInterruption("the display configuration changed");
            if (_hardwareTestCts != null) _hardwareTestCts.Cancel();
        }

        private void DisplaysRefreshed(List<string> before, Exception error)
        {
            _displayRefreshRunning = false;
            if (_isExited) return;
            if (error != null) App.Log("Display refresh error: " + error);
            if (_hardwareTestRunning)
            {
                // The test started while the screens were being read, and its display objects were just replaced
                _displayRefreshAgain = false;
                StopHardwareTestForDisplayChange();
                return;
            }
            try
            {
                // First, so that nothing below can skip it. Only when a screen is there that wasn't: a monitor can come
                // back without Windows reporting the display on, but an HDR switch or a game changing the resolution
                // isn't a reason to write to every monitor
                if (ScreenAppeared(before, DisplayKeys(_engine.Displays))) ScheduleBacklightRewrite();

                RestoreDisplayProfiles(); // a screen a room switch just turned on gets its own settings
                PopulateDisplays();
                RoomsDisplaysChanged();
                ShowSelectedDisplayOnSliders();

                RebuildRamps();
                ReassertDisplayState();
                StartHdrRecheck();
                bool forceSolar = _forceSolarAfterRefresh && !_manualOverrideActive;
                _forceSolarAfterRefresh = false;
                UpdateSolarCircadian(forceSolar); // a screen just back gets the sun's level now, not at the next tick
                if (_announceRefresh)
                {
                    _announceRefresh = false;
                    TxtStatus.Text = "Refreshed " + _engine.Displays.Count + " connected displays.";
                }
            }
            catch (Exception ex)
            {
                App.Log("Display change error: " + ex);
            }

            if (_displayRefreshAgain)
            {
                _displayRefreshAgain = false;
                HandleDisplayTopologyChange();
            }
        }

        private static List<string> DisplayKeys(IList<DisplayInfo> displays)
        {
            var keys = new List<string>();
            foreach (var d in displays) keys.Add(DisplayKey(d));
            return keys;
        }

        /// <summary>Whether a screen is on now that wasn't before (by model, since outputs change hands).</summary>
        internal static bool ScreenAppeared(IList<string> before, IList<string> after)
        {
            foreach (var key in after)
            {
                bool wasThere = false;
                foreach (var old in before)
                {
                    if (string.Equals(old, key, StringComparison.OrdinalIgnoreCase)) { wasThere = true; break; }
                }
                if (!wasThere) return true;
            }
            return false;
        }

        /// <summary>
        /// Runs a step at each of a list of seconds after <see cref="Start"/> (1 and 4, or 2, 6 and 15); starting again
        /// begins from the first. The step returns false to stop early.
        /// </summary>
        private sealed class SteppedTimer
        {
            private readonly int[] _seconds;
            private readonly Func<bool> _step;
            private readonly System.Windows.Threading.DispatcherTimer _timer = new System.Windows.Threading.DispatcherTimer();
            private int _index;

            public SteppedTimer(int[] seconds, Func<bool> step)
            {
                _seconds = seconds;
                _step = step;
                _timer.Tick += (s, ev) => Tick();
            }

            public void Start()
            {
                _timer.Stop();
                _index = 0;
                _timer.Interval = TimeSpan.FromSeconds(SecondsBeforeStep(_seconds, 0));
                _timer.Start();
            }

            public void Stop()
            {
                _timer.Stop();
            }

            private void Tick()
            {
                bool goOn = _step();
                _index++;
                int wait = SecondsBeforeStep(_seconds, _index);
                if (!goOn || wait < 0)
                {
                    _timer.Stop();
                    return;
                }
                _timer.Interval = TimeSpan.FromSeconds(wait);
            }
        }

        /// <summary>How long to wait before a step, counted from the one before it; -1 when there is no such step.</summary>
        internal static int SecondsBeforeStep(int[] seconds, int step)
        {
            if (seconds == null || step < 0 || step >= seconds.Length) return -1;
            return step == 0 ? seconds[0] : seconds[step] - seconds[step - 1];
        }

        private const int WM_POWERBROADCAST = 0x0218;
        private const long PBT_POWERSETTINGCHANGE = 0x8013;
        private const int DisplayStateOff = 0, DisplayStateUnknown = -1;
        private static readonly Guid GuidConsoleDisplayState = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        // How long after a wake the backlight is written again: the monitor may still be coming up for the first ones
        internal static readonly int[] BacklightRewriteSeconds = { 2, 6, 15 };
        // While Solar Sync drives, this often too: a monitor switched off with its own button comes back without Windows noticing
        private static readonly TimeSpan SolarBacklightRewriteEvery = TimeSpan.FromMinutes(10);

        private IntPtr _displayStateNotification = IntPtr.Zero;
        private int _displayState = DisplayStateUnknown;
        private SteppedTimer _backlightRewrite;
        private DateTime _lastBacklightRewriteUtc = DateTime.UtcNow;

        private void WatchScreenPower(IntPtr hwnd)
        {
            var guid = GuidConsoleDisplayState;
            _displayStateNotification = RegisterPowerSettingNotification(hwnd, ref guid, 0); // 0 = DEVICE_NOTIFY_WINDOW_HANDLE
            if (_displayStateNotification == IntPtr.Zero) App.Log("Screen power notification not available");
        }

        private void StopWatchingScreenPower()
        {
            if (_backlightRewrite != null) _backlightRewrite.Stop();
            if (_hdrRecheck != null) _hdrRecheck.Stop();
            if (_displayStateNotification != IntPtr.Zero)
            {
                try { UnregisterPowerSettingNotification(_displayStateNotification); } catch { }
                _displayStateNotification = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Off to on or dimmed. Not the first report, which Windows sends on registering and which is just the current state.
        /// </summary>
        internal static bool ScreensCameOn(int previous, int state)
        {
            return previous == DisplayStateOff && state != DisplayStateOff;
        }

        private void OnPowerSetting(IntPtr setting)
        {
            try
            {
                // POWERBROADCAST_SETTING: a GUID (16 bytes), the data length (4), then the data
                byte[] guidBytes = new byte[16];
                System.Runtime.InteropServices.Marshal.Copy(setting, guidBytes, 0, 16);
                if (new Guid(guidBytes) != GuidConsoleDisplayState) return;
                if (System.Runtime.InteropServices.Marshal.ReadInt32(setting, 16) < 4) return; // the state is a DWORD

                int state = System.Runtime.InteropServices.Marshal.ReadInt32(setting, 20);
                bool cameOn = ScreensCameOn(_displayState, state);
                _displayState = state;
                if (cameOn)
                {
                    App.Log("Screens on: writing the backlight again");
                    Dispatcher.BeginInvoke((Action)ScheduleBacklightRewrite);
                }
            }
            catch (Exception ex)
            {
                App.Log("Screen power notification error: " + ex.Message);
            }
        }

        /// <summary>
        /// A monitor in standby accepts a backlight write and reports the new value, but wakes at its old brightness until
        /// it's written again (measured on the AORUS FO32U2). Solar Sync's writes during standby stayed unseen, and it
        /// skipped the rewrite because the value already matched. A repeated value doesn't change anything on screen.
        /// </summary>
        private void ScheduleBacklightRewrite()
        {
            if (_isExited) return;
            if (_backlightRewrite == null) _backlightRewrite = new SteppedTimer(BacklightRewriteSeconds, RewriteBacklights);
            _backlightRewrite.Start();
        }

        /// <summary>
        /// Whether the monitor reports the brightness Displicare set, compared in the monitor's own steps (15% and 16% are
        /// the same step on a 0-50 monitor). Only then is writing it again safe: another value was set on the monitor
        /// itself (its buttons, another tool), and with Solar Sync off that is the user's to keep.
        /// </summary>
        internal static bool BacklightHoldsOurValue(DisplayInfo d)
        {
            if (d == null || !d.SupportsDdcCi || !d.UseHardwareBrightness) return false;
            return MonitorEngine.PercentToRaw((int)d.CurrentBrightness, d.MinBrightness, d.MaxBrightness)
                == MonitorEngine.PercentToRaw(d.UserBrightness, d.MinBrightness, d.MaxBrightness);
        }

        /// <summary>Writes each DDC/CI backlight that already holds our value once more. False when there's no point going on.</summary>
        private bool RewriteBacklights()
        {
            if (_isExited) return false;
            // Off again: a write now is the kind that isn't shown. The screens coming on schedules it again
            if (_displayState == DisplayStateOff) return false;
            if (_hardwareTestRunning) return true;

            foreach (var d in _engine.Displays)
            {
                if (BacklightHoldsOurValue(d)) _engine.QueueHardwareBrightness(d, d.UserBrightness);
            }
            _lastBacklightRewriteUtc = DateTime.UtcNow;
            return true;
        }

        internal static bool BacklightRewriteDue(DateTime lastUtc, DateTime nowUtc)
        {
            return nowUtc < lastUtc || nowUtc - lastUtc >= SolarBacklightRewriteEvery;
        }

        /// <summary>
        /// From the Solar Sync tick. A monitor switched off with its own button (or on another input) accepts Solar Sync's
        /// writes like one in standby, and comes back without a display change or a screens-on report, so nothing else
        /// writes its backlight again. Only while Solar Sync drives: otherwise nothing was written behind the user's back.
        /// </summary>
        private void RewriteBacklightsForSolarSync()
        {
            bool drives = _settings != null && _settings.SolarSyncEnabled && !_manualOverrideActive;
            if (drives && BacklightRewriteDue(_lastBacklightRewriteUtc, DateTime.UtcNow)) RewriteBacklights();
        }

        /// <summary>
        /// Writes every ramp from the current settings. The ramp depends on HDR (PQ in HDR, 2.2 in SDR), so the watchdog's
        /// "rewrite the last ramp" isn't enough after a display change.
        /// </summary>
        private void RebuildRamps()
        {
            foreach (var d in _engine.Displays)
            {
                if (!d.SupportsDdcCi || !d.UseHardwareBrightness || _engine.HasCustomGammaOrColor(d))
                {
                    _engine.ApplyColorAndGamma(d);
                }
                else if (d.LastAppliedRamp != null)
                {
                    // On its backlight with neutral colors now, but our ramp is still there (software dimming from a
                    // missed DDC/CI read, say): the watchdog skips neutral displays, so it would stay until a resync
                    _engine.ResetGammaRamp(d);
                }
            }
        }

        private static readonly int[] HdrRecheckSeconds = { 1, 4 };
        private SteppedTimer _hdrRecheck;

        /// <summary>
        /// Windows reports the display change while HDR is still switching, so the refresh may have read the old state:
        /// look again after 1 and 4 seconds and rebuild the ramps and cards if it changed.
        /// </summary>
        private void StartHdrRecheck()
        {
            if (_hdrRecheck == null) _hdrRecheck = new SteppedTimer(HdrRecheckSeconds, RecheckHdrNow);
            _hdrRecheck.Start();
        }

        private bool RecheckHdrNow()
        {
            if (_isExited) return false;
            if (_hardwareTestRunning || !_engine.RecheckHdr()) return true;
            RebuildRamps();
            PopulateDisplays();
            BuildMonitorRows();
            BuildDisplayHardwareCards();
            return true;
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
    }
}
