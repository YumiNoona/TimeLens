#define MyAppName "TimeLens"
#define MyAppExeName "TimeLens.TrayApp.exe"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{7B8F3A2D-6C1E-4E95-A0D4-92F1B8E3C5A7}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppName}
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=TimeLens-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
DisableProgramGroupPage=yes

[Files]
Source: "..\src\TimeLens.TrayApp\bin\Release\net9.0\win-x64\publish\TimeLens.TrayApp.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\TimeLens.TrayApp\bin\Release\net9.0\win-x64\publish\dashboard\*"; DestDir: "{app}\dashboard"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch TimeLens now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden waituntilterminated
