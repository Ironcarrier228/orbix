; Orbix installer (Inno Setup 6).
;
;   iscc /DAppVersion=1.0.0 /DSourceExe=..\artifacts\portable\Orbix.exe installer.iss
; or through the publish script (it passes the same defines):
;   pwsh build/publish.ps1 -Variant portable -Installer
;
; The setup installs the portable (self-contained) Orbix.exe, offers an autostart entry in
; HKCU\Software\Microsoft\Windows\CurrentVersion\Run and asks the running instance to exit
; before an upgrade.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\artifacts\portable\Orbix.exe"
#endif

[Setup]
AppId={{B7E2A1C4-5D6F-4A8E-9B0C-ORBIX0000001}
AppName=Orbix
AppVersion={#AppVersion}
AppPublisher=Orbix
AppComments=Radial quick-launch menu
DefaultDirName={autopf}\Orbix
DefaultGroupName=Orbix
OutputBaseFilename=Orbix-Setup-{#AppVersion}
OutputDir=..\artifacts
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Orbix.exe
DisableProgramGroupPage=yes
WizardStyle=modern

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Запускать вместе с Windows / Start with Windows"; Flags: unchecked
Name: "desktopicon"; Description: "Значок на рабочем столе / Desktop icon"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "Orbix.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\Orbix"; Filename: "{app}\Orbix.exe"
Name: "{autodesktop}\Orbix"; Filename: "{app}\Orbix.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Orbix"; ValueData: """{app}\Orbix.exe"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\Orbix.exe"; Description: "Запустить Orbix / Start Orbix"; Flags: nowait postinstall skipifsilent

[Code]
// Ask a running instance to quit so the file can be replaced.
procedure PrepareToInstall(var NeedsRestart: Boolean);
var
  ResultCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\Orbix.exe')) then
  begin
    Exec(ExpandConstant('{app}\Orbix.exe'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
