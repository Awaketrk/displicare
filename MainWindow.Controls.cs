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
    /// <summary>Brightness and color controls: sliders, quick buttons, the display selection, and applying the values to the displays.</summary>
    public partial class MainWindow
    {
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
            var targets = _engine.Displays; // a fresh copy

            if (!syncAll)
            {
                int idx = CmbDisplaySelect.SelectedIndex - 1;
                var selected = idx >= 0 && idx < targets.Count ? targets[idx] : null;
                targets.Clear();
                if (selected != null) targets.Add(selected);
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

        /// <summary>
        /// Puts each screen's saved calibration on it: at startup, and after display changes, when room switching brings
        /// up a screen that wasn't on before. Screens already on match their profile (it's synced on every change).
        /// </summary>
        private void RestoreDisplayProfiles()
        {
            if (_settings.DisplayProfiles == null || _settings.DisplayProfiles.Count == 0) return;
            foreach (var d in _engine.Displays)
            {
                var prof = ConfigManager.FindProfile(_settings.DisplayProfiles, d.MonitorKey, d.FriendlyName, d.DeviceName);
                if (prof == null) continue;
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

        private void SyncDisplayProfilesToSettings()
        {
            if (_settings == null || _engine == null) return;
            if (_settings.DisplayProfiles == null) _settings.DisplayProfiles = new List<DisplayProfile>();

            foreach (var d in _engine.Displays)
            {
                var prof = ConfigManager.FindProfile(_settings.DisplayProfiles, d.MonitorKey, d.FriendlyName, d.DeviceName);
                if (prof == null)
                {
                    prof = new DisplayProfile();
                    _settings.DisplayProfiles.Add(prof);
                }
                prof.MonitorKey = d.MonitorKey;
                prof.FriendlyName = d.FriendlyName;
                prof.DeviceName = d.DeviceName;

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
                var displays = _engine.Displays;
                if (monIdx >= 0 && monIdx < displays.Count)
                {
                    var d = displays[monIdx];
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
    }
}
