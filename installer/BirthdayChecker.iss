; Inno Setup 6 script. Build with installer\build.ps1 (it publishes the app and passes the version).
; Per-user install: no admin rights, data (entries.json, settings.json, data\, Backup\) lives next to the exe
; and is never touched by install, update or uninstall (a first install only seeds a starter entries.json).

#ifndef AppVersion
  #error AppVersion is not set - build with installer\build.ps1 (it reads Directory.Build.props).
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

; Fixed forever: identifies the app for updates and uninstall.
#define AppGuid "{B60A9491-1176-449B-AAC4-748EA3848800}"
#define AppName "BirthDay Checker"
#define AppExe "BirthdayChecker.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define RunValue "BirthDayChecker"

[Setup]
; "{{" escapes the brace in [Setup]; the registry key itself is "{GUID}_is1".
AppId={{#AppGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Zlatous Leha
AppPublisherURL=https://github.com/Raptar212/BirthDay-Checker
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=BirthdayChecker-Setup-{#AppVersion}
SetupIconFile=..\src\BirthdayChecker.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; The tray app may be running: close it for the update, the new version is started at the end.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
; First install only: on updates the app's own setting is the truth (Inno would otherwise re-apply the remembered task).
Name: "autostart"; Description: "Запускать вместе с Windows"; GroupDescription: "Дополнительно:"; Check: not IsUpgrade
Name: "desktopicon"; Description: "Ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[InstallDelete]
; Program files of versions before 1.11 (now inside the single exe). Updates only: a first install may share a folder.
Type: files; Name: "{app}\*.dll"; Check: IsUpgrade
Type: files; Name: "{app}\*.pdb"; Check: IsUpgrade
Type: files; Name: "{app}\createdump.exe"; Check: IsUpgrade
Type: files; Name: "{app}\BirthdayChecker.deps.json"; Check: IsUpgrade
Type: files; Name: "{app}\BirthdayChecker.runtimeconfig.json"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\cs"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\de"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\es"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\fr"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\it"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\ja"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\ko"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\pl"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\pt-BR"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\ru"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\tr"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\zh-Hans"; Check: IsUpgrade
Type: filesandordirs; Name: "{app}\zh-Hant"; Check: IsUpgrade

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Starter list with holidays: first install into a folder without any entries file only. Never replaced,
; never uninstalled — from then on it is the user's data.
Source: "default-entries.json"; DestDir: "{app}"; DestName: "entries.json"; Flags: onlyifdoesntexist uninsneveruninstall; Check: NoEntriesYet

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app's own "Запускать вместе с Windows" setting writes, so both stay in sync.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunValue}"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The tray app holds its files; Restart Manager usually closes it, this is the fallback.
; Only this installed copy — not a dev build or another copy elsewhere.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -Command ""Get-Process BirthdayChecker -ErrorAction SilentlyContinue | Where-Object Path -eq '{app}\{#AppExe}' | Stop-Process -Force"""; Flags: runhidden; RunOnceId: "StopApp"

[Code]
function IsUpgrade: Boolean;
begin
  Result := RegValueExists(HKEY_CURRENT_USER,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppGuid}_is1', 'UninstallString');
end;

// Leftovers of a lost entries.json must stay visible to the app (it asks to restore them), not be masked.
function NoEntriesYet: Boolean;
begin
  Result := not IsUpgrade
    and not FileExists(ExpandConstant('{app}\entries.json'))
    and not FileExists(ExpandConstant('{app}\entries.json.tmp'))
    and not FileExists(ExpandConstant('{app}\data\entries.json.bak'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Autostart may have been switched on from the app's settings, not the installer task.
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', '{#RunValue}');
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', '{#RunValue}');
  end;
end;
