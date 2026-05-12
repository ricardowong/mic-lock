; MicLock Inno Setup 6 Script
; Builds a zero-elevation, lightweight Windows installer

#define MyAppName "MicLock"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MicLock Open Source"
#define MyAppURL "https://github.com/developer/mic-lock"
#define MyAppExeName "MicLock.exe"

[Setup]
AppId={{9F5744C2-F068-4EA4-A754-0C635DA11A1B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
; Per-user install: zero admin elevation / zero UAC prompt required!
PrivilegesRequired=lowest
OutputDir=..\dist\installer
OutputBaseFilename=MicLock-Setup-v{#MyAppVersion}
SetupIconFile=..\src\MicLock.App\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startwithwindows"; Description: "Start MicLock automatically when logging into Windows"; GroupDescription: "Startup Options:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\MicLock.App\Assets\app.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "..\src\MicLock.App\Assets\mic-locked.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "..\src\MicLock.App\Assets\mic-unlocked.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Standard Windows Run key for user autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: startwithwindows
; Windows Explorer StartupApproved key so it appears enabled in Task Manager Startup tab
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueType: binary; ValueName: "{#MyAppName}"; ValueData: 02 00 00 00 00 00 00 00 00 00 00 00; Flags: uninsdeletevalue; Tasks: startwithwindows

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
