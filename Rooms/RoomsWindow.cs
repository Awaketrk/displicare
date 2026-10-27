using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Displicare.Rooms
{
    /// <summary>
    /// The big room settings window: rooms on the left, the selected room's settings on the right, options for all
    /// rooms underneath the list. Same look as the tray window; opened from the Rooms tab or the tray menu.
    /// </summary>
    internal sealed class RoomsWindow : Window
    {
        readonly RoomManager rooms;
        readonly RoomsView view;
        readonly StackPanel listHost = new StackPanel();
        readonly StackPanel detailHost = new StackPanel();
        string selectedKey;
        bool rebuildPending;

        public RoomsWindow(RoomManager rooms, Func<string, object> resource, Action<string, string, bool> notify)
        {
            this.rooms = rooms;
            view = new RoomsView(rooms, resource, notify);
            view.EditFinished = () =>
            {
                if (!rebuildPending) return;
                rebuildPending = false;
                Dispatcher.BeginInvoke((Action)Rebuild, System.Windows.Threading.DispatcherPriority.Background);
            };

            Title = "Displicare Rooms";
            Width = 940;
            Height = 660;
            MinWidth = 760;
            MinHeight = 520;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI, Segoe UI Variable Display, sans-serif");
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/icon_64.png")); }
            catch (Exception) { }

            Content = BuildShell();
            rooms.Changed += Rebuild;
            Closed += (s, e) => rooms.Changed -= Rebuild;
            KeyDown += (s, e) => { if (e.Key == Key.Escape && !RoomsView.Editing(this)) Close(); };
            Rebuild();
        }

        UIElement BuildShell()
        {
            var outer = new Border();
            outer.CornerRadius = new CornerRadius(20);
            outer.BorderBrush = RoomsView.MakeBrush("#4A3650");
            outer.BorderThickness = new Thickness(1.2);
            outer.Margin = new Thickness(10);
            var sky = new LinearGradientBrush();
            sky.StartPoint = new Point(0, 0);
            sky.EndPoint = new Point(0, 1);
            sky.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#2A1730"), 0));
            sky.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#170F17"), 0.35));
            sky.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#110C11"), 1));
            outer.Background = sky;
            outer.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 28, ShadowDepth = 6, Opacity = 0.6 };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(BuildHeader());

            var body = new Grid();
            body.Margin = new Thickness(16, 12, 16, 16);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new ScrollViewer();
            left.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            left.Margin = new Thickness(0, 0, 14, 0);
            left.Content = listHost;
            body.Children.Add(left);

            var right = new ScrollViewer();
            right.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            right.Content = detailHost;
            Grid.SetColumn(right, 1);
            body.Children.Add(right);

            Grid.SetRow(body, 1);
            grid.Children.Add(body);
            outer.Child = grid;
            return outer;
        }

        UIElement BuildHeader()
        {
            var header = new Border();
            header.Padding = new Thickness(18, 14, 14, 12);
            header.BorderThickness = new Thickness(0, 0, 0, 2);
            var horizon = new LinearGradientBrush();
            horizon.StartPoint = new Point(0, 0);
            horizon.EndPoint = new Point(1, 0);
            horizon.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00FF6B5A"), 0));
            horizon.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF6B5A"), 0.3));
            horizon.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFC37A"), 0.5));
            horizon.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF6B5A"), 0.7));
            horizon.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00FF6B5A"), 1));
            header.BorderBrush = horizon;
            header.Background = Brushes.Transparent; // so the whole bar can be dragged
            header.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 1) DragMove(); };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var logo = new Image();
            logo.Width = 32;
            logo.Height = 32;
            logo.Margin = new Thickness(0, 0, 10, 0);
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icon_64.png")); }
            catch (Exception) { }
            grid.Children.Add(logo);

            var titles = new StackPanel();
            titles.VerticalAlignment = VerticalAlignment.Center;
            var title = RoomsView.Label("ROOMS", 15, true, RoomsView.TextPrimary);
            title.FontWeight = FontWeights.Bold;
            titles.Children.Add(title);
            titles.Children.Add(RoomsView.Label("Displicare · one screen at a time, with its own resolution, HDR and sound", 11, false, RoomsView.TextMuted));
            Grid.SetColumn(titles, 1);
            grid.Children.Add(titles);

            var close = new Button();
            close.Style = (Style)FindResourceSafe("HeaderIconButton");
            close.ToolTip = "Close";
            var x = RoomsView.Label("✕", 14, true, RoomsView.TextMuted);
            x.FontWeight = FontWeights.Bold;
            close.Content = x;
            close.Click += (s, e) => Close();
            Grid.SetColumn(close, 2);
            grid.Children.Add(close);

            header.Child = grid;
            return header;
        }

        object FindResourceSafe(string key)
        {
            var main = Application.Current != null ? Application.Current.MainWindow : null;
            return main != null ? main.TryFindResource(key) : null;
        }

        public void Select(string key)
        {
            selectedKey = key;
            Rebuild();
        }

        void Rebuild()
        {
            if (RoomsView.Editing(this))
            {
                rebuildPending = true;
                return;
            }
            var statuses = rooms.Statuses();
            RoomStatus selected = null;
            foreach (var s in statuses) if (s.Room.Key == selectedKey) selected = s;
            if (selected == null)
            {
                foreach (var s in statuses) if (s.Active) selected = s;
                if (selected == null && statuses.Count > 0) selected = statuses[0];
                if (selected != null) selectedKey = selected.Room.Key;
            }

            listHost.Children.Clear();
            listHost.Children.Add(RoomsView.SectionLabel("ROOMS"));
            foreach (var s in statuses) listHost.Children.Add(Tile(s, s == selected));
            if (statuses.Count == 0)
                listHost.Children.Add(RoomsView.Label("No screens found yet. Connected TVs and monitors show up here on their own.", 11.5, false, RoomsView.TextMuted));
            listHost.Children.Add(RoomsView.SectionLabel("ALL ROOMS"));
            listHost.Children.Add(view.Card(view.GeneralSettings(statuses, 100), false));

            detailHost.Children.Clear();
            if (selected != null) detailHost.Children.Add(Detail(selected));
        }

        UIElement Tile(RoomStatus s, bool isSelected)
        {
            var tile = new Border();
            tile.CornerRadius = new CornerRadius(12);
            tile.Padding = new Thickness(12, 10, 12, 10);
            tile.Margin = new Thickness(0, 0, 0, 8);
            tile.Background = isSelected ? RoomsView.Selected : RoomsView.CardBg;
            tile.BorderBrush = isSelected ? RoomsView.Accent : RoomsView.CardBorder;
            tile.BorderThickness = new Thickness(isSelected ? 1.5 : 1);
            tile.Cursor = Cursors.Hand;
            tile.Opacity = s.Room.Hidden ? 0.55 : 1;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = RoomsView.Label(RoomsView.Icon(s), 22, false, RoomsView.TextPrimary);
            icon.Margin = new Thickness(0, 0, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);

            var text = new StackPanel();
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = RoomsView.Label(s.Room.Name + (s.Room.Hidden ? " (hidden)" : ""), 13, true, RoomsView.TextPrimary);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.TextWrapping = TextWrapping.NoWrap;
            top.Children.Add(name);
            var badge = RoomsView.Badge(s);
            badge.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(badge, 1);
            top.Children.Add(badge);
            text.Children.Add(top);
            text.Children.Add(RoomsView.Label(RoomsView.Summary(s.Room), 11, false, RoomsView.TextMuted));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            tile.Child = grid;

            string key = s.Room.Key;
            tile.MouseLeftButtonUp += (o, e) => Select(key);
            return tile;
        }

        UIElement Detail(RoomStatus s)
        {
            var stack = new StackPanel();

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = RoomsView.Label(RoomsView.Icon(s), 34, false, RoomsView.TextPrimary);
            icon.Margin = new Thickness(0, 0, 14, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(icon);

            var titles = new StackPanel();
            titles.VerticalAlignment = VerticalAlignment.Center;
            var name = RoomsView.Label(s.Room.Name, 20, true, RoomsView.TextPrimary);
            name.FontWeight = FontWeights.Bold;
            titles.Children.Add(name);
            titles.Children.Add(RoomsView.Label(RoomsView.Details(s, rooms.Settings.RoomController), 11.5, false, RoomsView.TextMuted));
            Grid.SetColumn(titles, 1);
            top.Children.Add(titles);

            var go = view.SwitchButton(s);
            go.Padding = new Thickness(18, 10, 18, 10);
            go.FontSize = 13;
            go.VerticalAlignment = VerticalAlignment.Center;
            go.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(go, 2);
            top.Children.Add(go);
            stack.Children.Add(top);

            var line = new Border();
            line.Height = 1;
            line.Background = RoomsView.CardBorder;
            line.Margin = new Thickness(0, 16, 0, 16);
            stack.Children.Add(line);

            stack.Children.Add(view.RoomSettings(s));
            var card = view.Card(stack, s.Active);
            card.Padding = new Thickness(22, 20, 22, 20);
            return card;
        }
    }
}
