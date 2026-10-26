using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Displicare
{
    public partial class App : Application
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public const string WakeupMessageName = "Displicare_Wakeup_Message";
        public static readonly IntPtr HWND_BROADCAST = (IntPtr)0xffff;

        private static Mutex _singleInstanceMutex;
        private static bool _ownsMutex = false;
        private MainWindow _mainWindow;
        private static readonly object _logLock = new object();

        public static bool IsPortableMode { get; set; }

        /// <summary>Version shown to the user, e.g. "2.1.0" or "2.1.0-test" (from AssemblyInformationalVersion).</summary>
        public static string Version
        {
            get
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var attr = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
                return attr != null ? attr.InformationalVersion : asm.GetName().Version.ToString(3);
            }
        }

        public static void Log(string msg)
        {
            try
            {
                lock (_logLock)
                {
                    string dir;
                    if (IsPortableMode || File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portable.dat")))
                    {
                        dir = AppDomain.CurrentDomain.BaseDirectory;
                    }
                    else
                    {
                        dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Displicare");
                    }

                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string logPath = Path.Combine(dir, "app.log");

                    FileInfo fi = new FileInfo(logPath);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        var lines = File.ReadAllLines(logPath);
                        int keep = Math.Min(lines.Length, 150);
                        string[] recent = new string[keep];
                        Array.Copy(lines, lines.Length - keep, recent, 0, keep);
                        File.WriteAllLines(logPath, recent);
                    }

                    File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " - " + msg + Environment.NewLine);
                }
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            Log("OnStartup entered (v" + Version + ")");
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                Log("UnhandledException: " + ev.ExceptionObject);
            };

            DispatcherUnhandledException += (s, ev) =>
            {
                Log("DispatcherUnhandledException: " + ev.Exception);
                ev.Handled = true;
            };

            const string mutexName = @"Local\Displicare_SingleInstance_Mutex";
            bool isNewInstance = false;
            _singleInstanceMutex = new Mutex(true, mutexName, out isNewInstance);
            _ownsMutex = isNewInstance;

            string roomArg = RoomArgument(e.Args);

            if (!isNewInstance)
            {
                // "--room <name>" (a Steam or desktop shortcut) goes to the running instance; it only listens with rooms on.
                if (roomArg != null && Rooms.RoomManager.SendRoomCommand(roomArg))
                {
                    Log("Handed --room \"" + roomArg + "\" to the running instance.");
                    Shutdown();
                    return;
                }
                Log("Another instance already running. Sending wakeup signal and exiting.");
                uint msg = RegisterWindowMessage(WakeupMessageName);
                if (msg != 0)
                {
                    PostMessage(HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                }
                Shutdown();
                return;
            }

            bool startMinimized = false;
            bool hardwareTest = false;
            if (e.Args != null)
            {
                foreach (var arg in e.Args)
                {
                    if (arg.Equals("--hardware-test", StringComparison.OrdinalIgnoreCase))
                    {
                        hardwareTest = true;
                    }
                    if (arg.Equals("--portable", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("-p", StringComparison.OrdinalIgnoreCase))
                    {
                        IsPortableMode = true;
                    }
                    if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
                    {
                        startMinimized = true;
                    }
                }
            }

            Log("Creating MainWindow...");
            _mainWindow = new MainWindow();
            this.MainWindow = _mainWindow;
            Log("MainWindow created");

            if (roomArg != null)
            {
                // Switching rooms from a shortcut shouldn't also pop the window up
                startMinimized = true;
                _mainWindow.SwitchRoomFromCommandLine(roomArg);
            }

            if (!startMinimized)
            {
                Log("Showing MainWindow");
                _mainWindow.Show();
                _mainWindow.Activate();
            }
            else
            {
                Log("Started minimized in tray");
            }

            if (hardwareTest)
            {
                Log("Starting hardware test from the command line");
                _mainWindow.OpenHardwareTest(true);
            }

            base.OnStartup(e);
            Log("OnStartup finished");
        }

        /// <summary>The value after --room (several words are joined, so the quotes are optional), or null.</summary>
        internal static string RoomArgument(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].Equals("--room", StringComparison.OrdinalIgnoreCase)) continue;
                var words = new System.Collections.Generic.List<string>();
                for (int j = i + 1; j < args.Length && !args[j].StartsWith("-"); j++) words.Add(args[j]);
                string room = string.Join(" ", words.ToArray()).Trim();
                return room.Length > 0 ? room : null;
            }
            return null;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log("OnExit called with code: " + e.ApplicationExitCode);
            try
            {
                if (_mainWindow != null)
                {
                    _mainWindow.Dispatcher.Invoke((Action)(() =>
                    {
                        _mainWindow.CleanExit();
                    }));
                }
            }
            catch { }

            if (_singleInstanceMutex != null)
            {
                try
                {
                    if (_ownsMutex)
                    {
                        _singleInstanceMutex.ReleaseMutex();
                    }
                    _singleInstanceMutex.Dispose();
                }
                catch { }
                _singleInstanceMutex = null;
            }
            base.OnExit(e);
        }
    }
}
