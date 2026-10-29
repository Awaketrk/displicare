# Roadmap

Ideas for future versions, roughly by value. Nothing here is promised; suggestions and pull requests are welcome in [issues](https://github.com/Awaketrk/displicare/issues).

## UX ideas

1. **One way to pick "all displays" vs one.** The "Sync All / Separate" toggle and the "All displays" entry in the target list mean the same thing and switch each other. Keep one, or show a slider per monitor in the flyout (each card with its own brightness), with a link icon to tie them together.
2. **Reset All asks first, or offers Undo.** It sits right next to Minimize, resets brightness, colour and gamma on every screen and saves at once. A "Reset? Undo" note for a few seconds would be gentler than a dialog.
3. **Solar Sync resumes by itself.** A manual slider move stops it until "Resume" is clicked. Resuming at the next sunrise or sunset (or after a set time) would match what people expect from Night Light.
4. **Location in the UI.** Today exact coordinates need a hand edit of `settings.json`. A latitude/longitude field (or a city search with a small built-in list) in the Solar schedule panel.
5. **Per-monitor software floor.** The 30% (SDR) and 8% (HDR) floors were tuned on one QD-OLED monitor; other screens may want lower or higher. A "lowest brightness" slider on each display card in the Displays tab.
6. **Per-app profiles.** The foreground hook already knows which app is in front: optional "when this app is in front, use this preset" (e.g. Gaming FPS for a game, Paper Read for a reader). Reading the foreground window only, no injection, so it's safe with anti-cheat.
7. **Follow the Windows theme.** The window is always dark purple. Following light/dark mode and the accent colour would make it look native on Windows 11.
8. **Update check on the About card.** An opt-in "Check for updates" that reads the latest GitHub release and links to it.
9. **Settings export/import.** A button pair on the About card for moving calibrations and rooms to a new PC or reinstall.
10. **First-run hint for DDC/CI.** When a monitor doesn't answer DDC/CI, the card could say "Turn on DDC/CI in the monitor's menu for real backlight control" right there, not only in the README.

Done: brightness from the tray icon's mouse wheel and optional hotkeys (2.4.5).

## Code health

- `SaveRampState` writes the 3 KB ramp file on every slider tick while a software-dimmed display is dragged (for crash safety). It works; debouncing it to the end of a drag would save disk writes, but a crash mid-drag would then restore an older ramp.
- `MainWindow` still builds the display cards in code with hard-coded colours. Moving the colours to resources in `MainWindow.xaml` would make a theme change (idea 7) much easier.
- Many `catch { }` blocks swallow errors silently around Win32 calls. Logging the rare ones (gamma write refused, DDC/CI write failed) would make user reports easier to read.
