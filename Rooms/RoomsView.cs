using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Displicare.Rooms
{
    /// <summary>
    /// Builds the room controls in Displicare's style, for both the tray window's Rooms tab (switch cards) and the
    /// Rooms window (room list, settings, general options). Styles come from the main window's resources.
    /// </summary>
    internal sealed class RoomsView
    {
        readonly RoomManager rooms;
        readonly Func<string, object> resource;
        readonly Action<string, string, bool> notify;

        public static readonly Brush CardBg = MakeBrush("#1E1520");
        public static readonly Brush CardBorder = MakeBrush("#3A2A3B");
        public static readonly Brush Accent = MakeBrush("#FF6B5A");
        public static readonly Brush AccentDark = MakeBrush("#4A1E2C");
        public static readonly Brush TextPrimary = MakeBrush("#FBF6F7");
        public static readonly Brush TextMuted = MakeBrush("#B3A3AE");
        public static readonly Brush FieldBg = MakeBrush("#2A1E2B");
        public static readonly Brush FieldBorder = MakeBrush("#433145");
        public static readonly Brush Selected = MakeBrush("#2E2130");

        public RoomsView(RoomManager rooms, Func<string, object> resource, Action<string, string, bool> notify)
        {
            this.rooms = rooms;
            this.resource = resource;
            this.notify = notify;
        }

        /// <summary>Set by the host: rebuild once the text box being edited loses focus.</summary>
        public Action EditFinished;

        public static Brush MakeBrush(string hex)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        // ---------------------------------------------------------------- small pieces

        public Border Card(UIElement child, bool highlight)
        {
            var card = new Border();
            card.Background = CardBg;
            card.CornerRadius = new CornerRadius(14);
            card.Padding = new Thickness(16, 14, 16, 14);
            card.BorderBrush = highlight ? Accent : CardBorder;
            card.BorderThickness = new Thickness(highlight ? 1.5 : 1);
            card.Margin = new Thickness(0, 0, 0, 10);
            card.Child = child;
            return card;
        }

        public static TextBlock Label(string text, double size, bool bold, Brush brush)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = size;
            t.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;
            t.Foreground = brush;
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        public static TextBlock SectionLabel(string text)
        {
            var t = Label(text, 10.5, true, TextMuted);
            t.FontWeight = FontWeights.Bold;
            t.Margin = new Thickness(2, 4, 0, 6);
            return t;
        }

        public ComboBox Combo()
        {
            var c = new ComboBox();
            c.Style = (Style)resource("DarkComboBox");
            c.Height = 32;
            return c;
        }

        public Button Pill(string text)
        {
            var b = new Button();
            b.Style = (Style)resource("PillButton");
            b.Padding = new Thickness(12, 8, 12, 8);
            b.Content = text;
            return b;
        }

        public CheckBox Toggle(string text, bool value)
        {
            var c = new CheckBox();
            c.Style = (Style)resource("ModernCheckBox");
            c.Content = text;
            c.IsChecked = value;
            return c;
        }

        static ComboBoxItem Item(string text, object tag)
        {
            var i = new ComboBoxItem();
            i.Content = text;
            i.Tag = tag;
            return i;
        }

        static void Select(ComboBox combo, object tag)
        {
            foreach (ComboBoxItem i in combo.Items)
                if (Equals(i.Tag, tag)) { combo.SelectedItem = i; return; }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        /// <summary>Label on the left, control on the right.</summary>
        public static Grid Row(string label, UIElement control, double labelWidth)
        {
            var g = new Grid();
            g.Margin = new Thickness(0, 0, 0, 8);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var t = Label(label, 12, false, TextMuted);
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t);
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        public static Border Badge(RoomStatus s)
        {
            var badge = new Border();
            badge.CornerRadius = new CornerRadius(4);
            badge.Padding = new Thickness(6, 1, 6, 1);
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.Background = s.Active ? AccentDark : Selected;
            var text = new TextBlock();
            text.FontSize = 9;
            text.FontWeight = FontWeights.Bold;
            text.Foreground = s.Active ? Accent : TextMuted;
            text.Text = s.Active ? "ON SCREEN" : s.Connected ? "CONNECTED" : "NOT CONNECTED";
            badge.Child = text;
            return badge;
        }

        public static string Icon(RoomStatus s)
        {
            string name = ((s.Room.DisplayName ?? "") + " " + (s.Room.Name ?? "")).ToUpperInvariant();
            bool tv = name.Contains("TV") || (s.Connection ?? "").StartsWith("HDMI");
            return tv ? "📺" : "🖥️";
        }

        /// <summary>"3840×2160 @ 120 Hz, HDR auto" or "Native resolution, highest refresh".</summary>
        public static string Summary(Room room)
        {
            string mode = room.Width > 0 ? room.Width + "×" + room.Height : "Native resolution";
            mode += room.RefreshHz > 0 ? " @ " + room.RefreshHz + " Hz" : ", highest refresh";
            if (room.Hdr == Room.HdrOn) mode += ", HDR";
            else if (room.Hdr == Room.HdrAuto) mode += ", HDR auto";
            return mode;
        }

        /// <param name="controller">Controller switching is on: show the room's View + D-pad combo.</param>
        public static string Details(RoomStatus s, bool controller)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(s.Room.DisplayName) && s.Room.DisplayName != s.Room.Name) parts.Add(s.Room.DisplayName);
            if (!string.IsNullOrEmpty(s.Connection)) parts.Add(s.Connection);
            parts.Add(Summary(s.Room));
            if (!string.IsNullOrEmpty(s.Room.Hotkey)) parts.Add(s.Room.Hotkey);
            string pad = RoomManager.PadLabel(s.Room.PadDirection);
            if (pad != null && controller) parts.Add(pad);
            return string.Join("  ·  ", parts.ToArray());
        }

        public Button SwitchButton(RoomStatus s)
        {
            var go = Pill(s.Active ? "On screen now" : rooms.Busy ? "Switching…" : "Switch here");
            go.IsEnabled = s.Connected && !s.Active && !rooms.Busy && !s.Room.Hidden;
            if (go.IsEnabled)
            {
                go.Background = AccentDark;
                go.BorderBrush = Accent;
            }
            var room = s.Room;
            go.Click += (o, e) => rooms.SwitchTo(room, false);
            return go;
        }

        // ---------------------------------------------------------------- tray window: switch cards

        /// <summary>The Rooms tab card: name, status, details and a switch button. Settings live in the Rooms window.</summary>
        public Border SwitchCard(RoomStatus s)
        {
            var stack = new StackPanel();
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Label(Icon(s) + "  " + s.Room.Name, 14, true, TextPrimary);
            name.FontWeight = FontWeights.Bold;
            top.Children.Add(name);
            var badge = Badge(s);
            Grid.SetColumn(badge, 1);
            top.Children.Add(badge);
            stack.Children.Add(top);

            var sub = Label(Details(s, rooms.Settings.RoomController), 11, false, TextMuted);
            sub.Margin = new Thickness(2, 2, 0, 10);
            stack.Children.Add(sub);

            var go = SwitchButton(s);
            go.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(go);
            return Card(stack, s.Active);
        }

        // ---------------------------------------------------------------- Rooms window: settings

        /// <summary>The selected room's settings: name, mode, HDR, sound, hotkey, Big Picture, hide, shortcut.</summary>
        public StackPanel RoomSettings(RoomStatus s)
        {
            var room = s.Room;
            var host = new StackPanel();
            const double labelWidth = 130;

            var name = TextField(room.Name, ConfigManager.MaxRoomNameLength, "Name shown in Displicare, the tray and shortcuts");
            name.LostKeyboardFocus += (o, e) =>
            {
                string text = name.Text.Trim();
                if (text.Length > 0 && text != room.Name)
                {
                    room.Name = text;
                    rooms.RoomEdited(room, false);
                }
                else name.Text = room.Name;
                Finished();
            };
            host.Children.Add(Row("Name", name, labelWidth));

            // Resolution: native, or one the display offers
            var res = Combo();
            string nativeLabel = s.Native != null ? "Native (" + s.Native.Value.Width + " × " + s.Native.Value.Height + ")" : "Native";
            res.Items.Add(Item(nativeLabel, ""));
            var sizes = new List<string>();
            foreach (var m in s.Modes)
            {
                string key = m.Width + "x" + m.Height;
                if (!sizes.Contains(key)) { sizes.Add(key); res.Items.Add(Item(m.Width + " × " + m.Height, key)); }
            }
            string current = room.Width > 0 ? room.Width + "x" + room.Height : "";
            if (current.Length > 0 && !sizes.Contains(current)) res.Items.Add(Item(room.Width + " × " + room.Height, current));
            Select(res, current);
            host.Children.Add(Row("Resolution", res, labelWidth));

            var hz = Combo();
            Action fillHz = () =>
            {
                hz.Items.Clear();
                var sel = res.SelectedItem as ComboBoxItem;
                string size = sel != null ? (string)sel.Tag : "";
                if (size.Length == 0 && s.Native != null) size = s.Native.Value.Width + "x" + s.Native.Value.Height;
                var rates = new List<int>();
                foreach (var m in s.Modes)
                    if ((m.Width + "x" + m.Height) == size && !rates.Contains(m.Hz)) rates.Add(m.Hz);
                rates.Sort((a, b) => b.CompareTo(a));
                hz.Items.Add(Item(rates.Count > 0 ? "Highest (" + rates[0] + " Hz)" : "Highest", 0));
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
                rooms.RoomEdited(room, true);
            };
            hz.SelectionChanged += (o, e) =>
            {
                var sel = hz.SelectedItem as ComboBoxItem;
                if (sel == null || (int)sel.Tag == room.RefreshHz) return;
                room.RefreshHz = (int)sel.Tag;
                rooms.RoomEdited(room, true);
            };
            host.Children.Add(Row("Refresh rate", hz, labelWidth));
            if (s.ModesFromEdid)
            {
                var note = Label("These come from the screen's own specs until it has been on screen once; after that only what Windows really offers on this connection is listed.", 10.5, false, TextMuted);
                note.Margin = new Thickness(labelWidth, -2, 0, 10);
                host.Children.Add(note);
            }

            var hdr = Combo();
            hdr.Items.Add(Item(s.HdrCapable ? "On when supported" : "Auto", Room.HdrAuto));
            hdr.Items.Add(Item("On", Room.HdrOn));
            hdr.Items.Add(Item("Off", Room.HdrOff));
            Select(hdr, room.Hdr);
            hdr.SelectionChanged += (o, e) =>
            {
                var sel = hdr.SelectedItem as ComboBoxItem;
                if (sel == null) return;
                room.Hdr = (string)sel.Tag;
                rooms.RoomEdited(room, false);
            };
            host.Children.Add(Row("HDR", hdr, labelWidth));

            var sound = Combo();
            sound.Items.Add(Item("This screen's speakers", Room.AudioAuto));
            sound.Items.Add(Item("Don't change", Room.AudioKeep));
            var seen = new HashSet<string>();
            try
            {
                foreach (var o in AudioOutputs.List())
                    if (seen.Add(o.Id)) sound.Items.Add(Item(o.Name, "id:" + o.Id));
            }
            catch (Exception ex) { RoomLog.Write("Couldn't list sound outputs: " + ex.Message); }
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
                rooms.RoomEdited(room, false);
            };
            var soundRow = Row("Sound", sound, labelWidth);
            soundRow.ToolTip = "\"This screen's speakers\" uses the TV's HDMI sound, as soon as Windows makes it available. Changing the output in Windows while a room is on screen also updates this.";
            host.Children.Add(soundRow);

            host.Children.Add(Row("Hotkey", HotkeyBox(room.Hotkey, keys =>
            {
                room.Hotkey = keys;
                rooms.RoomEdited(room, false);
            }), labelWidth));

            var pad = Combo();
            pad.Items.Add(Item("None", ""));
            foreach (var d in Room.PadDirections) pad.Items.Add(Item(RoomManager.PadLabel(d), d));
            Select(pad, room.PadDirection ?? "");
            pad.SelectionChanged += (o, e) =>
            {
                var sel = pad.SelectedItem as ComboBoxItem;
                if (sel == null || (string)sel.Tag == (room.PadDirection ?? "")) return;
                string d = (string)sel.Tag;
                // One room per direction: the room that had it gives it up.
                if (d.Length > 0)
                    foreach (var other in rooms.Settings.Rooms)
                        if (other != room && other.PadDirection == d) other.PadDirection = "";
                room.PadDirection = d;
                rooms.RoomEdited(room, false);
            };
            var padRow = Row("Controller", pad, labelWidth);
            padRow.ToolTip = "Hold View and press this D-pad direction to switch here, even when you can't see a screen: on the couch with the TV still blank, View + its direction brings the picture to it.";
            host.Children.Add(padRow);

            var bigPicture = Toggle("Open Steam Big Picture when switching here", room.OpenBigPicture);
            bigPicture.Margin = new Thickness(labelWidth, 4, 0, 10);
            bigPicture.Click += (o, e) =>
            {
                room.OpenBigPicture = bigPicture.IsChecked == true;
                rooms.RoomEdited(room, false);
            };
            host.Children.Add(bigPicture);

            var hide = Toggle("Hide this room (not in the tab, tray or hotkeys)", room.Hidden);
            hide.Margin = new Thickness(labelWidth, 0, 0, 16);
            hide.Click += (o, e) =>
            {
                room.Hidden = hide.IsChecked == true;
                rooms.RoomEdited(room, false);
            };
            host.Children.Add(hide);

            // Shortcut for Steam / Stream Deck
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string command = "\"" + exe + "\" --room \"" + room.Name + "\"";
            var shortcut = new Grid();
            shortcut.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shortcut.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hint = Label("Switch here from a Steam or desktop shortcut:\n--room \"" + room.Name + "\"", 11, false, TextMuted);
            hint.VerticalAlignment = VerticalAlignment.Center;
            shortcut.Children.Add(hint);
            var copy = Pill("Copy command");
            copy.Click += (o, e) =>
            {
                try
                {
                    Clipboard.SetText(command);
                    copy.Content = "Copied";
                }
                catch (Exception) { }
            };
            Grid.SetColumn(copy, 1);
            shortcut.Children.Add(copy);
            var box = new Border();
            box.Background = FieldBg;
            box.CornerRadius = new CornerRadius(10);
            box.Padding = new Thickness(12, 10, 12, 10);
            box.Child = shortcut;
            host.Children.Add(box);
            return host;
        }

        /// <summary>Options for all rooms: when to ask, the home room and its hotkey.</summary>
        public StackPanel GeneralSettings(List<RoomStatus> statuses, double labelWidth)
        {
            var settings = rooms.Settings;
            var stack = new StackPanel();

            var ask = Combo();
            ask.Items.Add(Item("Only for new rooms", AppSettings.RoomAskNew));
            ask.Items.Add(Item("Every switch", AppSettings.RoomAskAlways));
            ask.Items.Add(Item("Never", AppSettings.RoomAskNever));
            Select(ask, settings.RoomAskToKeep);
            ask.SelectionChanged += (s, e) =>
            {
                var i = ask.SelectedItem as ComboBoxItem;
                if (i == null) return;
                settings.RoomAskToKeep = (string)i.Tag;
                rooms.RoomEdited(null, false);
            };
            var askRow = Row("Ask to keep", ask, labelWidth);
            askRow.ToolTip = "\"Keep this display layout?\" goes back on its own after 15 s without an answer, so a TV that is off can't leave you without a screen. A room also asks again after its resolution or refresh rate changes.";
            stack.Children.Add(askRow);

            var home = Combo();
            foreach (var s in statuses) home.Items.Add(Item(s.Room.Name, s.Room.Key));
            var homeRoom = rooms.HomeRoom;
            Select(home, homeRoom != null ? homeRoom.Key : null);
            home.SelectionChanged += (s, e) =>
            {
                var i = home.SelectedItem as ComboBoxItem;
                if (i == null) return;
                settings.RoomHomeKey = (string)i.Tag;
                rooms.RoomEdited(null, false);
            };
            var homeRow = Row("Home room", home, labelWidth);
            homeRow.ToolTip = "Where the home hotkey takes you, without asking: the way back from any room.";
            stack.Children.Add(homeRow);

            stack.Children.Add(Row("Home hotkey", HotkeyBox(settings.RoomHomeHotkey, keys =>
            {
                settings.RoomHomeHotkey = keys;
                rooms.RoomEdited(null, false);
            }), labelWidth));

            stack.Children.Add(OptionToggle("Switch with a controller: hold View + Menu", settings.RoomController,
                "Opens a room picker on screen: up and down to choose, A to switch, B to close. A and B also answer \"Keep this display layout?\". View + a D-pad direction goes straight to the room on that direction (set per room), even when you can't see a screen. Works with Xbox controllers and anything Steam shows as one. Displicare only reads the controller the way games do: it doesn't hook, inject or send input, so it is safe with anti-cheat.",
                v => settings.RoomController = v));
            stack.Children.Add(OptionToggle("Turn the screens off while streaming", settings.RoomStreamScreensOff,
                "When Steam Link (or Apollo, Sunshine) streams to a virtual display, the physical screens are switched off, so an OLED isn't left showing a still desktop. They come back as they were when the stream ends, with every window minimized so nothing sits still on screen.",
                v => settings.RoomStreamScreensOff = v));
            stack.Children.Add(OptionToggle("Show the room on screen after switching", settings.RoomShowOverlay,
                "A short note on the new screen with the room, resolution, refresh rate, HDR and sound.",
                v => settings.RoomShowOverlay = v));
            return stack;
        }

        CheckBox OptionToggle(string text, bool value, string tooltip, Action<bool> set)
        {
            var toggle = Toggle(text, value);
            toggle.Margin = new Thickness(0, 6, 0, 2);
            toggle.ToolTip = tooltip;
            toggle.Click += (o, e) =>
            {
                set(toggle.IsChecked == true);
                rooms.RoomEdited(null, false);
            };
            return toggle;
        }

        public TextBox TextField(string value, int maxLength, string tooltip)
        {
            var box = new TextBox();
            box.Text = value ?? "";
            box.Height = 32;
            box.Padding = new Thickness(8, 6, 8, 6);
            box.FontSize = 12.5;
            box.Foreground = TextPrimary;
            box.Background = FieldBg;
            box.BorderBrush = FieldBorder;
            box.CaretBrush = Accent;
            if (maxLength > 0) box.MaxLength = maxLength;
            box.ToolTip = tooltip;
            box.KeyDown += (o, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            return box;
        }

        /// <summary>A hotkey such as Ctrl+Alt+2 (empty = none), checked when the box loses focus.</summary>
        public TextBox HotkeyBox(string value, Action<string> changed)
        {
            var box = TextField(value, 40, "Ctrl, Alt or Win plus a key, e.g. Ctrl+Alt+2. Empty for none.");
            box.LostKeyboardFocus += (o, e) =>
            {
                string text = box.Text.Trim();
                string normalized = text.Length == 0 ? "" : Hotkey.Normalize(text);
                if (normalized == null)
                {
                    box.Text = value ?? "";
                    notify("Not a hotkey", "Use Ctrl, Alt or Win plus a key, e.g. Ctrl+Alt+2.", true);
                }
                else if (normalized != (value ?? ""))
                {
                    value = normalized;
                    box.Text = normalized;
                    changed(normalized);
                }
                Finished();
            };
            return box;
        }

        void Finished()
        {
            if (EditFinished != null) EditFinished();
        }

        /// <summary>True while a text box inside <paramref name="host"/> is being edited (rebuilding would lose the text).</summary>
        public static bool Editing(DependencyObject host)
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (!(focused is TextBox)) return false;
            for (var d = focused; d != null; d = VisualTreeHelper.GetParent(d)) if (d == host) return true;
            return false;
        }
    }
}
