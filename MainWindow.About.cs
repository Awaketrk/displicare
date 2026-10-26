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
    /// <summary>The About card and app settings: version, settings folder, Windows color and display settings, the hardware test, and start-with-Windows options.</summary>
    public partial class MainWindow
    {
        private void BtnOpenColorCpl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(Environment.SystemDirectory, "colorcpl.exe");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnOpenWindowsDisplay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
            }
            catch { }
        }

        private string GetVersionInfo()
        {
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string built = File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd HH:mm");
            string mode = ConfigManager.IsPortableMode() ? "Portable" : "Installed";
            return string.Format("Displicare v{0}\nBuilt {1} • {2} • {3}\n{4}",
                App.Version, built, mode, Environment.Is64BitProcess ? "x64" : "x86",
                Environment.OSVersion.VersionString);
        }

        private void PopulateAbout()
        {
            TxtHeaderSubtitle.Text = "Display & Color Studio • v" + App.Version;
            TxtAboutVersion.Text = "v" + App.Version;

            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            TxtAboutDetails.Text = string.Format("Built {0} • {1} mode\nDisplay & color calibration with solar sync",
                File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd HH:mm"),
                ConfigManager.IsPortableMode() ? "Portable" : "Installed");
        }

        private void BtnCopyVersion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(GetVersionInfo());
                TxtStatus.Text = "Version info copied to clipboard.";
            }
            catch
            {
                // Clipboard can be briefly locked by another app
                TxtStatus.Text = "Couldn't access the clipboard, try again.";
            }
        }

        private void BtnOpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(ConfigManager.GetSettingsFolder()) { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnHardwareTest_Click(object sender, RoutedEventArgs e)
        {
            OpenHardwareTest(false);
        }

        internal void OpenHardwareTest(bool startNow)
        {
            if (_hardwareTestWindow == null)
            {
                _hardwareTestWindow = new HardwareTestWindow(this);
                _hardwareTestWindow.Closed += (s, ev) => { _hardwareTestWindow = null; };
                _hardwareTestWindow.Show();
            }
            _hardwareTestWindow.Activate();
            if (startNow) _hardwareTestWindow.StartTest();
        }

        /// <summary>
        /// Runs the hardware test with everything else that writes display state paused, then picks up again
        /// as if nothing happened (Solar Sync target, deferred display refresh).
        /// </summary>
        internal async System.Threading.Tasks.Task RunHardwareTestAsync(HardwareTest test, System.Threading.CancellationTokenSource cts)
        {
            if (_hardwareTestRunning) return;
            _hardwareTestRunning = true;
            _hardwareTest = test;
            _hardwareTestCts = cts;
            MainContentRoot.IsEnabled = false;
            try
            {
                await test.RunAsync(cts.Token);
            }
            finally
            {
                _hardwareTestRunning = false;
                _hardwareTest = null;
                _hardwareTestCts = null;
                MainContentRoot.IsEnabled = true;

                if (_topologyChangedDuringTest)
                {
                    _topologyChangedDuringTest = false;
                    HandleDisplayTopologyChange();
                }
                if (_settings != null && _settings.SolarSyncEnabled)
                {
                    UpdateSolarCircadian(true);
                }
                ReassertDisplayState();
            }
        }

        private void ChkRunAtStartup_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkRunAtStartup == null) return;
            bool enable = ChkRunAtStartup.IsChecked == true;
            ConfigManager.SetRunOnStartup(enable, _settings.StartMinimized);
            _settings.StartWithWindows = enable;
            if (_tray != null) _tray.UpdateStartupMenuState(enable);
            ScheduleSave();
        }

        private void ChkStartMinimized_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkStartMinimized == null) return;
            _settings.StartMinimized = ChkStartMinimized.IsChecked == true;
            // The startup entry carries --minimized, so rewrite it to match
            if (_settings.StartWithWindows)
            {
                ConfigManager.SetRunOnStartup(true, _settings.StartMinimized);
            }
            ScheduleSave();
        }

        private void ChkRestoreOnExit_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkRestoreOnExit == null) return;
            _settings.RestoreOnExit = ChkRestoreOnExit.IsChecked == true;
            ScheduleSave();
        }
    }
}
