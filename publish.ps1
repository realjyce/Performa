# Builds PerformaSetup.exe in this folder: a normal Windows installer carrying a
# single self-contained Performa.exe that runs on a machine with no .NET.
#
#   pwsh ./publish.ps1             -> PerformaSetup.exe
#   pwsh ./publish.ps1 -Install    -> also runs it, so it is installed here
#   pwsh ./publish.ps1 -Portable   -> also leaves the bare exe in build/
#
# Trimming is off deliberately. Avalonia resolves controls and converters by
# reflection, so a trimmed build publishes clean and then fails at runtime,
# which is the worst place to find out.
#
# The Inno Setup script is generated below rather than kept as a file: it is a
# build detail, and one recipe in one place beats two that can drift apart.

param(
    [switch]$Install,
    [switch]$Portable
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$build = Join-Path $root "build"

Get-Process Performa -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Remove-Item $build -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish "$root/src/Performa.Desktop" `
    -c Release `
    -p:PublishSingleFile=true `
    -o $build `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Symbols are not part of a shipped build.
Get-ChildItem $build -Filter *.pdb | Remove-Item -Force

# Credentials travel beside the binary so the app needs no setup at all. They
# live outside the repository. Without them Performa still runs, it just asks
# for a key in Settings.
$appData = Join-Path $env:APPDATA "performa"
foreach ($name in @("app-credentials.json", "google-credentials.json")) {
    $src = Join-Path $appData $name
    if (Test-Path $src) {
        Copy-Item $src $build -Force
    } else {
        Write-Warning "$name not found in $appData - the build will ask the user for that credential."
    }
}

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup not found. Install it with: winget install JRSoftware.InnoSetup"
}

# Installs under LOCALAPPDATA rather than Program Files on purpose: a per-user
# install needs no administrator rights, and an elevation prompt on an unsigned
# binary is exactly what makes people cancel.
$iss = @"
#define AppName "Performa"
#define AppVersion "1.0.0"
#define AppExe "Performa.exe"

[Setup]
AppId={{8F3A6C21-4D5E-4B9A-9C77-2E1B5A0D7F42}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Jason Clarence
AppSupportURL=https://github.com/realjyce/Performa
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=$root
OutputBaseFilename=PerformaSetup
SetupIconFile=$root\src\Performa.Desktop\Assets\performa.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
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
Source: "$build\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
; Optional: a build made without them still runs and asks in Settings.
Source: "$build\app-credentials.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "$build\google-credentials.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The single-file exe extracts itself here on every run.
Type: filesandordirs; Name: "{localappdata}\Temp\.net\{#AppName}"
"@

$issPath = Join-Path $build "Performa.iss"
Set-Content -Path $issPath -Value $iss -Encoding UTF8

& $iscc $issPath | Out-Null
if ($LASTEXITCODE -ne 0) { throw "installer compile failed" }

$setup = Join-Path $root "PerformaSetup.exe"
$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "built PerformaSetup.exe ($mb MB)"

if ($Portable) {
    Write-Host "portable exe left at $build\Performa.exe"
} else {
    # Nothing here is worth keeping once the installer carries it.
    Remove-Item $build -Recurse -Force -ErrorAction SilentlyContinue
}

if ($Install) {
    Start-Process $setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/TASKS=desktopicon" -Wait
    Write-Host "installed to $env:LOCALAPPDATA\Programs\Performa with a Desktop shortcut"
}
