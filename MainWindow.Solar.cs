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
    /// <summary>Solar Sync: following the sun's position and the manual override.</summary>
    public partial class MainWindow
    {
        private void ApplySolarCircadianTargets(int targetBrightness, int targetKelvin, bool applyColor, bool forceApply)
        {
            foreach (var d in _engine.Displays)
            {
                bool kelvinChanged = forceApply || (d.ColorTemperature != targetKelvin);
                // Also when the monitor's own backlight drifted from the target (woke from standby, a display switch,
                // DDC/CI turned off and on): comparing only with our last target never put those right
                bool brightnessChanged = forceApply || (d.UserBrightness != targetBrightness)
                    || (d.SupportsDdcCi && d.UseHardwareBrightness && d.CurrentBrightness != (uint)targetBrightness);
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

        #region Solar Circadian Methods

        // The sun phase when a manual change paused Solar Sync; it resumes by itself when the phase changes
        private string _overridePhase;

        /// <summary>
        /// A manual change (slider, tray wheel, hotkeys, preset) while Solar Sync runs: it pauses until the sun reaches its
        /// next phase, as f.lux does. Up to 2.5.2 it stayed paused until Resume was clicked, so one scroll in the
        /// afternoon kept the screen there all evening.
        /// </summary>
        private void PauseSolarSync()
        {
            if (!_manualOverrideActive)
            {
                _overridePhase = _lastSolarStatus != null ? _lastSolarStatus.PhaseName : null;
                _settings.SolarPausedInPhase = _overridePhase;
                _settings.SolarPausedAtTicks = DateTime.UtcNow.Ticks;
                ScheduleSave();
                App.Log("Solar Sync: paused by a manual change until the next sun phase (now " + _overridePhase + ")");
            }
            _manualOverrideActive = true;
            if (BadgeSolarActive != null) BadgeSolarActive.Visibility = Visibility.Collapsed;
            if (BtnResumeSolar != null) BtnResumeSolar.Visibility = Visibility.Visible;
        }

        private void ClearSolarPause()
        {
            _manualOverrideActive = false;
            _overridePhase = null;
            _settings.SolarPausedInPhase = null;
            _settings.SolarPausedAtTicks = 0;
            ScheduleSave();
        }

        /// <summary>
        /// At startup: a pause saved by the last run still holds in the same sun phase. Without this, a restart let the sun
        /// take brightness and warmth back from a preset. Only within 12 hours, or yesterday's "Peak Daylight" would count.
        /// </summary>
        internal static bool SolarPauseHolds(string pausedIn, long pausedAtTicks, string phaseNow, DateTime utcNow)
        {
            if (string.IsNullOrEmpty(pausedIn) || pausedIn != phaseNow || pausedAtTicks <= 0) return false;
            var age = utcNow - new DateTime(pausedAtTicks, DateTimeKind.Utc);
            return age >= TimeSpan.Zero && age < TimeSpan.FromHours(12);
        }

        /// <summary>Whether a pause from <paramref name="pausedIn"/> is over now that the sun is in <paramref name="phaseNow"/>.</summary>
        internal static bool SolarPauseEnds(string pausedIn, string phaseNow)
        {
            return pausedIn != null && phaseNow != null && pausedIn != phaseNow;
        }

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

            if (_manualOverrideActive && SolarPauseEnds(_overridePhase, status.PhaseName))
            {
                ClearSolarPause();
                App.Log("Solar Sync: resumed at " + status.PhaseName);
            }

            // Update UI elements
            TxtSolarPhase.Text = string.Format("{0} • {1:0.0}°", status.PhaseName, status.ElevationDeg);
            TxtSolarTimes.Text = status.SunTimesText;
            BarSunJourney.Value = status.DayProgressPercent;
            TxtSolarTargetBrightness.Text = "Target: " + status.TargetBrightness + "%";
            TxtSolarTargetKelvin.Text = "Warmth: " + status.TargetKelvin + " K";

            UpdatePresetStates();

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
                UpdateSliderLabels();

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
            ClearSolarPause();
            UpdatePresetStates();

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
            UpdatePresetStates();
            UpdateSolarLimitsLabels();

            if (_settings.SolarSyncEnabled)
            {
                ClearSolarPause();
                if (!enableColor)
                {
                    // The sun lets go of the color: each screen gets the user's own back. Up to 2.5.3 this reset to 6500 K
                    // and saved that, so a preset chosen earlier was lost.
                    RestoreManualColors();
                    ShowManualKelvinOnSliders();

                    foreach (var d in _engine.Displays)
                    {
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

        /// <summary>
        /// The color temperature a screen returns to when warmth sync lets go. With screen sync off each screen has its own
        /// in its profile (the sun's never gets saved there); with sync on they all share the slider's.
        /// </summary>
        internal static int ManualKelvin(bool syncAll, DisplayProfile profile, int settingsKelvin)
        {
            return !syncAll && profile != null ? profile.ColorTemperature : settingsKelvin;
        }

        /// <summary>What a screen's profile keeps: under warmth sync the screen shows the sun's color, not the user's.</summary>
        internal static int KelvinToSave(bool warmthSyncDrives, int savedKelvin, int screenKelvin)
        {
            return warmthSyncDrives ? savedKelvin : screenKelvin;
        }

        private void RestoreManualColors()
        {
            foreach (var d in _engine.Displays)
            {
                var profile = ConfigManager.FindProfile(_settings.DisplayProfiles, d.MonitorKey, d.FriendlyName, d.DeviceName);
                d.ColorTemperature = ManualKelvin(_settings.SyncAllMonitors, profile, _settings.ColorTemperature);
            }
        }

        private void ShowManualKelvinOnSliders()
        {
            int kelvin = _settings.ColorTemperature;
            var displays = _engine.Displays;
            int selected = CmbDisplaySelect.SelectedIndex - 1;
            if (!_settings.SyncAllMonitors && selected >= 0 && selected < displays.Count) kelvin = displays[selected].ColorTemperature;

            _isUpdatingUi = true;
            SliderKelvin.Value = kelvin;
            TxtKelvinValue.Text = kelvin + " K";
            double r, g, b;
            MonitorEngine.KelvinToRgb(kelvin, out r, out g, out b);
            var swatchBrush = new SolidColorBrush(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
            swatchBrush.Freeze();
            BorderKelvinSwatch.Background = swatchBrush;
            UpdateSliderLabels();
            _isUpdatingUi = false;
        }

        private void RestoreManualSettings()
        {
            int brightness = _settings.Brightness;

            if (BadgeSolarActive != null) BadgeSolarActive.Visibility = Visibility.Collapsed;
            if (BtnResumeSolar != null) BtnResumeSolar.Visibility = Visibility.Collapsed;

            // Solar Sync only touched the colors with warmth sync on; otherwise each screen still has its own
            if (_settings.SolarColorSyncEnabled) RestoreManualColors();

            _isUpdatingUi = true;
            SliderBrightness.Value = brightness;
            TxtBrightnessValue.Text = brightness + "%";
            _isUpdatingUi = false;
            ShowManualKelvinOnSliders(); // the icon, warmth text and active preset follow the restored values

            // Solar Sync drove every display, so restore every display
            foreach (var d in _engine.Displays)
            {
                d.UserBrightness = brightness;
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
            ClearSolarPause();
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
    }
}
