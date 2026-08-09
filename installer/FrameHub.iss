#define MyAppName "FrameHub"
#ifndef MyAppVersion
  #error "MyAppVersion must be provided by installer\\Build-Installer.ps1."
#endif
#define MyAppPublisher "9Erza"
#define MyAppURL "https://github.com/9Erza/FrameHub"
#define MyAppExeName "FrameHub.App.exe"
#define PresentMonVersion "2.5.1"
#define PresentMonMsiFileName "PresentMon-v" + PresentMonVersion + ".msi"
#define PresentMonMsiPath "..\artifacts\prerequisites\PresentMon\PresentMon-v2.5.1.msi"

#ifnexist PresentMonMsiPath
  #error "Pinned PresentMon prerequisite is missing. Run installer\\Build-Installer.ps1."
#endif

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
Source: "{#PresentMonMsiPath}"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\FrameHub"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\FrameHub"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Uruchom FrameHub"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  PresentMonServiceRegistryPath = 'SYSTEM\CurrentControlSet\Services\PresentMonSharedService';
  PresentMonApiDllName = 'PresentMonAPI2.dll';
  RequiredPresentMonVersionMS = $00020005;
  RequiredPresentMonVersionLS = $00010000;

function ServiceExecutableFromImagePath(const ImagePath: String): String;
var
  Value: String;
  QuotePosition: Integer;
  SpacePosition: Integer;
begin
  Value := Trim(ImagePath);
  if (Length(Value) > 0) and (Value[1] = '"') then begin
    Delete(Value, 1, 1);
    QuotePosition := Pos('"', Value);
    if QuotePosition > 0 then begin
      Result := Copy(Value, 1, QuotePosition - 1);
      exit;
    end;
  end;

  SpacePosition := Pos(' ', Value);
  if SpacePosition > 0 then
    Result := Copy(Value, 1, SpacePosition - 1)
  else
    Result := Value;
end;

function TryGetPresentMonApiPath(var ApiPath: String): Boolean;
var
  ImagePath: String;
  ServiceExecutable: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, PresentMonServiceRegistryPath, 'ImagePath', ImagePath) then begin
    ServiceExecutable := ServiceExecutableFromImagePath(ImagePath);
    ApiPath := AddBackslash(ExtractFileDir(ServiceExecutable)) + PresentMonApiDllName;
    if FileExists(ApiPath) then begin
      Result := True;
      exit;
    end;
  end;

  ApiPath := ExpandConstant('{autopf}\Intel\PresentMonSharedService\' + PresentMonApiDllName);
  Result := FileExists(ApiPath);
end;

function TryGetPresentMonVersion(var ApiPath: String; var Version: String; var VersionMS: Cardinal; var VersionLS: Cardinal): Boolean;
begin
  Result := TryGetPresentMonApiPath(ApiPath) and GetVersionNumbers(ApiPath, VersionMS, VersionLS) and GetVersionNumbersString(ApiPath, Version);
end;

function IsCompatiblePresentMonInstalled(): Boolean;
var
  ApiPath: String;
  Version: String;
  VersionMS: Cardinal;
  VersionLS: Cardinal;
begin
  Result := TryGetPresentMonVersion(ApiPath, Version, VersionMS, VersionLS) and (VersionMS = RequiredPresentMonVersionMS) and (VersionLS = RequiredPresentMonVersionLS);
  if Result then
    Log(Format('Reusing PresentMon Shared Service/API %s at %s.', [Version, ApiPath]));
end;

function HasNewerPresentMonInstalled(): Boolean;
var
  ApiPath: String;
  Version: String;
  VersionMS: Cardinal;
  VersionLS: Cardinal;
begin
  Result := TryGetPresentMonVersion(ApiPath, Version, VersionMS, VersionLS) and ((VersionMS > RequiredPresentMonVersionMS) or ((VersionMS = RequiredPresentMonVersionMS) and (VersionLS > RequiredPresentMonVersionLS)));
  if Result then
    Log(Format('A non-pinned PresentMon Shared Service/API version is already installed: %s at %s.', [Version, ApiPath]));
end;

function HasUnverifiedPresentMonInstalled(): Boolean;
var
  ApiPath: String;
  Version: String;
  VersionMS: Cardinal;
  VersionLS: Cardinal;
begin
  Result := TryGetPresentMonApiPath(ApiPath) and not TryGetPresentMonVersion(ApiPath, Version, VersionMS, VersionLS);
  if Result then
    Log(Format('An unverifiable PresentMon Shared Service/API file is already installed at %s.', [ApiPath]));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  MsiPath: String;
  ResultCode: Integer;
begin
  Result := '';
  if IsCompatiblePresentMonInstalled() then
    exit;

  if HasNewerPresentMonInstalled() or HasUnverifiedPresentMonInstalled() then begin
    Result := 'A newer or unverified Intel PresentMon Shared Service/API is already installed. FrameHub Setup will not downgrade or replace this shared component automatically.';
    exit;
  end;

  ExtractTemporaryFile('{#PresentMonMsiFileName}');
  MsiPath := ExpandConstant('{tmp}\{#PresentMonMsiFileName}');
  if not Exec(ExpandConstant('{sys}\msiexec.exe'), '/i "' + MsiPath + '" /qn /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then begin
    Result := 'FrameHub Setup could not start the embedded Intel PresentMon prerequisite installer.';
    exit;
  end;

  if (ResultCode <> 0) and (ResultCode <> 3010) then begin
    Result := Format('The embedded Intel PresentMon prerequisite installation failed with Windows Installer exit code %d.', [ResultCode]);
    exit;
  end;

  if ResultCode = 3010 then
    Log('PresentMon MSI returned 3010. FrameHub will not restart Windows automatically.');

  if not IsCompatiblePresentMonInstalled() then
    Result := 'Intel PresentMon was installed, but the required PresentMon Shared Service/API v2.5.1 could not be verified.';
end;
