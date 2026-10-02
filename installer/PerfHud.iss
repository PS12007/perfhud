; Inno Setup 6 script — per-user install, no admin rights required.
; Build: ./build.ps1 -Installer   (or: ISCC.exe /DAppVersion=1.0.0 /DSourceDir=..\dist\PerfHud installer\PerfHud.iss)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\PerfHud"
#endif

[Setup]
AppId={{6C1F3B0E-5B7A-4E7B-9C1D-2F0B7E8A9D41}
AppName=PerfHud
AppVersion={#AppVersion}
AppPublisher=PerfHud
AppPublisherURL=https://github.com/PS12007/perfhud
DefaultDirName={localappdata}\Programs\PerfHud
DefaultGroupName=PerfHud
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=PerfHud-{#AppVersion}-setup
SetupIconFile=..\src\PerfHud\Assets\app.ico
UninstallDisplayIcon={app}\PerfHud.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force

[Tasks]
Name: "startup"; Description: "Start PerfHud with Windows"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\PerfHud"; Filename: "{app}\PerfHud.exe"
Name: "{group}\Uninstall PerfHud"; Filename: "{uninstallexe}"
Name: "{autodesktop}\PerfHud"; Filename: "{app}\PerfHud.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PerfHud"; \
  ValueData: """{app}\PerfHud.exe"" --startup"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\PerfHud.exe"; Description: "Launch PerfHud"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM PerfHud.exe /F"; Flags: runhidden; RunOnceId: "KillPerfHud"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""PerfHud (elevated sensors)"" /F"; Flags: runhidden; RunOnceId: "DeleteTask"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\PerfHud\logs"
