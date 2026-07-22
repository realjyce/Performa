; Inno Setup script for Performa.
;
; Produces installer/out/PerformaSetup.exe: a normal Windows installer with a
; Start Menu entry, an optional Desktop shortcut, and an uninstaller that shows
; up in Add/Remove Programs.
;
; Installs per-user (LOCALAPPDATA) rather than into Program Files on purpose:
; it needs no administrator rights, which matters because the app is unsigned
; and an elevation prompt on an unsigned binary is exactly what makes people
; cancel. Run publish.ps1 first so dist/ exists.

#define AppName "Performa"
#define AppVersion "1.0.0"
#define AppPublisher "Jason Clarence"
#define AppExe "Performa.exe"

[Setup]
AppId={{8F3A6C21-4D5E-4B9A-9C77-2E1B5A0D7F42}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL=https://github.com/realjyce/Performa
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=out
OutputBaseFilename=PerformaSetup
SetupIconFile=..\src\Performa.Desktop\Assets\performa.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Per-user install, so no UAC prompt.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
DisableDirPage=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
; Credentials are what let the app work without the user typing anything. They
; are optional: without them Performa runs and asks for a key in Settings.
Source: "..\dist\app-credentials.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\google-credentials.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The exe extracts itself here on every run; leaving it behind is litter.
Type: filesandordirs; Name: "{localappdata}\Temp\.net\{#AppName}"
