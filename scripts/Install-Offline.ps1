param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [Parameter(Mandatory = $true)][string]$MsvcBin
)
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameDirectory).Path
$repo = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $game 'Asphalt 8 Airborne 4.8 Offline.exe'
if (Get-Process Asphalt8 -ErrorAction SilentlyContinue) { throw 'Close Asphalt 8 before installing.' }
if (Get-Process | Where-Object { $_.Path -eq $launcher }) { throw 'Exit the existing trainer from its tray menu first.' }
foreach ($name in 'cl.exe', 'link.exe') {
    if (!(Test-Path -LiteralPath (Join-Path $MsvcBin $name))) { throw "Missing x86 build tool: $name" }
}
$runtime = Get-AppxPackage -Name Microsoft.VCLibs.120.00 | Where-Object { $_.Architecture -eq 'X86' }
if (!$runtime) { throw 'Install the original game dependency Microsoft.VCLibs.120.00 (x86) first.' }
& python (Join-Path $PSScriptRoot 'patch_offline.py') --game-dir $game
if ($LASTEXITCODE -ne 0) { throw 'Offline patch failed. No package registration was attempted.' }
& (Join-Path $repo 'trainer/Build-Trainer.ps1') -MsvcBin $MsvcBin -Output $launcher
Add-AppxPackage -Register (Join-Path $game 'AppxManifest.xml') -DisableDevelopmentMode
Write-Host "Installed. Launch: $launcher"
Write-Host 'Press Insert to show or hide the trainer. Select English in the game language settings.'
