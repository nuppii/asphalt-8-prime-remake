param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\build\multiplayer'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build-Rooms.ps1') -OutputDirectory $OutputDirectory
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /reference:System.Windows.Forms.dll /reference:System.Security.dll /reference:System.Runtime.Serialization.dll "/reference:$OutputDirectory\OfflineLab.Rooms.dll" "/out:$OutputDirectory\NativeBridgeCheck.exe" (Join-Path $PSScriptRoot '..\trainer\NativeLobbyBridge.cs') (Join-Path $PSScriptRoot '..\trainer\NativeNetworkState.cs') (Join-Path $PSScriptRoot 'NativeBridgeCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native bridge fixture compilation failed.' }
