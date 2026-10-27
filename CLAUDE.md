# Displicare: working notes for Claude

Windows tray app for monitor brightness and color. WPF, .NET Framework 4.8, x64. See README.md for features and layout.

## Build and test

```bash
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe Tests\Displicare.Tests.csproj /p:Configuration=Release /p:Platform=x64
```

```bash
.\Tests\bin\Release\Displicare.Tests.exe
```

Building the test project also builds the app (`bin\Release\Displicare.exe`). All tests must pass before committing. The MSB3644 "reference assemblies not found" warning is expected without the .NET 4.8 Developer Pack.

- **C# 5 only.** The Framework MSBuild uses the old compiler: no `$"..."` interpolation, `?.`, `nameof`, expression-bodied members, `out var` or tuples.
- **No NuGet packages.** The app has zero dependencies; the tests use the small runner in `Tests/TestRunner.cs` (`[Test]` on public static methods). Keep it that way.
- Testable logic is `internal` and visible to tests via `InternalsVisibleTo`; prefer pure static helpers (see `MonitorEngine.BuildRamp`, `ConfigManager.Parse`).

## Versions and releases

- Between releases, `AssemblyInformationalVersion` in `Properties/AssemblyInfo.cs` ends in `-test` (e.g. `2.1.2-test`) so the About card shows test builds. Bump it right after each release.
- Release = tag the tested commit and push the tag (`git tag vX.Y.Z`, `git push origin vX.Y.Z`). GitHub Actions runs the tests, runs `build-release.ps1` and publishes the GitHub Release; it never overwrites an existing release. A manual run of the Build workflow packages without publishing.
- After a release: replace the auto-generated release notes with a written summary, add the entry to `dist/VERSIONS.md` (CI only updates it on the runner) and bump the `-test` version.
- Hand test builds over as a portable folder (with `portable.dat`) under `dist/test-build-*`, which git ignores.

## Testing on real hardware (important)

The app changes real display state: the monitor backlight (DDC/CI), the GPU gamma ramp and the Windows Magnification color filter.
- **Screenshots can't show dimming or color changes**; Windows captures the image before the gamma ramp, filter and backlight. Verify by reading state back: `GetDeviceGammaRamp`, `MagGetFullscreenColorEffect`, DDC `GetMonitorBrightness`.
- Record the backlight, ramp and filter before a test and **restore them afterwards**.
- Only one instance can run (single-instance mutex): a running installed copy blocks test builds. **Never close the user's running copy without asking.**
- Use portable test builds so the installed copy's settings in `%LOCALAPPDATA%\Displicare` stay untouched. Don't delete `settings.json` or `app.log` in a folder you didn't create.

## Behavior worth knowing

- **HDR:** Microsoft documents the gamma ramp as undefined in HDR. With HDR on, software brightness, color temperature and RGB go through the color filter (`ComputeHdrFilter`, linear light, values converted with gamma 2.2); contrast and gamma stay on the ramp.
- The HDR software floor (`HdrMinLuminance = 0.08`) and the SDR floor (`SoftwareMinLuminance = 0.30`) were tuned by eye so software matches hardware brightness on the maintainer's AORUS FO32U2 (QD-OLED, HDR on, SDR content brightness 240 nits). Different monitors may want different values.
- A DDC/CI display switched to software mode gets its backlight set to 100 so the two dimmers never stack.
- **DDC/CI at startup:** monitors often miss the first read (waking up, busy right after sign-in). A miss only sets `SupportsDdcCi = false`; it never touches the user's `UseHardwareBrightness`. Displays set to hardware are asked again in the background after 1, 3, 10 and 30 s (`MonitorEngine.RetryDdc`), and `MainWindow.OnDdcRecovered` moves dimming to the backlight. Up to 2.3.0 a miss switched hardware brightness off and saved it; `RestoreHardwarePreference` undoes that once (`HardwareBrightnessRestored`). The log records each monitor's DDC/CI answer.
- **Backlight range:** `CurrentBrightness`, `ReadBacklight` and `SetBacklight` work in 0-100%; only the DDC calls convert to the monitor's own range (`PercentToRaw`/`RawToPercent`, e.g. 0-50 or 0-255).
- **Code layout:** `MainWindow` is split into partial files by area: `MainWindow.xaml.cs` (startup, tabs, exit), `.Controls.cs` (sliders, applying settings), `.DisplayCards.cs` (Displays tab), `.DisplayEvents.cs` (window messages, foreground/Start menu hooks, display changes, DDC recovery), `.Presets.cs`, `.Solar.cs`, `.About.cs` and `.Rooms.cs`.

## Room switching

Optional feature, **off by default** (`RoomsEnabled`; toggle in Displays > General Settings). For a PC wired to TVs in other rooms: every connected screen becomes a room; switching makes it the only (primary) display with its resolution, refresh, HDR and sound. With it off, nothing runs: no hotkeys, no pipe, no audio notifications, no controller polling.

- Code: `Rooms\` (`LayoutEngine` = CCD `SetDisplayConfig` with LUID/target remapping and a rebuilt-topology fallback; `Edid` = mode list from EDID base/CTA VICs/DisplayID; `RoomManager` = discovery, switching, learning; `RoomsView`/`RoomsWindow` = UI), `MainWindow.Rooms.cs` (tab + toggle), small hooks in `MainWindow.xaml` and `MainWindow.DisplayEvents.cs`, `App.xaml.cs` (`--room <name>`), `TrayManager.cs`, `ConfigManager.cs` (`Room` list, sanitized in `SanitizeRooms`).
- `--room` reaches the running app through `CommandPipe`: only the current Windows account may connect (network denied), reads are overlapped with a 2 s deadline and a 1 KB limit, and a stalled or oversized client gets no reply.
- Rooms are keyed by monitor model from the device path (`DISPLAY#GSM82CD#...` gives `GSM82CD`), so they survive port/GPU changes.
- **Best mode** = the EDID's preferred (native) size, then the highest refresh; TVs also list 4096x2160, which must not win.
- **Learning:** mode/HDR/offered modes are learned from the active room after display changes (not within 20 s of our own switch) and right before switching away. A learned mode that equals the best one is stored as "Native" / "Highest" (0, `AsDefaults`), so only a mode the user picked is pinned; older test builds' exact values are tidied on start (`TidyLearnedModes`). Sound is learned only from the user's own default-output changes (Core Audio `IMMNotificationClient`), never on leave.
- **Sound** is set by `RoomManager`, not the layout: a TV's HDMI output can appear more than 10 s after the picture, so it waits for notifications up to 60 s. Windows may name it "1 - LG TV SSCR2 (...)"; `AudioOutputs.DevicePart` strips that.
- **Keep prompt** (`ConfirmForm`, WinForms, laid out in physical pixels for the new primary screen): asks until a room was kept once (`Confirmed`); only resolution/refresh edits reset that. No answer in 15 s switches back. The home hotkey never asks.
- **Virtual displays** (SudoVDA for Steam Link, Apollo, Parsec) can report an ordinary connector such as HDMI; they are recognised by their software adapter (`\\?\ROOT#...` adapter path, `ROOT\...` device ID: `DisplayConfigApi.IsVirtualAdapter`, `MonitorEngine.IsVirtualAdapter`). They never become rooms and get no brightness or color from `MonitorEngine`.
- **Streaming** (`RoomStreamScreensOff`): an active virtual display next to physical ones means a stream; 3 s later `RoomManager.ScreensOffForStream` makes it the only display and keeps the physical layout in `beforeStream`, applied again when the virtual display goes away. `beforeStream` comes from `lastLayout`, recorded at every display change without a stream: by the time the stream's display shows up, Windows may already have dropped a screen. Windows also learns "virtual only" for that set of screens and may do the switch itself; either way, 3 s after a stream ends every window is minimized (`Shell.Application.MinimizeAll`) so Steam's window doesn't sit on an OLED. Taskbar auto-hide is turned off around Displicare's own stream layout changes (`Taskbar`), or Explorer complains about two hidden toolbars on one edge and drops it. Learning is paused while a stream is on.
- **Tray window size** (`TrayManager.PositionWindowNearTray`) comes from the design size (440x640) times the target screen's scaling, never the window's current pixels: those belong to the last screen it was on.
- **Controller** (`RoomController`, `Gamepad`): XInput polling only (20 Hz with a controller, a 2 s check without), on its own thread; View + Menu held 800 ms opens the picker (`RoomOverlay`, a click-through, no-activate topmost window). A/B answer `ConfirmForm` too. View + a D-pad direction switches straight to the room on it (`Room.PadDirection`; `AssignPadDirections` gives home Up, the rest Right/Left/Down; null = not given yet, "" = none). It has to work blind: on the couch the TV is still blank and the picker would open on the desk monitor. Never add hooks, input injection into games or virtual devices: that's what anti-cheat flags.
- **Launcher** per room (`Room.Launcher`: none / Big Picture / Xbox mode; older settings' `OpenBigPicture` is read into it and kept in step for downgrades). Big Picture uses `steam://open|close/bigpicture`. Xbox mode has no command or URI, only the Win + F11 toggle: `XboxMode.Set` sends it with `SendInput` only when the state differs, and reads the state from Explorer's visible `GamingFallbackWindow` (present only while Xbox mode is on; checked on build 26300, Xbox app 2610). `INPUT` must be 40 bytes on x64 or `SendInput` returns 0. While Xbox mode is in front it takes the controller exclusively: background apps get no XInput changes and almost no Raw Input HID reports (measured), so View + D-pad can't leave it. Don't work around that with hooks or drivers.
- Real EDIDs from the maintainer's screens are in `Tests\Fixtures\damepc-edids.txt`.
- **Testing:** done on the maintainer's gaming PC (DAMEPC: RTX 5080 + AMD iGPU; AORUS FO32U2 on DP, LG C4 and an iFFalcon/TCL "Beyond TV" on HDMI, a SudoMaker virtual display for streaming) with a portable build. Switching moves the user's real screens: only do it with the user at the PC, never unattended. A running copy locks `bin\Release\Displicare.exe`; ask the user to exit it before rebuilding. Checked there and released in 2.3.0: the Rooms window, native 3840x2160 on the TVs, late TV sound and the prompt asking only once.
- Memory: room switching adds about 2.5 MB working set, 2 threads and ~20 handles; the app's ~80 MB idle footprint (more after the window has been opened) is WPF, not rooms.
