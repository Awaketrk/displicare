using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace Displicare.Rooms
{
    internal enum PadButton { Combo, RoomUp, RoomRight, RoomDown, RoomLeft, Up, Down, A, B }

    /// <summary>
    /// Turns raw controller readings into presses: holding View + Menu together (<see cref="PadButton.Combo"/>), View +
    /// a D-pad direction for the room on that direction, and the D-pad / left stick, A and B for moving through the
    /// picker. Kept apart from the polling so it can be tested.
    /// </summary>
    internal sealed class PadTracker
    {
        public const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008,
            Menu = 0x0010, View = 0x0020, ButtonA = 0x1000, ButtonB = 0x2000;
        /// <summary>Long enough that pressing both by accident in a game doesn't count.</summary>
        public const int HoldMs = 800;
        const short StickThreshold = 16000;

        ushort lastButtons;
        int lastStick; // -1 down, 0 centre, 1 up
        long comboSince = -1;
        bool comboFired;

        /// <summary>The press this reading completes, or null.</summary>
        public PadButton? Update(ushort buttons, short stickY, long nowMs)
        {
            ushort pressed = (ushort)(buttons & ~lastButtons);
            lastButtons = buttons;
            int stick = stickY > StickThreshold ? 1 : stickY < -StickThreshold ? -1 : 0;
            bool stickMoved = stick != lastStick;
            lastStick = stick;

            if ((buttons & (View | Menu)) == (View | Menu))
            {
                if (comboSince < 0) comboSince = nowMs;
                if (!comboFired && nowMs - comboSince >= HoldMs)
                {
                    comboFired = true;
                    return PadButton.Combo;
                }
                return null; // nothing else counts while the combo is held
            }
            comboSince = -1;
            comboFired = false;

            if ((buttons & View) != 0)
            {
                if ((pressed & DpadUp) != 0) return PadButton.RoomUp;
                if ((pressed & DpadRight) != 0) return PadButton.RoomRight;
                if ((pressed & DpadDown) != 0) return PadButton.RoomDown;
                if ((pressed & DpadLeft) != 0) return PadButton.RoomLeft;
            }

            if ((pressed & DpadUp) != 0 || (stickMoved && stick == 1)) return PadButton.Up;
            if ((pressed & DpadDown) != 0 || (stickMoved && stick == -1)) return PadButton.Down;
            if ((pressed & ButtonA) != 0) return PadButton.A;
            if ((pressed & ButtonB) != 0) return PadButton.B;
            return null;
        }
    }

    /// <summary>
    /// Watches Xbox-style controllers (and anything Steam presents as one) through XInput. This is the public, read-only
    /// API every game and launcher uses: Displicare never hooks or injects anything, sends no input to games and never opens a game's process,
    /// which is what anti-cheat looks for. It reads the controllers 20 times a second while one is connected and checks
    /// for one every 2 s otherwise. Presses arrive on the UI thread; moves, A and B only while <see cref="Navigating"/>.
    /// </summary>
    internal sealed class Gamepad : IDisposable
    {
        const int PollMs = 50;
        const int ScanMs = 2000;
        const int Slots = 4;

        readonly Action<PadButton> pressed;
        readonly Dispatcher dispatcher;
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly Thread thread;
        readonly PadTracker[] trackers = new PadTracker[Slots];

        /// <summary>The picker or the keep prompt is open, so moves, A and B matter.</summary>
        public volatile bool Navigating;

        public Gamepad(Action<PadButton> pressed, Dispatcher dispatcher)
        {
            this.pressed = pressed;
            this.dispatcher = dispatcher;
            for (int i = 0; i < Slots; i++) trackers[i] = new PadTracker();
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "Displicare controller";
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Start();
        }

        /// <summary>True when a controller answered at the last reading.</summary>
        public bool Connected { get; private set; }

        void Run()
        {
            var connected = new bool[Slots];
            long lastScan = -ScanMs;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (true)
                {
                    long now = clock.ElapsedMilliseconds;
                    // Asking an empty slot is slow, so empty slots are only checked every few seconds.
                    bool scan = now - lastScan >= ScanMs - PollMs;
                    if (scan) lastScan = now;
                    bool any = false;
                    for (int i = 0; i < Slots; i++)
                    {
                        if (!connected[i] && !scan) continue;
                        XINPUT_STATE state;
                        connected[i] = XInputGetState((uint)i, out state) == 0;
                        if (!connected[i]) continue;
                        any = true;
                        var press = trackers[i].Update(state.Gamepad.wButtons, state.Gamepad.sThumbLY, now);
                        if (press == null) continue;
                        if (press >= PadButton.Up && !Navigating) continue; // picker moves, A and B
                        var p = press.Value;
                        dispatcher.BeginInvoke((Action)delegate { pressed(p); });
                    }
                    if (any != Connected) RoomLog.Write(any ? "Controller connected." : "Controller disconnected.");
                    Connected = any;
                    if (stop.WaitOne(any ? PollMs : ScanMs)) return;
                }
            }
            catch (DllNotFoundException) { RoomLog.Write("Controllers unavailable: XInput isn't installed."); }
            catch (EntryPointNotFoundException) { RoomLog.Write("Controllers unavailable: XInput isn't installed."); }
        }

        public void Dispose()
        {
            // The event isn't disposed: a reading still in progress after the wait would throw on it.
            stop.Set();
            thread.Join(1000);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger, bRightTrigger;
            public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll")]
        static extern int XInputGetState(uint userIndex, out XINPUT_STATE state);
    }
}
