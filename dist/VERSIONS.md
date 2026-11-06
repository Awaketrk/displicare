# Displicare - Version Release Catalog

Every Displicare release with its packages and SHA256 checksums. The packages themselves are on the [Releases page](https://github.com/Awaketrk/displicare/releases).

Versions before 2.1.0 were private test builds (1.x under the earlier name Lumina Display) and were never published.

---
### Version 2.1.0 Release
- **Release Date**: 2026-10-24 22:47:26
- **Assembly Version**: 2.1.0.0
- **Packages in dist/v2.1.0/**:
  - **Setup Installer**: `Displicare-v2.1.0-Setup.exe` (2.06 MB)
    - SHA256: `543211B0A62B08BC499AA451E4B6E93278C1044C2F6ED82FE2B6189D6D6D163A`
  - **Portable Package**: `Displicare-v2.1.0-Portable.zip` (117.5 KB)
    - SHA256: `AD3D2A75D7928332B05BADD340C4EA761EA07C583F08F487351AA65CEAC08C32`
- **Notes**: Dusk redesign and new logo; bug, security and memory-leak fixes; no global input hooks; read-before-write gamma watchdog; software brightness reaches full white and matches hardware more closely; HDR software dimming via Windows color filter; About card with version info.

### Version 2.1.1 Release
- **Release Date**: 2026-10-25 04:44:23
- **Assembly Version**: 2.1.1.0
- **Packages in dist/v2.1.1/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.1.1-Setup.exe` (2.06 MB)
    - SHA256: `0ECDD16B5356E4F420A155AE0BC6349FB74110FC50B86ED318B2892B7D18B852`
  - **Portable Package**: `Displicare-v2.1.1-Portable.zip` (119.2 KB)
    - SHA256: `FF0E0A6D5D899E9A499AB78D333BF0103B940FCBE6957D330F2FD9140D0A5073`
- **Notes**: Color temperature and RGB through the color filter in HDR; fixes for settings files with missing fields and for oversized preset lists; installer links point to the GitHub repo; unit tests and GitHub Actions releases.

### Version 2.2.0 Release
- **Release Date**: 2026-10-25 14:25:28
- **Assembly Version**: 2.2.0.0
- **Packages in dist/v2.2.0/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.2.0-Setup.exe` (2.07 MB)
    - SHA256: `D85F958230CC46255111FD3DD53E51BA2599A1832704C8A5E87F0F26FE15254E`
  - **Portable Package**: `Displicare-v2.2.0-Portable.zip` (132.6 KB)
    - SHA256: `C80A88D54533AE292B3E76FD7E984E22E1BE06268E8B5122BF1293017248361D`
- **Notes**: In-app hardware test (About card or `--hardware-test`) that checks the gamma ramp, HDR color filter and DDC/CI backlight and saves a report; fix for a contrast/gamma curve left by a crash being kept as the monitor's baseline.

### Version 2.3.0 Release
- **Release Date**: 2026-10-26 18:13:31
- **Assembly Version**: 2.3.0.0
- **Packages in dist/v2.3.0/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.3.0-Setup.exe` (2.10 MB)
    - SHA256: `4A24EDF6556FA2F2E7A8A677E2A3718E8C5DE0512D5FEBBE1E444190E88DF37C`
  - **Portable Package**: `Displicare-v2.3.0-Portable.zip` (173.1 KB)
    - SHA256: `DEAD55B1E7464D532435B3A543A7B310A906ED99BA9F13C16442BF941F696005`
- **Notes**: Optional room switching (off by default): every connected screen becomes a room that can be made the only display with its native resolution, highest refresh, HDR and its own sound, with a keep-this-layout prompt, hotkeys, a Rooms window and `--room`; hardware test recovery file renamed so it can't be mistaken for a report.
### Version 2.3.1 Release
- **Release Date**: 2026-10-26 21:51:13
- **Assembly Version**: 2.3.1.0
- **Packages in dist/v2.3.1/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.3.1-Setup.exe` (2.10 MB)
    - SHA256: `66823C2EF696D3A74285D80F8600624A955E7EEDA6AB0DF6CE671ED5AF23876E`
  - **Portable Package**: `Displicare-v2.3.1-Portable.zip` (175.8 KB)
    - SHA256: `57428F127227811AE122D2D1EC8D9C0CCB2E44D121DC1713DBC81E6262308801`
- **Notes**: DDC/CI monitors that answer late no longer fall back to software brightness (background retries, one-time restore of displays switched off by the bug); backlight set in the monitor's own DDC/CI range; room command pipe limited to the current account with timeouts; faster foreground-window checks.

### Version 2.4.0 Release
- **Release Date**: 2026-10-27 03:13:51
- **Assembly Version**: 2.4.0.0
- **Packages in dist/v2.4.0/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.4.0-Setup.exe` (2.11 MB)
    - SHA256: `7C8CA270A5796C180CEDAEA89F0E3502F41EDC9E303243EB478B7009F6527574`
  - **Portable Package**: `Displicare-v2.4.0-Portable.zip` (186.0 KB)
    - SHA256: `FA0D431CC507AEAA71A97ED74E246D207BB94591068793D47D2463A8E655A56E`
- **Notes**: Controller room switching (View + a D-pad direction goes straight to a room, View + Menu opens a picker, A/B answer the keep prompt; XInput only, safe with anti-cheat); physical screens switched off while a stream uses a virtual display and restored afterwards with all windows minimized; virtual displays recognised by their software adapter and kept out of brightness and color; a note on screen after each switch; taskbar auto-hide kept through layout changes; tray window no longer shrinks after visiting a screen with other scaling.

### Version 2.4.1 Release
- **Release Date**: 2026-10-27 03:39:09
- **Assembly Version**: 2.4.1.0
- **Packages in dist/v2.4.1/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.4.1-Setup.exe` (2.11 MB)
    - SHA256: `EED671D95AF1A6438E078FD2D513FB4D82F8B9B9467F75217A2B60ADD46882DD`
  - **Portable Package**: `Displicare-v2.4.1-Portable.zip` (187.2 KB)
    - SHA256: `5D2EB388440E06AB7123DF9FC82A9C48C33332AB0B43DFABBE107E8E289F338A`
- **Notes**: Per-room "Open on switch" choice: nothing, Steam Big Picture or Windows 11's Xbox mode (opened and closed with its Win + F11 shortcut after checking its state); older Big Picture settings carried over. Xbox mode keeps the controller to itself, so View + D-pad can't leave it.

### Version 2.4.2 Release
- **Release Date**: 2026-10-27 03:52:32
- **Assembly Version**: 2.4.2.0
- **Packages in dist/v2.4.2/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.4.2-Setup.exe` (2.11 MB)
    - SHA256: `50EF1C1BF4710382818635525DAD4BAD60A0BE92590D4D15131229918743E08F`
  - **Portable Package**: `Displicare-v2.4.2-Portable.zip` (187.6 KB)
    - SHA256: `6007553EEC58C070EC9E57C7D4EBAF513B57A4AAFDBF86947EDF14106FF569DB`
- **Notes**: Leaving a Big Picture or Xbox mode room for one without a launcher minimizes every window once Steam's (or the Xbox app's) window is back, so nothing sits still on an OLED.

### Version 2.4.3 Release
- **Release Date**: 2026-10-27 04:24:18
- **Assembly Version**: 2.4.3.0
- **Packages in dist/v2.4.3/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.4.3-Setup.exe` (2.11 MB)
    - SHA256: `806158F8CE2452822874081D541D90F9F039AE52C5618FF46B4210F06DA54B61`
  - **Portable Package**: `Displicare-v2.4.3-Portable.zip` (188.3 KB)
    - SHA256: `B44CA7AB73E928B476FF6CC20E3577EB96E5999FAD492AF7A4D91BACA1BAE0E7`
- **Notes**: Screens keep their own name and calibration when room switching moves them between graphics card outputs: the active monitor on each output is used, named from its EDID and keyed by model; saved calibrations are matched by model, then name, and re-applied after display changes.

### Version 2.4.4 Release
- **Release Date**: 2026-10-27 16:28:41
- **Assembly Version**: 2.4.4.0
- **Packages in dist/v2.4.4/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.4.4-Setup.exe` (2.11 MB)
    - SHA256: `E4C1860A7D9A319109DCF41438AC4A77E015DF485E8EBC3ECBFE11DCDA49ED2C`
  - **Portable Package**: `Displicare-v2.4.4-Portable.zip` (188.5 KB)
    - SHA256: `F7CBE4417B5F7275DC6A34A72C566718AA990BCA1DB16C9E9DF06358A46F95D9`
- **Notes**: Taskbar auto-hide is turned off while a stream runs and back on after it, so screens Windows brings back on its own no longer trigger Explorer's "toolbar already hidden" error; after a stream, windows are minimized once the screens and Steam's window are back.

### Version 2.4.5 Release
- **Release Date**: 2026-10-28 02:37:02
- **Assembly Version**: 2.4.5.0
- **Packages in dist/v2.4.5/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.4.5-Setup.exe` (2.12 MB)
    - SHA256: `C143300E4C21F624E35427D93B46ECBAC25AE244DB992B1E88528623F520FFB7`
  - **Portable Package**: `Displicare-v2.4.5-Portable.zip` (192.1 KB)
    - SHA256: `6BBC3131DED5666DECF806F9A4282450D6A7182DED4DAE39E90657907155BEF1`
- **Notes**: Brightness from the mouse wheel over the tray icon (5% steps) and optional Ctrl+Alt+Up / Down hotkeys (10% steps), with a level bar above the taskbar; fewer gamma checks from shell events; Reset All no longer blocks the window; dated log lines.

### Version 2.5.0 Release
- **Release Date**: 2026-11-02 15:41:25
- **Assembly Version**: 2.5.0.0
- **Packages in dist/v2.5.0/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.5.0-Setup.exe` (2.13 MB)
    - SHA256: `FC7B9CB7041761D642CF7DD57712B42A28CB9C02A4D0A246E112D45E882F20AD`
  - **Portable Package**: `Displicare-v2.5.0-Portable.zip` (209.0 KB)
    - SHA256: `313EAFD6BD108DF786D88CC32CF73C799837454269034084D2FFA5136FF70226`
- **Notes**: Room switching becomes display setups: any combination of screens with their places, resolution, refresh rate, HDR and sound, edited like DisplayFusion's monitor profiles (drag and snap, per-screen modes per setup); rooms carry over; the way back (Ctrl+Alt+0 or View + B held 4 s) goes to the safe setup without asking, closes launchers and falls back to every connected screen.

### Version 2.5.1 Release
- **Release Date**: 2026-11-03 03:01:50
- **Assembly Version**: 2.5.1.0
- **Packages in dist/v2.5.1/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.5.1-Setup.exe` (2.13 MB)
    - SHA256: `B19978EFA76D084632DC52E66D80AF8C51582A611D4A243129D4E4C4496CF052`
  - **Portable Package**: `Displicare-v2.5.1-Portable.zip` (211.5 KB)
    - SHA256: `0635F55628013898168FA6053028A363D588A57FF84AD0A200151433F806446F`
- **Notes**: Games keep VRR: the Windows color filter is gone and HDR tint and software dimming use a PQ-aware gamma ramp (down to 8% light); SDR screens with Auto Color Management are no longer treated as HDR; SDR software dimming follows the hardware curve (down to 22%); HDR on/off is caught when Windows reports it late; optional Deeper software dimming lifts Windows' gamma limit (GdiIcmGammaRange) and puts it back when turned off or on uninstall.

### Version 2.5.2 Release
- **Release Date**: 2026-11-03 03:15:32
- **Assembly Version**: 2.5.2.0
- **Packages in dist/v2.5.2/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.5.2-Setup.exe` (2.13 MB)
    - SHA256: `362DE415E388E76B174265DC631D6F641C0E3E366C49D48E398DAD28AEBC4533`
  - **Portable Package**: `Displicare-v2.5.2-Portable.zip` (212.7 KB)
    - SHA256: `4234DA822DAE68A993AB54ACC5E5426D195D20FB52BB0645157289866D00FCC5`
- **Notes**: The uninstaller closes a running Displicare first (gamma put back, settings saved), then puts back the Deeper software dimming setting and removes the files; a second start opens the running copy (the wake-up message now goes straight to its window).

### Version 2.5.3 Release
- **Release Date**: 2026-11-06 15:32:42
- **Assembly Version**: 2.5.3.0
- **Packages in dist/v2.5.3/** (built by GitHub Actions, also on the GitHub Release):
  - **Setup Installer**: `Displicare-v2.5.3-Setup.exe` (2.14 MB)
    - SHA256: `02DC2C3328B9C5204DD7535AEA36DC8DF89641624AB5B1A81B83DC5656098180`
  - **Portable Package**: `Displicare-v2.5.3-Portable.zip` (216.9 KB)
    - SHA256: `7BE04308DE60E8599A19273B3879BBF06B71AFD1FFDE0E73E04ED722F5E4B0CA`
- **Notes**: A Solar Sync pause ends at the next sun phase and survives a restart; your colors are kept apart from the sun's and come back when warmth sync or Solar Sync turns off; the active preset is highlighted, and presets lock under warmth sync; a monitor that answered DDC/CI stays on its backlight through HDMI handshakes instead of stacking software dimming; sync off keeps the selected screen after a restart; default brightness is 100; Reset All asks first; uninstalling removes the startup entry.
