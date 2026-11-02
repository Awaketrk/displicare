using System;
using System.Threading.Tasks;
using System.Windows;

namespace Displicare
{
    /// <summary>
    /// "Deeper software dimming": lifts Windows' gamma ramp limit (<see cref="GammaRange"/>) when the user turns it on,
    /// and puts the old value back when they turn it off. Never changed without the toggle; Windows asks for admin.
    /// </summary>
    public partial class MainWindow
    {
        private bool _gammaRangeBusy;

        private void InitializeDeepDimming()
        {
            UpdateDeepDimmingUi();
        }

        /// <summary>The toggle and its note from the registry as it is now, so f.lux or a manual change shows too.</summary>
        private void UpdateDeepDimmingUi()
        {
            if (ChkDeepDimming == null || TxtDeepDimming == null) return;
            int? value;
            bool readable = GammaRange.TryRead(out value);
            bool on = GammaRange.IsUnlimited(value);
            bool ours = !string.IsNullOrEmpty(_settings.GammaRangeBackup);
            if (readable && !on && ours && !_gammaRangeBusy)
            {
                // Already put back (the uninstaller, or by hand): nothing left to restore
                _settings.GammaRangeBackup = null;
                ScheduleSave();
                ours = false;
            }

            bool wasUpdating = _isUpdatingUi;
            _isUpdatingUi = true;
            ChkDeepDimming.IsChecked = on;
            _isUpdatingUi = wasUpdating;
            // Set by another app: not Displicare's to undo
            ChkDeepDimming.IsEnabled = readable && !_gammaRangeBusy && (!on || ours);

            string text;
            if (!readable)
                text = "Can't read this Windows setting right now.";
            else if (on && !ours)
                text = "On: another app (such as f.lux) lifted Windows' limit, so software dimming in SDR goes down to 8% light.";
            else if (on && !MonitorEngine.RampFullRange)
                text = "Sign out of Windows or restart to finish: Windows applies this at sign-in.";
            else if (on)
                text = "Software dimming in SDR goes down to 8% light, and the warmest color temperatures aren't cut short. " +
                       "Turning it off or uninstalling Displicare puts back what was there.";
            else
                text = "Windows stops software dimming in SDR at 22% light and cuts the warmest color temperatures short. This lifts " +
                       "that limit for all apps, as f.lux's expanded range does: it asks for admin, works after you sign out or restart, " +
                       "and turning it off puts back what was there.";
            TxtDeepDimming.Text = text;
        }

        private void ChkDeepDimming_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _settings == null || ChkDeepDimming == null || _gammaRangeBusy) return;
            bool turnOn = ChkDeepDimming.IsChecked == true;

            int? before;
            if (!GammaRange.TryRead(out before)) { UpdateDeepDimmingUi(); return; } // no backup without knowing what was there
            string arguments;
            if (turnOn)
            {
                if (GammaRange.IsUnlimited(before)) { UpdateDeepDimmingUi(); return; }
                arguments = GammaRange.SetArguments(GammaRange.Unlimited);
            }
            else
            {
                arguments = GammaRange.RestoreArguments(_settings.GammaRangeBackup);
                if (arguments == null) { UpdateDeepDimmingUi(); return; }
            }

            _gammaRangeBusy = true;
            ChkDeepDimming.IsEnabled = false;
            Task.Run(() => GammaRange.RunElevated(arguments)).ContinueWith(t =>
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    _gammaRangeBusy = false;
                    var result = t.Result;
                    if (result == GammaRange.Result.Done)
                    {
                        if (turnOn) _settings.GammaRangeBackup = GammaRange.BackupOf(before);
                        else _settings.GammaRangeBackup = null;
                        ScheduleSave();
                        App.Log("Gamma range: " + (turnOn ? "Windows' limit lifted (was " + _settings.GammaRangeBackup + ")" : "put back"));
                        // Turning it off: stay within the limit right away, since the next sign-in brings it back
                        if (!turnOn)
                        {
                            MonitorEngine.RampFullRange = false;
                            ApplyCurrentSettings(true);
                            if (TxtStatus != null) TxtStatus.Text = "Back to Windows' default after you sign out or restart.";
                        }
                    }
                    else if (result == GammaRange.Result.Failed && TxtStatus != null)
                    {
                        TxtStatus.Text = "Couldn't change the Windows setting.";
                    }
                    UpdateDeepDimmingUi();
                }));
            });
        }
    }
}
