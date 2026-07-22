# Builds the shipping executable: one self-contained Performa.exe that runs on a
# machine with no .NET installed.
#
# Trimming is deliberately off. Avalonia resolves controls and converters by
# reflection, so a trimmed build publishes fine and then fails at runtime, which
# is the worst possible place to discover it.
#
#   pwsh ./publish.ps1              -> dist/Performa.exe
#   pwsh ./publish.ps1 -Installer   -> also installer/out/PerformaSetup.exe
#   pwsh ./publish.ps1 -Install     -> also installs here with a Desktop shortcut

param(
    [string]$Output = "dist",
    [switch]$Install,
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Get-Process Performa -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet publish "$root/src/Performa.Desktop" `
    -c Release `
    -p:PublishSingleFile=true `
    -o "$root/$Output" `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Symbols are not part of a shipped build.
Get-ChildItem "$root/$Output" -Filter *.pdb | Remove-Item -Force

# Credentials travel beside the binary so the app needs no setup. They live
# outside the repository and are gitignored; without them Performa still runs,
# it just asks for a key in Settings.
$appData = Join-Path $env:APPDATA "performa"
foreach ($name in @("app-credentials.json", "google-credentials.json")) {
    $src = Join-Path $appData $name
    if (Test-Path $src) {
        Copy-Item $src "$root/$Output" -Force
    } else {
        Write-Warning "$name not found in $appData - the build will ask the user for that credential."
    }
}

$exe = Join-Path "$root/$Output" "Performa.exe"
$mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "built $exe ($mb MB)"

if ($Installer) {
    # Inno Setup installs per-user, so ISCC lives under LOCALAPPDATA here.
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $iscc) {
        throw "Inno Setup not found. Install it with: winget install JRSoftware.InnoSetup"
    }

    & $iscc "$root/installer/Performa.iss"
    if ($LASTEXITCODE -ne 0) { throw "installer compile failed" }

    $setup = Join-Path $root "installer/out/PerformaSetup.exe"
    $setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-Host "built $setup ($setupMb MB)"
}

if ($Install) {
    $target = Join-Path $env:LOCALAPPDATA "Programs\Performa"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item "$root/$Output/*" -Destination $target -Force

    $ws = New-Object -ComObject WScript.Shell
    $lnk = $ws.CreateShortcut((Join-Path $env:USERPROFILE "Desktop\Performa.lnk"))
    $lnk.TargetPath = Join-Path $target "Performa.exe"
    $lnk.WorkingDirectory = $target
    $lnk.IconLocation = (Join-Path $target "Performa.exe") + ",0"
    $lnk.Description = "A local-first developer chief of staff."
    $lnk.Save()

    Write-Host "installed to $target with a Desktop shortcut"
}
