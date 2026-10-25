using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Displicare
{
    /// <summary>
    /// Runs <see cref="HardwareTest"/> and shows its report, with buttons to copy it or open the folder it was saved in.
    /// Built in code to keep it to one file; colors follow MainWindow.xaml.
    /// </summary>
    internal sealed class HardwareTestWindow : Window
    {
        private readonly MainWindow _main;
        private readonly TextBlock _status;
        private readonly TextBox _log;
        private readonly Button _btnStart;
        private readonly Button _btnCopy;
        private readonly Button _btnFolder;
        private readonly Button _btnClose;
        private CancellationTokenSource _cts;
        private HardwareTest _lastRun;

        private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xFB, 0xF6, 0xF7));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0xB3, 0xA3, 0xAE));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x6B));

        public HardwareTestWindow(MainWindow main)
        {
            _main = main;
            Title = "Displicare Hardware Test";
            Width = 640;
            Height = 620;
            MinWidth = 480;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x15, 0x20));
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/icon_64.png")); } catch { }

            var pill = (Style)main.FindResource("PillButton");

            var root = new DockPanel { Margin = new Thickness(18, 16, 18, 16) };

            var intro = new StackPanel();
            intro.Children.Add(new TextBlock
            {
                Text = "Hardware test",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Text,
                Margin = new Thickness(0, 0, 0, 8)
            });
            intro.Children.Add(new TextBlock
            {
                Text = "Checks that this PC's displays accept and keep what Displicare writes: the GPU gamma ramp, " +
                       "the HDR color filter and the monitor backlight (DDC/CI).\n\n" +
                       "Your screen will dim and change color for a few seconds (up to about 30 with a DDC/CI monitor). " +
                       "Everything is put back afterwards. " +
                       "Press Esc to stop at any time.\n\n" +
                       "The report is saved next to your settings. It lists the app version, Windows version, display " +
                       "names, GPU and the measured values; nothing else from your settings.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Muted,
                LineHeight = 18
            });
            _status = new TextBlock
            {
                Text = "Ready.",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Accent,
                Margin = new Thickness(0, 12, 0, 8)
            };
            intro.Children.Add(_status);
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            _btnStart = new Button { Content = "Start test", Style = pill, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
            _btnCopy = new Button { Content = "Copy report", Style = pill, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
            _btnFolder = new Button { Content = "Open folder ↗", Style = pill, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
            _btnClose = new Button { Content = "Close", Style = pill, Padding = new Thickness(12, 8, 12, 8) };
            _btnStart.Click += BtnStart_Click;
            _btnCopy.Click += BtnCopy_Click;
            _btnFolder.Click += BtnFolder_Click;
            _btnClose.Click += (s, e) => Close();
            buttons.Children.Add(_btnStart);
            buttons.Children.Add(_btnCopy);
            buttons.Children.Add(_btnFolder);
            buttons.Children.Add(_btnClose);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            _log = new TextBox
            {
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11.5,
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x0F, 0x15)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xED, 0xE3, 0xE7)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x3B)),
                Padding = new Thickness(8),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap
            };
            root.Children.Add(_log);

            Content = root;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && _cts != null)
                {
                    _cts.Cancel();
                    e.Handled = true;
                }
            };

            // Closing mid-run stops the test; the window closes once everything is restored
            Closing += (s, e) =>
            {
                if (_cts != null)
                {
                    e.Cancel = true;
                    _closeWhenDone = true;
                    _cts.Cancel();
                }
            };
        }

        private bool _closeWhenDone;

        /// <summary>Starts a run unless one is already going (used by --hardware-test).</summary>
        public void StartTest()
        {
            if (_cts == null) BtnStart_Click(this, null);
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                return;
            }

            _log.Clear();
            _btnCopy.IsEnabled = false;
            _btnFolder.IsEnabled = false;
            _btnStart.Content = "Stop";

            var test = new HardwareTest(_main.Engine);
            test.LineWritten += line => { _log.AppendText(line + Environment.NewLine); _log.ScrollToEnd(); };
            test.StepChanged += step => _status.Text = step;
            _cts = new CancellationTokenSource();

            try
            {
                await _main.RunHardwareTestAsync(test, _cts);
            }
            catch (Exception ex)
            {
                App.Log("Hardware test window error: " + ex);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
            }

            _lastRun = test;
            _status.Text = HardwareTest.SummaryLine(test.Passed, test.Failed, test.Cancelled) +
                (test.ReportPath != null ? " • saved as " + Path.GetFileName(test.ReportPath) : "");
            _btnStart.Content = "Run again";
            _btnCopy.IsEnabled = true;
            _btnFolder.IsEnabled = true;
            _log.ScrollToHome();

            if (_closeWhenDone) Close();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_lastRun == null) return;
            try
            {
                Clipboard.SetText(_lastRun.ReportText);
                _status.Text = "Report copied to the clipboard.";
            }
            catch
            {
                // Clipboard can be briefly locked by another app
                _status.Text = "Couldn't access the clipboard, try again.";
            }
        }

        private void BtnFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastRun != null && _lastRun.ReportPath != null)
                {
                    Process.Start("explorer.exe", "/select,\"" + _lastRun.ReportPath + "\"");
                }
                else
                {
                    Process.Start(new ProcessStartInfo(ConfigManager.GetSettingsFolder()) { UseShellExecute = true });
                }
            }
            catch { }
        }
    }
}
