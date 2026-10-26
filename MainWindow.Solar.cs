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
    }
}
