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
    /// <summary>Built-in and custom presets: the preset list, applying, saving and deleting.</summary>
    public partial class MainWindow
    {
        /// <summary>Warmth sync drives the color temperature, so presets and the warmth slider would only fight it.</summary>
        private bool WarmthSyncDrivesColor
        {
            get { return _settings != null && _settings.SolarSyncEnabled && _settings.SolarColorSyncEnabled; }
        }

        private const string PresetsLockedMessage = "Warmth sync is controlling color temperature. Turn it off in Solar Sync to use presets.";

        /// <summary>
        /// The preset the current values are, as a tray key: a built-in's name or "custom:id", or null. Worked out from the
        /// values, not remembered, so it's right for each screen and goes away when a slider moves. A custom preset also
        /// has a brightness, and wins over a built-in with the same colors.
        /// </summary>
        internal static string MatchPreset(int kelvin, double red, double green, double blue, double contrast, double gamma, int brightness,
            IList<ColorPreset> builtIn, IList<CustomPreset> custom)
        {
            if (custom != null)
            {
                foreach (var p in custom)
                {
                    if (p.Brightness == brightness && SameColors(kelvin, red, green, blue, contrast, gamma,
                        p.ColorTemperature, p.RedGain, p.GreenGain, p.BlueGain, p.Contrast, p.Gamma)) return "custom:" + p.Id;
                }
            }
            if (builtIn != null)
            {
                foreach (var p in builtIn)
                {
                    if (SameColors(kelvin, red, green, blue, contrast, gamma,
                        p.ColorTemp, p.RedGain, p.GreenGain, p.BlueGain, p.Contrast, p.Gamma)) return p.Name;
                }
            }
            return null;
        }

        private static bool SameColors(int k1, double r1, double g1, double b1, double c1, double m1,
            int k2, double r2, double g2, double b2, double c2, double m2)
        {
            const double tolerance = 0.005;
            return k1 == k2 && Math.Abs(r1 - r2) < tolerance && Math.Abs(g1 - g2) < tolerance && Math.Abs(b1 - b2) < tolerance
                && Math.Abs(c1 - c2) < tolerance && Math.Abs(m1 - m2) < tolerance;
        }

        private readonly Dictionary<string, Border> _customPresetCards = new Dictionary<string, Border>();
        private static readonly Brush ActivePresetBackground = FrozenBrush(90, 36, 51);
        private static readonly Brush ActivePresetBorder = FrozenBrush(232, 86, 74);
        private static readonly Brush CustomPresetBorder = FrozenBrush(58, 42, 59);

        private static Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>Highlights the active preset here and in the tray, and locks the color controls under warmth sync.</summary>
        private void UpdatePresetStates()
        {
            if (GridBuiltInPresets == null || _settings == null) return;

            bool locked = WarmthSyncDrivesColor;
            string active = locked ? null : MatchPreset((int)SliderKelvin.Value, SliderRed.Value, SliderGreen.Value, SliderBlue.Value,
                SliderContrast.Value, SliderGamma.Value, (int)SliderBrightness.Value, MonitorEngine.GetPresets(), _settings.CustomPresets);

            foreach (var child in GridBuiltInPresets.Children)
            {
                var button = child as Button;
                if (button == null) continue;
                if (active != null && Equals(button.Tag, active))
                {
                    button.Background = ActivePresetBackground;
                    button.BorderBrush = ActivePresetBorder;
                }
                else
                {
                    button.ClearValue(Control.BackgroundProperty);
                    button.ClearValue(Control.BorderBrushProperty);
                }
            }
            foreach (var card in _customPresetCards)
            {
                card.Value.BorderBrush = "custom:" + card.Key == active ? ActivePresetBorder : CustomPresetBorder;
            }

            double dimmed = locked ? 0.45 : 1.0;
            GridBuiltInPresets.IsEnabled = !locked;
            GridBuiltInPresets.Opacity = dimmed;
            StackCustomPresets.IsEnabled = !locked;
            StackCustomPresets.Opacity = dimmed;
            TxtPresetsLocked.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            SliderKelvin.IsEnabled = !locked;
            SliderKelvin.Opacity = dimmed;
            GridQuickKelvin.IsEnabled = !locked;
            GridQuickKelvin.Opacity = dimmed;
            TxtKelvinLocked.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;

            if (_tray != null) _tray.UpdatePresetMenuState(active, locked);
        }

        private void BuildCustomPresetsList()
        {
            if (StackCustomPresets == null) return;
            StackCustomPresets.Children.Clear();
            _customPresetCards.Clear();

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
                _customPresetCards[p.Id] = card;

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
            UpdatePresetStates();
        }

        private void ApplyPresetByName(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName)) return;
            if (WarmthSyncDrivesColor)
            {
                TxtStatus.Text = PresetsLockedMessage;
                return;
            }

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
                    UpdateSliderLabels();

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
            if (WarmthSyncDrivesColor)
            {
                TxtStatus.Text = PresetsLockedMessage;
                return;
            }

            if (_settings != null && _settings.SolarSyncEnabled)
            {
                PauseSolarSync();
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
            UpdateSliderLabels();

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
            if (_settings.CustomPresets.Count >= ConfigManager.MaxCustomPresets)
            {
                // Past the limit a preset would save fine and be gone after the next restart
                TxtStatus.Text = "You have the maximum of " + ConfigManager.MaxCustomPresets + " custom presets. Delete one to save another.";
                return;
            }

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

        private void BtnPreset_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag != null)
            {
                ApplyPresetByName(btn.Tag.ToString());
            }
        }
    }
}
