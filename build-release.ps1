<#
.SYNOPSIS
    Builds, packages, and archives a versioned release of Displicare.
.DESCRIPTION
    Automates updating assembly and installer version metadata, builds the Release binary
    with MSBuild, creates a zero-dependency clean portable package, compiles the Inno Setup
    installer, generates SHA256 checksums, and safely archives the release in dist/v<Version>/
    so earlier versions are permanently preserved.
.PARAMETER Version
    The version tag to build (e.g. "2.0.0", or "2.4.6-test.1" for a test build).
.PARAMETER Force
    Overwrites an existing version archive if it already exists.
.EXAMPLE
    .\build-release.ps1 -Version "2.0.0"
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $false)]
    [string]$Version = "2.0.0",

    [Parameter(Mandatory = $false)]
    [switch]$Force,

    [Parameter(Mandatory = $false)]
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$msbuildPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$isccPath = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $isccPath) {
    Write-Error "Inno Setup 6 (ISCC.exe) not found. Install it from https://jrsoftware.org/isinfo.php"
    exit 1
}

# Clean up version string
$cleanVersion = $Version.Trim().TrimStart('v', 'V')
if ([string]::IsNullOrWhiteSpace($cleanVersion)) {
    Write-Error "Invalid version specified."
    exit 1
}

# A pre-release such as 2.4.6-test.1: the numbers go into the assembly version, the whole text into
# the file names and the version the app shows
$label = ""
$dash = $cleanVersion.IndexOf('-')
if ($dash -gt 0) {
    $label = $cleanVersion.Substring($dash)
    $cleanVersion = $cleanVersion.Substring(0, $dash)
}

# Determine display version and 4-part assembly version
$parts = $cleanVersion.Split('.')
$major = if ($parts.Length -gt 0) { $parts[0] } else { "2" }
$minor = if ($parts.Length -gt 1) { $parts[1] } else { "0" }
$build = if ($parts.Length -gt 2) { $parts[2] } else { "0" }
$revision = if ($parts.Length -gt 3) { $parts[3] } else { "0" }

$assemblyVer = "$major.$minor.$build.$revision"
$displayVer = $(if ($parts.Length -le 2) { "$major.$minor" } else { "$major.$minor.$build" }) + $label

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Displicare Release Builder" -ForegroundColor Cyan
Write-Host "   Target Version: v$displayVer (Assembly: $assemblyVer)" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

$distDir = Join-Path $root "dist"
$versionDir = Join-Path $distDir "v$displayVer"

if ((Test-Path $versionDir) -and -not $Force) {
    Write-Warning "Directory '$versionDir' already exists! Earlier builds are protected from accidental overwrite."
    Write-Warning "Use -Force if you explicitly want to re-package v$displayVer, or specify a new version."
    exit 1
}

# 1. Update Properties\AssemblyInfo.cs
Write-Host "[1/6] Updating AssemblyInfo.cs version to $assemblyVer..." -ForegroundColor Green
$assemblyInfoPath = Join-Path $root "Properties\AssemblyInfo.cs"
if (Test-Path $assemblyInfoPath) {
    # Read as UTF-8 explicitly: PS 5.1 defaults to ANSI here, which mangled the © sign on every release
    $content = Get-Content $assemblyInfoPath -Raw -Encoding UTF8
    $content = [System.Text.RegularExpressions.Regex]::Replace($content, '\[assembly:\s*AssemblyVersion\("[^"]+"\)', "[assembly: AssemblyVersion(`"$assemblyVer`")")
    $content = [System.Text.RegularExpressions.Regex]::Replace($content, '\[assembly:\s*AssemblyFileVersion\("[^"]+"\)', "[assembly: AssemblyFileVersion(`"$assemblyVer`")")
    $content = [System.Text.RegularExpressions.Regex]::Replace($content, '\[assembly:\s*AssemblyInformationalVersion\("[^"]+"\)', "[assembly: AssemblyInformationalVersion(`"$displayVer`")")
    Set-Content -Path $assemblyInfoPath -Value $content -Encoding UTF8
}

# 2. Update installer.iss
Write-Host "[2/6] Updating Inno Setup installer metadata to v$displayVer..." -ForegroundColor Green
$installerPath = Join-Path $root "installer.iss"
if (Test-Path $installerPath) {
    $issContent = Get-Content $installerPath -Raw -Encoding UTF8
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, '#define\s+MyAppVersion\s+"[^"]+"', "#define MyAppVersion `"$displayVer`"")
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, 'OutputBaseFilename=Displicare-v[^\r\n]+', "OutputBaseFilename=Displicare-v$displayVer-Setup")
    $issContent = [System.Text.RegularExpressions.Regex]::Replace($issContent, 'VersionInfoVersion=[^\r\n]+', "VersionInfoVersion=$assemblyVer")
    Set-Content -Path $installerPath -Value $issContent -Encoding UTF8
}

# 3. Run the unit tests first, then compile the release
Write-Host "      Running unit tests..." -ForegroundColor Green
& $msbuildPath (Join-Path $root "Tests\Displicare.Tests.csproj") /p:Configuration=Release /p:Platform=x64 /verbosity:quiet /nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Test project failed to build"
    exit $LASTEXITCODE
}
& (Join-Path $root "Tests\bin\Release\Displicare.Tests.exe")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Unit tests failed - not building a release"
    exit $LASTEXITCODE
}

Write-Host "[3/6] Compiling Displicare (Release x64)..." -ForegroundColor Green
& $msbuildPath (Join-Path $root "Displicare.csproj") /p:Configuration=Release /p:Platform=x64 /t:Rebuild /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "MSBuild compilation failed with code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 4. Prepare Versioned Release Directory
Write-Host "[4/6] Creating release archive at dist\v$displayVer..." -ForegroundColor Green
if (!(Test-Path $versionDir)) {
    New-Item -ItemType Directory -Path $versionDir -Force | Out-Null
}

# Stage the portable folder in temp; only the zip is kept in the release folder
$portableDir = Join-Path $env:TEMP "Displicare-v$displayVer-Portable"
if (Test-Path $portableDir) { Remove-Item $portableDir -Recurse -Force }
New-Item -ItemType Directory -Path $portableDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $portableDir "Assets") -Force | Out-Null

Copy-Item (Join-Path $root "bin\Release\Displicare.exe") (Join-Path $portableDir "Displicare.exe") -Force
Copy-Item (Join-Path $root "Assets\app.ico") (Join-Path $portableDir "Assets\app.ico") -Force
Copy-Item (Join-Path $root "Assets\logo_512.png") (Join-Path $portableDir "Assets\logo_512.png") -Force
Set-Content -Path (Join-Path $portableDir "portable.dat") -Value "mode=portable" -Encoding ASCII

# Generate clean README for portable
$readmeText = @"
=====================================================
          DISPLICARE: DISPLAY & COLOR STUDIO
              Version $displayVer (Portable)
=====================================================

Features:
- 100% Zero-Dependency Standalone Executable (.NET Framework 4.8)
- Zero-Footprint Portable Mode: Settings & logs are kept directly in this folder.
- DDC/CI Hardware Brightness Control (DXVA2)
- Software brightness via GPU gamma ramp for monitors without DDC/CI (dims to ~50%)
- Calibration is re-checked after Alt+Tab, games and display changes, and only rewritten if Windows reset it.
- Planckian Color Temperature Calibration (2500K - 10000K)
- Full RGB Gain, Contrast, and Gamma Calibration Controls
- Custom Presets Studio: Save, rename, apply, and delete customized color/brightness setups
- Astronomical Solar Circadian Rhythm Engine (NOAA Local Math)
- Windows 11 Fluent Dark Flyout docked to System Tray

How to Run:
1. Double-click 'Displicare.exe'.
2. The Displicare icon appears in your system tray (near the clock).
3. Left-click the tray icon to open the flyout control studio.
4. Right-click the tray icon for instant quick brightness & preset menus.

Command-line Options:
- --minimized  (or -m) : Starts minimized directly into the system tray.
- --portable   (or -p) : Enforces local directory configuration mode.

To remove:
Delete this folder. No AppData files are used. If you enabled "Start with Windows",
turn it off first (it adds an HKCU Run registry entry).
"@
Set-Content -Path (Join-Path $portableDir "README.txt") -Value $readmeText -Encoding UTF8

$portableZip = Join-Path $versionDir "Displicare-v$displayVer-Portable.zip"
if (Test-Path $portableZip) { Remove-Item $portableZip -Force }
Compress-Archive -Path (Join-Path $portableDir "*") -DestinationPath $portableZip -Force
Remove-Item $portableDir -Recurse -Force

# 5. Compile Inno Setup Installer straight into the version folder (no extra copies in dist\)
Write-Host "[5/6] Compiling Setup Installer via Inno Setup 6..." -ForegroundColor Green
& $isccPath "/O$versionDir" (Join-Path $root "installer.iss")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed with code $LASTEXITCODE"
    exit $LASTEXITCODE
}
$setupInVer = Join-Path $versionDir "Displicare-v$displayVer-Setup.exe"

# 6. Generate Checksums & Update VERSIONS.md
Write-Host "[6/6] Generating SHA256 checksums and updating release catalog..." -ForegroundColor Green
$setupHash = (Get-FileHash -Algorithm SHA256 $setupInVer).Hash
$zipHash = (Get-FileHash -Algorithm SHA256 $portableZip).Hash

$setupSize = (Get-Item $setupInVer).Length
$zipSize = (Get-Item $portableZip).Length

$setupSizeMB = [math]::Round($setupSize / 1MB, 2)
$zipSizeKB = [math]::Round($zipSize / 1KB, 1)

$sha256Text = "$zipHash *Displicare-v$displayVer-Portable.zip`r`n$setupHash *Displicare-v$displayVer-Setup.exe`r`n"
Set-Content -Path (Join-Path $versionDir "SHA256SUMS.txt") -Value $sha256Text -Encoding ASCII

# Update or create dist\VERSIONS.md
$versionsFile = Join-Path $distDir "VERSIONS.md"
$buildDate = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")

$newEntry = @"

### Version $displayVer Release
- **Release Date**: $buildDate
- **Assembly Version**: $assemblyVer
- **Packages in `dist/v$displayVer/`**:
  - **Setup Installer**: ``Displicare-v$displayVer-Setup.exe`` ($setupSizeMB MB)
    - SHA256: ``$setupHash``
  - **Portable Package**: ``Displicare-v$displayVer-Portable.zip`` ($zipSizeKB KB)
    - SHA256: ``$zipHash``
- **Notes**: $(if ([string]::IsNullOrWhiteSpace($Notes)) { "Official release build v$displayVer." } else { $Notes })
"@

if (Test-Path $versionsFile) {
    $existingVersions = Get-Content $versionsFile -Raw
    if ($existingVersions -notmatch [regex]::Escape("### Version $displayVer Release")) {
        Add-Content -Path $versionsFile -Value $newEntry -Encoding UTF8
    } else {
        # Update existing entry if forced
        $existingVersions = [System.Text.RegularExpressions.Regex]::Replace(
            $existingVersions,
            "(?s)### Version $displayVer Release.*?(?=\r?\n### Version|\Z)",
            $newEntry.TrimStart()
        )
        Set-Content -Path $versionsFile -Value $existingVersions -Encoding UTF8
    }
} else {
    $header = @"
# Displicare - Version Release Catalog

This catalog maintains a permanent history of all official Displicare releases. Every released version is preserved in its own dedicated `dist/v<Version>/` folder to ensure earlier builds are never lost.

---
"@
    Set-Content -Path $versionsFile -Value ($header + $newEntry) -Encoding UTF8
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "   Build & Release Packaging Completed Successfully!" -ForegroundColor Green
Write-Host "   Release Location: dist\v$displayVer\" -ForegroundColor Yellow
Write-Host "   - Setup Installer : Displicare-v$displayVer-Setup.exe" -ForegroundColor White
Write-Host "   - Portable Zip    : Displicare-v$displayVer-Portable.zip" -ForegroundColor White
Write-Host "   - Checksums       : SHA256SUMS.txt" -ForegroundColor White
Write-Host "   - Version Catalog : dist\VERSIONS.md" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
