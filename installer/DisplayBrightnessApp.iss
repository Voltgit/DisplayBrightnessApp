#define MyAppName "DisplayBrightnessApp"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppPublisher "Aslan Ismailov"
#define MyAppURL "https://github.com/Voltgit/DisplayBrightnessApp"
#define MyAppExeName "DisplayBrightnessApp.exe"

[Setup]
; Fixed AppId so future versions upgrade in place instead of installing side by side.
AppId={{B6E1B6C1-6E1E-4C7B-9C4A-2E7B6D2F6A11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install location and no elevation prompt when run without admin
; rights -- matches the app itself, which only ever touches HKCU.
PrivilegesRequired=lowest
LicenseFile=..\LICENSE
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
OutputDir=Output
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
; The app self-registers its own autostart entry (HKCU Run key) on first
; launch -- nothing to do here beyond offering to start it once installed.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up the autostart registry value StartupRegistration.cs writes, so
; uninstalling doesn't leave a dangling Run entry pointing at a deleted exe.
Type: files; Name: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "DisplayBrightnessApp"; Flags: deletevalue uninsdeletevalue
