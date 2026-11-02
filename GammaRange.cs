using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Displicare
{
    /// <summary>
    /// Windows' gamma ramp limit. By default Windows rejects a ramp more than 32760 from identity (so a program can't
    /// black out the screen), which stops software dimming in SDR at 22% light and the warmest color temperatures early.
    /// The documented way around it is the GdiIcmGammaRange registry value (256 = no limit), which f.lux's "expanded
    /// range" sets too. It's system-wide (HKLM), so Displicare only changes it when the user asks, through an elevated
    /// reg.exe (UAC prompt), and remembers what was there to put it back. Windows reads it at sign-in.
    /// </summary>
    internal static class GammaRange
    {
        private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";
        private const string ValueName = "GdiIcmGammaRange";
        internal const int Unlimited = 256;

        /// <summary>The backup when the value didn't exist before Displicare set it.</summary>
        internal const string BackupAbsent = "absent";

        internal enum Result { Done, Cancelled, Failed }

        /// <summary>The registry value now, or null if it isn't set (or can't be read).</summary>
        public static int? Read()
        {
            int? value;
            TryRead(out value);
            return value;
        }

        /// <summary>
        /// False if the registry couldn't be read, so "can't tell" isn't mistaken for "not set": that would throw away the
        /// backup or write a wrong one.
        /// </summary>
        public static bool TryRead(out int? value)
        {
            value = null;
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(KeyPath))
                {
                    object v = key != null ? key.GetValue(ValueName) : null;
                    if (v is int) value = (int)v;
                }
                return true;
            }
            catch (Exception ex)
            {
                App.Log("Gamma range: can't read the registry (" + ex.Message + ")");
                return false;
            }
        }

        internal static bool IsUnlimited(int? value)
        {
            return value.HasValue && value.Value >= Unlimited;
        }

        /// <summary>What to save before Displicare sets the value: so turning it off can put back exactly this.</summary>
        internal static string BackupOf(int? value)
        {
            return value.HasValue ? value.Value.ToString() : BackupAbsent;
        }

        /// <summary>
        /// The reg.exe arguments that put a backup back: delete the value if it wasn't there, else write the old number.
        /// Null for no backup, i.e. something else (f.lux, the user) set the value and it isn't Displicare's to remove.
        /// </summary>
        internal static string RestoreArguments(string backup)
        {
            if (string.IsNullOrEmpty(backup)) return null;
            if (backup == BackupAbsent) return "delete \"HKLM\\" + KeyPath + "\" /v " + ValueName + " /f";
            int old;
            // settings.json is writable by any program running as the user: only a plausible old value goes back, elevated
            if (!int.TryParse(backup, out old) || old < 0 || old > Unlimited) return null;
            return SetArguments(old);
        }

        internal static string SetArguments(int value)
        {
            return "add \"HKLM\\" + KeyPath + "\" /v " + ValueName + " /t REG_DWORD /d " + value + " /f";
        }

        /// <summary>Runs reg.exe elevated (the UAC prompt) and waits for it. Blocks: call it off the UI thread.</summary>
        public static Result RunElevated(string arguments)
        {
            try
            {
                var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"), arguments)
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(info))
                {
                    if (p == null) return Result.Failed;
                    if (!p.WaitForExit(15000)) return Result.Failed;
                    if (p.ExitCode != 0)
                    {
                        App.Log("Gamma range: reg.exe exited with " + p.ExitCode);
                        return Result.Failed;
                    }
                }
                return Result.Done;
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return Result.Cancelled; // the user said no at the UAC prompt
                App.Log("Gamma range: " + ex.Message);
                return Result.Failed;
            }
            catch (Exception ex)
            {
                App.Log("Gamma range: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
