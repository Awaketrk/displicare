# Lumina Display - Version Release Catalog

This catalog maintains a permanent history of all official Lumina Display releases. Every released version is preserved in its own dedicated dist/v<Version>/ folder to ensure earlier builds are never lost.

---
### Version 1.0 Release
- **Release Date**: 2026-10-22 18:31:11
- **Assembly Version**: 1.0.0.0
- **Packages in dist/v1.0/**:
  - **Setup Installer**: `LuminaDisplay-v1.0-Setup.exe` (2.18 MB)
    - SHA256: `419A1A3BD8297156F069E2CD024C9F15B51C1174CB19528764FA634BAD8160FC`
  - **Portable Package**: `LuminaDisplay-v1.0-Portable.zip` (207.1 KB)
    - SHA256: `1940E73BEA8BEA77E7BC971820059F05A5CDF872E29FF5D6E2C7B53672927F9A`
- **Notes**: Official 1.0 Release. Complete display brightness, Planckian color temperature (2500K-10000K), DDC/CI hardware control, 0% flicker GPU layered software dimmer, astronomical solar circadian sync, Win32 foreground hook persistent across Alt+Tab, and multi-monitor synchronization.


### Version 1.0.1 Release
- **Release Date**: 2026-10-22 18:56:09
- **Assembly Version**: 1.0.1.0
- **Packages in dist/v1.0.1/**:
  - **Setup Installer**: `LuminaDisplay-v1.0.1-Setup.exe` (2.18 MB)
    - SHA256: `CDE6D13FC324D720BFFA1233B05DAF2B011D3EC69BD5FEC83346D8A927FC8A04`
  - **Portable Package**: `LuminaDisplay-v1.0.1-Portable.zip` (207.6 KB)
    - SHA256: `D9B49101EC6246FF1F96754746849D97EBFA140841F029B3503B1C34DAC47428`
- **Notes**: Fixed multi-monitor Per-Monitor DPI scaling and tray flyout positioning bug where the window could split or spill across display borders. Implemented dynamic screen detection from mouse cursor, native Per-Monitor DPI scaling query, and strict mathematical clamping to the target screen working area.

### Version 1.0.2 Release
- **Release Date**: 2026-10-22 19:09:36
- **Assembly Version**: 1.0.2.0
- **Packages in dist/v1.0.2/**:
  - **Setup Installer**: `LuminaDisplay-v1.0.2-Setup.exe` (2.18 MB)
    - SHA256: `08C3422A8CE26C01624BBEC6B388F487F592F8DF57F071A23557864EC1515E76`
  - **Portable Package**: `LuminaDisplay-v1.0.2-Portable.zip` (207.4 KB)
    - SHA256: `89740C00EB5F42F44617457A5C8AB40376D2DD28DEAF192E6E8B1B8DBA2A3045`
- **Notes**: Fixed Night Light and color temperature presets by correcting GDI32 SetDeviceGammaRamp flat marshaling; fixed Gaming FPS preset curve (Gamma 1.24, Contrast 1.0) to illuminate and lift shadows instead of darkening them.

### Version 1.0.3 Release
- **Release Date**: 2026-10-22 19:23:08
- **Assembly Version**: 1.0.3.0
- **Packages in dist/v1.0.3/**:
  - **Setup Installer**: `LuminaDisplay-v1.0.3-Setup.exe` (2.18 MB)
    - SHA256: `3F1FECD3BC1DA5BFB99AB2E1AF213F00B3D09443DDC0355A719F052931F1F245`
  - **Portable Package**: `LuminaDisplay-v1.0.3-Portable.zip` (208.1 KB)
    - SHA256: `E543B21CEC18B9B87DCF1CF40A191FEE58B5CC15CFA62C04D1BE901F6AB89694`
- **Notes**: Fixed Adapt Color Warmth toggle and Night Light presets by implementing Windows DWM hardware color transformation matrix via Magnification API, bypassing the hardcoded 32768 lower-bound heuristic clamping in GDI32 SetDeviceGammaRamp that rejected warm Kelvin color temperatures.

### Version 1.0.4 Release
- **Release Date**: 2026-10-22 19:32:37
- **Assembly Version**: 1.0.4.0
- **Packages in dist/v1.0.4/**:
  - **Setup Installer**: `LuminaDisplay-v1.0.4-Setup.exe` (2.18 MB)
    - SHA256: `3EC1ED9EA8D7DD48246570992CDBD65D07A11B8DBEA7893915977E1A0576FFE6`
  - **Portable Package**: `LuminaDisplay-v1.0.4-Portable.zip` (209.1 KB)
    - SHA256: `7E96BD6C16A0BE859F36C33B3138D3BB1F782C82FB4FFFE16298E1F46E9794A8`
- **Notes**: Comprehensive code quality, memory, and concurrency optimization: added _displaysLock collection synchronization to eliminate background queue race conditions, added _i2cBusLock hardware serialization for DDC/CI commands, implemented pre-allocated _gammaBuffer to eliminate GC allocations during slider drags, cached Magnification color matrix to eliminate redundant DWM P/Invoke calls, added PruneDisconnectedOverlays to prevent layered window leaks when monitors are disconnected, optimized watchdog timer interval from 2s to 5s, and guaranteed CleanExit during OS session shutdown.

### Version 1.0.5 Release
- **Release Date**: 2026-10-22 19:43:31
- **Assembly Version**: 1.0.5.0
- **Packages in dist/v1.0.5/**:
  - **Setup Installer**: `LuminaDisplay-v1.0.5-Setup.exe` (2.18 MB)
    - SHA256: `F3C16FABEE70C3C021D12C195FE1A1072E5D4C39171CB9CCD769C0B850BE09CF`
  - **Portable Package**: `LuminaDisplay-v1.0.5-Portable.zip` (209.2 KB)
    - SHA256: `A9740994C68B686D7EDE4138CD955CCB74E3584574D311B8212BE46419404237`
- **Notes**: Refined Night Light preset to clean reference warm amber (3400K, Contrast 1.0, Gamma 1.0, 100% RGB) eliminating gray washed-out blacks. Fixed gamma scaling in ApplyGammaRamp to scale proportionally over the display calibrated hardware baseline curves (OriginalRamp), eliminating the abrupt color jump/break when adjusting Gamma away from 1.00.

### Version 1.0.6 Release
- **Release Date**: 2026-10-22 21:04:33
- **Assembly Version**: 1.0.6.0
- **Packages in dist/v1.0.6/**:
  - **Setup Installer**: `LuminaDisplay-v1.0.6-Setup.exe` (2.18 MB)
    - SHA256: `A59C70FC18E2ED28CDF5A14B0E2985FB325555077D7506DEDFAB1FEE02B7E0C7`
  - **Portable Package**: `LuminaDisplay-v1.0.6-Portable.zip` (208.7 KB)
    - SHA256: `960B1296FFF0D1356D8F45C8062527EEC6E388D638AAE55476558C69B801DF0E`
- **Notes**: Fixed VRR (Variable Refresh Rate / G-Sync / FreeSync) compatibility in games by eliminating Windows Magnification API (MagInitialize) which was intercepting DWM composition and disabling Direct Flip / Hardware MPO. Replaced with pure hardware GPU DAC gamma ramps (SetDeviceGammaRamp) with identity deviation safety clamping and monotonic sequence enforcement, enabling 100% unrestricted G-Sync/VRR frame pacing alongside custom color warmth, contrast, and gamma.

### Version 1.0.7 Release
- **Release Date**: 2026-10-22 21:12:45
- **Assembly Version**: 1.0.7.0
- **Packages in dist/v1.0.7/**:
  - **Setup Installer**: `LuminaDisplay-v1.0.7-Setup.exe` (2.18 MB)
    - SHA256: `2A32650CFF7449ADA587274D7545AEFAF8E3338998950609AD6F44FCADA5FEC0`
  - **Portable Package**: `LuminaDisplay-v1.0.7-Portable.zip` (209 KB)
    - SHA256: `B7D8D3A9FF14AA88C764CB93E70B72F64853502351FFA7D80DBF6979B32AB810`
- **Notes**: Fixed red-tint compounding bug and reset unresponsiveness: implemented baseline validation (IsNeutralBaseline) in CaptureOriginalGammaRamp to reject pre-tinted hardware LUTs, ensured ResetGammaRamp unconditionally restores true linear sRGB reference ramp, and enabled manual override in ResetAll to prevent SolarSync from immediately overwriting user resets.

### Version 1.0.8 Release
- **Release Date**: 2026-10-23 02:53:29
- **Assembly Version**: 1.0.8.0
- **Packages in dist/v1.0.8/**:
  - **Setup Installer**: `LuminaDisplay-v1.0.8-Setup.exe` (2.19 MB)
    - SHA256: `3EA5391303E581FB555690AEE6F97EE1BE84B7830C13A3FEF4C7452551608041`
  - **Portable Package**: `LuminaDisplay-v1.0.8-Portable.zip` (212 KB)
    - SHA256: `C648ACBBAEEB01322D1E4A6FD0168318E430F358040192009BE1083F8E054EE3`
- **Notes**: Resolved Solar Circadian Sync multi-monitor issue when displays are calibrated separately: preserved independent RGB gains, contrast, and gamma offsets across all connected displays during solar transitions; eliminated monitor calibration resets during display re-enumeration and topology updates; added per-monitor profile persistence (DisplayProfiles); introduced Room Lighting Mode (Lit Room floor >= 55%) for comfortable display readability under indoor lamp/bulb illumination; replaced the window Exit button with Minimize (minimizes to system tray) while preserving clean termination via tray right-click.

### Version 1.0.9 Release
- **Release Date**: 2026-10-23 03:01:41
- **Assembly Version**: 1.0.9.0
- **Packages in dist/v1.0.9/**:
  - **Setup Installer**: `LuminaDisplay-v1.0.9-Setup.exe` (2.19 MB)
    - SHA256: `3B39CD76EEC92E21C403A827F0E78C042E49A67B49189873D1A758796FF7C91F`
  - **Portable Package**: `LuminaDisplay-v1.0.9-Portable.zip` (211.2 KB)
    - SHA256: `03DAFCD3551FE7DA8D04455722F41FAF02A8C3921460886CA08AC2DFFC1269CA`
- **Notes**: Removed redundant Room Lighting Mode toggle in favor of user-customizable Night Floor slider (range expanded to 10%-80%), allowing users in illuminated rooms or dark environments to directly and intuitively set their exact preferred nighttime brightness floor without hardcoded limits or separate toggles.

### Version 1.1.0 Release
- **Release Date**: 2026-10-23 03:12:50
- **Assembly Version**: 1.1.0.0
- **Packages in dist/v1.1.0/**:
  - **Setup Installer**: `LuminaDisplay-v1.1.0-Setup.exe` (2.19 MB)
    - SHA256: `6065F7447CA09A35985CC0B56C8C7EC8A2B1C7247B6B4A4A8223370D7DDDFC97`
  - **Portable Package**: `LuminaDisplay-v1.1.0-Portable.zip` (215.3 KB)
    - SHA256: `F38E816CE58C895E50B8791B12FBE2E6ED5BBBC65DF3146E889B1DC65077BA06`
- **Notes**: Introduced Custom Preset Studio (replacing legacy Windows ICC file list): users can now save any custom combination of Brightness, Color Warmth (Kelvin), RGB Channel Gains, Contrast, and Gamma with custom names; presets feature fast 1-click application and inline deletion, full persistence across app restarts in settings.json, and automatic synchronization with the Windows system tray context menu.

### Version 2.0.0 Release
- **Release Date**: 2026-10-23 03:34:55
- **Assembly Version**: 2.0.0.0
- **Packages in dist/v2.0.0/**:
  - **Setup Installer**: `Displicare-v2.0.0-Setup.exe` (3 MB)
    - SHA256: `A081B6FF8995984F5127C285A75D1DA2F6B50BB73403A03800329826EBDB6995`
  - **Portable Package**: `Displicare-v2.0.0-Portable.zip` (907.3 KB)
    - SHA256: `B85AA3FF79D7327FFA2EA08286A34004BD8BBB020BEFF4BC1543432AD9DE977D`
- **Notes**: Official Displicare 2.0 release: rebranded to Displicare with brand new modern 3D glassmorphic squircle logo and high-resolution multi-size app icon; migrated settings seamlessly.

### Version 2.0.1 Release
- **Release Date**: 2026-10-24 04:37:08
- **Assembly Version**: 2.0.1.0
- **Packages in dist/v2.0.1/**:
  - **Setup Installer**: `Displicare-v2.0.1-Setup.exe` (3 MB)
    - SHA256: `CC20A4EBD3288406BC681706F1DA7DAFF27861B30B8ACB260174112120365924`
  - **Portable Package**: `Displicare-v2.0.1-Portable.zip` (908.5 KB)
    - SHA256: `A9D799DE27020DB8861F42725BA3C0ED972972868169233C6591DC44CB82BEEB`
- **Notes**: Fixed software calibrator and dimmer losing settings when opening Windows Start Menu: added RegisterShellHookWindow for shell notifications, low-level mouse and keyboard hooks for instant Start button / Win key detection, multi-stage burst reassertion across shell animations, tightened background watchdog interval to 800ms, and hardened dimmer overlay Z-order with SWP_SHOWWINDOW.

### Version 2.0.2 Release
- **Release Date**: 2026-10-24 04:56:51
- **Assembly Version**: 2.0.2.0
- **Packages in dist/v2.0.2/**:
  - **Setup Installer**: `Displicare-v2.0.2-Setup.exe` (3 MB)
    - SHA256: `6BEFD826DD777C9469801429F17D66FAACCC7087961FAA662264F5D4178C5BF1`
  - **Portable Package**: `Displicare-v2.0.2-Portable.zip` (908.9 KB)
    - SHA256: `45FE48A4EBC0B361BDCFD303D9EF0A3A710D0B89448A134E95CA0064743D1F3F`
- **Notes**: Renamed Sync Both to Sync All. Added high-frequency dismiss polling and multi-layered WinEvent & low-level input hooks for 0ms calibration recovery across Windows Start Menu transitions.

### Version 2.0.3 Release
- **Release Date**: 2026-10-24 05:08:44
- **Assembly Version**: 2.0.3.0
- **Packages in dist/v2.0.3/**:
  - **Setup Installer**: `Displicare-v2.0.3-Setup.exe` (3 MB)
    - SHA256: `C21C11424C1532B68211800F34858EE25E397C479A233D87D58576370A7DEFC4`
  - **Portable Package**: `Displicare-v2.0.3-Portable.zip` (909.3 KB)
    - SHA256: `06D337C93CD61CBD5A7CFB0FB6DCB24477A4202E4C16D3D5394189C4363EF418`
- **Notes**: Calibrated software dimmer luminance curve to match physical monitor backlight scaling (CIE L* curve), added multi-head physical monitor DDC/CI discovery, and added hardware setup guidance.

### Version 2.0.4 Release
- **Release Date**: 2026-10-24 05:25:16
- **Assembly Version**: 2.0.4.0
- **Packages in dist/v2.0.4/**:
  - **Setup Installer**: `Displicare-v2.0.4-Setup.exe` (3 MB)
    - SHA256: `79605F7AF71F5EE16534A62F8006533DE662D86B15148E6092D9615358323D4C`
  - **Portable Package**: `Displicare-v2.0.4-Portable.zip` (909.3 KB)
    - SHA256: `DB808813F1EE1C187E922E6500D447ECCDBA74413C652619A4CAA83FD9E4F5AD`
- **Notes**: Replaced transparent overlay window with True GPU Hardware-LUT Scanout Software Brightness. The mouse pointer, taskbar previews, and all Windows elements are now 100% uniformly dimmed via GPU video lookup tables with zero overlay window, matching physical monitor backlight range.

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