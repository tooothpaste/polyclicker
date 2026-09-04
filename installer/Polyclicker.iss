; Inno Setup script for Polyclicker. Build with installer\make-installer.ps1.
;
; Per-user install under %LOCALAPPDATA%\Programs: no UAC prompt, which is
; the right default for an unsigned binary. Settings, takes and profiles
; live in %APPDATA%\Polyclicker and are never touched by install or
; uninstall.

#define AppExe "..\Polyclicker.exe"
#define AppVer GetVersionNumbersString(AppExe)

[Setup]
AppId={{7E5B2C0A-4C2D-4D1E-9F5A-2B8C6D3E1F40}
AppName=Polyclicker
AppVersion={#AppVer}
AppVerName=Polyclicker {#AppVer}
AppPublisher=tooothpaste
AppPublisherURL=https://github.com/tooothpaste/polyclicker
DefaultDirName={localappdata}\Programs\Polyclicker
DefaultGroupName=Polyclicker
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Polyclicker-Setup-{#AppVer}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\Polyclicker.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Polyclicker"; Filename: "{app}\Polyclicker.exe"
Name: "{group}\Uninstall Polyclicker"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Polyclicker"; Filename: "{app}\Polyclicker.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Polyclicker.exe"; Description: "Launch Polyclicker"; Flags: nowait postinstall skipifsilent
