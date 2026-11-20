param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [Parameter(Mandatory = $true)][string]$MsvcBin
)
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameDirectory).Path
$repo = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $game 'Asphalt 8 Prime Remake.exe'
$legacyLaunchers = @(
    (Join-Path $game 'Asphalt 8 Airborne 4.8.exe')
    (Join-Path $game 'Asphalt 8 Airborne 4.8 Offline.exe')
)
if (Get-Process Asphalt8 -ErrorAction SilentlyContinue) { throw 'Close Asphalt 8 before installing.' }
if (Get-Process | Where-Object { $_.Path -eq $launcher -or $_.Path -in $legacyLaunchers }) { throw 'Exit the existing trainer from its tray menu first.' }
foreach ($name in 'cl.exe', 'link.exe') {
    if (!(Test-Path -LiteralPath (Join-Path $MsvcBin $name))) { throw "Missing x86 build tool: $name" }
}
$runtime = Get-AppxPackage -Name Microsoft.VCLibs.120.00 | Where-Object { $_.Architecture -eq 'X86' }
if (!$runtime) { throw 'Install the original game dependency Microsoft.VCLibs.120.00 (x86) first.' }
& python (Join-Path $PSScriptRoot 'patch_offline.py') --game-dir $game
if ($LASTEXITCODE -ne 0) { throw 'Offline patch failed. No package registration was attempted.' }
$gui = Join-Path $game 'Data\gui'
$nativeStage = Join-Path $game '_offline_patch_backup\native-ui-generated'
$nativeBuilder = Join-Path $repo 'tools\native-ui\build_native_ui.py'
foreach ($kind in 'panel', 'keyboard', 'roster') {
    $destination = Join-Path $nativeStage $kind
    $mode = "--$kind-only"
    & python $nativeBuilder --gui $gui --output $destination $mode
    if ($LASTEXITCODE -ne 0) { throw "Native $kind resource generation failed." }
    $name = if ($kind -eq 'keyboard') { 'offline_lobby_ui.bgin' } else { "offline_lobby_$kind.bgin" }
    Copy-Item -LiteralPath (Join-Path $destination $name) -Destination (Join-Path $gui $name) -Force
}
& (Join-Path $repo 'trainer/Build-Trainer.ps1') -MsvcBin $MsvcBin -Output $launcher
$manifestPath = Join-Path $game 'AppxManifest.xml'
$identity = ([xml](Get-Content -LiteralPath $manifestPath -Raw)).Package.Identity
$registered = Get-AppxPackage -Name $identity.Name | Where-Object {
    $_.InstallLocation -eq $game -and $_.Version -eq [version]$identity.Version
}
if (!$registered) {
    Add-AppxPackage -Register $manifestPath
}
foreach ($legacyLauncher in $legacyLaunchers) {
    if (Test-Path -LiteralPath $legacyLauncher -PathType Leaf) {
        $legacyBackup = Join-Path $game ('_offline_patch_backup\launcher-' + [Guid]::NewGuid().ToString('N') + '.exe')
        Move-Item -LiteralPath $legacyLauncher -Destination $legacyBackup
    }
}
Write-Host "Installed. Launch: $launcher"
Write-Host 'Press Insert to show or hide the trainer. Select English in the game language settings.'
