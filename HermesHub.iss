; Script generated for Inno Setup 6
; Agents Hub Installer Configuration

#define MyAppName "Agents Hub"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "ZeroNuxs"
#define MyAppURL "https://github.com/ZerroDevs/HermesOn"
#define MyAppExeName "HermesHub.exe"

[Setup]
; Unique AppId identifier (do not change across versions to allow seamless upgrades)
AppId={{E5D4A27A-94B8-4FE2-9E83-6A728D84B501}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=LICENSE
OutputDir=dist
OutputBaseFilename=AgentsHub-Setup-v{#MyAppVersion}
SetupIconFile=assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; Platform target: 64-bit Windows
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; User permission mode: allows per-user or administrative install
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline

; Detect running instances via mutex and close cleanly
AppMutex=Global\AgentsHub_SingleInstance_Mutex,AgentsHub_SingleInstance_Mutex,Global\HermesHub_SingleInstance_Mutex,HermesHub_SingleInstance_Mutex
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Launch Agents Hub minimized to system tray on Windows startup"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "bin\Release\net10.0-windows\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net10.0-windows\win-x64\publish\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--tray"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
