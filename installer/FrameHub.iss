#define MyAppName "FrameHub"
#define MyAppVersion "0.5.0"
#define MyAppPublisher "9Erza"
#define MyAppURL "https://github.com/9Erza/FrameHub"
#define MyAppExeName "FrameHub.App.exe"

[Setup]
; NIE ZMIENIAĆ - AppId używany od FrameHub 0.4.0
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

PrivilegesRequired=admin
UsePreviousAppDir=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=..\artifacts\installer
OutputBaseFilename=FrameHub-Setup-{#MyAppVersion}

SetupIconFile=..\FrameHub.App\Assets\FrameHub.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName=FrameHub

Compression=lzma2
SolidCompression=yes

WizardStyle=modern

CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"; GroupDescription: "Dodatkowe skróty:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\FrameHub"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\FrameHub"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Uruchom FrameHub"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser