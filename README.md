<p align="center">
  <img src="Assets/icon_128.png" width="96" alt="Displicare logo">
</p>

<h1 align="center">Displicare</h1>

<p align="center">
  Monitor brightness, color and sun-synced dimming for Windows, from the system tray.
</p>

<p align="center">
  <a href="https://github.com/Awaketrk/displicare/releases/latest"><img src="https://img.shields.io/github/v/release/Awaketrk/displicare?label=release" alt="Latest release"></a>
  <a href="https://github.com/Awaketrk/displicare/actions/workflows/build.yml"><img src="https://github.com/Awaketrk/displicare/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 and 11">
</p>

<p align="center">
  <img src="docs/screenshot.png" width="360" alt="Displicare window">
</p>

> [!NOTE]
> **Displicare is vibecoded.** It was designed and written through conversation with AI coding assistants (first Google Antigravity with Gemini, then Claude Code), with every build tested by hand on real hardware. The code is reviewed and covered by unit tests, but treat it like any hobby project: try it on your setup before relying on it.

## Features

- **Hardware brightness** over DDC/CI: changes the real backlight of supported monitors.
- **Software brightness** for monitors without DDC/CI, and an **HDR mode**: with Windows HDR on, dimming, color temperature and RGB go through a Windows color filter, so they also work in HDR games and video.
- **Color calibration**: color temperature (2500–10000 K), RGB gains, contrast and gamma.
- **Solar Sync**: follows the sun at your location, dimming (and optionally warming) the screen toward night.
- **Presets**: built-in profiles (Night Light, Paper Read, OLED Vivid, Gaming FPS and more), plus your own.
- **Multi-monitor**: control every display together or each one separately; settings are remembered per monitor.
- **Quick brightness**: scroll over the tray icon, or use optional hotkeys, without opening the window.
- **Room switching** (optional): for a PC wired to a desk monitor and TVs in other rooms, make one screen the only display with its own resolution, refresh rate, HDR and sound.
- **Lightweight**: no dependencies, ~0% CPU when idle, no global input hooks. Calibration is only rewritten when Windows or a game resets it.

## Install

Download the latest release from the [Releases page](https://github.com/Awaketrk/displicare/releases):

- **`Displicare-vX.Y.Z-Setup.exe`**: installs for the current user (no admin rights needed), optionally starting with Windows.
- **`Displicare-vX.Y.Z-Portable.zip`**: unzip anywhere and run; settings stay in that folder.

Each release includes `SHA256SUMS.txt` to verify the downloads.

**Requirements:** Windows 10 or 11 (x64) with .NET Framework 4.8, which current Windows versions include.

## Usage

| Do this | To |
|---|---|
| **Left-click** the tray icon | Open the control window (it hides when you click elsewhere; pin it to keep it open) |
| **Right-click** the tray icon | Quick brightness, presets, Solar Sync, start with Windows, Exit |
| **Scroll** over the tray icon | Change brightness in steps of 5%; a small bar above the taskbar shows the level |
| **Ctrl+Alt+Up / Down** | Change brightness in steps of 10% from anywhere (turn on **Brightness hotkeys** in Displays > General Settings) |

The **Displays** tab shows each monitor's capabilities, switches it between hardware and software brightness, and holds the About card.

**Something not working on your monitor?** Click **Hardware Test…** on the About card. It checks that your displays accept and keep what Displicare writes (gamma ramp, HDR color filter, DDC/CI backlight) and saves a report next to your settings; attach it to an [issue](https://github.com/Awaketrk/displicare/issues). The screen dims and changes color for a few seconds while it runs, and everything is put back afterwards.

### Brightness modes

| Mode | Used when | How it dims |
|---|---|---|
| Hardware | The monitor supports DDC/CI (and it's on for that display) | Sets the monitor's own backlight |
| Software | No DDC/CI, or hardware turned off for that display | GPU gamma ramp, down to ~30% light |
| Software, HDR | Windows HDR is on | Windows color filter, down to 8% light; also dims HDR content |

Turn on DDC/CI in your monitor's on-screen menu to get hardware brightness.

### Room switching

Turn it on in **Displays > General Settings**. Every connected screen becomes a room, with a card on the **Rooms** tab.

- **Switch here** makes that screen the only (primary) display. The first time, Displicare asks "Keep this display layout?" and switches back after 15 seconds without an answer, so a TV that can't show the picture never leaves you stuck.
- **⚙ Room settings…** sets each room's resolution and refresh rate (**Native** and **Highest** unless you pick others), HDR, sound output, hotkey, and what opens there: **Steam Big Picture** or Windows 11's **Xbox mode**. Leaving such a room closes it again and minimizes every window, so nothing sits still on screen.
- Rooms remember what you use there: change the resolution, HDR or sound output in Windows and the room keeps it.
- **Hotkeys:** Ctrl+Alt+1, 2, 3… switch to each room; Ctrl+Alt+0 goes back to the home room without asking. `Displicare.exe --room "LG TV"` does the same from a shortcut.
- **Controller:** hold **View + Menu** for a second to open a room picker (up/down chooses, **A** switches, **B** closes), or press **View + a D-pad direction** to go straight to a room, even while the TV is still blank: home is on Up, the others on Right, Left and Down. A and B also answer "Keep this display layout?". It works with Xbox controllers and anything Steam presents as one, and only reads the controller through XInput, as games do, so it's safe with anti-cheat.
- **Streaming:** while Steam Link (or Apollo, Sunshine) streams to a virtual display, your physical screens are switched off so an OLED isn't left showing a still desktop. They come back as they were when the stream ends, with every window minimized.
- After each switch, a short note on the new screen shows the room, resolution, refresh rate, HDR and sound.

The controller, turning screens off while streaming, and the note after a switch can each be turned off under **⚙ Room settings… > All rooms**.

> [!TIP]
> While Xbox mode is open it keeps the controller to itself, so View + D-pad can't switch rooms from inside it. Leave it from its menu (Xbox button) first, or use a keyboard hotkey.

### Settings and command line

Settings and the log live in `%LOCALAPPDATA%\Displicare` (portable builds: next to the exe).

| Option | Effect |
|---|---|
| `--minimized`, `-m` | Start in the tray |
| `--portable`, `-p` | Keep settings next to the exe |
| `--hardware-test` | Open the hardware test and start it right away |
| `--room "<name>"` | Switch to that room (room switching must be on) |

## Known limitations

- **Location is estimated from your time zone.** For exact coordinates, close the app, set `UserLatitude` / `UserLongitude` in `settings.json` and set `AutoLocation` to `false`.
- **Other color tools conflict.** While custom settings are active, Displicare restores its calibration if f.lux, Windows Night Light or a vendor tool changes it.
- **HDR mode** covers all screens at once and can't run together with Windows' own color filters. Contrast and gamma still use the gamma ramp, which Windows doesn't define for HDR, so they may look different there.

## Building

Requirements: Windows, the .NET Framework 4.8 Developer Pack, and [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer.

```bash
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe Displicare.csproj /p:Configuration=Release /p:Platform=x64
```

The app is written to `bin\Release\Displicare.exe`.

### Tests

The test project needs no packages: it's a small console runner that exits with an error if any test fails. Building it also builds the app.

```bash
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe Tests\Displicare.Tests.csproj /p:Configuration=Release /p:Platform=x64
```

```bash
.\Tests\bin\Release\Displicare.Tests.exe
```

They cover the brightness and gamma math, the sun-position calculations, settings validation and the room switching logic (display modes from real EDIDs, layouts, sound output matching).

### Releasing

GitHub Actions builds and tests every push. To publish a release, tag a commit and push the tag:

```bash
git tag vX.Y.Z
```

```bash
git push origin vX.Y.Z
```

The workflow runs the tests, builds the installer and portable zip with `build-release.ps1`, and publishes them as a GitHub Release. To check the packages without publishing, run the **Build** workflow manually from the Actions tab and enter a version. `build-release.ps1 -Version X.Y.Z` does the same locally, writing to `dist\vX.Y.Z\`.

Between releases the version in `Properties\AssemblyInfo.cs` ends in `-test` (e.g. `X.Y.Z-test`), so test builds are easy to tell apart in the About card. The release script replaces it with the plain version.

### Project layout

| Path | What's in it |
|---|---|
| `MainWindow.xaml(.cs)` | The UI, startup and exit. The rest of the window's code is split by area: `.Controls.cs` (sliders, applying settings), `.DisplayCards.cs` (Displays tab), `.DisplayEvents.cs` (reacting to Windows and display changes), `.BrightnessKeys.cs` (tray wheel and hotkeys), `.Presets.cs`, `.Solar.cs`, `.About.cs` and `.Rooms.cs` |
| `MonitorEngine.cs` | Display detection, DDC/CI, gamma ramps, presets |
| `SolarEngine.cs` | Sun position and Solar Sync targets |
| `ConfigManager.cs` | Settings loading/validation and start-with-Windows |
| `ColorFilterDimmer.cs`, `HdrHelper.cs` | HDR detection and HDR dimming |
| `TrayManager.cs`, `TrayWheel.cs` | Tray icon, menu and mouse wheel |
| `HardwareTest.cs`, `HardwareTestWindow.cs` | In-app hardware test and its report |
| `Rooms/` | Room switching: display layouts, EDID modes, sound outputs, controller, on-screen notes, the Rooms tab and window |
| `Tests/` | Unit tests |
| `build-release.ps1`, `installer.iss` | Release packaging |
