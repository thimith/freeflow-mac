; Inno Setup script — builds FreeFlow-Setup.exe (per-user install, no admin needed)
#define AppVersion GetEnv("FF_VERSION")
#if AppVersion == ""
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6B1E8C2A-4F3D-4E8B-9A57-0F2EE0F10001}
AppName=FreeFlow
AppVersion={#AppVersion}
AppVerName=FreeFlow {#AppVersion}
AppPublisher=FreeFlow
DefaultDirName={localappdata}\Programs\FreeFlow
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..\out
OutputBaseFilename=FreeFlow-Setup
SetupIconFile=..\FreeFlow\app.ico
UninstallDisplayIcon={app}\FreeFlow.exe
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
CloseApplications=force

[Tasks]
Name: "startup"; Description: "Start FreeFlow when I sign in to Windows"
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\FreeFlow"; Filename: "{app}\FreeFlow.exe"
Name: "{autodesktop}\FreeFlow"; Filename: "{app}\FreeFlow.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FreeFlow"; ValueData: """{app}\FreeFlow.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\FreeFlow.exe"; Description: "Launch FreeFlow now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM FreeFlow.exe"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM whisper-server.exe"; Flags: runhidden; RunOnceId: "KillWhisper"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM FreeFlow.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM whisper-server.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;
