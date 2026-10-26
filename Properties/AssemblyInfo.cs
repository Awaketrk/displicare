using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: AssemblyTitle("Displicare")]
[assembly: AssemblyDescription("Modern Display Calibration, Color Temperature & Solar Circadian Studio")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Displicare Studio")]
[assembly: AssemblyProduct("Displicare")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

// Lets the test project reach internal helpers (ramp math, settings parsing) without making them public
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Displicare.Tests")]

// Resolve P/Invoke DLLs (dxva2, shcore, ...) from System32 only, so a planted DLL next to a portable exe can't be loaded
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]

[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]
// Version shown in the app (About card, header, tray). build-release.ps1 sets it to the release version;
// a "-test" suffix marks builds that didn't come from the release script.
[assembly: AssemblyInformationalVersion("2.3.1-test")]


















