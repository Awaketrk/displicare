using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Displicare.Rooms;

namespace Displicare
{
    /// <summary>
    /// Room switching in the main window: the on/off switch, the Rooms tab (one card per room, built in code like the
    /// display cards) and the tray submenu. All of it stays idle while room switching is off.
    /// </summary>
    public partial class MainWindow
    {
        private RoomManager _rooms;
        private readonly HashSet<string> _expandedRooms = new HashSet<string>();
        private bool _roomsRebuildPending;
        private string _pendingRoomCommand;

        private void InitializeRooms()
        {
            _isUpdatingUi = true;
            ChkRoomsEnabled.IsChecked = _settings.RoomsEnabled;
            _isUpdatingUi = false;

            _tray.OnRoomSelected += key =>
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (_rooms != null) _rooms.SwitchTo(_rooms.FindByKey(key), false);
                }));
            };
            ApplyRoomsEnabled();
        }

        private void ApplyRoomsEnabled()
        {
            bool on = _settings.RoomsEnabled;
            ColTabRooms.Width = on ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            TabRooms.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on && TabRooms.IsChecked == true) TabMonitors.IsChecked = true;

            if (on)
            {
                if (_rooms == null)
                {
                    _rooms = new RoomManager(_settings, ScheduleSave, Dispatcher);
                    _rooms.CanSwitch = () => !_hardwareTestRunning;
                    _rooms.Changed += RoomsChanged;
                    _rooms.Notify += (title, text, warning) => _tray.ShowBalloon(title, text, warning);
                }
                _rooms.Start();
                if (_pendingRoomCommand != null)
                {
                    SwitchRoomFromCommandLine(_pendingRoomCommand);
                    _pendingRoomCommand = null;
                }
            }
            else
            {
                if (_rooms != null)
                {
                    _rooms.Stop();
                }
                StackRooms.Children.Clear();
                _tray.UpdateRoomsMenu(null, false);
            }
        }

        private void ChkRoomsEnabled_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi) return;
            _settings.RoomsEnabled = ChkRoomsEnabled.IsChecked == true;
            ScheduleSave();
            ApplyRoomsEnabled();
            if (_settings.RoomsEnabled) TabRooms.IsChecked = true;
        }

        /// <summary>From HandleDisplayTopologyChange: new screens become rooms, and the active room learns its settings.</summary>
        private void RoomsDisplaysChanged()
        {
            if (_rooms != null && _rooms.IsRunning) _rooms.Refresh();
        }

        private void ShutdownRooms()
        {
            if (_rooms != null)
            {
                _rooms.Dispose();
                _rooms = null;
            }
        }

        /// <summary>"Displicare.exe --room Living room" when this is the first instance.</summary>
        internal void SwitchRoomFromCommandLine(string room)
        {
            if (_rooms == null || !_rooms.IsRunning)
            {
                if (!_settings.RoomsEnabled)
                {
                    _tray.ShowBalloon("Room switching is off", "Turn it on in Displays > General Settings.", true);
                    return;
                }
                _pendingRoomCommand = room;
                return;
            }
            var target = _rooms.FindRoom(room);
            if (target == null) _tray.ShowBalloon("No room called \"" + room + "\"", "", true);
            else _rooms.SwitchTo(target, false);
        }

        private void RoomsChanged()
        {
            if (_rooms == null) return;
            var statuses = _rooms.Statuses();

            var menu = new List<Tuple<string, string, bool, bool>>();
            foreach (var s in statuses)
                if (!s.Room.Hidden) menu.Add(Tuple.Create(s.Room.Name, s.Room.Key, s.Active, s.Connected));
            _tray.UpdateRoomsMenu(menu, _rooms.Busy);

            // Rebuilding while a name or hotkey box has focus would throw away what is being typed; do it on blur.
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused is TextBox && IsChildOf(focused, StackRooms))
            {
                _roomsRebuildPending = true;
                return;
            }
            BuildRoomCards(statuses);
        }

        // ---------------------------------------------------------------- cards

        private static readonly Brush RoomCardBg = MakeBrush("#1E1520");
        private static readonly Brush RoomCardBorder = MakeBrush("#3A2A3B");
        private static readonly Brush RoomAccent = MakeBrush("#FF6B5A");
        private static readonly Brush RoomTextPrimary = MakeBrush("#FBF6F7");
        private static readonly Brush RoomTextMuted = MakeBrush("#B3A3AE");
        private static readonly Brush RoomFieldBg = MakeBrush("#2A1E2B");
        private static readonly Brush RoomFieldBorder = MakeBrush("#433145");

        private static Brush MakeBrush(string hex)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        private void BuildRoomCards(List<RoomStatus> statuses)
        {
            StackRooms.Children.Clear();
            StackRooms.Children.Add(BuildRoomsHeaderCard(statuses));

            foreach (var s in statuses) if (!s.Room.Hidden) StackRooms.Children.Add(BuildRoomCard(s));

            var hidden = statuses.FindAll(s => s.Room.Hidden);
            if (hidden.Count > 0)
            {
                StackRooms.Children.Add(SectionLabel("HIDDEN ROOMS", new Thickness(2, 6, 0, 6)));
                foreach (var s in hidden) StackRooms.Children.Add(BuildRoomCard(s));
            }
        }

        private Border Card(UIElement child, bool highlight)
        {
            var card = new Border();
            card.Background = RoomCardBg;
            card.CornerRadius = new CornerRadius(14);
            card.Padding = new Thickness(16, 14, 16, 14);
            card.BorderBrush = highlight ? RoomAccent : RoomCardBorder;
            card.BorderThickness = new Thickness(highlight ? 1.5 : 1);
            card.Margin = new Thickness(0, 0, 0, 10);
            card.Child = child;
            return card;
        }

        private static TextBlock SectionLabel(string text, Thickness margin)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = 10.5;
            t.FontWeight = FontWeights.Bold;
            t.Foreground = RoomTextMuted;
            t.Margin = margin;
            return t;
        }

        private static TextBlock Muted(string text)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = 11;
            t.Foreground = RoomTextMuted;
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        private ComboBox Combo(double width)
        {
            var c = new ComboBox();
            c.Style = (Style)FindResource("DarkComboBox");
            c.Height = 32;
            if (width > 0) c.Width = width;
            return c;
        }

        private static ComboBoxItem Item(string text, object tag)
        {
            var i = new ComboBoxItem();
            i.Content = text;
            i.Tag = tag;
            return i;
        }

        private static void Select(ComboBox combo, object tag)
        {
            foreach (ComboBoxItem i in combo.Items)
                if (Equals(i.Tag, tag)) { combo.SelectedItem = i; return; }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        /// <summary>Two-column row: label on the left, control on the right.</summary>
        private static Grid Row(string label, UIElement control)
        {
            var g = new Grid();
            g.Margin = new Thickness(0, 0, 0, 8);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var t = new TextBlock();
            t.Text = label;
            t.FontSize = 12;
            t.Foreground = RoomTextMuted;
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t);
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        private Border BuildRoomsHeaderCard(List<RoomStatus> statuses)
        {
            var stack = new StackPanel();
            var title = new TextBlock();
            title.Text = "Rooms";
            title.FontSize = 13;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = RoomTextPrimary;
            title.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(title);
            stack.Children.Add(Muted("Every connected screen is a room. Switching turns that screen on as the only one and remembers the resolution, HDR and sound you use there."));

            var ask = Combo(0);
            ask.Items.Add(Item("Only for new rooms", AppSettings.RoomAskNew));
            ask.Items.Add(Item("Every switch", AppSettings.RoomAskAlways));
            ask.Items.Add(Item("Never", AppSettings.RoomAskNever));
            Select(ask, _settings.RoomAskToKeep);
            ask.SelectionChanged += (s, e) =>
            {
                var i = ask.SelectedItem as ComboBoxItem;
                if (i == null) return;
                _settings.RoomAskToKeep = (string)i.Tag;
                ScheduleSave();
            };
            var askRow = Row("Ask to keep", ask);
            askRow.Margin = new Thickness(0, 12, 0, 8);
            askRow.ToolTip = "\"Keep this display layout?\" goes back on its own after 15 s without an answer, so a TV that is off can't leave you without a screen.";
            stack.Children.Add(askRow);

            var home = Combo(0);
            foreach (var s in statuses) home.Items.Add(Item(s.Room.Name, s.Room.Key));
            var homeRoom = _rooms.HomeRoom;
            Select(home, homeRoom != null ? homeRoom.Key : null);
            home.SelectionChanged += (s, e) =>
            {
                var i = home.SelectedItem as ComboBoxItem;
                if (i == null) return;
                _settings.RoomHomeKey = (string)i.Tag;
                ScheduleSave();
            };
            var homeRow = Row("Home", home);
            homeRow.ToolTip = "The room the home hotkey switches to, without asking.";
            stack.Children.Add(homeRow);

            var homeKey = HotkeyBox(_settings.RoomHomeHotkey, keys =>
            {
                _settings.RoomHomeHotkey = keys;
                _rooms.RoomEdited(null);
            });
            stack.Children.Add(Row("Home hotkey", homeKey));
            return Card(stack, false);
        }

        private Border BuildRoomCard(RoomStatus s)
        {
            var room = s.Room;
            var stack = new StackPanel();

            // Name (editable) and status
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBox();
            name.Text = room.Name;
            name.FontSize = 14;
            name.FontWeight = FontWeights.Bold;
            name.Foreground = RoomTextPrimary;
            name.Background = Brushes.Transparent;
            name.BorderThickness = new Thickness(0, 0, 0, 1);
            name.BorderBrush = Brushes.Transparent;
            name.CaretBrush = RoomAccent;
            name.MaxLength = ConfigManager.MaxRoomNameLength;
            name.ToolTip = "Click to rename";
            name.GotKeyboardFocus += (o, e) => name.BorderBrush = RoomAccent;
            name.LostKeyboardFocus += (o, e) =>
            {
                name.BorderBrush = Brushes.Transparent;
                string text = name.Text.Trim();
                if (text.Length > 0 && text != room.Name)
                {
                    room.Name = text;
                    ScheduleSave();
                }
                FinishRoomsEdit();
            };
            name.KeyDown += (o, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            top.Children.Add(name);

            var badge = new Border();
            badge.CornerRadius = new CornerRadius(4);
            badge.Padding = new Thickness(6, 1, 6, 1);
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.Background = MakeBrush(s.Active ? "#4A1E2C" : "#2E2130");
            var badgeText = new TextBlock();
            badgeText.FontSize = 9;
            badgeText.FontWeight = FontWeights.Bold;
            badgeText.Foreground = s.Active ? RoomAccent : RoomTextMuted;
            badgeText.Text = s.Active ? "ON SCREEN" : s.Connected ? "CONNECTED" : "NOT CONNECTED";
            badge.Child = badgeText;
            Grid.SetColumn(badge, 1);
            top.Children.Add(badge);
            stack.Children.Add(top);

            var details = new List<string>();
            if (!string.IsNullOrEmpty(room.DisplayName) && room.DisplayName != room.Name) details.Add(room.DisplayName);
            if (!string.IsNullOrEmpty(s.Connection)) details.Add(s.Connection);
            details.Add(Summary(room));
            if (!string.IsNullOrEmpty(room.Hotkey)) details.Add(room.Hotkey);
            var sub = Muted(string.Join("  ·  ", details.ToArray()));
            sub.Margin = new Thickness(2, 2, 0, 10);
            stack.Children.Add(sub);

            // Switch button and settings toggle
            var actions = new Grid();
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var go = new Button();
            go.Style = (Style)FindResource("PillButton");
            go.Padding = new Thickness(12, 8, 12, 8);
            go.Content = s.Active ? "On screen now" : _rooms.Busy ? "Switching…" : "Switch here";
            go.IsEnabled = s.Connected && !s.Active && !_rooms.Busy && !room.Hidden;
            if (go.IsEnabled)
            {
                go.Background = MakeBrush("#4A1E2C");
                go.BorderBrush = RoomAccent;
            }
            go.Click += (o, e) => _rooms.SwitchTo(room, false);
            actions.Children.Add(go);

            bool expanded = _expandedRooms.Contains(room.Key);
            var more = new Button();
            more.Style = (Style)FindResource("PillButton");
            more.Padding = new Thickness(12, 8, 12, 8);
            more.Margin = new Thickness(8, 0, 0, 0);
            more.Content = expanded ? "Settings ▴" : "Settings ▾";
            more.Click += (o, e) =>
            {
                if (!_expandedRooms.Remove(room.Key)) _expandedRooms.Add(room.Key);
                RoomsChanged();
            };
            Grid.SetColumn(more, 1);
            actions.Children.Add(more);
            stack.Children.Add(actions);

            if (expanded)
            {
                var settings = new StackPanel();
                settings.Margin = new Thickness(0, 14, 0, 0);
                BuildRoomSettings(s, settings);
                stack.Children.Add(settings);
            }
            return Card(stack, s.Active);
        }

        private static string Summary(Room room)
        {
            string mode = room.Width > 0 ? room.Width + "×" + room.Height : "Best resolution";
            mode += room.RefreshHz > 0 ? " @ " + room.RefreshHz + " Hz" : "";
            if (room.Hdr == Room.HdrOn || room.Hdr == Room.HdrAuto) mode += room.Hdr == Room.HdrOn ? ", HDR" : ", HDR auto";
            return mode;
        }

        private void BuildRoomSettings(RoomStatus s, StackPanel host)
        {
            var room = s.Room;

            // Resolution: "Best available" or one the display offers
            var res = Combo(0);
            res.Items.Add(Item("Best available", ""));
            var sizes = new List<string>();
            foreach (var m in s.Modes)
            {
                string key = m.Width + "x" + m.Height;
                if (!sizes.Contains(key)) { sizes.Add(key); res.Items.Add(Item(m.Width + " × " + m.Height, key)); }
            }
            string current = room.Width > 0 ? room.Width + "x" + room.Height : "";
            if (current.Length > 0 && !sizes.Contains(current)) res.Items.Add(Item(room.Width + " × " + room.Height, current));
            Select(res, current);
            host.Children.Add(Row("Resolution", res));

            // Refresh rate for that resolution
            var hz = Combo(0);
            Action fillHz = () =>
            {
                hz.Items.Clear();
                hz.Items.Add(Item("Highest", 0));
                var sel = res.SelectedItem as ComboBoxItem;
                string size = sel != null ? (string)sel.Tag : "";
                var rates = new List<int>();
                foreach (var m in s.Modes)
                {
                    bool match = size.Length == 0 ? (s.Modes.Count > 0 && m.Width == s.Modes[0].Width && m.Height == s.Modes[0].Height)
                        : (m.Width + "x" + m.Height) == size;
                    if (match && !rates.Contains(m.Hz)) rates.Add(m.Hz);
                }
                rates.Sort((a, b) => b.CompareTo(a));
                foreach (int r in rates) hz.Items.Add(Item(r + " Hz", r));
                if (room.RefreshHz > 0 && !rates.Contains(room.RefreshHz)) hz.Items.Add(Item(room.RefreshHz + " Hz", room.RefreshHz));
                Select(hz, room.RefreshHz);
            };
            fillHz();
            res.SelectionChanged += (o, e) =>
            {
                var sel = res.SelectedItem as ComboBoxItem;
                if (sel == null) return;
                string size = (string)sel.Tag;
                if (size.Length == 0) { room.Width = 0; room.Height = 0; }
                else
                {
                    string[] wh = size.Split('x');
                    room.Width = int.Parse(wh[0]);
                    room.Height = int.Parse(wh[1]);
                }
                room.RefreshHz = 0;
                fillHz();
                _rooms.RoomEdited(room);
            };
            hz.SelectionChanged += (o, e) =>
            {
                var sel = hz.SelectedItem as ComboBoxItem;
                if (sel == null || (int)sel.Tag == room.RefreshHz) return;
                room.RefreshHz = (int)sel.Tag;
                _rooms.RoomEdited(room);
            };
            var hzRow = Row("Refresh rate", hz);
            hzRow.ToolTip = "Rates seen while this room was on screen, plus the ones the display advertises. Some TVs only offer their highest rate once they are on.";
            host.Children.Add(hzRow);

            // HDR
            var hdr = Combo(0);
            hdr.Items.Add(Item(s.HdrCapable ? "On when supported" : "Auto", Room.HdrAuto));
            hdr.Items.Add(Item("On", Room.HdrOn));
            hdr.Items.Add(Item("Off", Room.HdrOff));
            Select(hdr, room.Hdr);
            hdr.SelectionChanged += (o, e) =>
            {
                var sel = hdr.SelectedItem as ComboBoxItem;
                if (sel == null) return;
                room.Hdr = (string)sel.Tag;
                _rooms.RoomEdited(room);
            };
            host.Children.Add(Row("HDR", hdr));

            // Sound
            var sound = Combo(0);
            sound.Items.Add(Item("This screen's speakers", "auto"));
            sound.Items.Add(Item("Don't change", "keep"));
            var seen = new HashSet<string>();
            try
            {
                foreach (var o in AudioOutputs.List())
                    if (seen.Add(o.Id)) sound.Items.Add(Item(o.Name, "id:" + o.Id));
            }
            catch (Exception ex) { App.Log("Rooms: couldn't list sound outputs: " + ex.Message); }
            if (room.Audio == Room.AudioDevice && !string.IsNullOrEmpty(room.AudioId) && !seen.Contains(room.AudioId))
                sound.Items.Add(Item(room.AudioName ?? "Saved output", "id:" + room.AudioId));
            Select(sound, room.Audio == Room.AudioDevice ? "id:" + room.AudioId : room.Audio);
            sound.SelectionChanged += (o, e) =>
            {
                var sel = sound.SelectedItem as ComboBoxItem;
                if (sel == null) return;
                string tag = (string)sel.Tag;
                if (tag.StartsWith("id:"))
                {
                    room.Audio = Room.AudioDevice;
                    room.AudioId = tag.Substring(3);
                    room.AudioName = (string)sel.Content;
                }
                else room.Audio = tag;
                _rooms.RoomEdited(room);
            };
            var soundRow = Row("Sound", sound);
            soundRow.ToolTip = "\"This screen's speakers\" uses the TV's HDMI sound when it has one. A TV's own output only appears in the list while it is on screen.";
            host.Children.Add(soundRow);

            host.Children.Add(Row("Hotkey", HotkeyBox(room.Hotkey, keys =>
            {
                room.Hotkey = keys;
                _rooms.RoomEdited(room);
            })));

            var bigPicture = new CheckBox();
            bigPicture.Style = (Style)FindResource("ModernCheckBox");
            bigPicture.Content = "Open Steam Big Picture here";
            bigPicture.IsChecked = room.OpenBigPicture;
            bigPicture.Margin = new Thickness(0, 4, 0, 8);
            bigPicture.Click += (o, e) =>
            {
                room.OpenBigPicture = bigPicture.IsChecked == true;
                ScheduleSave();
            };
            host.Children.Add(bigPicture);

            var hide = new CheckBox();
            hide.Style = (Style)FindResource("ModernCheckBox");
            hide.Content = "Hide this room";
            hide.IsChecked = room.Hidden;
            hide.Click += (o, e) =>
            {
                room.Hidden = hide.IsChecked == true;
                _rooms.RoomEdited(room);
            };
            host.Children.Add(hide);

            var shortcut = Muted("Steam or desktop shortcut: Displicare.exe --room \"" + room.Name + "\"");
            shortcut.Margin = new Thickness(0, 10, 0, 0);
            host.Children.Add(shortcut);
        }

        /// <summary>A text box for a hotkey such as Ctrl+Alt+2 (empty = none), checked when it loses focus.</summary>
        private TextBox HotkeyBox(string value, Action<string> changed)
        {
            var box = new TextBox();
            box.Text = value ?? "";
            box.Height = 32;
            box.Padding = new Thickness(8, 6, 8, 6);
            box.FontSize = 12.5;
            box.Foreground = RoomTextPrimary;
            box.Background = RoomFieldBg;
            box.BorderBrush = RoomFieldBorder;
            box.CaretBrush = RoomAccent;
            box.ToolTip = "Ctrl, Alt or Win plus a key, e.g. Ctrl+Alt+2. Empty for none.";
            box.KeyDown += (o, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            box.LostKeyboardFocus += (o, e) =>
            {
                string text = box.Text.Trim();
                string normalized = text.Length == 0 ? "" : Hotkey.Normalize(text);
                if (normalized == null)
                {
                    box.Text = value ?? "";
                    _tray.ShowBalloon("Not a hotkey", "Use Ctrl, Alt or Win plus a key, e.g. Ctrl+Alt+2.", true);
                }
                else if (normalized != (value ?? ""))
                {
                    value = normalized;
                    box.Text = normalized;
                    changed(normalized);
                }
                FinishRoomsEdit();
            };
            return box;
        }

        private void FinishRoomsEdit()
        {
            if (!_roomsRebuildPending) return;
            _roomsRebuildPending = false;
            Dispatcher.BeginInvoke((Action)RoomsChanged, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
