# Displicare — Display & Color Studio

A Windows system-tray app for controlling monitor brightness and color: hardware brightness over DDC/CI, software color/gamma calibration through the GPU gamma ramp, and an optional "Solar Sync" mode that follows the sun's position through the day.

> The app was originally called **LuminaDisplay**. Displicare still migrates settings from the old `%LOCALAPPDATA%\LuminaDisplay` folder and removes the old `LuminaDisplay` startup entry.

- **Platform:** Windows 10/11, x64
- **Stack:** C# / WPF, .NET Framework 4.8, no NuGet dependencies (Win32 P/Invoke only)
- **Current installer version:** 2.0.4 (`installer.iss`)

---

## Features

### Brightness
- **Hardware brightness (DDC/CI)** — on monitors that support it, the real backlight is changed via `dxva2.dll` (`SetMonitorBrightness`). Slider changes are queued and sent at most every 35 ms on a background thread so dragging never freezes the UI or floods the monitor's I²C bus.
- **Software brightness (fallback)** — on monitors without DDC/CI (or with hardware brightness turned off for that display), brightness is simulated by scaling the GPU gamma ramp. **This can only dim to about 50%**: the slider maps 0–100% to a 50–100% white point.

### HDR displays
When Windows HDR is on for a display, Microsoft documents the gamma ramp as having undefined behavior, so software brightness switches to a **Windows color filter** (the Magnification API full-screen color effect, the same mechanism as Windows' own color filters):
- 0% = 8% light, 100% = unchanged, scaling linearly in between (the 8% floor was tuned so night levels match an AORUS FO32U2's hardware brightness). That's far deeper than Windows' SDR content brightness slider, which stops at 80 nits.
- It is applied by the compositor, so it dims everything, including HDR games and videos.
- Windows removes it automatically when the app exits or crashes, so it can never be left stuck on.
- It covers all screens at once (it follows the first HDR display in software mode), and it can't run while Windows' own color filters or Magnifier color inversion are on.
- Hardware DDC/CI brightness is unaffected by HDR.
- The Displays tab shows an "HDR is on" note on each HDR display. Color temperature, RGB, contrast and gamma still go through the gamma ramp and may look different in HDR.

### Color calibration
All applied through `SetDeviceGammaRamp`, per display:
- Color temperature, 2500–10000 K (Tanner Helland Planckian approximation)
- Red / green / blue gain
- Contrast (0.5–1.8) and gamma (0.5–2.5)

The ramp is clamped to stay within what Windows' driver accepts (±32760 from identity, monotonic), so very aggressive settings are softened rather than rejected.

### Presets
Built-in: **Balanced** (6500 K), **Night Light** (3400 K), **Paper Read** (4600 K warm sepia), **OLED Vivid** (extra contrast/gamma), **Gaming FPS** (shadow-boosted gamma), **Cool Daylight** (8500 K).
You can also save the current settings as named **custom presets**, which appear in the window and in the tray menu.

### Solar Sync (circadian mode)
Every 20 seconds the app computes the sun's elevation for your location using NOAA solar-position formulas and blends between your **day** and **night** limits:
- Sun ≥ +20° → full day values; sun ≤ −12° → full night values; smooth cosine curve in between.
- Brightness always follows the sun; **color warmth is optional** ("Adapt Color Warmth" toggle).
- Configurable limits: day brightness 50–100%, night floor 10–85%, day color 4500–8000 K, night warmth 2000–4500 K.
- Moving a slider manually pauses Solar Sync ("manual override"); **↺ Re-Sync Sun** resumes it.

### Multi-monitor
- "Sync All" applies settings to every display; "Separate" lets you target one display.
- Settings are saved per display, matched by device name (`\\.\DISPLAY1`) or monitor model name, so they survive re-plugging.
- Monitor plug/unplug, resolution changes and session unlock trigger a re-scan.

### Tray integration
- **Left-click** the tray icon: open/close the control window (docked next to the taskbar, DPI-aware, works with taskbar on any edge).
- **Right-click**: Solar Sync toggle, quick brightness (25/50/75/100%), color presets, open controls, reset to default, start with Windows, exit.
- The window hides when it loses focus unless pinned. Closing it only hides it; use **Exit** in the tray menu to quit.
- Single instance: launching it again just brings up the existing window.

---

## How it works

| File | Responsibility |
|---|---|
| `App.xaml.cs` | Startup, single-instance mutex + wake-up broadcast message, command-line args, logging to `app.log` (trimmed when > 512 KB). |
| `MainWindow.xaml(.cs)` | The UI (tabs: Brightness, Color, Presets, Displays) and all orchestration: applying settings, Solar Sync loop, presets, and the "keep calibration applied" machinery. |
| `MonitorEngine.cs` | Display enumeration, DDC/CI brightness, gamma-ramp generation, Kelvin→RGB, built-in presets. |
| `SolarEngine.cs` | Sun elevation, sunrise/sunset, phase names, day/night blending. |
| `ConfigManager.cs` | Load/save settings JSON (validated and clamped on load, written atomically via temp file + replace), Run-at-startup registry key, migration from the old `LuminaDisplay` settings folder. |
| `TrayManager.cs` | Tray icon, dark context menu, flyout positioning. |
| `HdrHelper.cs` | Windows HDR detection via the DisplayConfig API. |
| `ColorFilterDimmer.cs` | Full-screen dimming through the Magnification API color effect, used for software brightness in HDR. |
### Keeping the calibration applied
Windows, GPU drivers, fullscreen games, Alt+Tab and the Start menu can silently reset the gamma ramp. For any display with non-default settings the app checks the ramp:
- every **1.5 s** (background-priority watchdog);
- in a short burst (5 × 35 ms) after foreground-window changes and shell events;
- after display changes and session unlock.

Each check **reads** the GPU ramp and compares it with the last one the app wrote; it only **writes** again if something reset it. Writing the ramp can cause a visible frame hitch on some GPUs/VRR setups, so it's avoided when nothing changed. There are no global mouse or keyboard hooks.

---

## Settings & files

| Mode | Settings | Log |
|---|---|---|
| Installed (default) | `%LOCALAPPDATA%\Displicare\settings.json` | `%LOCALAPPDATA%\Displicare\app.log` |
| Portable | `settings.json` next to the exe | `app.log` next to the exe |

Portable mode is active if you pass `--portable`, or if `portable.dat` or `settings.json` exists next to the exe.

**Start with Windows** writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Displicare` = `"<exe>"`, plus `--minimized` when "Start minimized to system tray" is on and `--portable` in portable mode.

### Command-line options
| Option | Effect |
|---|---|
| `--minimized`, `-m` | Start hidden in the tray |
| `--portable`, `-p` | Force portable mode |

---

## Building

Requirements: .NET Framework 4.8 developer pack / MSBuild, and [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer.

Plain build:
```bash
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe Displicare.csproj /p:Configuration=Release /p:Platform=x64
```
Output: `bin\Release\Displicare.exe`.

Full release (bumps version in `AssemblyInfo.cs` and `installer.iss`, builds, creates the portable zip and installer, writes SHA256 sums and appends to `dist\VERSIONS.md`):
```bash
powershell -ExecutionPolicy Bypass -File build-release.ps1 -Version 2.0.5 -Notes "What changed"
```
Each release goes to `dist\v<version>\` and contains exactly the installer, the portable zip and `SHA256SUMS.txt`. An existing version folder is not overwritten unless you pass `-Force`. Both scripts find the project folder themselves, and `ISCC.exe` is looked up in the standard Inno Setup 6 install locations.

The installer installs per-user to `%LOCALAPPDATA%\Programs\Displicare` (no admin needed), with optional desktop icon and start-with-Windows tasks.

---

## Known limitations

- **No real location detection.** With "auto location" on (the default), latitude/longitude are *guessed from your time zone* — e.g. any "Turkey" zone → Istanbul, "Eastern" → New York, "Central" (non-Europe) → Chicago, otherwise longitude from UTC offset and latitude 40°. There is no UI to enter coordinates; to set them, close the app, edit `UserLatitude` / `UserLongitude` in `settings.json` and set `AutoLocation` to `false`.
- **Software dimming floor is ~50%** on non-DDC/CI displays.
- **Conflicts with other gamma tools.** While it has non-default settings active, Displicare restores its own ramp whenever another tool (f.lux, Windows Night Light, vendor calibration, some HDR/ICC setups) changes it.
- **Portable mode isn't entirely registry-free**: enabling "Start with Windows" writes the `HKCU\...\Run` value.
- Turning Solar Sync off returns all displays to your last manual brightness and color temperature (applied to every display, since Solar Sync drives every display).
- "Restore default gamma on app exit" is off by default, so quitting leaves the last ramp in place until Windows resets it (sign-out, display change, etc.).
