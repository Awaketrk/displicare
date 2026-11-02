using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Displicare.Rooms
{
    /// <summary>
    /// Builds the setup controls in Displicare's style, for both the tray window's Setups tab (switch cards) and the
    /// Setups window (setup list, editor, general options). Styles come from the main window's resources.
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

        /// <summary>Shows a tray balloon (title, text, warning).</summary>
        public void Notify(string title, string text, bool warning)
        {
            if (notify != null) notify(title, text, warning);
        }

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

        /// <summary>
        /// A small drawing of where the setup's screens go, the main one in the accent colour, fitted into
        /// <paramref name="width"/> x <paramref name="height"/>.
        /// </summary>
        public static Canvas Thumbnail(IList<Box> boxes, double width, double height)
        {
            var canvas = new Canvas();
            canvas.Width = width;
            canvas.Height = height;
            if (boxes == null || boxes.Count == 0) return canvas;
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            foreach (var b in boxes)
            {
                left = Math.Min(left, b.X);
                top = Math.Min(top, b.Y);
                right = Math.Max(right, b.Right);
                bottom = Math.Max(bottom, b.Bottom);
            }
            double scale = Math.Min(width / Math.Max(1, right - left), height / Math.Max(1, bottom - top));
            double offsetX = (width - (right - left) * scale) / 2 - left * scale;
            double offsetY = (height - (bottom - top) * scale) / 2 - top * scale;
            for (int i = 0; i < boxes.Count; i++)
            {
                var r = new Border();
                r.CornerRadius = new CornerRadius(2);
                r.Background = i == 0 ? AccentDark : Selected;
                r.BorderBrush = i == 0 ? Accent : TextMuted;
                r.BorderThickness = new Thickness(1);
                r.Width = Math.Max(3, boxes[i].W * scale - 1.5);
                r.Height = Math.Max(3, boxes[i].H * scale - 1.5);
                Canvas.SetLeft(r, offsetX + boxes[i].X * scale + 0.75);
                Canvas.SetTop(r, offsetY + boxes[i].Y * scale + 0.75);
                canvas.Children.Add(r);
            }
            return canvas;
        }

        /// <summary>"3840×2160 @ 120 Hz, HDR auto" or "Native resolution, highest refresh".</summary>
        public static string Summary(SetupScreen e)
        {
            if (e == null) return "Screen not known";
            string mode = e.Width > 0 ? e.Width + "×" + e.Height : "Native resolution";
            mode += e.RefreshHz > 0 ? " @ " + e.RefreshHz + " Hz" : ", highest refresh";
            if (e.Hdr == Room.HdrOn) mode += ", HDR";
            else if (e.Hdr == Room.HdrAuto) mode += ", HDR auto";
            return mode;
        }

        /// <summary>The main screen's mode, or for several screens "AORUS FO32U2 + Dell".</summary>
        public static string Summary(RoomStatus s)
        {
            if (s.Setup.Screens.Count < 2) return Summary(s.Setup.Entry(s.Setup.MainScreen));
            return RoomManager.ScreenName(s.Screen) + " + " + s.AlsoOnNames;
        }

        /// <param name="controller">Controller switching is on: show the room's View + D-pad combo.</param>
        public static string Details(RoomStatus s, bool controller)
        {
            var parts = new List<string>();
            string screenName = s.Screen != null ? s.Screen.DisplayName : null;
            if (s.Setup.Screens.Count < 2 && !string.IsNullOrEmpty(screenName) && screenName != s.Setup.Name) parts.Add(screenName);
            if (!string.IsNullOrEmpty(s.Connection)) parts.Add(s.Connection);
            parts.Add(Summary(s));
            if (!string.IsNullOrEmpty(s.MissingNames)) parts.Add(s.MissingNames + " not connected");
            if (!string.IsNullOrEmpty(s.Setup.Hotkey)) parts.Add(s.Setup.Hotkey);
            string pad = RoomManager.PadLabel(s.Setup.PadDirection);
            if (pad != null && controller) parts.Add(pad);
            return string.Join("  ·  ", parts.ToArray());
        }

        public Button SwitchButton(RoomStatus s)
        {
            var go = Pill(s.Active ? "On screen now" : rooms.Busy ? "Switching…" : "Switch here");
            go.IsEnabled = s.Connected && !s.Active && !rooms.Busy && !s.Setup.Hidden;
            if (go.IsEnabled)
            {
                go.Background = AccentDark;
                go.BorderBrush = Accent;
            }
            var setup = s.Setup;
            go.Click += (o, e) => rooms.SwitchTo(setup, false);
            return go;
        }

        // ---------------------------------------------------------------- tray window: switch cards

        /// <summary>The Setups tab card: drawing, name, status, details and a switch button. Editing is in the Setups window.</summary>
        public Border SwitchCard(RoomStatus s)
        {
            var stack = new StackPanel();
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var thumb = Thumbnail(s.Boxes, 54, 30);
            thumb.Margin = new Thickness(0, 0, 10, 0);
            thumb.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(thumb);
            var name = Label(s.Setup.Name, 14, true, TextPrimary);
            name.FontWeight = FontWeights.Bold;
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            top.Children.Add(name);
            var badge = Badge(s);
            Grid.SetColumn(badge, 2);
            top.Children.Add(badge);
            stack.Children.Add(top);

            var sub = Label(Details(s, rooms.Settings.RoomController), 11, false, TextMuted);
            sub.Margin = new Thickness(2, 6, 0, 10);
            stack.Children.Add(sub);

            var go = SwitchButton(s);
            go.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(go);
            return Card(stack, s.Active);
        }

        // ---------------------------------------------------------------- Setups window: the editor

        /// <summary>
        /// The selected setup's editor: its screens drawn as on the desk (drag to move, click to pick one), the picked
        /// screen's resolution, refresh rate and HDR, then the setup's own options. Every change is saved right away;
        /// switching applies it.
        /// </summary>
        /// <param name="selectedScreen">The screen whose settings show, or null for the main one.</param>
        /// <param name="selectScreen">Called with a screen key when another screen is clicked.</param>
        public StackPanel SetupEditor(RoomStatus s, string selectedScreen, Action<string> selectScreen)
        {
            var setup = s.Setup;
            var host = new StackPanel();
            const double labelWidth = 130;
            var infos = rooms.ScreenInfos();
            if (selectedScreen == null || setup.Entry(selectedScreen) == null) selectedScreen = setup.MainScreen;

            var name = TextField(setup.Name, ConfigManager.MaxRoomNameLength, "Name shown in Displicare, the tray and shortcuts");
            name.LostKeyboardFocus += (o, e) =>
            {
                string text = name.Text.Trim();
                if (text.Length > 0 && text != setup.Name)
                {
                    setup.Name = text;
                    rooms.SetupEdited(setup, false);
                }
                else name.Text = setup.Name;
                Finished();
            };
            host.Children.Add(Row("Name", name, labelWidth));

            var screensLabel = SectionLabel("SCREENS");
            screensLabel.Margin = new Thickness(2, 10, 0, 2);
            host.Children.Add(screensLabel);
            var hint = Label("Drag a screen to move it; click one to change it. Changes are saved as you go, and apply the next time you switch here.", 11, false, TextMuted);
            hint.Margin = new Thickness(2, 0, 0, 8);
            host.Children.Add(hint);
            host.Children.Add(Desk(s, infos, selectedScreen, selectScreen));
            host.Children.Add(AddScreens(setup, infos));

            var selected = infos.Find(i => i.Screen != null && string.Equals(i.Screen.Key, selectedScreen, StringComparison.OrdinalIgnoreCase));
            if (selected != null) host.Children.Add(Card(ScreenPanel(setup, selected, labelWidth), false));

            var optionsLabel = SectionLabel("THIS SETUP");
            optionsLabel.Margin = new Thickness(2, 10, 0, 8);
            host.Children.Add(optionsLabel);
            SetupOptions(s, host, labelWidth);
            return host;
        }

        /// <summary>
        /// The setup's screens drawn to scale where they go. A screen dragged and let go snaps to the nearest edge that
        /// fits, as in Windows Settings > Display.
        /// </summary>
        Border Desk(RoomStatus s, List<ScreenInfo> infos, string selectedScreen, Action<string> selectScreen)
        {
            var setup = s.Setup;
            var desk = new Border();
            desk.Height = 220;
            desk.Background = MakeBrush("#110C11");
            desk.BorderBrush = CardBorder;
            desk.BorderThickness = new Thickness(1);
            desk.CornerRadius = new CornerRadius(12);
            desk.ClipToBounds = true;
            var canvas = new Canvas();
            desk.Child = canvas;

            var boxes = s.Boxes;
            Action draw = () =>
            {
                canvas.Children.Clear();
                double width = desk.ActualWidth - 2, height = desk.ActualHeight - 2;
                if (width <= 0 || height <= 0 || boxes.Count == 0) return;
                int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
                foreach (var b in boxes)
                {
                    left = Math.Min(left, b.X);
                    top = Math.Min(top, b.Y);
                    right = Math.Max(right, b.Right);
                    bottom = Math.Max(bottom, b.Bottom);
                }
                const double pad = 24;
                // Room to drag a screen past the others: never fill the whole desk.
                double scale = Math.Min((width - 2 * pad) / Math.Max(1, right - left) * 0.8, (height - 2 * pad) / Math.Max(1, bottom - top) * 0.8);
                double offsetX = (width - (right - left) * scale) / 2 - left * scale;
                double offsetY = (height - (bottom - top) * scale) / 2 - top * scale;

                for (int i = 0; i < boxes.Count; i++)
                {
                    var b = boxes[i];
                    var info = infos.Find(x => x.Screen != null && string.Equals(x.Screen.Key, b.Key, StringComparison.OrdinalIgnoreCase));
                    bool main = i == 0;
                    bool picked = string.Equals(b.Key, selectedScreen, StringComparison.OrdinalIgnoreCase);
                    bool connected = info != null && info.Connected;
                    var tile = ScreenTile(info, b, main, picked, connected);
                    tile.Width = Math.Max(24, b.W * scale - 4);
                    tile.Height = Math.Max(18, b.H * scale - 4);
                    double x0 = offsetX + b.X * scale + 2, y0 = offsetY + b.Y * scale + 2;
                    Canvas.SetLeft(tile, x0);
                    Canvas.SetTop(tile, y0);
                    canvas.Children.Add(tile);

                    string key = b.Key;
                    int bx = b.X, by = b.Y;
                    Point start = new Point();
                    bool down = false, moved = false;
                    tile.MouseLeftButtonDown += (o, e) =>
                    {
                        Keyboard.ClearFocus(); // saves a name being typed, so the window can redraw
                        start = e.GetPosition(canvas);
                        down = true;
                        moved = false;
                        tile.CaptureMouse();
                        e.Handled = true;
                    };
                    tile.MouseMove += (o, e) =>
                    {
                        if (!down || boxes.Count < 2) return;
                        var p = e.GetPosition(canvas);
                        if (!moved && Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) < 5) return;
                        moved = true;
                        tile.Cursor = Cursors.SizeAll;
                        Panel.SetZIndex(tile, 10);
                        tile.Opacity = 0.85;
                        Canvas.SetLeft(tile, x0 + p.X - start.X);
                        Canvas.SetTop(tile, y0 + p.Y - start.Y);
                    };
                    tile.MouseLeftButtonUp += (o, e) =>
                    {
                        if (!down) return;
                        down = false;
                        tile.ReleaseMouseCapture();
                        e.Handled = true;
                        if (!moved)
                        {
                            selectScreen(key);
                            return;
                        }
                        var p = e.GetPosition(canvas);
                        int x = bx + (int)Math.Round((p.X - start.X) / scale);
                        int y = by + (int)Math.Round((p.Y - start.Y) / scale);
                        rooms.MoveScreen(setup, key, x, y); // rebuilds the window, snapped
                        if (Canvas.GetLeft(tile) != x0 && canvas.Children.Contains(tile))
                        {
                            // It didn't move after all (snapped back): put it back.
                            Canvas.SetLeft(tile, x0);
                            Canvas.SetTop(tile, y0);
                            tile.Opacity = 1;
                            Panel.SetZIndex(tile, 0);
                        }
                    };
                    tile.LostMouseCapture += (o, e) => down = false;
                }
            };
            desk.SizeChanged += (o, e) => draw();
            return desk;
        }

        /// <summary>One screen on the desk: name, size and refresh rate, MAIN on the main one.</summary>
        static Border ScreenTile(ScreenInfo info, Box b, bool main, bool picked, bool connected)
        {
            var tile = new Border();
            tile.CornerRadius = new CornerRadius(5);
            tile.Background = main ? AccentDark : Selected;
            tile.BorderBrush = picked ? TextPrimary : main ? Accent : FieldBorder;
            tile.BorderThickness = new Thickness(picked ? 2 : 1.2);
            tile.Cursor = Cursors.Hand;
            if (!connected) tile.Opacity = 0.55;

            var text = new StackPanel();
            text.VerticalAlignment = VerticalAlignment.Center;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.Margin = new Thickness(4);
            var name = Label(info != null ? info.Name : b.Key, 11.5, true, TextPrimary);
            name.TextAlignment = TextAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(name);
            var size = Label(b.W + " × " + b.H, 10.5, false, TextMuted);
            size.TextAlignment = TextAlignment.Center;
            text.Children.Add(size);
            string tag = main ? "MAIN" : null;
            if (!connected) tag = (tag != null ? tag + " · " : "") + "NOT CONNECTED";
            if (tag != null)
            {
                var t = Label(tag, 9, true, main ? Accent : TextMuted);
                t.FontWeight = FontWeights.Bold;
                t.TextAlignment = TextAlignment.Center;
                t.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(t);
            }
            var view = new Viewbox();
            view.Stretch = Stretch.Uniform;
            view.StretchDirection = StretchDirection.DownOnly;
            view.Child = text;
            tile.Child = view;
            return tile;
        }

        /// <summary>The known screens that aren't in the setup, each a button that adds it; and taking what's on now.</summary>
        UIElement AddScreens(Setup setup, List<ScreenInfo> infos)
        {
            var wrap = new WrapPanel();
            wrap.Margin = new Thickness(0, 8, 0, 10);
            foreach (var info in infos)
            {
                if (info.Screen == null || setup.Entry(info.Screen.Key) != null) continue;
                var add = Pill("+  " + info.Name + (info.Connected ? "" : " (not connected)"));
                add.Margin = new Thickness(0, 0, 6, 6);
                add.ToolTip = "Turn this screen on too in this setup. It goes to the right; drag it where you want it.";
                add.IsEnabled = setup.Screens.Count <= ConfigManager.MaxAlsoOn;
                string key = info.Screen.Key;
                add.Click += (o, e) => rooms.AddScreen(setup, key);
                wrap.Children.Add(add);
            }
            var now = Pill("Start from what's on now");
            now.Margin = new Thickness(0, 0, 6, 6);
            now.IsEnabled = !rooms.Busy;
            now.ToolTip = "Makes this setup exactly the screens on now: which ones, where, resolution, refresh rate and HDR, as in Windows Settings > Display.";
            now.Click += (o, e) =>
            {
                if (!rooms.TakeCurrent(setup)) notify("Couldn't read the screens", "No screen is on, or a stream or a switch is running.", true);
            };
            wrap.Children.Add(now);
            return wrap;
        }

        /// <summary>The picked screen in this setup: resolution, refresh rate, HDR, main screen, leave out.</summary>
        StackPanel ScreenPanel(Setup setup, ScreenInfo info, double labelWidth)
        {
            var host = new StackPanel();
            var entry = setup.Entry(info.Screen.Key);
            string key = info.Screen.Key;
            bool main = string.Equals(setup.MainScreen, key, StringComparison.OrdinalIgnoreCase);

            var top = new Grid();
            top.Margin = new Thickness(0, 0, 0, 10);
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(Label(info.Name + (main ? "  ·  main screen" : ""), 13, true, TextPrimary));
            titles.Children.Add(Label(info.Connected ? info.Connection : "Not connected now; it's left out when you switch until it is.", 11, false, TextMuted));
            top.Children.Add(titles);
            var actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.VerticalAlignment = VerticalAlignment.Top;
            if (!main)
            {
                var makeMain = Pill("Make main");
                makeMain.ToolTip = "The main screen gets the taskbar and new windows, and its speakers are the setup's sound.";
                makeMain.Margin = new Thickness(6, 0, 0, 0);
                makeMain.Click += (o, e) => rooms.MakeMain(setup, key);
                actions.Children.Add(makeMain);
            }
            if (setup.Screens.Count > 1)
            {
                var leave = Pill("Leave out");
                leave.ToolTip = "Stays off in this setup. The other screens close up.";
                leave.Margin = new Thickness(6, 0, 0, 0);
                leave.Click += (o, e) => rooms.RemoveScreen(setup, key);
                actions.Children.Add(leave);
            }
            Grid.SetColumn(actions, 1);
            top.Children.Add(actions);
            host.Children.Add(top);

            // Resolution: native, or one the display offers
            var res = Combo();
            string nativeLabel = info.Native != null ? "Native (" + info.Native.Value.Width + " × " + info.Native.Value.Height + ")" : "Native";
            res.Items.Add(Item(nativeLabel, ""));
            var sizes = new List<string>();
            foreach (var m in info.Modes)
            {
                string size = m.Width + "x" + m.Height;
                if (!sizes.Contains(size)) { sizes.Add(size); res.Items.Add(Item(m.Width + " × " + m.Height, size)); }
            }
            string current = entry.Width > 0 ? entry.Width + "x" + entry.Height : "";
            if (current.Length > 0 && !sizes.Contains(current)) res.Items.Add(Item(entry.Width + " × " + entry.Height, current));
            Select(res, current);
            host.Children.Add(Row("Resolution", res, labelWidth));

            var hz = Combo();
            string shown = current.Length > 0 ? current : info.Native != null ? info.Native.Value.Width + "x" + info.Native.Value.Height : "";
            var rates = new List<int>();
            foreach (var m in info.Modes)
                if ((m.Width + "x" + m.Height) == shown && !rates.Contains(m.Hz)) rates.Add(m.Hz);
            rates.Sort((a, b) => b.CompareTo(a));
            hz.Items.Add(Item(rates.Count > 0 ? "Highest (" + rates[0] + " Hz)" : "Highest", 0));
            foreach (int r in rates) hz.Items.Add(Item(r + " Hz", r));
            if (entry.RefreshHz > 0 && !rates.Contains(entry.RefreshHz)) hz.Items.Add(Item(entry.RefreshHz + " Hz", entry.RefreshHz));
            Select(hz, entry.RefreshHz);
            res.SelectionChanged += (o, e) =>
            {
                var sel = res.SelectedItem as ComboBoxItem;
                if (sel == null) return;
                string size = (string)sel.Tag;
                int w = 0, h = 0;
                if (size.Length > 0)
                {
                    string[] wh = size.Split('x');
                    w = int.Parse(wh[0]);
                    h = int.Parse(wh[1]);
                }
                rooms.SetMode(setup, key, w, h, 0);
            };
            hz.SelectionChanged += (o, e) =>
            {
                var sel = hz.SelectedItem as ComboBoxItem;
                if (sel == null) return;
                rooms.SetMode(setup, key, entry.Width, entry.Height, (int)sel.Tag);
            };
            host.Children.Add(Row("Refresh rate", hz, labelWidth));
            if (info.ModesFromEdid)
            {
                var note = Label("These come from the screen's own specs until it has been on once; after that only what Windows really offers on this connection is listed.", 10.5, false, TextMuted);
                note.Margin = new Thickness(labelWidth, -2, 0, 10);
                host.Children.Add(note);
            }

            var hdr = Combo();
            hdr.Items.Add(Item(info.HdrCapable ? "On when supported" : "Auto", Room.HdrAuto));
            hdr.Items.Add(Item("On", Room.HdrOn));
            hdr.Items.Add(Item("Off", Room.HdrOff));
            Select(hdr, entry.Hdr);
            hdr.SelectionChanged += (o, e) =>
            {
                var sel = hdr.SelectedItem as ComboBoxItem;
                if (sel != null) rooms.SetHdr(setup, key, (string)sel.Tag);
            };
            var hdrRow = Row("HDR", hdr, labelWidth);
            hdrRow.Margin = new Thickness(0);
            host.Children.Add(hdrRow);
            return host;
        }

        /// <summary>Sound, hotkey, controller, launcher, safe setup, hide, the --room shortcut and delete.</summary>
        void SetupOptions(RoomStatus s, StackPanel host, double labelWidth)
        {
            var room = s.Setup;

            var sound = Combo();
            sound.Items.Add(Item("The main screen's speakers", Room.AudioAuto));
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
                rooms.SetupEdited(room, false);
            };
            var soundRow = Row("Sound", sound, labelWidth);
            soundRow.ToolTip = "\"The main screen's speakers\" uses a TV's HDMI sound, as soon as Windows makes it available. Changing the output in Windows while this setup is on screen also updates this.";
            host.Children.Add(soundRow);

            host.Children.Add(Row("Hotkey", HotkeyBox(room.Hotkey, keys =>
            {
                room.Hotkey = keys;
                rooms.SetupEdited(room, false);
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
                // One setup per direction: the one that had it gives it up.
                if (d.Length > 0)
                    foreach (var other in rooms.Settings.Setups)
                        if (other != room && other.PadDirection == d) other.PadDirection = "";
                room.PadDirection = d;
                rooms.SetupEdited(room, false);
            };
            var padRow = Row("Controller", pad, labelWidth);
            padRow.ToolTip = "Hold View and press this D-pad direction to switch here, even when you can't see a screen: on the couch with the TV still blank, View + its direction brings the picture to it.";
            host.Children.Add(padRow);

            var launcher = Combo();
            launcher.Items.Add(Item("Nothing", Room.LaunchNone));
            launcher.Items.Add(Item("Steam Big Picture", Room.LaunchBigPicture));
            launcher.Items.Add(Item("Xbox mode", Room.LaunchXbox));
            Select(launcher, room.Launcher ?? Room.LaunchNone);
            launcher.SelectionChanged += (o, e) =>
            {
                var sel = launcher.SelectedItem as ComboBoxItem;
                if (sel == null) return;
                room.Launcher = (string)sel.Tag;
                rooms.SetupEdited(room, false);
            };
            var launcherRow = Row("Open on switch", launcher, labelWidth);
            launcherRow.ToolTip = "Opens when you switch here and closes when you switch to a setup that doesn't use it. Xbox mode is Windows 11's full screen Xbox experience (Settings > Gaming); Displicare turns it on and off with its Win + F11 shortcut. While Xbox mode is open it keeps the controller to itself, so View + D-pad can't switch there: leave it from its menu (Xbox button) and View + D-pad works again. Keyboard hotkeys always work.";
            launcherRow.Margin = new Thickness(0, 0, 0, 12);
            host.Children.Add(launcherRow);

            // The safe setup: where the safe hotkey goes without asking
            UIElement safe;
            if (rooms.SafeSetup == room)
                safe = Label("This is the safe setup: " + (string.IsNullOrEmpty(rooms.Settings.RoomHomeHotkey) ? "the safe hotkey" : rooms.Settings.RoomHomeHotkey) + " always comes back here, without asking.", 11.5, false, TextPrimary);
            else
            {
                var makeSafe = Pill("Make this the safe setup");
                makeSafe.HorizontalAlignment = HorizontalAlignment.Left;
                makeSafe.ToolTip = "The safe hotkey (" + rooms.Settings.RoomHomeHotkey + ") always comes back to the safe setup, without asking: the way back from a screen you can't see.";
                makeSafe.Click += (o, e) =>
                {
                    rooms.Settings.SafeSetupId = room.Id;
                    rooms.OptionsEdited();
                };
                safe = makeSafe;
            }
            ((FrameworkElement)safe).Margin = new Thickness(labelWidth, 0, 0, 12);
            host.Children.Add(safe);

            var hide = Toggle("Hide this setup (not in the tab, tray or hotkeys)", room.Hidden);
            hide.Margin = new Thickness(labelWidth, 0, 0, 16);
            hide.Click += (o, e) =>
            {
                room.Hidden = hide.IsChecked == true;
                rooms.SetupEdited(room, false);
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

            // Delete: asks by a second click, no dialog. The screens stay known, so the setup doesn't come back by itself.
            var delete = Pill("Delete this setup");
            delete.HorizontalAlignment = HorizontalAlignment.Left;
            delete.Margin = new Thickness(0, 14, 0, 0);
            delete.ToolTip = "Removes this setup from the tab, tray, hotkeys and controller. Its screens stay known, so it doesn't come back by itself. \"+ New setup\" makes one again.";
            bool armed = false;
            delete.Click += (o, e) =>
            {
                if (!armed)
                {
                    armed = true;
                    delete.Content = "Click again to delete \"" + room.Name + "\"";
                    delete.BorderBrush = Accent;
                    return;
                }
                rooms.DeleteSetup(room);
            };
            delete.LostKeyboardFocus += (o, e) =>
            {
                if (!armed) return;
                armed = false;
                delete.Content = "Delete this setup";
                delete.ClearValue(Control.BorderBrushProperty);
            };
            host.Children.Add(delete);
        }

        /// <summary>Options for all setups: when to ask, the safe setup and its hotkey.</summary>
        public StackPanel GeneralSettings(List<RoomStatus> statuses, double labelWidth)
        {
            var settings = rooms.Settings;
            var stack = new StackPanel();

            var ask = Combo();
            ask.Items.Add(Item("Only for new or changed setups", AppSettings.RoomAskNew));
            ask.Items.Add(Item("Every switch", AppSettings.RoomAskAlways));
            ask.Items.Add(Item("Never", AppSettings.RoomAskNever));
            Select(ask, settings.RoomAskToKeep);
            ask.SelectionChanged += (s, e) =>
            {
                var i = ask.SelectedItem as ComboBoxItem;
                if (i == null) return;
                settings.RoomAskToKeep = (string)i.Tag;
                rooms.OptionsEdited();
            };
            var askRow = Row("Ask to keep", ask, labelWidth);
            askRow.ToolTip = "\"Keep this display layout?\" goes back on its own after 15 s without an answer, so a TV that is off can't leave you without a screen. A setup also asks again after you change its screens, where they go, or a resolution or refresh rate.";
            stack.Children.Add(askRow);

            var home = Combo();
            foreach (var s in statuses) home.Items.Add(Item(s.Setup.Name, s.Setup.Id));
            var safe = rooms.SafeSetup;
            Select(home, safe != null ? safe.Id : null);
            home.SelectionChanged += (s, e) =>
            {
                var i = home.SelectedItem as ComboBoxItem;
                if (i == null) return;
                settings.SafeSetupId = (string)i.Tag;
                rooms.OptionsEdited();
            };
            var homeRow = Row("Safe setup", home, labelWidth);
            homeRow.ToolTip = "Where the safe hotkey takes you, without asking: the way back from any setup, even one on a screen you can't see.";
            stack.Children.Add(homeRow);

            stack.Children.Add(Row("Safe hotkey", HotkeyBox(settings.RoomHomeHotkey, keys =>
            {
                settings.RoomHomeHotkey = keys;
                rooms.OptionsEdited();
            }), labelWidth));

            stack.Children.Add(OptionToggle("Switch with a controller: hold View + Menu", settings.RoomController,
                "Opens a setup picker on screen: up and down to choose, A to switch, B to close. A and B also answer \"Keep this display layout?\". View + a D-pad direction goes straight to the setup on that direction (set per setup), even when you can't see a screen. Works with Xbox controllers and anything Steam shows as one. Displicare only reads the controller the way games do: it doesn't hook anything or send input to games, so it is safe with anti-cheat.",
                v => settings.RoomController = v));
            stack.Children.Add(OptionToggle("Turn the screens off while streaming", settings.RoomStreamScreensOff,
                "When Steam Link (or Apollo, Sunshine) streams to a virtual display, the physical screens are switched off, so an OLED isn't left showing a still desktop. They come back as they were when the stream ends, with every window minimized so nothing sits still on screen.",
                v => settings.RoomStreamScreensOff = v));
            stack.Children.Add(OptionToggle("Show the setup on screen after switching", settings.RoomShowOverlay,
                "A short note on the new screen with the setup, resolution, refresh rate, HDR and sound.",
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
                rooms.OptionsEdited();
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
