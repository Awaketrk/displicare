using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Displicare.Rooms;

namespace Displicare
{
    /// <summary>
    /// Room switching in the main window: the on/off switch, the Rooms tab (switch cards and a button for the Rooms
    /// window, where settings live) and the tray submenu. All of it stays idle while room switching is off.
    /// </summary>
    public partial class MainWindow
    {
        private RoomManager _rooms;
        private RoomsView _roomsView;
        private RoomsWindow _roomsWindow;
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
            _tray.OnRoomsWindowRequested += () => Dispatcher.BeginInvoke((Action)OpenRoomsWindow);
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
                    _roomsView = new RoomsView(_rooms, key => FindResource(key), (title, text, warning) => _tray.ShowBalloon(title, text, warning));
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
                if (_roomsWindow != null) _roomsWindow.Close();
                if (_rooms != null) _rooms.Stop();
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
            if (_roomsWindow != null)
            {
                _roomsWindow.Close();
                _roomsWindow = null;
            }
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

        private void OpenRoomsWindow()
        {
            if (_rooms == null || !_rooms.IsRunning) return;
            if (_roomsWindow == null)
            {
                _roomsWindow = new RoomsWindow(_rooms, key => FindResource(key), (title, text, warning) => _tray.ShowBalloon(title, text, warning));
                _roomsWindow.Closed += (s, e) => _roomsWindow = null;
                if (IsVisible) Hide(); // the tray window would sit on top of it
                _roomsWindow.Show();
            }
            if (_roomsWindow.WindowState == WindowState.Minimized) _roomsWindow.WindowState = WindowState.Normal;
            _roomsWindow.Activate();
        }

        private void RoomsChanged()
        {
            if (_rooms == null) return;
            var statuses = _rooms.Statuses();

            var menu = new List<Tuple<string, string, bool, bool>>();
            foreach (var s in statuses)
                if (!s.Room.Hidden) menu.Add(Tuple.Create(s.Room.Name, s.Room.Key, s.Active, s.Connected));
            _tray.UpdateRoomsMenu(menu, _rooms.Busy);

            StackRooms.Children.Clear();
            var intro = new StackPanel();
            intro.Children.Add(RoomsView.Label("Rooms", 13, true, RoomsView.TextPrimary));
            var text = RoomsView.Label("Every connected screen is a room. Switching makes it the only screen, with the resolution, HDR and sound you use there.", 11, false, RoomsView.TextMuted);
            text.Margin = new Thickness(0, 4, 0, 12);
            intro.Children.Add(text);
            var open = _roomsView.Pill("⚙  Room settings…");
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += (o, e) => OpenRoomsWindow();
            intro.Children.Add(open);
            StackRooms.Children.Add(_roomsView.Card(intro, false));

            foreach (var s in statuses) if (!s.Room.Hidden) StackRooms.Children.Add(_roomsView.SwitchCard(s));
        }
    }
}
