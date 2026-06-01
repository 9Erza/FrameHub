; FrameHub Installer Script
; Version: 0.4.0
; IMPORTANT:
; AppId must stay the same across all FrameHub releases.
; This AppId was recovered from the installed v0.3.1 registry entry.

#define MyAppName "FrameHub"
#define MyAppVersion "0.4.0"
#define MyAppPublisher "9Erza"
#define MyAppURL "https://github.com/9Erza/FrameHub"
#define MyAppExeName "FrameHub.App.exe"
#define MyPublishDir "C:\dev\repos\FrameHub\FrameHub.App\bin\Release\net10.0-windows\publish\win-x64"

[Setup]
AppId={{A073F5F3-E7D0-45C7-A233-8A98B033385B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

DefaultDirName={autopf}\FrameHub
DefaultGroupName=FrameHub
DisableDirPage=yes
DisableProgramGroupPage=yes
AllowNoIcons=yes

UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

OutputDir=C:\Users\erykz\Desktop
OutputBaseFilename=FrameHub_Setup_v0.4.0
SetupIconFile=C:\dev\repos\FrameHub\FrameHub.App\Assets\FrameHub.ico

Compression=lzma2
SolidCompression=yes
WizardStyle=modern

VersionInfoVersion=0.4.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=FrameHub Windows performance and game optimization hub
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser