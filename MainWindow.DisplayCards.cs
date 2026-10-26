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
    /// <summary>The display picker, the monitor rows and the hardware cards on the Displays tab.</summary>
    public partial class MainWindow
    {
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
    }
}
