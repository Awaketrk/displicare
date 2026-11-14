; Displicare: Display & Color Studio - Inno Setup 6 Script
#define MyAppName "Displicare"
#define MyAppVersion "2.1.0"
#define MyAppPublisher "Displicare Studio"
#define MyAppExeName "Displicare.exe"
; Folder containing this script, so the project can live anywhere
#define SourceDir RemoveBackslashUnlessRoot(SourcePath)

[Setup]
AppId={{6D80A21B-4032-4752-9A92-6F0110825B3E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/Awaketrk/displicare
AppSupportURL=https://github.com/Awaketrk/displicare
AppUpdatesURL=https://github.com/Awaketrk/displicare/releases
DefaultDirName={localappdata}\Programs\Displicare
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#SourceDir}\dist
OutputBaseFilename=Displicare-v2.1.0-Setup
SetupIconFile={#SourceDir}\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Assets\app.ico
UninstallDisplayName={#MyAppName}
VersionInfoVersion=2.1.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer
VersionInfoProductName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
; Only offered on a first install. On an update the app's own "Start with Windows" setting stands: Setup remembers the
; task, so up to 2.5.3 every update wrote the startup entry again after it had been turned off in the app.
Name: "startwithwindows"; Description: "Launch Displicare automatically on Windows startup (System Tray)"; GroupDescription: "Startup Options:"; Check: IsFirstInstall

[Files]
Source: "{#SourceDir}\bin\Release\Displicare.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Assets\app.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "{#SourceDir}\Assets\logo_512.png"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Comment: "Display Brightness & Color Temperature Studio"
Name: "{autoprograms}\{#MyAppName}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; IconFilename: "{app}\Assets\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon; Comment: "Display Brightness & Color Temperature Studio"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Displicare"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: startwithwindows

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Runs before the files are removed: closes a running Displicare (so its files aren't in use), then puts Windows'
; gamma ramp limit (GdiIcmGammaRange) back if Deeper software dimming changed it (Windows asks for admin only then).
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "DisplicareUninstall"

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6D80A21B-4032-4752-9A92-6F0110825B3E}_is1';

var
  FirstInstall: Boolean;

function InitializeSetup(): Boolean;
begin
  { Read before anything is installed: Setup writes this key itself at the end }
  FirstInstall := not RegKeyExists(HKEY_CURRENT_USER, UninstallKey) and not RegKeyExists(HKEY_LOCAL_MACHINE, UninstallKey);
  Result := True;
end;

function IsFirstInstall(): Boolean;
begin
  Result := FirstInstall;
end;


















