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
        /// A display set to hardware brightness answered DDC/CI late (common right after sign-in): hand dimming from
        /// the software dimmer to its backlight, as if hardware mode had just been turned on.
        /// </summary>
        private void OnDdcRecovered(DisplayInfo d)
        {
            if (_hardwareTestRunning || !_engine.Displays.Contains(d)) return;
            RestoreHardwarePreference(new[] { d });
            if (!d.UseHardwareBrightness) return;
            try
            {
                _engine.QueueHardwareBrightness(d, d.UserBrightness);
                _engine.ApplyColorAndGamma(d); // rebuilt without software dimming; also updates the HDR color filter
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
                RestoreDisplayProfiles(); // a screen a room switch just turned on gets its own settings
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
    }
}
