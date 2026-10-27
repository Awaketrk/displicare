using System;
using System.Windows;

namespace Displicare
{
    /// <summary>
    /// Brightness without opening the window: the mouse wheel over the tray icon, and optional global hotkeys
    /// (Ctrl+Alt+Up / Down, RegisterHotKey, no hooks). Both move the brightness slider, so they act like dragging it
    /// (same displays, Solar Sync override), and show a small level note above the taskbar.
    /// </summary>
    public partial class MainWindow
    {
        internal const string BrightnessUpHotkey = "Ctrl+Alt+Up", BrightnessDownHotkey = "Ctrl+Alt+Down";
        private const int WheelStep = 5, HotkeyStep = 10;
        private const int HotkeyUpId = 1, HotkeyDownId = 2;
        private const int LevelNoteMs = 1500;

        private Rooms.HotkeyWindow _brightnessHotkeys;

        private void InitializeBrightnessKeys()
        {
            _tray.OnBrightnessWheel += step => Dispatcher.BeginInvoke((Action)(() => NudgeBrightness(step, WheelStep)));
            if (ChkBrightnessHotkeys != null)
            {
                ChkBrightnessHotkeys.Content = "Brightness hotkeys (" + BrightnessUpHotkey + " / Down)";
                ChkBrightnessHotkeys.IsChecked = _settings.BrightnessHotkeys;
            }
            UpdateBrightnessHotkeys();
        }

        /// <summary>Registers or drops the hotkeys to match the setting.</summary>
        private void UpdateBrightnessHotkeys()
        {
            if (_brightnessHotkeys != null)
            {
                _brightnessHotkeys.Dispose();
                _brightnessHotkeys = null;
            }
            if (!_settings.BrightnessHotkeys) return;

            _brightnessHotkeys = new Rooms.HotkeyWindow(id => NudgeBrightness(id == HotkeyUpId ? 1 : -1, HotkeyStep));
            var taken = new System.Collections.Generic.List<string>();
            if (!_brightnessHotkeys.Register(HotkeyUpId, BrightnessUpHotkey)) taken.Add(BrightnessUpHotkey);
            if (!_brightnessHotkeys.Register(HotkeyDownId, BrightnessDownHotkey)) taken.Add(BrightnessDownHotkey);
            if (taken.Count == 0) return;
            string which = string.Join(" and ", taken.ToArray());
            App.Log("Brightness hotkeys: " + which + " already used by another app");
            if (TxtStatus != null) TxtStatus.Text = which + " is already used by another app.";
        }

        private void ChkBrightnessHotkeys_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkBrightnessHotkeys == null) return;
            _settings.BrightnessHotkeys = ChkBrightnessHotkeys.IsChecked == true;
            UpdateBrightnessHotkeys();
            ScheduleSave();
        }

        /// <summary>One step up (+1) or down (-1) to the next multiple of <paramref name="step"/>.</summary>
        private void NudgeBrightness(int direction, int step)
        {
            if (_hardwareTestRunning || _isExited || SliderBrightness == null) return;
            int value = NextLevel((int)Math.Round(SliderBrightness.Value), step, direction);
            if (value != (int)Math.Round(SliderBrightness.Value)) SliderBrightness.Value = value;
            try { Rooms.RoomOverlay.ShowLevel("☀  Brightness", BrightnessTarget(), value, LevelNoteMs); }
            catch (Exception ex) { App.Log("Brightness note: " + ex.Message); }
        }

        /// <summary>
        /// The next multiple of <paramref name="step"/> above (direction &gt; 0) or below the current value, within
        /// 0-100: 63 goes to 65 or 60 in steps of 5, 60 to 65 or 55.
        /// </summary>
        internal static int NextLevel(int current, int step, int direction)
        {
            current = Math.Max(0, Math.Min(100, current));
            int next = direction > 0
                ? (current / step + 1) * step
                : (current % step == 0 ? current - step : current - current % step);
            return Math.Max(0, Math.Min(100, next));
        }

        /// <summary>Which displays the slider moves, for the note: "All displays", or the selected one's name.</summary>
        private string BrightnessTarget()
        {
            var displays = _engine.Displays;
            bool syncAll = ChkSyncDisplays.IsChecked == true || CmbDisplaySelect.SelectedIndex <= 0;
            if (displays.Count == 1) return displays[0].FriendlyName;
            if (syncAll) return "All displays";
            int idx = CmbDisplaySelect.SelectedIndex - 1;
            return idx < displays.Count ? displays[idx].FriendlyName : "All displays";
        }

        private void ShutdownBrightnessKeys()
        {
            if (_brightnessHotkeys != null)
            {
                _brightnessHotkeys.Dispose();
                _brightnessHotkeys = null;
            }
        }
    }
}
