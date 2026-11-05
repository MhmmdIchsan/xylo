#ifndef PayloadDir
  #define PayloadDir "..\artifacts\release\payload"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif
#define AppVersion "0.1.0"

[Setup]
AppId={{6F93DF20-4E8C-4F1B-9D0D-0F247CBFB1D1}
AppName=Xylo
AppVersion={#AppVersion}
AppPublisher=Xylo
AppPublisherURL=https://github.com/MhmmdIchsan/xylo
DefaultDirName={localappdata}\Programs\Xylo
DefaultGroupName=Xylo
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=Xylo-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\Xylo.exe
AppMutex=Local\Xylo.Desktop

[Tasks]
Name: "startup"; Description: "Start Xylo when I sign in to Windows"; Flags: checkedonce

[Files]
Source: "{#PayloadDir}\Xylo.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\third-party\WinIsland.LICENSE"; DestDir: "{app}\third-party"; Flags: ignoreversion
Source: "..\integrations\*"; DestDir: "{app}\integrations"; Excludes: "__pycache__\*,*.pyc"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Xylo"; Filename: "{app}\Xylo.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Xylo"; ValueData: """{app}\Xylo.exe"""; Flags: uninsdeletevalue; Tasks: startup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Xylo"; Flags: deletevalue; Check: not WizardIsTaskSelected('startup')

[Run]
Filename: "{app}\Xylo.exe"; Description: "Launch Xylo"; Flags: nowait postinstall skipifsilent
