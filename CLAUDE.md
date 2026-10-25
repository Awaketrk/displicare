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
