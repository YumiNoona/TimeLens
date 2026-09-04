#ifndef AppVersion
#define AppVersion "5.5.0"
#endif

#define AppName "TimeLens"
#define AppPublisher "Veil"
#define AppExeName "TimeLens.exe"
#define AppUrl "https://timelens.venusapp.in"

[Setup]
AppId={{4C8DB8EA-3E7C-47E6-A55B-E0EE0A16B132}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/docs
AppUpdatesURL={#AppUrl}
AppComments=Private activity tracking for Windows
DefaultDirName={localappdata}\Programs\TimeLens
DefaultGroupName=TimeLens
DisableProgramGroupPage=auto
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.10240
OutputDir=output
OutputBaseFilename=TimeLens-Setup
SetupIconFile=..\src\TimeLens.TrayApp\TimeLens.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
ShowLanguageDialog=no
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
AppMutex=TimeLens-TrayApp-Instance
SetupLogging=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=TimeLens Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
FinishedLabel=Setup has installed [name] on your computer.%n%nTimeLens runs in the notification area beside the Windows clock. If its icon is hidden, open the ^ menu to find it. Right-click the icon and choose Open Dashboard.

[Tasks]
Name: "startup"; Description: "Start TimeLens automatically when I sign in"; GroupDescription: "Startup"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts"; Flags: unchecked

[Files]
Source: "..\TimeLens.exe"; DestDir: "{app}"; DestName: "{#AppExeName}"; Flags: ignoreversion

[Icons]
Name: "{group}\TimeLens"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; IconIndex: 0
Name: "{autodesktop}\TimeLens"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; IconIndex: 0; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TimeLens"; ValueData: """{app}\{#AppExeName}"" --startup"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch TimeLens"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not WizardIsTaskSelected('startup')) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TimeLens');
end;
