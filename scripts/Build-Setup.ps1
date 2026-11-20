param([Parameter(Mandatory=$true)][string]$MsvcBin)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $repo 'build\setup'
$output = Join-Path $repo 'build\release'
New-Item -ItemType Directory -Path $stage, $output -Force | Out-Null
$launcher = Join-Path $stage 'offline-launcher.exe'
& (Join-Path $repo 'trainer\Build-Trainer.ps1') -MsvcBin $MsvcBin -Output $launcher -ExperimentalBotRace

$arguments = @(
    '-m', 'PyInstaller', '--noconfirm', '--onefile', '--windowed',
    '--name', 'Asphalt8PrimeRemake-Setup',
    '--distpath', $output, '--workpath', $stage, '--specpath', $stage,
    '--paths', (Join-Path $repo 'tools\native-ui'),
    '--add-data', "$launcher;.",
    (Join-Path $PSScriptRoot 'setup.py')
)
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Setup packaging failed.' }
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $output 'Asphalt8PrimeRemake-Setup.exe')
